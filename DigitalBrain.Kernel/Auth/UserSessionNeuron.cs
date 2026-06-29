using System.Security.Cryptography;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.user-session.v1")]
public sealed class UserSessionNeuron : Neuron, IUserSessionNeuron
{
    private const int PasswordHashIterations = 100_000;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    public UserSessionNeuron(ILogger<UserSessionNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);

        if (!ActiveSessions().Any())
        {
            Broadcast(LoginSurface());
        }
    }

    public async Task HandleAsync(LoginRequest request)
    {
        var username = NormalizeUsername(request.Username);
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? "flutter" : request.ClientId.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(request.Password))
        {
            await RejectAsync(username, "username and password are required", clientId);
            return;
        }

        // Dev-only convenience: the seeded credentials always authenticate (provisioned on first use,
        // password-bypassed if an account already exists) so a fresh checkout can sign in without setup.
        // Off outside Development, so the deployed app is unaffected.
        var isDevCredentials = DevAuthEnabled() && DevAuth.Matches(username, request.Password);

        var users = RegisteredUsers().ToList();
        var user = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            if (!isDevCredentials && (!AllowFirstUserProvisioning() || users.Count > 0))
            {
                await RejectAsync(username, "invalid username or password", clientId);
                return;
            }

            user = CreateLocalUser(username, request.Password);
            await FireAsync(user);
        }
        else if (!isDevCredentials && !VerifyPassword(request.Password, user.PasswordSaltBase64, user.PasswordHashBase64))
        {
            await RejectAsync(username, "invalid username or password", clientId);
            return;
        }

        var sessionId = "session-" + Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);

        await FireAsync(new LoginSucceeded(user.UserId, sessionId, user.DisplayName, user.Roles, clientId));
        await FireAsync(new UserSessionCreated(user.UserId, sessionId, expiresAt, clientId));

        BroadcastSignedIn(user, sessionId, clientId);

        // Reuse the existing product-surface startup path after a real session exists.
        await GrainFactory.GetGrain<IAspireNeuron>("aspire-main").FireAsync(new StartDistributedApp("digitalbrain"));
        await BroadcastProductHomeAsync(user, sessionId);
    }

    public async Task HandleAsync(LogoutRequest request)
    {
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? "flutter" : request.ClientId.Trim();

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            await FireAsync(new UserSessionEnded(request.SessionId, clientId));
        }

        Broadcast(LoginSurface(clientId: clientId));
    }

    public Task<UserSessionState?> GetSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.FromResult<UserSessionState?>(null);
        }

        var ended = OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<UserSessionEnded>()
            .Any(e => string.Equals(e.SessionId, sessionId, StringComparison.Ordinal));
        if (ended)
        {
            return Task.FromResult<UserSessionState?>(null);
        }

        var created = OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<UserSessionCreated>()
            .DistinctBy(s => s.SynapseId)
            .LastOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        if (created is null || created.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Task.FromResult<UserSessionState?>(null);
        }

        var login = OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<LoginSucceeded>()
            .DistinctBy(s => s.SynapseId)
            .LastOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));

        return Task.FromResult<UserSessionState?>(new UserSessionState(
            created.UserId,
            created.SessionId,
            login?.DisplayName ?? created.UserId.Value,
            login?.Roles ?? Array.Empty<string>(),
            created.ExpiresAt,
            Active: true));
    }

    public Task<UiSurface> BuildLoginSurfaceAsync(string? clientId = null) =>
        Task.FromResult(LoginSurface(clientId: clientId));

    // In Development the login surface is pre-filled with seeded dev credentials that always authenticate
    // (see HandleAsync). Disabled outside Development unless DigitalBrain:Auth:DevAutoLogin is set explicitly.
    private UiSurface LoginSurface(string? error = null, string? clientId = null)
    {
        var resolvedClientId = string.IsNullOrWhiteSpace(clientId) ? "flutter" : clientId;
        return DevAuthEnabled()
            ? UiSurfaceSamples.Login(error, resolvedClientId, DevAuth.Username, DevAuth.Password)
            : UiSurfaceSamples.Login(error, resolvedClientId);
    }

    private bool DevAuthEnabled() =>
        DevAuth.Enabled(ServiceProvider.GetService<IConfiguration>(), ServiceProvider.GetService<IHostEnvironment>());

    private async Task BroadcastProductHomeAsync(LocalUserRegistered user, string sessionId)
    {
        var userId = user.UserId.Value;
        var taskEvents = OutgoingJournal.Concat(IncomingJournal).ToList();
        var surfaces = new[]
        {
            BuildSignedInShellSurface(user, sessionId),
            UiSurfaceLiveData.InstalledBundlesFromPacks(MarketplaceSeeds.LocalUiPacks, Array.Empty<NeuroPack>(), userId, sessionId),
            UiSurfaceLiveData.MarketplaceListFromPacks(MarketplaceSeeds.LocalUiPacks, Array.Empty<NeuroPack>(), userId, sessionId),
            UiSurfaceLiveData.TaskManagerFromTasks(taskEvents, userId: userId, sessionId: sessionId)
        };

        foreach (var surface in surfaces)
        {
            await FireAsync(surface);
            Broadcast(surface);
        }
    }

    private UiSurface BuildSignedInShellSurface(LocalUserRegistered user, string sessionId)
    {
        var menuItems = new[]
        {
            MenuItem("Installed", UiSurfaceKinds.InstalledBundles),
            MenuItem("Marketplace", UiSurfaceKinds.MarketplaceList),
            MenuItem("Tasks", UiSurfaceKinds.TaskManager),
            MenuItem("INO Chat", "chat"),
            new UiWidgetTree(NeuronUiKit.Divider, new Dictionary<string, object?>()),
            new UiWidgetTree(NeuronUiKit.MenuItem, new Dictionary<string, object?>
            {
                ["label"] = "Sign Out",
                ["action"] = UiSurfaceSamples.SynapseAction(
                    "logout",
                    "Sign Out",
                    nameof(LogoutRequest),
                    new Dictionary<string, object?>
                    {
                        ["sessionId"] = sessionId,
                        ["clientId"] = "flutter"
                    })
            })
        };

        var tree = new UiWidgetTree(
            NeuronUiKit.Scaffold,
            new Dictionary<string, object?>
            {
                ["title"] = "DigitalBrain",
                ["activeContent"] = UiSurfaceKinds.InstalledBundles,
                ["userId"] = user.UserId.Value,
                ["sessionId"] = sessionId
            },
            new List<UiWidgetTree>
            {
                new(NeuronUiKit.Header, new Dictionary<string, object?>
                {
                    ["title"] = "DigitalBrain",
                    ["subtitle"] = user.DisplayName
                }),
                new("forui:sidebar", new Dictionary<string, object?> { ["title"] = user.DisplayName }, menuItems),
                new("content", new Dictionary<string, object?>
                {
                    ["defaultView"] = UiSurfaceKinds.InstalledBundles
                })
            });

        return new UiSurface(UiSurface.WidgetTreeKind, new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.SurfaceId] = "surface.shell." + user.UserId.Value,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "DigitalBrain",
            [UiSurfaceKeys.Priority] = 100,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["userId"] = user.UserId.Value,
            ["displayName"] = user.DisplayName,
            ["sessionId"] = sessionId
        });
    }

    private static UiWidgetTree MenuItem(string label, string targetSurfaceKind) =>
        new(NeuronUiKit.MenuItem, new Dictionary<string, object?>
        {
            ["label"] = label,
            ["targetSurfaceKind"] = targetSurfaceKind
        });

    private async Task RejectAsync(string username, string reason, string clientId)
    {
        await FireAsync(new LoginFailed(username, reason, clientId));
        Broadcast(LoginSurface(reason, clientId));
    }

    private void BroadcastSignedIn(LocalUserRegistered user, string sessionId, string clientId)
    {
        var surface = new UiSurface("session-status", new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "surface.session." + clientId,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Signed In",
            [UiSurfaceKeys.Priority] = 90,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Compact,
            ["userId"] = user.UserId.Value,
            ["displayName"] = user.DisplayName,
            ["sessionId"] = sessionId,
            ["status"] = "signed-in",
            ["body"] = $"Signed in as {user.DisplayName}"
        });

        Broadcast(surface);
    }

    private void Broadcast(UiSurface surface)
    {
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        bus?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
    }

    private IEnumerable<UserSessionCreated> ActiveSessions()
    {
        var ended = OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<UserSessionEnded>()
            .Select(e => e.SessionId)
            .ToHashSet(StringComparer.Ordinal);

        return OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<UserSessionCreated>()
            .DistinctBy(s => s.SynapseId)
            .Where(s => s.ExpiresAt > DateTimeOffset.UtcNow && !ended.Contains(s.SessionId));
    }

    private IEnumerable<LocalUserRegistered> RegisteredUsers() =>
        OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<LocalUserRegistered>()
            .DistinctBy(s => s.SynapseId)
            .GroupBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last());

    private bool AllowFirstUserProvisioning()
    {
        var config = ServiceProvider.GetService<IConfiguration>();
        return config?.GetValue("DigitalBrain:Auth:AllowFirstUserProvisioning", true) ?? true;
    }

    private static LocalUserRegistered CreateLocalUser(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt);
        var roles = new[] { "admin", "user" };
        var display = string.Join(" ", username
            .Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        if (string.IsNullOrWhiteSpace(display))
        {
            display = username;
        }

        return new LocalUserRegistered(
            new UserId(username),
            username,
            display,
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            roles);
    }

    private static bool VerifyPassword(string password, string saltBase64, string expectedHashBase64)
    {
        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(expectedHashBase64);
            var actual = HashPassword(password, salt);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordHashIterations,
            HashAlgorithmName.SHA256,
            32);

    private static string NormalizeUsername(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
