using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationAuthoringServiceTests : IDisposable
{
    private readonly string storeRoot = Path.Combine(
        Path.GetTempPath(), "digitalbrain-authoring-tests", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 60000)]
    public async Task Validation_rejects_a_compiling_program_with_an_unsupported_wire_contract()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "invalid-contract", actor);
        using var verified = VerifiedActor.Enter(actor);
        var service = new ApplicationAuthoringService(storeRoot);
        var source = SourceFor("greeting", "pong").Replace("Command<string, string>", "Command<decimal, string>", StringComparison.Ordinal);
        var saved = await service.SaveAsync(brain, "greeting", source, null, TestContext.Current.CancellationToken);

        var validation = await service.ValidateAsync(brain, "greeting", saved.SourceRevision, TestContext.Current.CancellationToken);
        Assert.False(validation.Succeeded);
        Assert.Contains("codec", validation.Diagnostics!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 60000)]
    public async Task Saved_source_validates_activates_and_serves_as_one_retained_revision()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "authoring", actor);
        using var actorScope = VerifiedActor.Enter(actor);
        var service = new ApplicationAuthoringService(storeRoot);
        var source = SourceFor("greeting", "retained-pong");

        var saved = await service.SaveAsync(
            brain, "greeting", source, expectedRevision: null, TestContext.Current.CancellationToken);
        Assert.Equal(source, (await service.ReadAsync(brain, "greeting", TestContext.Current.CancellationToken)).Source);
        Assert.Contains(await service.ListAsync(brain, TestContext.Current.CancellationToken), item => item.Key == "greeting");

        var validation = await service.ValidateAsync(
            brain, "greeting", saved.SourceRevision, TestContext.Current.CancellationToken);
        Assert.True(validation.Succeeded, validation.Diagnostics);
        await service.ActivateAsync(brain, "greeting", saved.SourceRevision, TestContext.Current.CancellationToken);

        var invalid = await service.SaveAsync(
            brain, "greeting", "this is not C#", saved.SourceRevision, TestContext.Current.CancellationToken);
        var invalidValidation = await service.ValidateAsync(
            brain, "greeting", invalid.SourceRevision, TestContext.Current.CancellationToken);
        Assert.False(invalidValidation.Succeeded);
        Assert.NotNull(invalidValidation.Diagnostics);
        Assert.NotEmpty(invalidValidation.Diagnostics);

        var command = brain.Application("greeting").Command<string, string>(
            "reply", (_, _, _) => Task.FromResult("caller-handler-must-not-run"));
        var invocation = await command.SubmitAsync("/ping", TestContext.Current.CancellationToken);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var serving = service.ServeAsync(brain, "greeting", stopping.Token);
        try
        {
            Assert.Equal("retained-pong", await invocation.ResultAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    [Fact]
    public async Task Save_rejects_a_stale_expected_source_revision()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var actor = new ActorContext(PrincipalId.New(), "author");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "authoring-cas", actor);
        using var actorScope = VerifiedActor.Enter(actor);
        var service = new ApplicationAuthoringService(storeRoot);
        var first = await service.SaveAsync(
            brain, "greeting", SourceFor("greeting", "one"), null, TestContext.Current.CancellationToken);
        await service.SaveAsync(
            brain, "greeting", SourceFor("greeting", "two"), first.SourceRevision, TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
            brain, "greeting", SourceFor("greeting", "three"), first.SourceRevision,
            TestContext.Current.CancellationToken));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string SourceFor(string applicationKey, string reply)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sdkProject = Path.Combine(repositoryRoot, "src", "Kernel", "DigitalBrain.Sdk", "DigitalBrain.Sdk.csproj")
            .Replace('\\', '/');
        return $$"""
            #:project {{sdkProject}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;

            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var application = brain.Application("{{applicationKey}}");
            application.Command<string, string>("reply", (_, _, _) => Task.FromResult("{{reply}}"));
            await application.RunAsync(args);
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

