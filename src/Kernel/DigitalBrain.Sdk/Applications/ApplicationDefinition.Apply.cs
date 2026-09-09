using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Abstractions.Scripting;

public sealed partial class ApplicationDefinition
{
    private CommandPort<bool, bool>? _apply;

    public void OnApply(Func<ApplicationExecutionContext, CancellationToken, Task> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_apply is not null) { throw new InvalidOperationException("An application can declare one configuration callback."); }
        _apply = Command<bool, bool>("$apply", async (_, run, ct) =>
        {
            await configure(run, ct).ConfigureAwait(false);
            return true;
        });
    }

    internal async Task ApplyAsync(string revision, CancellationToken cancellationToken = default)
    {
        await InstallAsync(revision, cancellationToken, activate: false).ConfigureAwait(false);
        using var actor = EnterActor();
        var operationId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"apply\0{revision}"))[..16]);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = ServeAsync(revision, stopping.Token);
        try
        {
            if (_apply is not null)
            {
                await _apply.InvokePinnedAsync(true, operationId, revision, cancellationToken).ConfigureAwait(false);
            }
            var initialization = await _kernel.Activate(revision).WaitAsync(cancellationToken).ConfigureAwait(false);
            foreach (var initializationId in initialization)
            {
                while (true)
                {
                    var result = await _kernel.ReadIfKnown(initializationId).WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (result?.Status == "completed") { break; }
                    if (result?.Status == "failed")
                    {
                        throw new InvalidOperationException($"Initialization input {initializationId} failed: {result.Error}");
                    }
                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            await worker.ConfigureAwait(false);
        }
    }
}
