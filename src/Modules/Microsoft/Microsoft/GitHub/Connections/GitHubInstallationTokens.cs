using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Sdk;

namespace DigitalBrain.Microsoft.GitHub;

/// <summary>Kernel-private App authentication. Tokens are narrowed to one numeric repository and read permissions.</summary>
internal sealed class GitHubInstallationTokens : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

    public GitHubInstallationTokens() : this(null, null) { }
    internal GitHubInstallationTokens(HttpMessageHandler? handler, TimeProvider? time)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 65536 };
        _time = time ?? TimeProvider.System;
    }

    internal async Task<string> GetTokenAsync(GitHubRepositoryBinding binding, bool refresh, CancellationToken cancellationToken)
    {
        binding.Authorize(binding.Owner, binding.Principal);
        if (_slots.Count >= 32 && !_slots.ContainsKey(binding.Id))
        {
            throw new McpOperationException("The GitHub connection capacity was reached.", McpFailureKind.Capacity);
        }
        var slot = _slots.GetOrAdd(binding.Id, static _ => new Slot());
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            binding.Authorize(binding.Owner, binding.Principal);
            if (!refresh && slot.Revision == binding.Revision && slot.Token is not null && slot.Expiry > _time.GetUtcNow().AddMinutes(2))
            {
                return slot.Token;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(binding.ApiHost,
                $"app/installations/{binding.InstallationId}/access_tokens"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateAppJwt(binding, _time.GetUtcNow()));
            AddHeaders(request);
            request.Content = JsonContent.Create(new
            {
                repository_ids = new[] { binding.RepositoryId },
                permissions = new { contents = "read", pull_requests = "read", checks = "read", statuses = "read", metadata = "read", administration = "read" },
            });
            // This authentication POST is never retried; a later independent read can request a new token.
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                throw new McpOperationException("GitHub App authentication was refused. Verify the installation, repository access and App permissions.", McpFailureKind.AccessDenied);
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new McpOperationException("GitHub App authentication is temporarily unavailable.", McpFailureKind.Unavailable);
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            var json = document.RootElement;
            var token = json.GetProperty("token").GetString();
            if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsControl)
                || !json.GetProperty("expires_at").TryGetDateTimeOffset(out var expiry) || expiry <= _time.GetUtcNow().AddMinutes(2))
            {
                throw new McpOperationException("GitHub returned invalid installation credentials.", McpFailureKind.AccessDenied);
            }
            if (json.TryGetProperty("repositories", out var repositories)
                && (repositories.ValueKind != JsonValueKind.Array || repositories.GetArrayLength() != 1
                    || repositories[0].GetProperty("id").GetInt64() != binding.RepositoryId))
            {
                throw new McpOperationException("GitHub did not scope the installation token to the configured repository.", McpFailureKind.AccessDenied);
            }
            if (!json.TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Object
                || permissions.EnumerateObject().Any(static permission => permission.Value.GetString() != "read"))
            {
                throw new McpOperationException("GitHub did not grant a read-only installation token.", McpFailureKind.AccessDenied);
            }
            binding.Authorize(binding.Owner, binding.Principal);
            slot.Token = token; slot.Expiry = expiry; slot.Revision = binding.Revision;
            return token;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or CryptographicException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new McpOperationException("GitHub App authentication could not be completed. Check the configured App key and connection.", McpFailureKind.Unavailable);
        }
        finally { slot.Gate.Release(); }
    }

    internal static string CreateAppJwt(GitHubRepositoryBinding binding, DateTimeOffset now)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = binding.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(binding.PrivateKeyPem);
        var message = $"{header}.{payload}";
        return $"{message}.{Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))}";
    }

    internal async Task VerifyRepositoryAsync(GitHubRepositoryBinding binding, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(binding.ApiHost, binding.RepositoryPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(binding, false, cancellationToken).ConfigureAwait(false));
        AddHeaders(request);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new McpOperationException("The configured GitHub repository could not be verified.", McpFailureKind.AccessDenied);
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            var repo = document.RootElement;
            if (repo.GetProperty("id").GetInt64() != binding.RepositoryId
                || !string.Equals(repo.GetProperty("name").GetString(), binding.RepoName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(repo.GetProperty("owner").GetProperty("login").GetString(), binding.RepoOwner, StringComparison.OrdinalIgnoreCase))
            {
                binding.Revoke();
                throw new McpOperationException("The repository was renamed or transferred. Reauthorize the GitHub binding.", McpFailureKind.ConnectionChanged);
            }
            binding.Authorize(binding.Owner, binding.Principal);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new McpOperationException("The configured GitHub repository could not be verified.", McpFailureKind.Unavailable);
        }
    }

    internal static void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("DigitalBrain/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose()
    {
        _http.Dispose();
        foreach (var slot in _slots.Values)
        {
            slot.Token = null;
            slot.Gate.Dispose();
        }
        _slots.Clear();
    }
    private sealed class Slot
    {
        internal readonly SemaphoreSlim Gate = new(1);
        internal string? Revision;
        internal string? Token;
        internal DateTimeOffset Expiry;
    }
}
