using DigitalBrain.Silo.Foundry;
using Xunit;

namespace DigitalBrain.Tests.Foundry;

public class InProcessAlcExecutorTests
{
    private readonly InProcessAlcExecutor _executor = new();

    [Fact]
    public void RunsAndReturnsValue()
    {
        const string source = @"
public static class Module
{
    public static object Run(System.Collections.Generic.IReadOnlyDictionary<string, object?> input)
        => ""hello-foundry"";
}";
        var result = _executor.Execute(source, "Run");
        Assert.True(result.Success, result.Error);
        Assert.Contains("hello-foundry", result.Output);
    }

    [Fact]
    public void ReportsCompileError()
    {
        var result = _executor.Execute("public static class Module { this is not C# }", "Run");
        Assert.False(result.Success);
        Assert.Contains("compile", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsBannedSymbol()
    {
        const string source = @"
public static class Module
{
    public static object Run(System.Collections.Generic.IReadOnlyDictionary<string, object?> input)
    {
        System.Diagnostics.Process.Start(""calc"");
        return null;
    }
}";
        var result = _executor.Execute(source, "Run");
        Assert.False(result.Success);
        Assert.Contains("capability", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }
}
