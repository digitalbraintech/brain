using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.E2E.Tests;

public sealed class BehaviorStudioProjectionTests
{
    [Fact]
    public void Studio_keeps_invalid_draft_separate_from_valid_active_program()
    {
        var principal = HttpActor.Current.PrincipalId;
        var id = NeuronId.For<IBehavior>(new OwnerId("owner"), PrincipalPartition.InstanceName(principal, "review"));
        var active = new BehaviorProgram(Guid.NewGuid(), "return Input;", ["Note"], ["Note"], BehaviorValidation.Valid, [], DateTimeOffset.UtcNow,
            BehaviorInputPolicy.LatestPerSubject | BehaviorInputPolicy.OncePerVersion);
        var draft = active with { Revision = Guid.NewGuid(), Source = "invalid C#", Validation = BehaviorValidation.Invalid, Diagnostics = ["CS1002: ; expected"] };

        var view = BehaviorStudioHttpMaps.Project(new(id, principal, draft, active, true, 4, 2, "Draft compilation failed."));

        Assert.Equal("review", view.Name);
        Assert.Equal("Invalid", view.Draft!.Validation);
        Assert.Equal("Valid", view.Active!.Validation);
        Assert.Equal("return Input;", view.Active.Source);
        Assert.Equal(2, view.PendingCount);
        Assert.True(view.Enabled);
        Assert.Equal(BehaviorInputPolicy.LatestPerSubject | BehaviorInputPolicy.OncePerVersion, view.Active.InputPolicy);
    }
}
