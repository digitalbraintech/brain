using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationDescriptionInputTests : IDisposable
{
    private readonly string storeRoot = Path.Combine(
        Path.GetTempPath(), "digitalbrain-description-input-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Event_inputs_are_not_exposed_as_callable_operations()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "description-input", actor);
        using var verified = VerifiedActor.Enter(actor);
        var service = new ApplicationAuthoringService(storeRoot);
        var saved = await service.SaveAsync(
            brain, "mixed", Source(), null, TestContext.Current.CancellationToken);
        var validation = await service.ValidateAsync(
            brain, "mixed", saved.SourceRevision, TestContext.Current.CancellationToken);
        Assert.True(validation.Succeeded, validation.Diagnostics);

        await service.ActivateAsync(
            brain, "mixed", saved.SourceRevision, TestContext.Current.CancellationToken);
        var inputPort = brain.Application("mixed").Command<string, bool>("activity/ingest");
        var typedError = await Assert.ThrowsAsync<InvalidOperationException>(() => inputPort.SubmitAsync(
            "event payload", TestContext.Current.CancellationToken));
        Assert.Contains("input", typedError.Message, StringComparison.OrdinalIgnoreCase);
        using var invokeTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        invokeTimeout.CancelAfter(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InvokeAsync(
            brain, "mixed", "activity/ingest", "\"event payload\"", Guid.NewGuid(),
            invokeTimeout.Token));
        Assert.Contains("input", error.Message, StringComparison.OrdinalIgnoreCase);

        var description = await service.DescribeAsync(
            brain, "mixed", saved.SourceRevision, TestContext.Current.CancellationToken);
        var operation = Assert.Single(description.Operations);
        Assert.Equal("reply", operation.Key);
    }

    private static string Source()
    {
        var sdkProject = Path.Combine(FindRepositoryRoot(), "src", "Kernel", "DigitalBrain.Sdk",
            "DigitalBrain.Sdk.csproj").Replace('\\', '/');
        return $$"""
            #:project {{sdkProject}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;

            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var app = brain.Application("mixed");
            app.Command<string, string>("reply", (request, _, _) => Task.FromResult(request));
            app.Behavior("activity").Handle<string>("ingest", (_, _, _) => Task.CompletedTask);
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
