using System.Collections.Concurrent;
using System.Threading.Channels;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Scripting.Startup;
// One executor for saved behavior programs. Journal notifications only wake it;
// durable claims, inputs and request checkpoints remain in each owning neuron.
internal sealed class BehaviorExecutionWorker(IDigitalBrain brain, IGrainFactory grains, BehaviorProgramRunner runner, ILogger<BehaviorExecutionWorker> logger) : BackgroundService
{
    private readonly Channel<NeuronId> _ready = Channel.CreateUnbounded<NeuronId>(new() { SingleReader = true });
    private readonly ConcurrentDictionary<NeuronId, Task> _dispatching = new();
    private readonly ConcurrentDictionary<(NeuronId Behavior, Guid Work), Task> _executing = new();
    private readonly SemaphoreSlim _capacity = new(8);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovery = Recover(stoppingToken);
        var notifications = Watch(stoppingToken);
        try
        {
            await foreach (var id in _ready.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (_dispatching.TryGetValue(id, out var previous) && !previous.IsCompleted)
                {
                    continue;
                }

                foreach (var completed in _executing.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key))
                {
                    _executing.TryRemove(completed, out _);
                }

                _dispatching[id] = Task.Run(() => Drain(id, stoppingToken), CancellationToken.None);
            }
        }
        finally
        {
            try
            {
                await Task.WhenAll(_dispatching.Values.Concat(_executing.Values)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception)
            { /* Leases fence abandoned workers after host shutdown. */
            }

            try
            {
                await Task.WhenAll(recovery, notifications).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception)
            { /* Transport teardown must not block process shutdown. */
            }
        }
    }

    private async Task Recover(CancellationToken token)
    {
        var registry = grains.GetGrain<IBehaviorsKernel>(NeuronId.For<IBehaviors>(brain.Owner, "default").ToGrainId());
        while (!token.IsCancellationRequested)
        {
            try
            {
                foreach (var id in await registry.ReadBehaviorIds().WaitAsync(token).ConfigureAwait(false))
                {
                    _ready.Writer.TryWrite(id);
                }
            }
            catch (OperationCanceledException)when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Behavior recovery will retry after reconnect");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
        }
    }

    private async Task Watch(CancellationToken token)
    {
        var cursor = 0L;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await foreach (var page in brain.Get<IBehaviors>().WatchJournalAsync(JournalKind.Outgoing, cursor, token).ConfigureAwait(false))
                {
                    cursor = page.ResumeSequence;
                    foreach (var delivery in page.Delta)
                    {
                        if (delivery.Signal is BehaviorWorkAvailable available)
                        {
                            _ready.Writer.TryWrite(available.Behavior);
                        }
                    }
                }
            }
            catch (OperationCanceledException)when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Behavior notifications disconnected; durable recovery continues");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        }
    }

    private async Task Drain(NeuronId id, CancellationToken stoppingToken)
    {
        if (!PrincipalPartition.TryParse(id.Name, out var principal, out _) || id.Owner != brain.Owner)
        {
            return;
        }

        try
        {
            using var actor = VerifiedActor.Enter(new ActorContext(principal, "_behavior"));
            var behavior = grains.GetGrain<IBehaviorKernel>(id.ToGrainId());
            var view = await behavior.ReadState().WaitAsync(stoppingToken).ConfigureAwait(false);
            foreach (var program in new[] { view.Draft, view.Active }.OfType<BehaviorProgram>().DistinctBy(program => program.Revision))
            {
                if (program.Validation != BehaviorValidation.Pending
                    && (program.Validation != BehaviorValidation.Valid || program.RuntimeFingerprint == BehaviorProgramRunner.RuntimeFingerprint))
                {
                    continue;
                }
                var declared = runner.DeclaredTypes(program);
                await behavior.ValidateDraft(program.Revision, runner.Validate(program), declared.Input, declared.Output, BehaviorProgramRunner.RuntimeFingerprint).WaitAsync(stoppingToken).ConfigureAwait(false);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await _capacity.WaitAsync(stoppingToken).ConfigureAwait(false);
                var dispatched = false;
                try
                {
                    var claim = await behavior.TryClaim().WaitAsync(stoppingToken).ConfigureAwait(false);
                    if (claim is null)
                    {
                        break;
                    }

                    _executing[(id, claim.WorkId)] = Task.Run(() => RunClaim(id, principal, behavior, claim, stoppingToken), CancellationToken.None);
                    dispatched = true;
                }
                finally
                {
                    if (!dispatched)
                    {
                        _capacity.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Behavior {Behavior} execution will recover from its durable state", id);
        }
    }

    private async Task RunClaim(NeuronId id, PrincipalId principal, IBehaviorKernel behavior, BehaviorClaim claim, CancellationToken stoppingToken)
    {
        using var actor = VerifiedActor.Enter(new ActorContext(principal, "_behavior"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var renewal = Renew(behavior, claim, deadline);
        try
        {
            var diagnostics = runner.Validate(claim.Program);
            if (diagnostics.Length > 0)
            {
                await behavior.Fail(claim, string.Join(Environment.NewLine, diagnostics), retryable: false).WaitAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            await using var client = DigitalBrainClient.ConnectExecution(grains, id, claim);
            var output = await runner.Run(claim.Program, client, claim.Input.Signal, id.Name, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            await behavior.Complete(claim, output).WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await behavior.Fail(claim, exception is OperationCanceledException ? "Execution cancelled, replaced or timed out." : exception.Message, retryable: true).WaitAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    logger.LogWarning(failure, "Behavior {Behavior} will recover its expired claim", id);
                }
            }
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            try
            {
                await renewal.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Behavior claim renewal ended");
            }

            _capacity.Release();
            _ready.Writer.TryWrite(id);
        }
    }

    private static async Task Renew(IBehaviorKernel behavior, BehaviorClaim claim, CancellationTokenSource cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellation.Token).ConfigureAwait(false);
            if (!await behavior.Renew(claim).WaitAsync(cancellation.Token).ConfigureAwait(false))
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                return;
            }
        }
    }
}
