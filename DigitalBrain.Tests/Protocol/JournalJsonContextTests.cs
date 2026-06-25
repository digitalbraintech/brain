using System.Reflection;
using System.Text.Json;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Protocol;

public class JournalJsonContextTests
{
    [Fact]
    public void ContextCoversEverySynapseSubtype()
    {
        var synapseTypes = typeof(Synapse).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Synapse).IsAssignableFrom(t))
            .ToList();

        var ctx = DigitalBrain.Silo.JournalJsonContext.Default;
        var missing = synapseTypes
            .Where(t => ctx.GetTypeInfo(t) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0, "JournalJsonContext missing: " + string.Join(", ", missing));
    }

    [Fact]
    public void SynapseId_And_Causation_RoundTrip_Through_Journal_Json()
    {
        var original = new DemoMessageSynapse("hello") { CausationId = "cause-123" };

        var json = JsonSerializer.Serialize(original, DigitalBrain.Silo.JournalJsonContext.Default.DemoMessageSynapse);
        var restored = JsonSerializer.Deserialize(json, DigitalBrain.Silo.JournalJsonContext.Default.DemoMessageSynapse)!;

        Assert.Equal(original.SynapseId, restored.SynapseId);
        Assert.Equal("cause-123", restored.CausationId);
        Assert.Equal("hello", restored.Text);
    }
}

