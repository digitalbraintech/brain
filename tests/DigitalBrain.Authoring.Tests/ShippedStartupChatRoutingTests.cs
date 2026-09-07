using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

[Collection("Compiled shipped startup")]
public sealed class ShippedStartupChatRoutingTests : IDisposable
{
    private readonly string artifacts = Path.Combine(
        Path.GetTempPath(), "db-startup-chat-routing", Guid.NewGuid().ToString("N"));

    [Theory(Timeout = 120000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shipped_compiled_startup_routes_user_messages_through_the_testing_assistant(bool bindConnectionActor)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule), typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
        });
        var actor = new ActorContext(PrincipalId.New(), "startup-chat-user");
        using var verified = VerifiedActor.Enter(actor);
        await using var brain = bindConnectionActor
            ? DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor)
            : DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner);
        var host = new ApplicationArtifactHost();
        var source = Path.Combine(FindRepositoryRoot(), "src", "Kernel", "DigitalBrain.Scripting",
            "scripts", "start.cs");
        var artifact = await new FileApplicationCompiler().CompileAsync(
            source, artifacts, brain, "start", host, ct);

        await host.InstallAsync(artifact, brain, "start", ct);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var serving = host.ServeAsync(artifact, brain, "start", stopping.Token);
        try
        {
        var composer = brain.Get<IComposer>(IComposer.DefaultInstanceName);
        var command = await new WorkspaceInject(brain, simulation.Grains).InjectUserMessage(
            "main", "hello from startup", actor, ct);
        using var responseDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        responseDeadline.CancelAfter(TimeSpan.FromSeconds(8));
        var response = await WaitForResponseAsync(composer, command, responseDeadline.Token);

        Assert.Equal("Test assistant reply.", response.Text);
        Assert.Equal(composer.Id, response.Chat);
        var journal = await composer.ReadJournalAsync(JournalKind.Outgoing, 0, ct);
        var input = Assert.Single(journal.Delta,
            delivery => delivery.Signal is UserMessaged message && message.CommandId == command);
        var output = Assert.Single(journal.Delta,
            delivery => delivery.Signal is Responded replied && replied.CommandId == command);
        Assert.Equal(input.CorrelationId, output.CorrelationId);
        Assert.Equal(actor.PrincipalId, output.Principal);
        Assert.DoesNotContain(journal.Delta, delivery => delivery.Signal is TurnLifecycle lifecycle
            && lifecycle.CommandId == command && lifecycle.Status == ChatTurnStatus.Failed);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    private static async Task<Responded> WaitForResponseAsync(
        NeuronReference<IComposer> composer, DigitalBrain.Product.Identity.CommandId command,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = (await composer.ReadJournalAsync(JournalKind.Outgoing, 0, cancellationToken)).Delta
                .Select(static delivery => delivery.Signal)
                .OfType<Responded>()
                .SingleOrDefault(item => item.CommandId == command);
            if (response is not null)
            {
                return response;
            }
            await Task.Delay(25, cancellationToken);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(artifacts))
        {
            Directory.Delete(artifacts, recursive: true);
        }
    }
}
