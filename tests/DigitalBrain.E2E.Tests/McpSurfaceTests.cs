using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
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

        var result = await client.CallToolAsync(
            SendChatMessageTool,
            new Dictionary<string, object?>
            {
                ["text"] = "MCP end-to-end check",
                ["commandId"] = Guid.NewGuid().ToString("D"),
                ["chatName"] = "mcp-e2e",
                ["timeoutSeconds"] = 30,
            },
            cancellationToken: cancellationToken);

        var responseText = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(static block => block.Text));
        Assert.False(result.IsError is true, $"send_chat_message returned an error: {responseText}");
        Assert.Equal("Test assistant reply.", responseText);
    }
}
