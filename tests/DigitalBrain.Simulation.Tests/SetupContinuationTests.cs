using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.Product.Interactions;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SetupContinuationTests
{
    [Fact]
    public void Only_the_exact_bounded_setup_can_resume_after_an_interrupted_turn()
    {
        var turn = new DurableTurnRecord(Guid.NewGuid(), Guid.NewGuid(), "Connect the saved review to this repository",
            new(new PrincipalId(Guid.NewGuid()), "owner"), ChatTurnStatus.Running, 1,
            AllowedToolNames: ["connect_github_repository"], CompletedUserActionId: "accepted-action",
            SetupContinuation: new("connect_github_repository", "https://github.com/intochat/digitalbrain", "review", Guid.NewGuid()));
        Assert.True(UI.Chat.CanRecoverSetupTurn(turn));
        Assert.True(UI.Chat.CanRecoverSetupTurn(turn with { SetupRecoveryAttempts = 2 }));
        Assert.False(UI.Chat.CanRecoverSetupTurn(turn with { SetupRecoveryAttempts = 3 }));
        Assert.False(UI.Chat.CanRecoverSetupTurn(turn with { Status = ChatTurnStatus.Cancelling }));
        Assert.False(UI.Chat.CanRecoverSetupTurn(turn with { Status = ChatTurnStatus.Cancelled }));
        Assert.False(UI.Chat.CanRecoverSetupTurn(turn with { CompletedUserActionId = null }));
        Assert.False(UI.Chat.CanRecoverSetupTurn(turn with { AllowedToolNames = ["connect_github_repository", "save_behavior"] }));
        Assert.False(UI.Chat.CanRecoverSetupTurn(turn with { SetupContinuation = null }));
        Assert.False(UI.Chat.CanRecoverSetupTurn(turn with { SetupContinuation = turn.SetupContinuation! with { BehaviorRevision = null } }));
        Assert.False(UI.Chat.CanRecoverSetupTurn(turn with { SetupContinuation = turn.SetupContinuation! with { ToolName = "run_script" } }));
    }
}
