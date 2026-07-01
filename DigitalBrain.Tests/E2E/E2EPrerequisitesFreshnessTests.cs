namespace DigitalBrain.Tests.E2E;

public class E2EPrerequisitesFreshnessTests
{
    [Fact]
    public void Fingerprint_is_stable_for_unchanged_files()
    {
        var appDir = CreateTempAppDir();
        try
        {
            var first = E2EPrerequisites.ComputeSourceFingerprint(appDir);
            var second = E2EPrerequisites.ComputeSourceFingerprint(appDir);

            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public void Fingerprint_changes_when_a_dart_file_is_touched()
    {
        var appDir = CreateTempAppDir();
        try
        {
            var before = E2EPrerequisites.ComputeSourceFingerprint(appDir);

            var mainDart = Path.Combine(appDir, "lib", "main.dart");
            File.WriteAllText(mainDart, "// changed");
            File.SetLastWriteTimeUtc(mainDart, DateTime.UtcNow.AddMinutes(1));

            var after = E2EPrerequisites.ComputeSourceFingerprint(appDir);

            Assert.NotEqual(before, after);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public void Bundle_is_stale_when_no_fingerprint_file_exists()
    {
        var appDir = CreateTempAppDir();
        var webBundleDir = Path.Combine(appDir, "build", "web");
        Directory.CreateDirectory(webBundleDir);
        try
        {
            Assert.True(E2EPrerequisites.IsWebBundleStale(appDir, webBundleDir));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public void Bundle_is_not_stale_when_stored_fingerprint_matches_current_source()
    {
        var appDir = CreateTempAppDir();
        var webBundleDir = Path.Combine(appDir, "build", "web");
        Directory.CreateDirectory(webBundleDir);
        File.WriteAllText(
            Path.Combine(webBundleDir, ".source-fingerprint"),
            E2EPrerequisites.ComputeSourceFingerprint(appDir));
        try
        {
            Assert.False(E2EPrerequisites.IsWebBundleStale(appDir, webBundleDir));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    private static string CreateTempAppDir()
    {
        var appDir = Path.Combine(Path.GetTempPath(), "dbt-freshness-" + Path.GetRandomFileName());
        var libDir = Path.Combine(appDir, "lib");
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(libDir, "main.dart"), "void main() {}");
        File.WriteAllText(Path.Combine(appDir, "pubspec.lock"), "packages: {}");
        return appDir;
    }
}
