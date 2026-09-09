using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Sdk.Webhooks;

namespace DigitalBrain.Microsoft.GitHub;
/// <summary>Stateless provider I/O. The source neuron owns every request, observation and retry.</summary>
internal sealed class GitHubWebhookProcessor(GitHubRepositoryBindings bindings, IGitHubRepositorySource source, IGrainFactory grains) : IWebhookProcessor
{
    public bool Handles(NeuronId id) => id.Type == "repository";
    public async Task<Signal[]> ProcessAsync(NeuronId id, WebhookReceipt receipt, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        token = deadline.Token;
        var binding = bindings.TryFor(id, out var found) ? found : throw new UnauthorizedAccessException("The GitHub repository is not connected.");
        if (binding.Revision != receipt.Epoch)
        {
            return [];
        }

        if (receipt.Input is RepositoryQuery query)
        {
            Signal response;
            if (query.Request.Signal is ReadPullRequest read)
            {
                response = new PullRequestRead(await source.GetPullRequestAsync(binding, read.Number, token), binding.Enabled);
            }
            else if (query.Request.Signal is ReadRequiredChecks required)
            {
                response = await source.GetRequiredChecksAsync(binding, required.Branch, token);
            }
            else if (query.Request.Signal is ReadReviewEvidence evidence)
            {
                var current = await source.GetPullRequestAsync(binding, evidence.Expected.Number, token);
                if (current.Revision != evidence.Expected.Revision || current.CiRevision != evidence.Expected.CiRevision)
                {
                    response = new ReviewEvidenceRead(current, null, "The PR or CI changed; use the current revision.");
                }
                else
                {
                    var data = await source.GetReviewEvidenceAsync(binding, current, token);
                    var confirmation = await source.GetPullRequestAsync(binding, current.Number, token);
                    response = confirmation.Revision == current.Revision && confirmation.CiRevision == current.CiRevision && data.Complete ? new ReviewEvidenceRead(confirmation, data) : new ReviewEvidenceRead(confirmation, null, "The evidence changed during retrieval or was incomplete.");
                }
            }
            else
            {
                throw new InvalidOperationException("Unsupported repository query.");
            }

            return [new RepositoryQueryResult(query.Request, response)];
        }

        if (receipt.Input is not RefreshRepository refresh)
        {
            throw new InvalidOperationException("Unsupported GitHub receipt.");
        }

        if (refresh.Revoke || !binding.Enabled)
        {
            return [new RepositoryObserved([], true)];
        }

        if (refresh.Action == "ping")
        {
            return [];
        }

        if (refresh.Number is { } number)
        {
            return [new RepositoryObserved([await source.GetPullRequestAsync(binding, number, token)])];
        }

        var known = await grains.GetGrain<IRepositoryProjection>(id.ToGrainId()).ReadProjectionAsync().WaitAsync(token);
        if (refresh.Sha is { Length: > 0 } sha)
        {
            var targets = known.Where(item => item.IsOpen && (item.HeadSha == sha || item.CiSha == sha || item.MergeSha == sha)).ToArray();
            if (targets.Length > 0)
            {
                var snapshots = new List<PullRequestSnapshot>();
                foreach (var target in targets)
                {
                    snapshots.Add(await source.GetPullRequestAsync(binding, target.Number, token));
                }

                return [new RepositoryObserved([.. snapshots])];
            }
        }

        var open = await source.ListOpenPullRequestsAsync(binding, token);
        var all = open.ToList();
        foreach (var old in known.Where(item => item.IsOpen && !open.Any(current => current.Number == item.Number)))
        {
            all.Add(await source.GetPullRequestAsync(binding, old.Number, token));
        }

        return [new RepositoryObserved([.. all])];
    }
}
