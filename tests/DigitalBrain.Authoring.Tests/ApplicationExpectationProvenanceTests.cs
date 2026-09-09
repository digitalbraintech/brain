using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class ApplicationExpectationProvenanceTests : IDisposable
{
    private readonly string storeRoot = Path.Combine(
        Path.GetTempPath(), "db-expectation-provenance", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 90000)]
    public async Task Explicit_expectations_are_immutable_across_source_edits_and_superseded_by_CAS()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "expectation-author");
        using var verified = VerifiedActor.Enter(actor);
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "expectation-owner", actor);
        var service = new ApplicationAuthoringService(storeRoot);

        var saved = await service.SaveAsync(brain, "ping", SourceFor("ping"), null, ct);
        var firstOperation = Guid.NewGuid();
        var firstDocument = Acceptance("pong", "Original user expectation.");
        var first = await service.SetExpectationsAsync(
            brain, "ping", firstDocument, firstOperation, expectedExpectationRevision: null, ct);
        Assert.Equal(actor.PrincipalId.Value, first.PrincipalId);
        Assert.Equal(firstOperation, first.OperationId);
        Assert.Equal(firstDocument, first.DocumentJson);

        var replay = await service.SetExpectationsAsync(
            brain, "ping", firstDocument, firstOperation, expectedExpectationRevision: null, ct);
        Assert.Equal(first.ExpectationRevision, replay.ExpectationRevision);
        Assert.Equal(first.RecordedAt, replay.RecordedAt);

        saved = await service.SaveFileAsync(brain, "ping", "helper.cs", "// source-only edit",
            saved.SourceRevision, ct);
        saved = await service.SaveFileAsync(brain, "ping", "acceptance.json",
            Acceptance("wrong", "A generic file edit must not redefine expectations."), saved.SourceRevision, ct);
        var validation = await service.ValidateAsync(brain, "ping", saved.SourceRevision, ct);
        Assert.True(validation.Succeeded, validation.Diagnostics);

        var report = await service.RunScenariosAsync(brain, "ping", saved.SourceRevision, ct);
        Assert.True(report.Passed, Assert.Single(report.Examples).Error);
        Assert.Equal(first.ExpectationRevision, report.ExpectationRevision);
        Assert.Equal("\"pong\"", Assert.Single(report.Examples).ActualJson);
        var retainedFirst = await service.ReadExpectationsAsync(
            brain, "ping", first.ExpectationRevision, ct);
        Assert.Equal(firstDocument, retainedFirst.DocumentJson);

        var secondOperation = Guid.NewGuid();
        var secondDocument = Acceptance("new-pong", "Explicit replacement expectation.");
        var second = await service.SetExpectationsAsync(
            brain, "ping", secondDocument, secondOperation, first.ExpectationRevision, ct);
        Assert.NotEqual(first.ExpectationRevision, second.ExpectationRevision);
        Assert.Equal(secondDocument, second.DocumentJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateAsync(brain, "ping", saved.SourceRevision, ct));
        service = new ApplicationAuthoringService(storeRoot);
        Assert.Equal(firstDocument, (await service.ReadExpectationsAsync(
            brain, "ping", first.ExpectationRevision, ct)).DocumentJson);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetExpectationsAsync(
            brain, "ping", Acceptance("stale", "Stale replacement."), Guid.NewGuid(),
            first.ExpectationRevision, ct));
    }

    private static string Acceptance(string expected, string instruction)
        => JsonSerializer.Serialize(new
        {
            instruction,
            examples = new[]
            {
                new { name = "ping", operation = "reply", inputJson = "\"/ping\"",
                    expectedJson = JsonSerializer.Serialize(expected) },
            },
        });

    private static string SourceFor(string key)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var sdk = Path.Combine(directory!.FullName, "src", "Kernel", "DigitalBrain.Sdk",
            "DigitalBrain.Sdk.csproj").Replace('\\', '/');
        return $$"""
            #:project {{sdk}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false
            using DigitalBrain.Abstractions;
            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var app = brain.Application("{{key}}");
            app.Command<string, string>("reply", (_, _, _) => Task.FromResult("pong"));
            await app.RunAsync(args);
            """;
    }

    public void Dispose()
    {
        if (Directory.Exists(storeRoot))
        {
            Directory.Delete(storeRoot, recursive: true);
        }
    }
}
