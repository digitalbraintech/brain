using System.Reflection;

namespace DigitalBrain.Tests.E2E;

public static class TravelPackSource
{
    public static string Read()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("TravelPack.cs")
            ?? throw new InvalidOperationException("Embedded TravelPack.cs not found; check the EmbeddedResource LogicalName.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
