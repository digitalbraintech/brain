using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Tests.E2E;

// Locates the prebuilt Flutter web bundle and gates the render E2E so it only runs deliberately.
public static class E2EPrerequisites
{
    public static string WebBundleDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "app", "build", "web"));

    public static string AppDir => Path.GetFullPath(Path.Combine(WebBundleDir, "..", ".."));

    public static bool OptedIn =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_FLUTTER_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    public static bool WebBundlePresent => File.Exists(Path.Combine(WebBundleDir, "index.html"));

    private static string FingerprintFile(string webBundleDir) => Path.Combine(webBundleDir, ".source-fingerprint");

    // Metadata fingerprint (path + last-write-time + length), not a content hash — fast enough to run on
    // every test invocation and sufficient because editors update mtime on save.
    internal static string ComputeSourceFingerprint(string appDir)
    {
        var sb = new StringBuilder();
        var libDir = Path.Combine(appDir, "lib");
        var dartFiles = Directory.Exists(libDir)
            ? Directory.EnumerateFiles(libDir, "*.dart", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

        foreach (var file in dartFiles)
        {
            var info = new FileInfo(file);
            sb.Append(Path.GetRelativePath(appDir, file)).Append('|')
              .Append(info.LastWriteTimeUtc.Ticks).Append('|')
              .Append(info.Length).Append(';');
        }

        var pubspecLock = Path.Combine(appDir, "pubspec.lock");
        if (File.Exists(pubspecLock))
        {
            var info = new FileInfo(pubspecLock);
            sb.Append("pubspec.lock|").Append(info.LastWriteTimeUtc.Ticks).Append('|').Append(info.Length);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    internal static bool IsWebBundleStale(string appDir, string webBundleDir)
    {
        var fingerprintPath = FingerprintFile(webBundleDir);
        if (!File.Exists(fingerprintPath)) return true;

        var stored = File.ReadAllText(fingerprintPath).Trim();
        return !string.Equals(stored, ComputeSourceFingerprint(appDir), StringComparison.Ordinal);
    }

    // Best-effort: builds the Flutter web bundle when missing or stale. Never throws — a missing Flutter
    // SDK or a build failure just leaves the existing (possibly absent) bundle in place, and
    // RequireRenderE2E()'s existing Skip.IfNot(WebBundlePresent, ...) reports the actionable message.
    public static void EnsureWebBundleFresh()
    {
        if (WebBundlePresent && !IsWebBundleStale(AppDir, WebBundleDir))
            return;

        if (!TryRunFlutterBuild(AppDir, out var output))
        {
            Console.WriteLine($"[E2EPrerequisites] Auto-build of the Flutter web bundle did not complete; " +
                               $"falling back to the existing bundle if present.\n{output}");
            return;
        }

        Directory.CreateDirectory(WebBundleDir);
        File.WriteAllText(FingerprintFile(WebBundleDir), ComputeSourceFingerprint(AppDir));
    }

    private static bool TryRunFlutterBuild(string appDir, out string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "flutter.bat" : "flutter",
            Arguments = "build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true",
            WorkingDirectory = appDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            output = $"Could not start flutter: {ex.Message}";
            return false;
        }

        if (process is null)
        {
            output = "Could not start the flutter process.";
            return false;
        }

        // Start draining both streams before blocking on exit so a chatty build can't deadlock on a full pipe buffer.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            output = "flutter build web timed out after 5 minutes.";
            return false;
        }

        output = stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult();
        return process.ExitCode == 0;
    }

    public static void RequireRenderE2E()
    {
        Skip.IfNot(OptedIn, "Set RUN_FLUTTER_E2E=true to run the Flutter render E2E.");
        Skip.IfNot(WebBundlePresent,
            $"Flutter web bundle not found at {WebBundleDir}. Build it first: " +
            "cd app && flutter build web --release --no-tree-shake-icons --dart-define=DIGITALBRAIN_E2E=true " +
            "(--no-tree-shake-icons is required: the app uses non-constant IconData).");
    }
}
