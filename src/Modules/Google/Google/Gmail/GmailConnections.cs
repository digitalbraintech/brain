using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Sdk;

namespace DigitalBrain.Google;

// No serializers, records or public token properties: this volatile kernel-private store
// is deliberately lost on restart. One selected validated Google account per owner.
internal sealed class GmailConnections(GmailOAuthConfiguration configuration) : IMcpCredentials<GmailIdentity>, IDisposable
{
    private readonly ConcurrentDictionary<OwnerId, Slot> _owners = new();
    private readonly HttpClient _oauth = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 };

    public GmailIdentity Connection(OwnerId owner) => Identity(owner);

    internal GmailIdentity Identity(OwnerId owner)
    {
        if (_owners.TryGetValue(owner, out var slot) && slot.Identity is { } identity)
        {
            return identity;
        }

        throw new McpAuthenticationRequiredException();
    }

    internal GmailIdentity Identity(OwnerId owner, PrincipalId principal)
    {
        var identity = Identity(owner);
        if (identity.Principal != principal)
        {
            throw new McpAuthenticationRequiredException();
        }
        return identity;
    }

    internal async Task AcceptAsync(OwnerId owner, PrincipalId principal, string sub, string email, string accessToken,
        string? refreshToken, string scopes, string? expiresIn, bool compose, Action<Action> commitIfActive,
        CancellationToken cancellationToken)
    {
        ValidateToken(accessToken);
        if (refreshToken is not null)
        {
            ValidateToken(refreshToken);
        }

        var grants = ParseScopes(scopes);
        if (!grants.Contains(GmailOAuthConfiguration.ReadScope) || !grants.Contains("openid")
            || !(grants.Contains("email") || grants.Contains("https://www.googleapis.com/auth/userinfo.email"))
            || compose && !grants.Contains(GmailOAuthConfiguration.ComposeScope))
        {
            throw new McpOperationException("Google did not grant all required Gmail and identity scopes.");
        }

        Slot slot;
        lock (_owners)
        {
            if (_owners.Count >= 128 && !_owners.ContainsKey(owner))
            {
                throw new McpOperationException("Too many Gmail connections. Restart the kernel to clear unused connections.");
            }
            slot = _owners.GetOrAdd(owner, static _ => new Slot());
        }
        var expiry = Expiry(expiresIn);
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Publication and pending-action cancellation share the same lock. Nothing can
            // publish credentials after cancellation wins the transaction.
            commitIfActive(() =>
            {
                var old = slot.Identity;
                slot.RefreshToken = refreshToken ?? (old?.Subject == sub && old.Principal == principal ? slot.RefreshToken : null);
                slot.AccessToken = accessToken;
                slot.ExpiresAt = expiry;
                slot.Identity = new GmailIdentity(sub, email, Guid.NewGuid(), grants.Contains(GmailOAuthConfiguration.ComposeScope), principal);
            });
        }
        finally { slot.Gate.Release(); }
    }

    public async Task<string> AccessTokenAsync(OwnerId owner, GmailIdentity expected, bool refresh,
        CancellationToken cancellationToken)
    {
        if (!_owners.TryGetValue(owner, out var slot))
        {
            throw new McpAuthenticationRequiredException();
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Identity != expected)
            {
                throw new McpAuthenticationRequiredException();
            }

            if (!refresh && slot.AccessToken is { } current && slot.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return current;
            }

            if (slot.RefreshToken is null) { Clear(slot); throw new McpAuthenticationRequiredException(); }
            configuration.RequireConfigured();
            using var response = await _oauth.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId,
                ["client_secret"] = configuration.ClientSecret,
                ["refresh_token"] = slot.RefreshToken,
                ["grant_type"] = "refresh_token",
            }), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    using var failure = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (failure.RootElement.TryGetProperty("error", out var error) && error.GetString() == "invalid_grant")
                    { Clear(slot); throw new McpAuthenticationRequiredException(); }
                }
                throw new McpOperationException($"Gmail token refresh failed (HTTP {(int)response.StatusCode}). Check OAuth configuration or try again later.");
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = json.RootElement;
            if (!root.TryGetProperty("token_type", out var tokenType)
                || !string.Equals(tokenType.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpOperationException("Google returned an unsupported token type.");
            }
            if (root.TryGetProperty("scope", out var scope))
            {
                var grants = ParseScopes(scope.GetString() ?? "");
                if (!grants.Contains(GmailOAuthConfiguration.ReadScope)
                    || expected.CanCompose && !grants.Contains(GmailOAuthConfiguration.ComposeScope))
                { Clear(slot); throw new McpAuthenticationRequiredException(); }
            }
            var token = root.GetProperty("access_token").GetString();
            ValidateToken(token);
            slot.AccessToken = token;
            slot.ExpiresAt = Expiry(root.GetProperty("expires_in").ToString());
            return token!;
        }
        catch (McpAuthenticationRequiredException) { throw; }
        catch (McpOperationException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new McpOperationException("Gmail token refresh is unavailable. Try again later; no new consent request was started."); }
        finally { slot.Gate.Release(); }
    }

    public async Task RejectAsync(OwnerId owner, GmailIdentity expected, CancellationToken cancellationToken)
    {
        if (!_owners.TryGetValue(owner, out var slot))
        {
            return;
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Identity == expected)
            {
                Clear(slot);
            }
        }
        finally { slot.Gate.Release(); }
    }

    private static void Clear(Slot slot) { slot.AccessToken = null; slot.RefreshToken = null; slot.Identity = null; }
    private static HashSet<string> ParseScopes(string scopes) => scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    private static DateTimeOffset Expiry(string? seconds) => double.TryParse(seconds, CultureInfo.InvariantCulture, out var value)
        && value > 0 && value <= 86400 ? DateTimeOffset.UtcNow.AddSeconds(value)
        : throw new McpOperationException("Google returned an invalid token lifetime.");
    private static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || token.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw new McpOperationException("Google returned an invalid token.");
        }
    }
    public void Dispose() { _oauth.Dispose(); _owners.Clear(); }
    private sealed class Slot
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal volatile GmailIdentity? Identity;
        internal string? AccessToken;
        internal string? RefreshToken;
        internal DateTimeOffset ExpiresAt;
    }
}

internal sealed record GmailIdentity(string Subject, string Email, Guid Revision, bool CanCompose, PrincipalId Principal);
