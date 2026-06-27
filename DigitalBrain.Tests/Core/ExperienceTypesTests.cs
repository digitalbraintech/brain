using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Core;

public class ExperienceTypesTests
{
    [Fact]
    public void ExperienceStep_carries_event_and_args_and_is_a_synapse()
    {
        var step = new ExperienceStep(
            Pack: "travel",
            ExperienceId: "plan-trip",
            EventName: "flight.selected",
            Args: new Dictionary<string, string> { ["flightId"] = "FL-001" });

        Assert.IsAssignableFrom<Synapse>(step);
        Assert.Equal(nameof(ExperienceStep), step.Type);
        Assert.Equal("flight.selected", step.EventName);
        Assert.Equal("FL-001", step.Args["flightId"]);
    }

    [Fact]
    public void Experience_holds_entry_action()
    {
        var entry = new Dictionary<string, object?> { ["synapseType"] = nameof(ExperienceStep) };
        var exp = new Experience("plan-trip", "Plan a trip", "experience", "Plan a multi-stop trip.", entry);

        Assert.Equal("plan-trip", exp.ExperienceId);
        Assert.Equal(nameof(ExperienceStep), exp.EntryAction["synapseType"]);
    }
}
