using DigitalBrain.Silo;
using Xunit;

namespace DigitalBrain.Tests.Sandbox;

public class OutOfProcessSandboxTests
{
    private readonly OutOfProcessSandbox _sandbox = new();

    [Fact]
    public void Reports_OutOfProcess_Tier() => Assert.Equal(SandboxTier.OutOfProcess, _sandbox.Tier);

    [Fact]
    public async Task Runs_Pack_In_A_Separate_Process()
    {
        // The pack prints its own process id — a different process than the test host proves real isolation.
        const string source = """
            public static class Pack
            {
                public static void Main() => System.Console.WriteLine("SANDBOXED:" + System.Environment.ProcessId);
            }
            """;

        var result = await _sandbox.RunAsync(source);

        Assert.True(result.Success, result.Error);
        Assert.Contains("SANDBOXED:", result.Output);
        Assert.DoesNotContain($"SANDBOXED:{System.Environment.ProcessId}\n", result.Output); // not the test process
    }

    [Fact]
    public async Task Capability_Gate_Rejects_Process_Launch_Before_Running()
    {
        const string source = """
            public static class Pack
            {
                public static void Main() => System.Diagnostics.Process.Start("calc");
            }
            """;

        var result = await _sandbox.RunAsync(source);

        Assert.False(result.Success);
        Assert.Contains("capability gate", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compile_Error_Is_Surfaced_Not_Run()
    {
        var result = await _sandbox.RunAsync("this is not valid c#");
        Assert.False(result.Success);
        Assert.Contains("compile error", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }
}
