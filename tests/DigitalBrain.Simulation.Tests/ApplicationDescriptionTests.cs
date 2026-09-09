using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationDescriptionTests : IDisposable
{
    private readonly string storeRoot = Path.Combine(
        Path.GetTempPath(), "digitalbrain-description-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Validated_commands_expose_bounded_json_contracts_before_activation()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "description", actor);
        using var verified = VerifiedActor.Enter(actor);
        var service = new ApplicationAuthoringService(storeRoot);

        var unvalidated = await service.SaveAsync(
            brain, "draft", SourceFor("draft"), null, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DescribeAsync(
            brain, "draft", unvalidated.SourceRevision, TestContext.Current.CancellationToken));

        var saved = await service.SaveAsync(
            brain, "ping", SourceFor("ping"), null, TestContext.Current.CancellationToken);
        var validation = await service.ValidateAsync(
            brain, "ping", saved.SourceRevision, TestContext.Current.CancellationToken);
        Assert.True(validation.Succeeded, validation.Diagnostics);

        var description = await service.DescribeAsync(
            brain, "ping", saved.SourceRevision, TestContext.Current.CancellationToken);

        Assert.Equal("ping", description.Key);
        Assert.Equal(saved.SourceRevision, description.SourceRevision);
        var operation = Assert.Single(description.Operations);
        Assert.Equal("reply", operation.Key);
        Assert.Equal("reply: string → string", operation.Summary);
        AssertStringContract(operation.Request);
        AssertStringContract(operation.Response);
    }

    private static void AssertStringContract(ApplicationJsonContract contract)
    {
        Assert.Equal("system.string/v1", contract.Name);
        using var schema = JsonDocument.Parse(contract.JsonSchema!);
        Assert.Equal("string", schema.RootElement.GetProperty("type").GetString());
        using var example = JsonDocument.Parse(contract.ExampleJson!);
        Assert.Equal("hello", example.RootElement.GetString());
    }

    private static string SourceFor(string key)
    {
        var sdkProject = Path.Combine(FindRepositoryRoot(), "src", "Kernel", "DigitalBrain.Sdk",
            "DigitalBrain.Sdk.csproj").Replace('\\', '/');
        return $$"""
            #:project {{sdkProject}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;

            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var app = brain.Application("{{key}}");
            app.Command<string, string>("reply", (request, _, _) => Task.FromResult(request));
            await app.RunAsync(args);
            """;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    public void Dispose()
    {
        if (Directory.Exists(storeRoot))
        {
            Directory.Delete(storeRoot, recursive: true);
        }
    }
}
