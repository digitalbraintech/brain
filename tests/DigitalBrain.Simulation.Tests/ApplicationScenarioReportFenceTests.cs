using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationScenarioReportFenceTests
{
    [Fact(Timeout = 120000)]
    public async Task Failed_revalidation_invalidates_a_retained_pass_for_the_same_source_revision()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "db-scenario-fence", Guid.NewGuid().ToString("N"));
        var store = Path.Combine(root, "store");
        var dependency = Path.Combine(root, "dependency");
        Directory.CreateDirectory(dependency);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dependency, "ScenarioDependency.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                </Project>
                """, ct);
            await File.WriteAllTextAsync(Path.Combine(dependency, "Reply.cs"), """
                namespace ScenarioDependency;
                public static class Reply { public static string Text => "pong"; }
                """, ct);

            await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
            var actor = new ActorContext(PrincipalId.New(), "author");
            using var verified = VerifiedActor.Enter(actor);
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, "scenario-fence", actor);
            var service = new ApplicationAuthoringService(store);
            var project = Path.Combine(dependency, "ScenarioDependency.csproj").Replace('\\', '/');
            var source = (await service.TemplateAsync(brain, "ping", ct))
                .Replace("#:property TargetFramework", $"#:project {project}\n#:property TargetFramework", StringComparison.Ordinal)
                .Replace("Task.FromResult(\"pong\")", "Task.FromResult(ScenarioDependency.Reply.Text)", StringComparison.Ordinal);
            var saved = await service.SaveAsync(brain, "ping", source, null, ct);
            saved = await service.SaveFileAsync(brain, "ping", "acceptance.json", JsonSerializer.Serialize(new
            {
                instruction = "Reply pong.",
                examples = new[] { new { name = "reply", operation = "reply",
                    inputJson = "\"ping\"", expectedJson = "\"pong\"" } },
            }), saved.SourceRevision, ct);
            Assert.True((await service.ValidateAsync(brain, "ping", saved.SourceRevision, ct)).Succeeded);
            Assert.True((await service.RunScenariosAsync(brain, "ping", saved.SourceRevision, ct)).Passed);

            Directory.Delete(dependency, recursive: true);
            var failed = await service.ValidateAsync(brain, "ping", saved.SourceRevision, ct);
            Assert.False(failed.Succeeded);

            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.ReadScenarioRunAsync(brain, "ping", saved.SourceRevision, ct));
        }
        finally
        {
            if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
        }
    }
}
