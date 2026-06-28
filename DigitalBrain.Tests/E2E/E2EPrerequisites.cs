using System;
using System.IO;
using Xunit;

namespace DigitalBrain.Tests.E2E;

// Locates the prebuilt Flutter web bundle and gates the render E2E so it only runs deliberately.
public static class E2EPrerequisites
{
    public static string WebBundleDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "app", "build", "web"));

    public static bool OptedIn =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_FLUTTER_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    public static bool WebBundlePresent => File.Exists(Path.Combine(WebBundleDir, "index.html"));

    public static void RequireRenderE2E()
    {
        Skip.IfNot(OptedIn, "Set RUN_FLUTTER_E2E=true to run the Flutter render E2E.");
        Skip.IfNot(WebBundlePresent,
            $"Flutter web bundle not found at {WebBundleDir}. Build it first: " +
            "cd app && flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true " +
            "(--no-tree-shake-icons is required: the app uses non-constant IconData).");
    }
}
