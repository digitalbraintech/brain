using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Core;

namespace DigitalBrain.Abstractions;

public static class Context
{
    public static IContext Current => ExecutionScope.Current;

    public static Task<IContext> ConnectAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<IContext>(
            new NotSupportedException("Process bootstrap is not part of the in-process communication slice."));
    }
}
