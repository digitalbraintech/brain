using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Scripting.Startup;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class StartupCompositionTests
{
    [Fact]
    public async Task Default_startup_bundle_compiles_and_reaches_neuron_wiring()
    {
        var script = await StartupScript.ReadAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "start.cs"), TestContext.Current.CancellationToken);
        script = script with { Input = new DigitalBrainActivated(new OwnerId("alice")) };
        var result = await new CSharpStartupScriptRunner().RunAsync(script, new FakeDigitalBrain("alice"), TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        Assert.False(result.IsSuccess);
        Assert.Contains("not supported", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Loaded_scripts_execute_from_the_hashed_snapshot_in_order()
    {
        var directory = Directory.CreateTempSubdirectory("brain-script-bundle-");
        try
        {
            var path = Path.Combine(directory.FullName, "start.cs");
            var first = Path.Combine(directory.FullName, "activities.cs");
            await File.WriteAllTextAsync(first, "string Activities() => \"activities\";", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "ui.cs"), "string UI() => \"ui\";", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(path, "#load \"activities.cs\"\n#load \"ui.cs\"\nreturn Activities() + \"/\" + UI();", TestContext.Current.CancellationToken);
            var script = await StartupScript.ReadAsync(path, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(first, "string Activities() => \"changed\";", TestContext.Current.CancellationToken);
            var result = await new CSharpStartupScriptRunner().RunAsync(script, new FakeDigitalBrain("alice"), TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess, result.Summary + string.Join("\n", result.Diagnostics));
            Assert.Equal("activities/ui", result.Summary);
            var changed = await StartupScript.ReadAsync(path, TestContext.Current.CancellationToken);
            Assert.NotEqual(script.Sha256, changed.Sha256);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task Activation_is_available_as_a_typed_script_input()
    {
        var script = StartupScript.FromSource("start.cs", "return ((DigitalBrainActivated)Input!).Owner.Value;")
            with { Input = new DigitalBrainActivated(new OwnerId("alice")) };
        var result = await new CSharpStartupScriptRunner().RunAsync(script, new FakeDigitalBrain("alice"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Summary + string.Join("\n", result.Diagnostics));
        Assert.Equal("alice", result.Summary);
    }

    [Fact]
    public async Task Missing_loaded_script_fails_before_recording_a_version()
    {
        var directory = Directory.CreateTempSubdirectory("brain-script-bundle-");
        try
        {
            var path = Path.Combine(directory.FullName, "start.cs");
            await File.WriteAllTextAsync(path, "#load \"missing.cs\"", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<FileNotFoundException>(() => StartupScript.ReadAsync(path, TestContext.Current.CancellationToken));
        }
        finally { directory.Delete(true); }
    }
}
