using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Mcp;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

[Collection("Compiled shipped startup")]
public sealed class AssistantTurnFailureTests : IDisposable
{
    private readonly string artifacts = Path.Combine(
        Path.GetTempPath(), "db-assistant-failure", Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 120000)]
    public async Task Mcp_chat_surfaces_provider_failure_as_a_terminal_turn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(AIModule), typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo => silo.Services.AddSingleton<IChatClient>(new ThrowingChatClient()),
        });
        var actor = new ActorContext(
            new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")), "owner");
        using var verified = VerifiedActor.Enter(actor);
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, DigitalBrainNames.DefaultOwner, actor);
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
            var tools = new ChatTools(brain, simulation.Grains);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tools.SendChatMessageAsync("fail at provider", Guid.NewGuid().ToString(),
                    timeoutSeconds: 8, cancellationToken: ct));

            Assert.Contains("Assistant turn failed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await stopping.CancelAsync();
            await serving;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(artifacts))
        {
            Directory.Delete(artifacts, recursive: true);
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

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Provider failed before producing a response.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await GetResponseAsync(messages, options, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
