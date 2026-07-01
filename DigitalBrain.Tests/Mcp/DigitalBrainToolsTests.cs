using DigitalBrain.Mcp.Tools;
using DigitalBrain.TestKit;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Mcp;

// The MCP tools are co-hosted in the silo and resolve grains via an in-process IGrainFactory.
// These tests exercise that exact path (TestCluster grain factory) without an HTTP transport.
public class DigitalBrainToolsTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public void Ping_Works_Standalone()
        => Assert.Contains("connected", DigitalBrainReadTools.PingDigitalBrain(), System.StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Publish_Then_List_Through_InProcess_GrainFactory()
    {
        var mutationTools = new DigitalBrainMutationTools(_cluster!.GrainFactory);
        var readTools = new DigitalBrainReadTools(_cluster!.GrainFactory);

        await mutationTools.PublishToMarketplace("McpPack", "1.0", "public class P {}", "mcp-user", false, 0.15);
        var listing = await readTools.ListMarketplace();

        Assert.Contains("McpPack@1.0", listing);
    }
}
