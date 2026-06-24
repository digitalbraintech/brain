using DigitalBrain.Silo.Foundry;
using Xunit;

namespace DigitalBrain.Tests.Foundry;

public class PackAlcEmbodierTests
{
    private readonly PackAlcEmbodier _embodier = new();

    [Fact]
    public void Embodies_Compiled_Pack_Runs_It_Then_Unloads()
    {
        const string code = """
            public sealed class UpperPack : DigitalBrain.Protocol.IPackBehavior
            {
                public string Respond(string input) => input.ToUpperInvariant();
            }
            """;

        using var pack = _embodier.Embody("UpperPack", code);
        Assert.Equal("UpperPack", pack.PackName);
        Assert.Equal("HELLO", pack.Respond("hello"));
        // Dispose at end of scope unloads the collectible ALC without throwing.
    }

    [Fact]
    public void Rejects_Code_That_Does_Not_Compile()
        => Assert.Throws<PackEmbodimentException>(() => _embodier.Embody("Bad", "this is not c#"));

    [Fact]
    public void Rejects_Pack_Without_IPackBehavior()
        => Assert.Throws<PackEmbodimentException>(() => _embodier.Embody("NoBehavior", "public class Plain { }"));

    [Fact]
    public void CapabilityGate_Rejects_Process_Launch()
    {
        const string code = """
            public sealed class EvilPack : DigitalBrain.Protocol.IPackBehavior
            {
                public string Respond(string input)
                {
                    System.Diagnostics.Process.Start("calc");
                    return "x";
                }
            }
            """;

        var ex = Assert.Throws<PackEmbodimentException>(() => _embodier.Embody("EvilPack", code));
        Assert.Contains("capability gate", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}
