using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Assistant;

internal sealed partial class Assistant
{
    private IReadOnlyList<AIFunction> BehaviorTools()
    {
        if (AgentTurnContext.Current is not { } turn || turn.Chat.Owner != Id.Owner)
        {
            return [];
        }
        var principal = turn.Actor.PrincipalId;
        NeuronId BehaviorId(string name) => NeuronId.For<IBehavior>(Id.Owner, PrincipalPartition.InstanceName(principal, name));
        Task<BehaviorView> ReadView(string name, CancellationToken ct)
            => GrainFactory.GetGrain<IBehaviorKernel>(BehaviorId(name).ToGrainId()).ReadState().WaitAsync(ct);

        async Task<string> Example([Description("github-pr-review or personal-code-review")] string name, CancellationToken cancellationToken)
        {
            if (name is not ("github-pr-review" or "personal-code-review"))
            {
                return "Available examples: github-pr-review, personal-code-review.";
            }
            using var stream = typeof(Assistant).Assembly.GetManifestResourceStream($"DigitalBrain.Examples.{name}.csx")
                ?? throw new InvalidOperationException("The requested behavior example is unavailable.");
            using var reader = new StreamReader(stream);
            return (await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(true))
                .Replace("\"__CHAT_INSTANCE__\"", JsonSerializer.Serialize(turn.Chat.Name), StringComparison.Ordinal);
        }

        async Task<string> Save(
            [Description("Stable local behavior name")] string name,
            [Description("Ordinary C# handler: connect with DigitalBrainClient.ConnectAsync(args), read Input<T>(), call neurons, return a signal or null")] string source,
            [Description("Input signal type names; null infers Input<T>()")] string[]? inputSignalTypes,
            [Description("Returned signal type names; null infers source")] string[]? outputSignalTypes,
            [Description("EveryEvent by default. PR review requires LatestPerSubject, ObserveFromActivation and OncePerVersion flags.")] BehaviorInputPolicy inputPolicy,
            CancellationToken cancellationToken)
            => JsonSerializer.Serialize((await RequestAsync(BehaviorId(name), new SaveBehaviorScript(source, inputSignalTypes, outputSignalTypes, InputPolicy: inputPolicy), cancellationToken).ConfigureAwait(true)).Behavior);

        async Task<string> List(CancellationToken cancellationToken)
        {
            var index = NeuronId.For<IBehaviors>(Id.Owner, "default");
            var ids = await GrainFactory.GetGrain<IBehaviorsKernel>(index.ToGrainId()).ReadBehaviorIds().WaitAsync(cancellationToken).ConfigureAwait(true);
            var views = new List<object>();
            foreach (var id in ids.Where(id => PrincipalPartition.OwnsInstance(principal, id.Name)))
            {
                var view = await GrainFactory.GetGrain<IBehaviorKernel>(id.ToGrainId()).ReadState().WaitAsync(cancellationToken).ConfigureAwait(true);
                if (view.Principal != principal)
                {
                    continue;
                }
                _ = PrincipalPartition.TryParse(id.Name, out _, out var name);
                views.Add(new { Name = name, Id = $"{id.Type}:{id.Name}", view.Enabled, view.PendingCount, view.Detail,
                    Draft = view.Draft is null ? null : new { view.Draft.Revision, Validation = view.Draft.Validation.ToString(), view.Draft.Diagnostics, view.Draft.InputSignalTypes, view.Draft.OutputSignalTypes }, ActiveRevision = view.Active?.Revision });
            }
            return JsonSerializer.Serialize(views);
        }

        async Task<string> Read([Description("Saved behavior name")] string name, CancellationToken cancellationToken)
            => JsonSerializer.Serialize(await ReadView(name, cancellationToken).ConfigureAwait(true));
        async Task<string> Activate([Description("Saved behavior name")] string name, CancellationToken cancellationToken)
            => JsonSerializer.Serialize((await RequestAsync(BehaviorId(name), new EnableBehavior(), cancellationToken).ConfigureAwait(true)).Behavior);
        async Task<string> Disable([Description("Saved behavior name")] string name, CancellationToken cancellationToken)
            => JsonSerializer.Serialize((await RequestAsync(BehaviorId(name), new DisableBehavior(), cancellationToken).ConfigureAwait(true)).Behavior);

        async Task<string> Invoke([Description("Saved behavior name")] string name,
            [Description("Declared input signal type name")] string inputType,
            [Description("JSON object containing the input signal fields")] string inputJson, CancellationToken cancellationToken)
        {
            var view = await ReadView(name, cancellationToken).ConfigureAwait(true);
            if (view.Active is null || !view.Active.InputSignalTypes.Contains(inputType, StringComparer.Ordinal))
            {
                return "Activate a valid revision and choose one of its declared input types.";
            }
            var candidates = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("DigitalBrain", StringComparison.Ordinal) == true)
                .SelectMany(SignalTypes).Where(t => t.Name == inputType).ToArray();
            if (candidates.Length != 1)
            {
                return "The requested input type is missing or ambiguous among installed contracts.";
            }
            var input = (Signal?)JsonSerializer.Deserialize(inputJson, candidates[0], new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new ArgumentException("Input must be a signal object.");
            return JsonSerializer.Serialize((await RequestAsync(BehaviorId(name), new InvokeBehavior(input), cancellationToken).ConfigureAwait(true)).Behavior);
        }

        async Task<string> SubscribeBehavior([Description("Saved behavior name")] string name,
            [Description("Authorized source instance, type:principal-qualified-name, copied from discovery")] string source,
            [Description("Declared input signal type")] string signalType, CancellationToken cancellationToken)
        {
            if (!NeuronId.TryParseInstance(source, Id.Owner, out var sourceId) || !PrincipalPartition.OwnsInstance(principal, sourceId.Name))
            {
                return "Choose an authorized source from this user's discovered neurons.";
            }
            var result = await SendAsync(BehaviorId(name), new Subscribe(sourceId, signalType), cancellationToken).ConfigureAwait(true);
            return $"Subscription {result.Outcome}.";
        }

        async Task<string> PublishHere([Description("Saved behavior name")] string name, CancellationToken cancellationToken)
        {
            var result = await SendAsync(turn.Chat, new Subscribe(BehaviorId(name), "Note"), cancellationToken).ConfigureAwait(true);
            return $"Chat note subscription {result.Outcome}. The behavior can return Note to publish here.";
        }

        return
        [
            Tool(Example, "read_behavior_example", "Read an ordinary C# behavior handler before customizing it."),
            Tool(Save, "save_behavior", "Save an editable IBehavior draft. Validation and activation are separate; inspect diagnostics before enabling."),
            Tool(List, "list_behaviors", "Discover this user's saved behaviors, contracts, active revisions and readiness."),
            Tool(Read, "read_behavior", "Read exact saved source and diagnostics before editing or invoking."),
            Tool(Activate, "activate_behavior", "Activate the validated revision. Historical inputs and composition are not replayed."),
            Tool(Disable, "disable_behavior", "Disable a behavior and its subscriptions, retaining editable source and history."),
            Tool(Invoke, "invoke_behavior", "Invoke a saved behavior with typed input. Reuse its revision; do not resave to rerun."),
            Tool(SubscribeBehavior, "subscribe_behavior", "Create a durable source-owned typed subscription."),
            Tool(PublishHere, "publish_behavior_in_this_chat", "Subscribe this conversation to Notes returned by a saved behavior."),
        ];
    }

    private static AIFunction Tool(Delegate method, string name, string description)
        => AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name, Description = description });

    private static IEnumerable<Type> SignalTypes(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { types = exception.Types.OfType<Type>().ToArray(); }
        return types.Where(type => !type.IsAbstract && !type.ContainsGenericParameters && typeof(Signal).IsAssignableFrom(type));
    }
}
