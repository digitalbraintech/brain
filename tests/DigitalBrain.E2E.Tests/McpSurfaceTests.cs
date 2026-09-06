using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// A real ModelContextProtocol.Core client crosses the Streamable HTTP tool surface.
[Collection(E2ECollection.Name)]
public sealed class McpSurfaceTests(AppHostFixture fixture)
{
    // McpSurface.cs (DigitalBrain.Mcp) and ProductSurfaceResources.cs (DigitalBrain.AppHost)
    // declare these as `internal`, so they aren't visible from this test project; duplicated
    // here per this repo's established pattern (see
    // tests/DigitalBrain.Aspire.Tests/ProductSurfaceResourceNames.cs) rather than granting
    // InternalsVisibleTo.
    private const string McpPath = "/mcp";
    private const string SendChatMessageTool = "send_chat_message";

    [Fact]
    public async Task TheFrozenMcpToolInvokesChatOverTheRealProtocol()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The mcp resource's HTTP endpoint is named "mcp" (ProductSurfaceResources.McpHttpEndpointName),
        // not the "http" default CreateHttpClient assumes when no endpoint name is given.
        using var http = fixture.CreateHttpClient("mcp", "mcp");

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, McpPath),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http);

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(SendChatMessageTool, toolNames);
        Assert.Contains("read_activities", toolNames);

        var firstCommand = Guid.NewGuid().ToString("N");
        var result = await client.CallToolAsync(
            SendChatMessageTool,
            new Dictionary<string, object?>
            {
                ["text"] = "MCP end-to-end check",
                ["commandId"] = firstCommand,
                ["chatName"] = "main",
                ["timeoutSeconds"] = 30,
            },
            cancellationToken: cancellationToken);

        var responseText = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(static block => block.Text));
        Assert.False(result.IsError is true, $"send_chat_message returned an error: {responseText}");
        Assert.Equal("Test assistant reply.", responseText);

        var secondCommand = Guid.NewGuid().ToString("N");
        var second = await client.CallToolAsync(SendChatMessageTool, new Dictionary<string, object?>
        {
            ["text"] = "Second independent MCP activity",
            ["commandId"] = secondCommand,
            ["chatName"] = "main",
            ["timeoutSeconds"] = 30,
        }, cancellationToken: cancellationToken);
        Assert.False(second.IsError is true);
        // Activity facts are delivered through a durable outbox after business work returns.
        JsonElement[] activities = [];
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var activitiesResult = await client.CallToolAsync("read_activities", cancellationToken: cancellationToken);
            Assert.False(activitiesResult.IsError is true);
            using var snapshot = JsonDocument.Parse(string.Join("\n", activitiesResult.Content.OfType<TextContentBlock>().Select(block => block.Text)));
            activities = snapshot.RootElement.GetProperty("activities").EnumerateArray().Select(item => item.Clone()).ToArray();
            if (new[] { firstCommand, secondCommand }.All(command => activities.Any(item =>
                item.GetProperty("commandId").GetString() == command && item.GetProperty("status").GetString() == "completed")))
            {
                break;
            }
            await Task.Delay(250, cancellationToken);
        }
        var firstActivity = Assert.Single(activities, item => item.GetProperty("commandId").GetString() == firstCommand);
        var secondActivity = Assert.Single(activities, item => item.GetProperty("commandId").GetString() == secondCommand);
        Assert.Equal("completed", firstActivity.GetProperty("status").GetString());
        Assert.Equal("completed", secondActivity.GetProperty("status").GetString());
        Assert.NotEqual(firstActivity.GetProperty("correlationId").GetString(), secondActivity.GetProperty("correlationId").GetString());
    }
}
