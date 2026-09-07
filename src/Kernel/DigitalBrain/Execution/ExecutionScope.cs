using DigitalBrain.Abstractions.Execution;

namespace DigitalBrain.Core;

internal static class ExecutionScope
{
    private static readonly AsyncLocal<IContext?> Bound = new();

    internal static IContext Current
        => Bound.Value
            ?? throw new InvalidOperationException("No execution context is bound.");

    internal static IDisposable Bind(IContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = Bound.Value;
        Bound.Value = context;
        return new Restore(previous);
    }

    private sealed class Restore(IContext? previous) : IDisposable
    {
        public void Dispose() => Bound.Value = previous;
    }
}
