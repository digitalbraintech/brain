using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;

namespace DigitalBrain.AI;

public abstract partial class Agent
{
    private sealed class TurnRequests(Agent source, CancellationToken turnCancellation) : IAgentRequests, IDisposable
    {
        private bool _active = true;

        public async Task<DeliveryOutcome> SendAsync(NeuronId target, Signal signal, CancellationToken cancellationToken = default)
        {
            RequireTarget(target);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(turnCancellation, cancellationToken);
            return (await source.SendAsync(target, signal, deadline.Token).ConfigureAwait(true)).Outcome;
        }

        public async Task<TResponse> RequestAsync<TResponse>(NeuronId target, Signal<TResponse> request, CancellationToken cancellationToken = default)
            where TResponse : Signal
        {
            RequireTarget(target);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(turnCancellation, cancellationToken);
            return await source.RequestAsync(target, request, deadline.Token).ConfigureAwait(true);
        }

        private void RequireTarget(NeuronId target)
        {
            if (!_active || target.Owner != source.Id.Owner || VerifiedActor.Current is not { } actor
                || !PrincipalPartition.OwnsInstance(actor.PrincipalId, target.Name))
            {
                throw new NeuronAuthorizationException("Composition requires an active turn and a neuron owned by the current user.");
            }
        }

        public async Task<AgentReply> RequestAsync<TAgent>(
            string instanceName, AgentRequest request, CancellationToken cancellationToken = default)
            where TAgent : IAgent
        {
            if (!_active)
            {
                throw new InvalidOperationException("This agent request capability has expired with its model turn.");
            }

            ArgumentNullException.ThrowIfNull(request);
            if (VerifiedActor.Current is not { } actor || !PrincipalPartition.OwnsInstance(actor.PrincipalId, instanceName))
            {
                throw new NeuronAuthorizationException("The delegated agent must belong to the current user.");
            }

            // Login continuations authorize named direct reads. They do not grant a
            // new specialist's entire toolset; preserve that boundary explicitly.
            if (AgentTurnContext.Current?.AllowedToolNames is not null)
            {
                throw new InvalidOperationException("Specialist delegation requires an ordinary user turn, not a restricted login continuation.");
            }

            var target = NeuronId.For<TAgent>(source.Id.Owner, instanceName);
            var parentTurn = AgentTurnContext.Current;
            using var specialistScope = parentTurn is null ? null : AgentTurnContext.Enter(parentTurn with
            {
                SpecialistRequest = new SpecialistRequest(target, request.Text),
            });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(turnCancellation, cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            deadline.Token.ThrowIfCancellationRequested();
            var operation = Guid.NewGuid();
            var started = Stopwatch.GetTimestamp();
            await source.RecordOutgoingAsync(new AgentActivity(operation, "delegation", "started", target.Type, target))
                .ConfigureAwait(true);
            var state = "failed";
            try
            {
                var reply = await source.RequestAsync(target, request, deadline.Token).ConfigureAwait(true);
                state = "completed";
                return reply;
            }
            catch (OperationCanceledException)
            {
                state = "cancelled";
                throw;
            }
            finally
            {
                await source.RecordOutgoingAsync(new AgentActivity(operation, "delegation", state, target.Type, target,
                    DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds)).ConfigureAwait(true);
            }
        }

        public void Dispose() => _active = false;
    }
}
