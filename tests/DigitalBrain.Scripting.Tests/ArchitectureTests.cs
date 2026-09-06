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
    public async Task Copied_start_script_reports_the_owner_without_activating_the_brain()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "start.cs");
        var script = await StartupScript.ReadAsync(scriptPath, TestContext.Current.CancellationToken);
        Assert.Contains("SubscribeToAsync<IComposer, UserMessaged>", script.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Get<IChat>", script.Source, StringComparison.Ordinal);
        var brain = new FakeDigitalBrain("alice");
        Assert.Equal(0, brain.ActivateCallCount);
    }
}
