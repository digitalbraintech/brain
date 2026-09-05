using DigitalBrain.Abstractions.Identity;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ComposerIdentityTests
{
    [Fact]
    public void ForComposer_UsesUsermessagesGrainTypeAndInboxName()
    {
        var owner = new OwnerId("alice");
        var id = NeuronId.For<IComposer>(owner, IComposer.DefaultInstanceName);

        Assert.Equal(IComposer.GrainTypeName, id.Type);
        Assert.Equal("inbox", id.Name);
        Assert.Equal(new NeuronId("usermessages", owner, "inbox"), id);
        Assert.Equal($"usermessages:{owner.Value}/inbox", id.ToString());
    }
}
