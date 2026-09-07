using DigitalBrain.Testing;

namespace DigitalBrain.Substrate.Tests;

public sealed class BrainWorld
{
    public BrainSimulation? Simulation { get; set; }

    public BrainSimulation Brain
        => Simulation ?? throw new InvalidOperationException("Given a running brain first.");
}
