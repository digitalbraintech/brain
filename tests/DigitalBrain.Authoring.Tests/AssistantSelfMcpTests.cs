using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Mcp;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Scripting.Applications;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class AssistantSelfMcpTests
{
    [Fact(Timeout = 120000)]
    public async Task Assistant_discovers_MCP_tools_and_saves_tests_activates_a_real_behavior()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Path.Combine(Path.GetTempPath(), "db-self-mcp", Guid.NewGuid().ToString("N"));
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]), UseExternalGateway = true,
            ConfigureSilo = silo => silo.Services.AddApplicationAuthoring(store),
        });
        var actor = new ActorContext(new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001")), "owner");
        await using var brain = DigitalBrainClient.Connect(simulation.Grains, "self-mcp", actor);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddSingleton(simulation.GetSiloService<IApplicationAuthoring>());
        builder.Services.AddSingleton(simulation.Grains);
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<GraphTools>().WithTools<ChatTools>();
        await using var server = builder.Build();
        server.Urls.Add("http://127.0.0.1:0");
        server.Use(async (_, next) =>
        {
            using var requestActor = VerifiedActor.Enter(actor);
            await next();
        });
        server.MapMcp("/mcp");
        await server.StartAsync(ct);
        await using var source = new DigitalBrainMcpToolSource(new Uri(server.Urls.Single() + "/mcp"),
            new OwnerId("self-mcp"), static () => new HttpClient(), new AcceptContent());
        using var context = new AgentToolContext(new NeuronId("assistant", new OwnerId("self-mcp"), "assistant"),
            actor.PrincipalId, new NoopRequests());
        using var verified = VerifiedActor.Enter(actor);
        var tools = (await source.GetToolsAsync(context, ct)).OfType<AIFunction>().ToDictionary(tool => tool.Name);
        Assert.DoesNotContain("send_chat_message", tools.Keys);
        Assert.Contains("read_journal", tools.Keys);
        Assert.Contains("set_application_expectations", tools.Keys);
        Assert.Contains("describe_application", tools.Keys);

        async Task<string> Call(string name, AIFunctionArguments args)
        {
            var result = Assert.IsType<JsonElement>(await tools[name].InvokeAsync(args, ct));
            Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.ToString());
            return result.GetProperty("content")[0].GetProperty("text").GetString()!;
        }
        var template = await Call("application_template", new() { ["key"] = "ping" });
        var saved = JsonSerializer.Deserialize<ApplicationSource>(await Call("save_application", new()
        {
            ["key"] = "ping", ["source"] = template, ["expectedRevision"] = null,
        }), JsonSerializerOptions.Web)!;
        var expectation = """{"instruction":"Return pong.","examples":[{"name":"ping","operation":"reply","inputJson":"\"ping\"","expectedJson":"\"pong\""}]}""";
        await Call("set_application_expectations", new()
        {
            ["key"] = "ping", ["documentJson"] = expectation, ["operationId"] = Guid.NewGuid(),
            ["expectedExpectationRevision"] = null,
        });
        var revision = new AIFunctionArguments { ["key"] = "ping", ["expectedSourceRevision"] = saved.SourceRevision };
        Assert.True(JsonDocument.Parse(await Call("validate_application", revision)).RootElement.GetProperty("succeeded").GetBoolean());
        await Call("describe_application", revision);
        Assert.True(JsonDocument.Parse(await Call("run_application_scenarios", revision)).RootElement.GetProperty("passed").GetBoolean());
        await Call("activate_application", revision);
        var invoked = JsonDocument.Parse(await Call("invoke_application", new()
        {
            ["key"] = "ping", ["operation"] = "reply", ["inputJson"] = "\"ping\"", ["operationId"] = Guid.NewGuid(),
        }));
        Assert.Equal("\"pong\"", invoked.RootElement.GetProperty("value").GetString());
        using var stranger = new AgentToolContext(context.Agent, PrincipalId.New(), new NoopRequests());
        await Assert.ThrowsAsync<NeuronAuthorizationException>(async () => await source.GetToolsAsync(stranger, ct));
        context.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await tools["list_applications"].InvokeAsync(new(), ct));
    }

    private sealed class AcceptContent : IUntrustedContentScreen
    {
        public Task ScreenAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class NoopRequests : IAgentRequests
    {
        public Task<AgentReply> RequestAsync<TAgent>(string instanceName, AgentRequest request, CancellationToken cancellationToken = default)
            where TAgent : IAgent => throw new NotSupportedException();
    }
}
