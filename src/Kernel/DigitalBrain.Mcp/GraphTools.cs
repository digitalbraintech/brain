using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class GraphTools(IDigitalBrain brain, IApplicationAuthoring authoring)
{
    private static readonly ActorContext OwnerActor = new(
        new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")),
        "owner");

    [McpServerTool(Name = McpSurface.ReadActivities)]
    [Description("Read durable activities for the current owner and principal, with explicit execution status and causal signal traces. A journal observation alone is not completion.")]
    public async Task<string> ReadActivitiesAsync(
        [Description("Maximum number of recent activities, from 1 to 500")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await brain.Get<IActivities>(IActivities.DefaultInstanceName)
            .RequestAsync(new ReadActivities(limit), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(snapshot, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.ReadSynapses)]
    [Description("Read source-owned synapses on a neuron. Use to verify subscribe/unsubscribe.")]
    public async Task<string> ReadSynapsesAsync(
        [Description("Neuron kind: chat, composer, assistant, activities, activitysource, or uirenderer")] string kind,
        [Description("Local instance name, for example 'main' or 'probe'")] string name = "default",
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var synapses = await Resolve(kind, name).SynapsesAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(synapses.Select(edge => new
        {
            source = edge.Source.ToString(),
            target = edge.Target.ToString(),
            signalType = edge.SignalType,
            kind = edge.Kind.ToString(),
            fireCount = edge.FireCount,
        }), JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.ReadJournal)]
    [Description("Read a neuron's incoming or outgoing journal. Use to verify publish/broadcast delivery.")]
    public async Task<string> ReadJournalAsync(
        [Description("Neuron kind: chat, composer, assistant, activities, activitysource, or uirenderer")] string kind,
        [Description("Local instance name")] string name = "default",
        [Description("Incoming or Outgoing")] string direction = "Outgoing",
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        if (!Enum.TryParse<JournalKind>(direction, ignoreCase: true, out var journalKind))
        {
            throw new ArgumentException("direction must be Incoming or Outgoing.", nameof(direction));
        }

        var read = await Resolve(kind, name).JournalAsync(journalKind, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            resumeSequence = read.ResumeSequence,
            truncated = read.ResetSnapshot is not null,
            deliveries = read.Delta.Where(delivery => delivery.Signal is not (ActivityChanged or ActivityExecutionChanged)
                || delivery.Principal is null || delivery.Principal == OwnerActor.PrincipalId).Select(delivery => new
                {
                    signalType = delivery.Signal.GetType().Name,
                    correlationId = delivery.CorrelationId.ToString(),
                    caller = delivery.Caller.ToString(),
                    sequence = delivery.Sequence,
                    summary = delivery.Signal.ToString(),
                }),
        }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.Subscribe)]
    [Description("Write a Bound synapse: the subscriber handles Subscribe so the source owns the edge.")]
    public async Task<string> SubscribeAsync(
        [Description("Subscriber kind")] string subscriberKind,
        [Description("Subscriber local name")] string subscriberName,
        [Description("Source kind")] string sourceKind,
        [Description("Source local name")] string sourceName,
        [Description("Signal type name, for example UserMessaged or ActivityChanged")] string signalType,
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var subscriber = Resolve(subscriberKind, subscriberName);
        var source = Resolve(sourceKind, sourceName);
        var outcome = await subscriber.SubscribeAsync(new Subscribe(source.Id, signalType), cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            outcome = outcome.ToString(),
            subscriber = subscriber.Id.ToString(),
            source = source.Id.ToString(),
            signalType,
        }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.Unsubscribe)]
    [Description("Remove the Bound synapse from the source.")]
    public async Task<string> UnsubscribeAsync(
        [Description("Subscriber kind")] string subscriberKind,
        [Description("Subscriber local name")] string subscriberName,
        [Description("Source kind")] string sourceKind,
        [Description("Source local name")] string sourceName,
        [Description("Signal type name")] string signalType,
        CancellationToken cancellationToken = default)
    {
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var subscriber = Resolve(subscriberKind, subscriberName);
        var source = Resolve(sourceKind, sourceName);
        var outcome = await subscriber.UnsubscribeAsync(new Unsubscribe(source.Id, signalType), cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            outcome = outcome.ToString(),
            subscriber = subscriber.Id.ToString(),
            source = source.Id.ToString(),
            signalType,
        }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.SendNote)]
    [Description("Send a Note to a chat neuron (point-to-point, not broadcast).")]
    public async Task<string> SendNoteAsync(
        [Description("Chat local name, for example 'main'")] string chatName,
        [Description("Note text")] string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        using var actor = Enter();
        await brain.ActivateAsync(cancellationToken);
        var chat = brain.Get<IChat>(Instance(chatName));
        var outcome = await chat.SendAsync(new Note(text), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            outcome = outcome.ToString(),
            chat = chat.Id.ToString(),
        }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.SaveApplication)]
    [Description("Save a complete ordinary C# application file. Saving does not execute it; validate and activate separately.")]
    public async Task<string> SaveApplicationAsync(
        [Description("Stable application key")] string key,
        [Description("Complete ordinary C# file source")] string source,
        [Description("Current source revision, or null only when creating the application")] string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var saved = await authoring.SaveAsync(
            brain, key, source, expectedRevision, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(saved, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "application_catalog")]
    [Description("Discover installed public neuron contracts, SDK project references and JSON schemas for authored applications.")]
    public async Task<string> ApplicationCatalogAsync(CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await authoring.CatalogAsync(brain, cancellationToken).ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "set_application_expectations")]
    [Description("Record an immutable instruction and acceptance examples independently of source edits. Replace only for an explicit user revision, preserving unrelated expectations. Reuse operationId for retries.")]
    public async Task<string> SetApplicationExpectationsAsync(string key, string documentJson, Guid operationId,
        string? expectedExpectationRevision, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await authoring.SetExpectationsAsync(brain, key, documentJson, operationId,
            expectedExpectationRevision, cancellationToken).ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "read_application_expectations")]
    [Description("Read an immutable accepted expectation revision and recorded provenance.")]
    public async Task<string> ReadApplicationExpectationsAsync(string key, string expectationRevision,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await authoring.ReadExpectationsAsync(brain, key, expectationRevision,
            cancellationToken).ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "list_applications")]
    [Description("List saved applications and their current source, validation and activation state.")]
    public async Task<string> ListApplicationsAsync(CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await authoring.ListAsync(brain, cancellationToken).ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "read_application")]
    [Description("Read an application's exact saved entry source and whole-bundle revision before editing.")]
    public async Task<string> ReadApplicationAsync(string key, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await authoring.ReadAsync(brain, key, cancellationToken).ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "list_application_files")]
    [Description("List the relative file paths in a saved application source bundle.")]
    public async Task<string> ListApplicationFilesAsync(string key, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await authoring.ListFilesAsync(brain, key, cancellationToken).ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "read_application_file")]
    [Description("Read one exact relative file and the whole-bundle revision before editing.")]
    public async Task<string> ReadApplicationFileAsync(
        string key, string path, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await authoring.ReadFileAsync(brain, key, path, cancellationToken).ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "save_application_file")]
    [Description("Create or replace one relative bundle file using the expected whole-bundle revision. Saving does not execute code.")]
    public async Task<string> SaveApplicationFileAsync(
        string key, string path, string source, string expectedRevision,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await authoring.SaveFileAsync(brain, key, path, source, expectedRevision, cancellationToken)
                .ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "run_application_scenarios")]
    [Description("Run the validated candidate's saved acceptance examples in isolated brain identities and retain the results.")]
    public async Task<string> RunApplicationScenariosAsync(
        string key, string expectedSourceRevision, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await authoring.RunScenariosAsync(brain, key, expectedSourceRevision, cancellationToken)
                .ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "application_scenario_results")]
    [Description("Read retained acceptance results for an exact saved source revision.")]
    public async Task<string> ReadApplicationScenariosAsync(
        string key, string expectedSourceRevision, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await authoring.ReadScenarioRunAsync(brain, key, expectedSourceRevision, cancellationToken)
                .ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = McpSurface.ValidateApplication)]
    [Description("Compile an exact saved application source revision and return diagnostics without activating it.")]
    public async Task<string> ValidateApplicationAsync(
        [Description("Stable application key")] string key,
        [Description("Exact source revision returned by save or read")] string expectedSourceRevision,
        CancellationToken cancellationToken = default)
    {
        var validation = await authoring.ValidateAsync(
            brain, key, expectedSourceRevision, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(validation, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = McpSurface.ActivateApplication)]
    [Description("Activate an exact successfully validated application source revision.")]
    public async Task<string> ActivateApplicationAsync(
        [Description("Stable application key")] string key,
        [Description("Exact successfully validated source revision")] string expectedSourceRevision,
        CancellationToken cancellationToken = default)
    {
        var activation = await authoring.ActivateAsync(
            brain, key, expectedSourceRevision, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(activation, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "describe_application")]
    [Description("Inspect an exact validated application's callable operations, dependencies and JSON contracts before invoking it.")]
    public async Task<string> DescribeApplicationAsync(string key, string expectedSourceRevision,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await authoring.DescribeAsync(brain, key, expectedSourceRevision,
            cancellationToken).ConfigureAwait(false), JsonSerializerOptions.Web);

    [McpServerTool(Name = "invoke_application")]
    [Description("Invoke a declared application operation and await its durable outcome. Reuse operationId when retrying the same input.")]
    public async Task<string> InvokeApplicationAsync(string key, string operation, string inputJson,
        Guid operationId, CancellationToken cancellationToken = default)
    {
        var invocation = await authoring.InvokeAsync(brain, key, operation, inputJson,
            operationId, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(invocation, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "application_invocation_status")]
    [Description("Read the retained status and pinned revision of an application invocation.")]
    public async Task<string> ApplicationInvocationStatusAsync(string key, Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var invocation = await authoring.ReadInvocationAsync(brain, key, operationId,
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(invocation, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "cancel_application_invocation")]
    [Description("Cancel a retained pending or running application invocation. Completed results are preserved.")]
    public async Task<string> CancelApplicationInvocationAsync(string key, Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var invocation = await authoring.CancelInvocationAsync(
            brain, key, operationId, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(invocation, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "application_template")]
    [Description("Get a compilable application starter file for this host and the chosen application key.")]
    public Task<string> ApplicationTemplateAsync(string key, CancellationToken cancellationToken = default)
        => authoring.TemplateAsync(brain, key, cancellationToken);

    private IDisposable Enter() => VerifiedActor.Enter(OwnerActor);

    private string Instance(string name) => PrincipalPartition.InstanceName(OwnerActor.PrincipalId, name);

    private GraphSubject Resolve(string kind, string name) => kind.Trim().ToLowerInvariant() switch
    {
        "chat" => GraphSubject.Of(brain.Get<IChat>(Instance(name))),
        "usermessages" or "composer" => GraphSubject.Of(brain.Get<IComposer>(IComposer.DefaultInstanceName)),
        "assistant" => GraphSubject.Of(brain.Get<IAssistant>("assistant")),
        "activities" => GraphSubject.Of(brain.Get<IActivities>(name == "default" ? IActivities.DefaultInstanceName : name)),
        "activitysource" or "execution" => GraphSubject.Of(brain.Get<IActivitySource>(name == "default" ? IActivitySource.DefaultInstanceName : name)),
        "uirenderer" => GraphSubject.Of(brain.Get<IUIRenderer>(name)),
        _ => throw new ArgumentException(
            "kind must be chat, usermessages, composer, assistant, activities, activitysource, execution, or uirenderer.", nameof(kind)),
    };

    private readonly record struct GraphSubject(
        NeuronId Id,
        Func<Subscribe, CancellationToken, Task<DeliveryOutcome>> SubscribeAsync,
        Func<Unsubscribe, CancellationToken, Task<DeliveryOutcome>> UnsubscribeAsync,
        Func<CancellationToken, Task<IReadOnlyList<DigitalBrain.Abstractions.Synapses.Synapse>>> SynapsesAsync,
        Func<JournalKind, CancellationToken, Task<JournalRead>> JournalAsync)
    {
        public static GraphSubject Of<TNeuron>(NeuronReference<TNeuron> neuron)
            where TNeuron : INeuron
            => new(
                neuron.Id,
                (signal, token) => neuron.SendAsync(signal, token),
                (signal, token) => neuron.SendAsync(signal, token),
                neuron.GetSynapsesAsync,
                (kind, token) => neuron.ReadJournalAsync(kind, 0, token));
    }
}
