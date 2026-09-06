using DigitalBrain.Scripting.Startup;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Kernel_does_not_reference_scripting()
    {
        var references = typeof(DigitalBrain.Core.Neuron).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name == "DigitalBrain.Scripting");
    }

    [Fact]
    public async Task Copied_start_script_requires_activation_without_activating_the_brain_itself()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "start.cs");
        var script = await StartupScript.ReadAsync(scriptPath, TestContext.Current.CancellationToken);
        var brain = new FakeDigitalBrain("alice");
        var result = await new CSharpStartupScriptRunner().RunAsync(script, brain, TestContext.Current.CancellationToken);
        Assert.Empty(result.Diagnostics);
        Assert.False(result.IsSuccess);
        Assert.Contains("requires this brain's DigitalBrainActivated", result.Summary, StringComparison.Ordinal);
        Assert.Equal(0, brain.ActivateCallCount);
    }
}
