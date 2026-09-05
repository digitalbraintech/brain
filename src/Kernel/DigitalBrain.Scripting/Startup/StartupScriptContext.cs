using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Scripting.Startup;

public sealed class StartupScriptContext
{
    internal StartupScriptContext(IDigitalBrain brain, CancellationToken cancellationToken, ScriptBehavior? behavior = null, Signal? input = null)
    {
        Brain = brain;
        CancellationToken = cancellationToken;
        Behavior = behavior;
        Input = input;
    }

    public IDigitalBrain Brain { get; }

    public CancellationToken CancellationToken { get; }

    public ScriptBehavior? Behavior { get; }

    public Signal? Input { get; }
    public Signal? Signal => Input;
#pragma warning disable IDE1006 // Match the ordinary top-level C# args identifier.
    public string[] args => [];
#pragma warning restore IDE1006
}
