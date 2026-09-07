using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class AgentApplicationFacadeTests
{
    [Fact(Timeout = 60000)]
    public async Task Fresh_artifact_declares_and_invokes_agent_through_public_sdk_facade()
    {
        var root = Path.Combine(FindRepositoryRoot(), ".agent-facade-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "brainstorm.cs");
            await File.WriteAllTextAsync(source, """
                #:project ../../src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
                #:project ../../src/Modules/AI/Sdk/DigitalBrain.Modules.AI.Sdk.csproj
                #:property TargetFramework=net11.0
                #:property PublishAot=false

                using DigitalBrain.Abstractions;
                using DigitalBrain.AI;

                await using var brain = await DigitalBrainClient.ConnectAsync(args);
                var app = brain.Application("brainstorming");
                app.Agent("brainstorm", (_, _, _) => Task.FromResult(new AgentReply("artifact-agent")));
                await app.RunAsync(args);
                """, TestContext.Current.CancellationToken);
            var artifact = await new FileApplicationCompiler().CompileAsync(
                source, Path.Combine(root, "artifacts"), TestContext.Current.CancellationToken);
            var host = new ApplicationArtifactHost();
            await using var simulation = await BrainSimulation.StartAsync(new()
            {
                Modules = new ModuleManifest([typeof(AIModule)]),
                Configuration = new Dictionary<string, string?>
                {
                    [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
                },
            });
            var actor = new ActorContext(PrincipalId.New(), "author");
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, "facade-owner", actor);
            await host.InstallAsync(artifact, brain, "brainstorming", TestContext.Current.CancellationToken);
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var serving = host.ServeAsync(artifact, brain, "brainstorming", stopping.Token);
            try
            {
                var requesting = brain.Get<IAgent>("brainstorm").RequestAsync(
                    new AgentRequest("hello"), TestContext.Current.CancellationToken);
                var completed = await Task.WhenAny(requesting, serving);
                if (completed == serving) { await serving; }
                var reply = await requesting;
                Assert.Equal("artifact-agent", reply.Text);
            }
            finally
            {
                await stopping.CancelAsync();
                await serving;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
}

