using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Ui;

public class ChatNeuronTests : IAsyncLifetime
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
    public async Task Visualize_Request_Emits_RfwCard_Into_Conversation()
    {
        var chat = _cluster!.GrainFactory.GetGrain<IChatNeuron>("chat-test");
        await chat.FireAsync(new VisualizeDataRequest("show sales", "[{\"m\":\"Jan\"}]", "bar", "req-1"));

        var conversation = await chat.GetConversationAsync();
        Assert.Contains(conversation, c => c.RootWidget == "DataChartCard");
        Assert.Contains(conversation, c => c.DataJson.Contains("show sales"));
    }
}

