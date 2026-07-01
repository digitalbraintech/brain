using System.Xml.Linq;

namespace DigitalBrain.Tests.E2E;

public class RenderRunSettingsTests
{
    private static string RunSettingsPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "e2e.runsettings"));

    [Fact]
    public void Runsettings_file_exists_at_the_repo_root()
    {
        Assert.True(File.Exists(RunSettingsPath), $"Expected {RunSettingsPath} to exist.");
    }

    [Fact]
    public void Runsettings_declares_the_render_loop_opt_in_and_fast_timeouts()
    {
        var doc = XDocument.Load(RunSettingsPath);
        var envVars = doc.Root?.Element("RunConfiguration")?.Element("EnvironmentVariables");

        Assert.NotNull(envVars);
        Assert.Equal("true", envVars!.Elements().FirstOrDefault(e => e.Name == "RUN_FLUTTER_E2E")?.Value);
        Assert.Equal("1", envVars.Elements().FirstOrDefault(e => e.Name == "FAST_UI_E2E")?.Value);
    }
}
