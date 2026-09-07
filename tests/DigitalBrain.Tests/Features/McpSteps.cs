using System.IO.Pipelines;
using System.Text.Json;
using DigitalBrain.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class McpSteps(BrainSteps brain)
{
    private McpClient? _client;
    private McpServer? _server;
    private Task? _serverRun;
    private ServiceProvider? _services;
    private CallToolResult? _last;

    [Given(@"an MCP client for principal ""(.*)""")]
    public async Task GivenClient(string principal)
    {
        Pipe toServer = new(), toClient = new();
        var services = new ServiceCollection();
        services.AddSingleton(brain.Brain.Grains);
        services.AddSingleton(new SessionPrincipal(principal));
        services.AddDigitalBrainMcp()
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        _services = services.BuildServiceProvider();
        _server = _services.GetRequiredService<McpServer>();
        _serverRun = _server.RunAsync();
        _client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
    }

    [When(@"the tool ""(\w+)"" is called with (\{.*\})$")]
    public async Task CallTool(string name, string json)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        _last = await _client!.CallToolAsync(name, args);
    }

    [Then(@"the tools are ""(.*)""")]
    public async Task ThenTools(string expected)
        => Assert.Equal(expected, string.Join(", ", (await _client!.ListToolsAsync()).Select(t => t.Name).Order(StringComparer.Ordinal)));

    [Then(@"every tool has a description longer than (\d+) characters")]
    public async Task ThenDescribed(int length)
        => Assert.All(await _client!.ListToolsAsync(), t => Assert.True((t.Description?.Length ?? 0) > length, t.Name));

    [Then(@"the last tool result contains ""(.*)""")]
    public void ThenResultContains(string fragment)
    {
        Assert.False(_last!.IsError ?? false, Text(_last));
        Assert.Contains(fragment, Text(_last), StringComparison.Ordinal);
    }

    [Then(@"the last tool call failed with a message containing ""(.*)""")]
    public void ThenFailed(string fragment)
    {
        Assert.True(_last!.IsError ?? false);
        Assert.Contains(fragment, Text(_last), StringComparison.Ordinal);
    }

    [AfterScenario]
    public async Task Teardown()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        if (_serverRun is not null)
        {
            try
            {
                await _serverRun;
            }
            catch (OperationCanceledException)
            {
                // The transport closes with the client; a cancelled run loop is the normal end.
            }
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
