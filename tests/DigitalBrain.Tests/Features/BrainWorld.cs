using DigitalBrain.Testing;

namespace DigitalBrain.Tests;

public sealed class BrainWorld
{
    public BrainSimulation? Simulation { get; set; }

    internal ScriptedChatClient Scripted { get; } = new();

    public BrainSimulation Brain => Simulation ?? throw new InvalidOperationException("Given a running brain first.");
}
