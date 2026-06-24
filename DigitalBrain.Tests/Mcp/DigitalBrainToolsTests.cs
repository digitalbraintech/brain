using DigitalBrain.Mcp.Tools;
using DigitalBrain.Tests.TestSupport;
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
        => Assert.Contains("connected", DigitalBrainTools.PingDigitalBrain(), System.StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Publish_Then_List_Through_InProcess_GrainFactory()
    {
        var tools = new DigitalBrainTools(_cluster!.GrainFactory);

        await tools.PublishToMarketplace("ToolPack", "1.0", "// code", "owner", isPrivate: false, commissionRate: 0.1);
        var listing = await tools.ListMarketplace();

        Assert.Contains("ToolPack@1.0", listing);
    }
}
