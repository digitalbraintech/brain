using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Kernel.Auth;

/// <summary>
/// Dev-stand gate: a single owner credential carried as HTTP Basic on every request.
/// Inactive unless both <see cref="UsernameConfigurationKey"/> and
/// <see cref="PasswordConfigurationKey"/> are configured, so local dev, Aspire, and the
/// E2E fixture stay open exactly as they are today.
/// </summary>
internal static class BasicAuthGate
{
    public const string UsernameConfigurationKey = "DigitalBrain:Auth:Username";
    public const string PasswordConfigurationKey = "DigitalBrain:Auth:Password";
    public const string CheckPath = "/auth/check";

    private const string BasicScheme = "Basic";

    // Generous for "user:pass" while keeping the decode buffer stack-safe.
    private const int MaxEncodedCredentialChars = 1024;

    // Probed by the shell's login screen and by container probes; never gated.
    private static readonly string[] AnonymousPaths = ["/health", "/alive"];

    public static WebApplication UseBasicAuthGate(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var credential = BasicAuthCredential.FromConfiguration(app.Configuration);
        if (credential is null)
        {
            // Unset credentials mean an open kernel — the local and test posture.
            return app;
        }

        app.Use(async (context, next) =>
        {
            if (IsAnonymous(context.Request) || credential.Matches(context.Request.Headers.Authorization))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            // No WWW-Authenticate: the browser's native Basic prompt would race the
            // Flutter login screen and cannot be dismissed from inside the app.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        });

        // Inside the gate, so reaching it at all proves the credential is good.
        app.MapGet(CheckPath, static () => Results.NoContent());

        return app;
    }

    private static bool IsAnonymous(HttpRequest request)
    {
        // Preflight carries no Authorization header by design; CORS answers it.
        if (HttpMethods.IsOptions(request.Method))
        {
            return true;
        }

        foreach (var path in AnonymousPaths)
        {
            if (request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal sealed class BasicAuthCredential
    {
        private readonly byte[] _expected;

        private BasicAuthCredential(string username, string password)
            => _expected = Encoding.UTF8.GetBytes($"{username}:{password}");

        public static BasicAuthCredential? FromConfiguration(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var username = configuration[UsernameConfigurationKey];
            var password = configuration[PasswordConfigurationKey];

            return string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)
                ? null
                : new BasicAuthCredential(username, password);
        }

        public bool Matches(string? authorizationHeader)
        {
            if (string.IsNullOrEmpty(authorizationHeader))
            {
                return false;
            }

            var separator = authorizationHeader.IndexOf(' ');
            if (separator <= 0
                || !authorizationHeader.AsSpan(0, separator).Equals(BasicScheme, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var encoded = authorizationHeader.AsSpan(separator + 1).Trim();

            // Bounded before the stackalloc: the header length is attacker-controlled.
            if (encoded.Length is 0 or > MaxEncodedCredentialChars)
            {
                return false;
            }

            Span<byte> presented = stackalloc byte[MaxEncodedCredentialChars / 4 * 3];
            if (!Convert.TryFromBase64Chars(encoded, presented, out var written))
            {
                return false;
            }

            // FixedTimeEquals over the whole "user:pass" pair: no length or prefix leak.
            return CryptographicOperations.FixedTimeEquals(presented[..written], _expected);
        }
    }
}
