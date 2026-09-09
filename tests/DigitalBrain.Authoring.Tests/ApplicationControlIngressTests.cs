using System.Net.Http.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Product.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class ApplicationControlIngressTests
{
    [Fact(Timeout = 60000)]
    public async Task Authenticated_button_activation_reaches_a_connected_authored_handler()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            UseExternalGateway = true,
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo => silo.Services.AddSingleton<IDigitalBrain>(services =>
                DigitalBrainClient.Connect(services.GetRequiredService<IGrainFactory>(), "controls")),
        });
        var actor = new ActorContext(PrincipalId.New(), "browser-user");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "controls", actor);
        var renderer = brain.Get<IUIRenderer>(ISurface.DefaultInstanceName);
        await renderer.SendAsync(new OpenSurface(CommandId.New(), "home", "Home",
            new SurfaceComponent("button", "refresh", new Dictionary<string, string>
            {
                ["intent"] = "refresh",
                ["enabled"] = "true",
            })), cancellationToken);

        var observed = new TaskCompletionSource<ControlActivated>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = brain.Application("control-observer");
        var observer = app.Behavior("observer");
        var input = observer.Handle<ControlActivated>("activated", (signal, _, _) =>
        {
            observed.TrySetResult(signal);
            return Task.CompletedTask;
        });
        app.Connect("button-activated", renderer.Events().ControlActivated, input);
        using (VerifiedActor.Enter(actor)) { await simulation.ApplyApplicationAsync(app, "r1", cancellationToken); }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = simulation.ServeApplicationAsync(app, "r1", stopping.Token);
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IDigitalBrain>(brain);
            await using var server = builder.Build();
            server.Use(async (_, next) =>
            {
                using var verified = VerifiedActor.Enter(actor);
                await next();
            });
            server.MapSurfaceControls();
            await server.StartAsync(cancellationToken);

            using var http = server.GetTestClient();
            var response = await http.PostAsJsonAsync(
                $"/surfaces/{ISurface.DefaultInstanceName}/controls/refresh/activate",
                new { surfaceKey = "home", intent = "refresh" }, cancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
            var activation = await observed.Task.WaitAsync(cancellationToken);
            Assert.Equal("home", activation.SurfaceKey);
            Assert.Equal("refresh", activation.ControlId);
            Assert.Equal("refresh", activation.Intent);
        }
        finally
        {
            await stopping.CancelAsync();
            await worker;
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("missing", "refresh", true, System.Net.HttpStatusCode.NotFound)]
    [InlineData("refresh", "refresh", false, System.Net.HttpStatusCode.Conflict)]
    [InlineData("refresh", "delete", true, System.Net.HttpStatusCode.Conflict)]
    public async Task Activation_rejects_controls_not_authorized_by_the_persisted_scene(
        string controlId,
        string intent,
        bool enabled,
        System.Net.HttpStatusCode expectedStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var simulation = await StartSimulationAsync();
        var actor = new ActorContext(PrincipalId.New(), "browser-user");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "controls", actor);
        await brain.Get<IUIRenderer>(ISurface.DefaultInstanceName).SendAsync(
            new OpenSurface(CommandId.New(), "home", "Home",
                new SurfaceComponent("button", "refresh", new Dictionary<string, string>
                {
                    ["intent"] = "refresh",
                    ["enabled"] = enabled ? "true" : "false",
                })), cancellationToken);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        await using var server = builder.Build();
        server.Use(async (_, next) =>
        {
            using var verified = VerifiedActor.Enter(actor);
            await next();
        });
        server.MapSurfaceControls();
        await server.StartAsync(cancellationToken);

        using var http = server.GetTestClient();
        var response = await http.PostAsJsonAsync(
            $"/surfaces/{ISurface.DefaultInstanceName}/controls/{controlId}/activate",
            new { surfaceKey = "home", intent }, cancellationToken);
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    private static Task<BrainSimulation> StartSimulationAsync()
        => BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            UseExternalGateway = true,
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo => silo.Services.AddSingleton<IDigitalBrain>(services =>
                DigitalBrainClient.Connect(services.GetRequiredService<IGrainFactory>(), "controls")),
        });
}
