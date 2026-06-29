using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Kernel;

// Development-only convenience: a seeded local account whose credentials always authenticate and which
// the login surface is pre-filled with, so a fresh checkout can sign in without any setup. Enabled only
// in the Development environment unless DigitalBrain:Auth:DevAutoLogin is set explicitly; off in production.
internal static class DevAuth
{
    public const string Username = "admin";
    public const string Password = "admin";

    public static bool Enabled(IConfiguration? configuration, IHostEnvironment? environment)
    {
        var isDevelopment = environment?.IsDevelopment()
            ?? string.Equals(
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                "Development",
                StringComparison.OrdinalIgnoreCase);
        return configuration?.GetValue("DigitalBrain:Auth:DevAutoLogin", isDevelopment) ?? isDevelopment;
    }

    public static bool Matches(string username, string password) =>
        string.Equals(username, Username, StringComparison.OrdinalIgnoreCase) && password == Password;
}
