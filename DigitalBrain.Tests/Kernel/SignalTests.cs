using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class SignalTests
{
    [Fact]
    public void Signal_Type_Equals_Name()
    {
        var signal = new Signal("X", new Dictionary<string, object?> { ["k"] = 1 });
        Assert.Equal("X", signal.Type);
        Assert.Equal("X", signal.Name);
    }

    [Fact]
    public void Signal_Props_Are_Preserved()
    {
        var props = new Dictionary<string, object?> { ["k"] = 1, ["v"] = "hello" };
        var signal = new Signal("test-event", props);
        Assert.Equal(1, signal.Props["k"]);
        Assert.Equal("hello", signal.Props["v"]);
    }

    [Fact]
    public void AskLlm_Type_Is_AskLlm()
    {
        var askLlm = new AskLlm("p", "RT", new Dictionary<string, object?>());
        Assert.Equal(nameof(AskLlm), askLlm.Type);
    }

    [Fact]
    public void AskLlm_Preserves_All_Fields()
    {
        var replyProps = new Dictionary<string, object?> { ["format"] = "markdown" };
        var askLlm = new AskLlm("What is 2+2?", "text", replyProps);
        Assert.Equal("What is 2+2?", askLlm.Prompt);
        Assert.Equal("text", askLlm.ReplyType);
        Assert.Equal("markdown", askLlm.ReplyProps["format"]);
    }

    [Fact]
    public void Signal_Orleans_Serialization_RoundTrip()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b => b.AddAssembly(typeof(Synapse).Assembly));
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer>();

        var original = new Signal("user.clicked", new Dictionary<string, object?> { ["buttonId"] = "submit", ["count"] = 3 });
        var bytes = serializer.SerializeToArray(original);
        var restored = serializer.Deserialize<Signal>(bytes);

        Assert.Equal("user.clicked", restored.Type);
        Assert.Equal(original.Type, restored.Type);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Props["buttonId"], restored.Props["buttonId"]);
        Assert.Equal(3, restored.Props["count"]);
    }

    [Fact]
    public void AskLlm_Orleans_Serialization_RoundTrip()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b => b.AddAssembly(typeof(Synapse).Assembly));
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer>();

        var original = new AskLlm("Summarize this", "structured", new Dictionary<string, object?> { ["schema"] = "json" });
        var bytes = serializer.SerializeToArray(original);
        var restored = serializer.Deserialize<AskLlm>(bytes);

        Assert.Equal(nameof(AskLlm), restored.Type);
        Assert.Equal(original.Prompt, restored.Prompt);
        Assert.Equal(original.ReplyType, restored.ReplyType);
        Assert.Equal(original.ReplyProps["schema"], restored.ReplyProps["schema"]);
    }
}
