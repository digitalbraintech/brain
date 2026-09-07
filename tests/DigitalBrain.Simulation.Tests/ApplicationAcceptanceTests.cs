using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ApplicationAcceptanceTests
{
    [Theory(Timeout = 90000)]
    [InlineData("pong", true, "acceptance.json", false)]
    [InlineData("wrong", false, "Acceptance.json", false)]
    [InlineData("pong", false, "acceptance.json", true)]
    public async Task Saved_examples_check_the_candidate_before_live_activation(
        string reply, bool passes, string acceptancePath, bool failOnShutdown)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Path.Combine(Path.GetTempPath(), "db-acceptance", Guid.NewGuid().ToString("N"));
        try
        {
            await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
            var actor = new ActorContext(PrincipalId.New(), "author");
            using var verified = VerifiedActor.Enter(actor);
            await using var brain = DigitalBrainClient.Connect(simulation.Grains, "my-brain", actor);
            var service = new ApplicationAuthoringService(store);
            var source = (await service.TemplateAsync(brain, "ping", ct))
                .Replace("Task.FromResult(\"pong\")", $"Task.FromResult(\"{reply}\")", StringComparison.Ordinal);
            if (failOnShutdown)
            {
                source = source.Replace("application.Command", "var invoked = false;\napplication.Command", StringComparison.Ordinal)
                    .Replace("=> Task.FromResult(\"pong\")", "=> { invoked = true; return Task.FromResult(\"pong\"); }", StringComparison.Ordinal)
                    + "\nif (invoked) throw new InvalidOperationException(\"worker cleanup failed\");";
            }
            var saved = await service.SaveAsync(brain, "ping", source, null, ct);
            var acceptance = JsonSerializer.Serialize(new
            {
                instruction = "Reply pong when I invoke reply with /ping.",
                examples = new[] { new { name = "ping replies pong", operation = "reply",
                    inputJson = "\"/ping\"", expectedJson = "\"pong\"" } },
            });
            saved = await service.SaveFileAsync(brain, "ping", acceptancePath, acceptance, saved.SourceRevision, ct);
            var validation = await service.ValidateAsync(brain, "ping", saved.SourceRevision, ct);
            Assert.True(validation.Succeeded, validation.Diagnostics);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ActivateAsync(brain, "ping", saved.SourceRevision, ct));

            var report = await service.RunScenariosAsync(brain, "ping", saved.SourceRevision, ct);
            Assert.Equal(passes, report.Passed);
            Assert.Equal(JsonSerializer.Serialize(reply), Assert.Single(report.Examples).ActualJson);
            if (failOnShutdown) { Assert.Contains("worker cleanup failed", Assert.Single(report.Examples).Error); }
            Assert.Null((await service.ReadAsync(brain, "ping", ct)).ActiveRevision);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                brain.Application("ping").Command<string, string>("reply").HeadAsync());
            Assert.Equal(acceptance, (await service.ReadFileAsync(brain, "ping", acceptancePath, ct)).Source);
            var recovered = await new ApplicationAuthoringService(store)
                .ReadScenarioRunAsync(brain, "ping", saved.SourceRevision, ct);
            Assert.Equal(saved.SourceRevision, recovered.SourceRevision);
            Assert.Equal(passes, recovered.Passed);
            if (passes)
            {
                await service.ActivateAsync(brain, "ping", saved.SourceRevision, ct);
                Assert.NotNull((await service.ReadAsync(brain, "ping", ct)).ActiveRevision);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.ActivateAsync(brain, "ping", saved.SourceRevision, ct));
            }
        }
        finally
        {
            if (Directory.Exists(store)) { Directory.Delete(store, recursive: true); }
        }
    }
}
