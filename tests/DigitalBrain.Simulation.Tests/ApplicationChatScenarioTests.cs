using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationChatScenarioTests
{
    [Fact(Timeout = 120000)]
    public async Task Saved_chat_example_runs_through_real_ingress_in_an_isolated_brain()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Path.Combine(Path.GetTempPath(), "db-chat-scenario", Guid.NewGuid().ToString("N"));
        try
        {
            await using var simulation = await BrainSimulation.StartAsync(new()
            {
                Modules = new ModuleManifest([typeof(UIModule)]),
                Configuration = new Dictionary<string, string?>
                {
                    [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
                },
            });
            var actor = new ActorContext(PrincipalId.New(), "author");
            using var verified = VerifiedActor.Enter(actor);
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, "live-owner", actor);
            var service = new ApplicationAuthoringService(store,
                scenarioDrivers: [new WorkspaceChatScenarioDriver(simulation.Grains)]);
            var source = (await service.TemplateAsync(brain, "ping", ct)).Replace(
                "application.Command<string, string>(\"reply\", (_, _, _) => Task.FromResult(\"pong\"));",
                "application.OnUserMessage(\"ping\", \"/ping\", (_, _, _) => Task.FromResult(\"pong\"));",
                StringComparison.Ordinal);
            var saved = await service.SaveAsync(brain, "ping", source, null, ct);
            var acceptance = JsonSerializer.Serialize(new
            {
                instruction = "Reply pong to /ping in the originating conversation.",
                examples = new object[]
                {
                    new
                    {
                        name = "ping replies in its conversation",
                        stimulus = new { kind = "chat.user-message/v1", conversation = "main", text = "/ping" },
                        expected = new { kind = "chat.responded/v1", conversation = "main", text = "pong" },
                    },
                },
            });
            saved = await service.SaveFileAsync(brain, "ping", "acceptance.json", acceptance, saved.SourceRevision, ct);
            Assert.True((await service.ValidateAsync(brain, "ping", saved.SourceRevision, ct)).Succeeded);

            var report = await service.RunScenariosAsync(brain, "ping", saved.SourceRevision, ct);

            Assert.True(report.Passed, Assert.Single(report.Examples).Error);
            Assert.Equal("pong", JsonDocument.Parse(Assert.Single(report.Examples).ActualJson!).RootElement
                .GetProperty("text").GetString());
            Assert.DoesNotContain("scenario-", (await service.ListAsync(brain, ct)).Select(item => item.Key));
            await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Application("ping")
                .Command<string, string>("reply").HeadAsync());
        }
        finally
        {
            if (Directory.Exists(store)) { Directory.Delete(store, recursive: true); }
        }
    }
}
