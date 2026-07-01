using DigitalBrain.Kernel.Foundry;

namespace DigitalBrain.Tests.Foundry;

public class CapabilityGateTests
{
    private static IReadOnlyList<string> Inspect(string source)
    {
        var compilation = FoundryCompilation.Create("gatecheck", source, FoundryCompilation.DefaultReferences());
        return CapabilityGate.FindViolations(compilation);
    }

    [Fact]
    public void AllowsBenignArithmetic()
    {
        var violations = Inspect("public static class M { public static object Run(System.Collections.Generic.IReadOnlyDictionary<string,object?> input) => 1 + 1; }");
        Assert.Empty(violations);
    }

    [Fact]
    public void FlagsProcessStart()
    {
        var violations = Inspect("public static class M { public static object Run(System.Collections.Generic.IReadOnlyDictionary<string,object?> input) { System.Diagnostics.Process.Start(\"calc\"); return null; } }");
        Assert.Contains(violations, v => v.Contains("System.Diagnostics.Process"));
    }
}
