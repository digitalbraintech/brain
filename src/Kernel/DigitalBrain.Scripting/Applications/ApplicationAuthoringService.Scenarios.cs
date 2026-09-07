using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;

namespace DigitalBrain.Scripting.Applications;

public sealed partial class ApplicationAuthoringService
{
    public async Task<ApplicationScenarioRun> ReadScenarioRunAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default)
    {
        var metadata = await RequireRevisionAsync(LocationFor(brain, key), expectedSourceRevision, cancellationToken)
            .ConfigureAwait(false);
        return metadata.Validation is { Succeeded: true, Artifact: { } artifact } validation
            && validation.SourceRevision == expectedSourceRevision
            && metadata.ScenarioRun is { } report && report.SourceRevision == expectedSourceRevision
            && report.ArtifactRevision == artifact.ApplicationRevision
            && (metadata.ExpectationRevision is null || report.ExpectationRevision == metadata.ExpectationRevision)
            ? report : throw new KeyNotFoundException("No scenario result is retained for this revision.");
    }

    public async Task<ApplicationScenarioRun> RunScenariosAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default)
    {
        var location = LocationFor(brain, key);
        await using var heldLock = await AcquireLockAsync(location, cancellationToken).ConfigureAwait(false);
        var metadata = await RequireRevisionAsync(location, expectedSourceRevision, cancellationToken).ConfigureAwait(false);
        var artifact = metadata.Validation is { Succeeded: true, Artifact: { } validated }
            && metadata.Validation.SourceRevision == expectedSourceRevision
            ? validated : throw new InvalidOperationException("Validate the saved revision before running its examples.");
        var expectationRevision = metadata.ExpectationRevision;
        string documentJson;
        if (expectationRevision is not null)
        {
            if (metadata.Expectations?.GetValueOrDefault(expectationRevision) is not { } expectation)
            {
                throw new InvalidDataException("The recorded expectation head is missing from its immutable history.");
            }
            documentJson = await ReadExpectationDocumentAsync(expectation.DocumentPath, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var acceptancePath = metadata.Files?.GetValueOrDefault("acceptance.json")
                ?? throw new InvalidOperationException("Save the original instruction and literal examples in acceptance.json first.");
            documentJson = await File.ReadAllTextAsync(acceptancePath, cancellationToken).ConfigureAwait(false);
        }
        var document = JsonSerializer.Deserialize<AcceptanceDocument>(documentJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The acceptance document is empty.");
        if (string.IsNullOrWhiteSpace(document.Instruction) || document.Examples is not { Length: > 0 and <= 32 })
        {
            throw new InvalidOperationException("Acceptance requires the original instruction and one to 32 operation examples.");
        }
        foreach (var example in document.Examples)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(example.Name);
            var operation = !string.IsNullOrWhiteSpace(example.Operation)
                && example.InputJson is not null && example.ExpectedJson is not null
                && example.Stimulus is null && example.Expected is null;
            var stimulus = example.Operation is null && example.InputJson is null && example.ExpectedJson is null
                && example.Stimulus is { ValueKind: JsonValueKind.Object }
                && example.Expected is { ValueKind: JsonValueKind.Object };
            if (!operation && !stimulus)
            {
                throw new InvalidOperationException(
                    "Each acceptance example must declare either one operation or one typed stimulus and expectation.");
            }
            _ = operation ? JsonNode.Parse(example.InputJson!) : null;
            _ = Expected(example);
            if (stimulus)
            {
                var kind = example.Stimulus!.Value.GetProperty("kind").GetString();
                if (string.IsNullOrWhiteSpace(kind) || !scenarioDrivers.ContainsKey(kind))
                {
                    throw new InvalidOperationException($"No application scenario driver is registered for '{kind}'.");
                }
            }
        }
        var client = brain switch
        {
            DigitalBrainClient direct => direct,
            BorrowedDigitalBrain { Inner: DigitalBrainClient borrowed } => borrowed,
            _ => throw new InvalidOperationException("Scenario execution requires a trusted SDK connection."),
        };
        var results = new List<ApplicationScenarioResult>();
        foreach (var example in document.Examples)
        {
            results.Add(await RunExampleAsync(client, artifact, key, example, cancellationToken).ConfigureAwait(false));
        }
        var report = new ApplicationScenarioRun(expectedSourceRevision, artifact.ApplicationRevision,
            results.All(result => result.Passed), results, expectationRevision);
        await WriteMetadataAsync(location, metadata with { ScenarioRun = report }, cancellationToken).ConfigureAwait(false);
        return report;
    }

    private async Task<ApplicationScenarioResult> RunExampleAsync(DigitalBrainClient client,
        FileApplicationArtifact artifact, string key, AcceptanceExample example, CancellationToken cancellationToken)
    {
        var actor = new ActorContext(PrincipalId.New(), "application-scenario");
        using var actorScope = VerifiedActor.Enter(actor);
        await using var isolated = client.CreateSibling(new OwnerId($"scenario-{Guid.NewGuid():N}"), actor);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopping.CancelAfter(TimeSpan.FromSeconds(30));
        Task? serving = null;
        ApplicationScenarioResult outcome = new(example.Name, false, null, "The example was cancelled.");
        try
        {
            await host.InstallAsync(artifact, isolated, key, stopping.Token).ConfigureAwait(false);
            serving = host.ServeAsync(artifact, isolated, key, stopping.Token);
            Task<string?> executing;
            Task<ApplicationInvocation>? invoking = null;
            if (example.Operation is not null)
            {
                invoking = isolated.Application(key).InvokeDeclaredAsync(example.Operation, example.InputJson!,
                    Guid.NewGuid(), stopping.Token);
                executing = ReadOperationResultAsync(invoking);
            }
            else
            {
                var stimulus = example.Stimulus!.Value;
                var kind = stimulus.GetProperty("kind").GetString();
                if (string.IsNullOrWhiteSpace(kind) || !scenarioDrivers.TryGetValue(kind, out var driver))
                {
                    throw new InvalidOperationException($"No application scenario driver is registered for '{kind}'.");
                }
                executing = ReadDriverResultAsync(driver, isolated, key, stimulus, stopping.Token);
            }
            if (await Task.WhenAny(executing, serving).ConfigureAwait(false) == serving)
            {
                await serving.ConfigureAwait(false);
                throw new InvalidOperationException("The scenario worker stopped before producing a result.");
            }
            var actual = await executing.ConfigureAwait(false);
            var passed = actual is not null &&
                JsonNode.DeepEquals(Expected(example), JsonNode.Parse(actual));
            var operationError = invoking is not null ? (await invoking.ConfigureAwait(false)).Error : null;
            outcome = new(example.Name, passed, actual,
                passed ? null : operationError ?? "The actual result does not match the saved expectation.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            outcome = new(example.Name, false, null,
                error is OperationCanceledException ? "The example exceeded its 30-second execution limit." : error.Message);
        }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            if (serving is not null)
            {
                try { await serving.ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                catch (Exception error)
                {
                    outcome = outcome with { Passed = false, Error = error.Message };
                }
            }
        }
        return outcome;
    }

    private static async Task<string?> ReadOperationResultAsync(Task<ApplicationInvocation> invoking)
    {
        var result = await invoking.ConfigureAwait(false);
        return result.Status == "completed"
            ? result.Value
            : throw new InvalidOperationException(result.Error ??
                $"The operation ended with status '{result.Status}'.");
    }

    private static async Task<string?> ReadDriverResultAsync(
        IApplicationScenarioDriver driver,
        IDigitalBrain brain,
        string applicationKey,
        JsonElement stimulus,
        CancellationToken cancellationToken)
        => await driver.RunAsync(brain, applicationKey, stimulus, cancellationToken).ConfigureAwait(false);

    private static JsonNode? Expected(AcceptanceExample example)
        => example.Expected is { } expected
            ? JsonNode.Parse(expected.GetRawText())
            : JsonNode.Parse(example.ExpectedJson!);

    private sealed record AcceptanceDocument(string Instruction, AcceptanceExample[] Examples);
    private sealed record AcceptanceExample(
        string Name,
        string? Operation = null,
        string? InputJson = null,
        string? ExpectedJson = null,
        JsonElement? Stimulus = null,
        JsonElement? Expected = null);
}
