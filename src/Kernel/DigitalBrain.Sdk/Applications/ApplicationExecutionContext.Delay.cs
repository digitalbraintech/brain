using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Abstractions.Scripting;

public sealed partial class ApplicationExecutionContext
{
    public async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        ApplicationRunScope.ThrowIfDefinitionEffect("wait on a durable delay");
        if (Interlocked.Exchange(ref _calling, 1) != 0)
        {
            throw new InvalidOperationException("Concurrent calls require separate execution branches.");
        }
        try
        {
            var ordinal = _ordinal++;
            var identity = $"delay:{duration.Ticks}";
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            var ready = await _kernel.BeginDelay(OperationId, _claim.Lease, ordinal, hash, duration.Ticks,
                    State.PendingWrites)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            State.Checkpointed();
            if (!ready) { throw new ApplicationExecutionSuspendedException(); }
        }
        finally
        {
            Volatile.Write(ref _calling, 0);
        }
    }
}

internal sealed class ApplicationExecutionSuspendedException : Exception;
