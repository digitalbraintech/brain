using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Telegram;

public class TelegramChatNeuronTests
{
    private static TestCluster Cluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        return builder.Build();
    }

    private static Signal Inbound(long chatId, string text) =>
        new("TelegramMessageReceived", new Dictionary<string, object?>
        {
            ["chatId"] = chatId, ["fromUserId"] = 1L, ["text"] = text, ["updateId"] = 1L
        });

    [Fact]
    public async Task Start_command_binds_the_chat_and_confirms()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-100");
            await chat.DeliverAsync(Inbound(100, "/start hello-world"));

            Assert.Equal("hello-world", await chat.GetBoundBundleAsync());

            var reply = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>().Single(s => s.Name == "TelegramReplyRequested");
            Assert.Equal(100L, System.Convert.ToInt64(reply.Props["chatId"]));
            Assert.Contains("hello-world", reply.Props["text"]?.ToString());
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Latest_start_wins_as_the_binding()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-101");
            await chat.DeliverAsync(Inbound(101, "/start alpha"));
            await chat.DeliverAsync(Inbound(101, "/start beta"));

            Assert.Equal("beta", await chat.GetBoundBundleAsync());
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Bound_chat_routes_a_normal_message_to_the_bound_bundle()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-102");
            await chat.DeliverAsync(Inbound(102, "/start hello-world"));
            await chat.DeliverAsync(Inbound(102, "hi there"));

            var forwarded = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>()
                .Where(s => s.Name == "TelegramMessageReceived" && s.Receiver is not null)
                .ToList();
            Assert.Contains(forwarded, s =>
                s.Receiver!.Value == "generated-hello-world" && s.Props["text"]?.ToString() == "hi there");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Unbound_chat_broadcasts_so_the_default_responder_handles_it()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-103");
            await chat.DeliverAsync(Inbound(103, "just a question"));

            var broadcast = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>()
                .Where(s => s.Name == "TelegramMessageReceived" && s.IsBroadcast)
                .ToList();
            Assert.Contains(broadcast, s => s.Props["text"]?.ToString() == "just a question");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Start_without_space_does_not_bind_and_broadcasts()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-104");
            await chat.DeliverAsync(Inbound(104, "/startfoo"));

            Assert.Null(await chat.GetBoundBundleAsync());

            var broadcast = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>()
                .Where(s => s.Name == "TelegramMessageReceived" && s.IsBroadcast)
                .ToList();
            Assert.Contains(broadcast, s => s.Props["text"]?.ToString() == "/startfoo");
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Forwarded_message_preserves_causation_from_the_inbound()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-105");
            await chat.DeliverAsync(Inbound(105, "/start hello-world"));
            await chat.DeliverAsync(Inbound(105, "hi there"));

            var inbound = (await chat.GetIncomingTimelineAsync())
                .OfType<Signal>().Last(s => s.Name == "TelegramMessageReceived" && s.Props["text"]?.ToString() == "hi there");
            var forwarded = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>().Single(s => s.Name == "TelegramMessageReceived" && s.Receiver is not null && s.Props["text"]?.ToString() == "hi there");

            Assert.Equal(inbound.SynapseId, forwarded.CausationId);
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Ignores_broadcast_echoes_to_avoid_self_loop()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-106");
            // A timeline echo arrives as a broadcast-flagged signal; the neuron must not act on it.
            await chat.DeliverAsync(Inbound(106, "hello") with { IsBroadcast = true });

            var reactions = (await chat.GetOutgoingTimelineAsync())
                .OfType<Signal>()
                .Where(s => s.Name == "TelegramMessageReceived" || s.Name == "TelegramReplyRequested")
                .ToList();
            Assert.Empty(reactions);
        }
        finally { await cluster.StopAllSilosAsync(); }
    }

    [Fact]
    public async Task Telegram_viz_signal_produces_UiSurface_handled_by_FlutterUiNeuron()
    {
        var cluster = Cluster();
        await cluster.DeployAsync();
        try
        {
            var chat = cluster.GrainFactory.GetGrain<ITelegramChatNeuron>("tg-chat-viz1");
            await chat.DeliverAsync(Inbound(300, "chart my excel sales data"));

            // Chain: tg inbound -> p2p viz to chart -> chart emits surface p2p delivered to flutter (owns handle)
            var chart = cluster.GrainFactory.GetGrain<IDataVisualizationNeuron>("viz-default");
            var chartOut = await chart.GetOutgoingTimelineAsync();
            Assert.Contains(chartOut, s => s is DataChartGenerated || s is UiSurface);

            var flutter = cluster.GrainFactory.GetGrain<IFlutterUiNeuron>("flutter-ui");
            var flIncoming = await flutter.GetIncomingTimelineAsync();
            Assert.Contains(flIncoming, s => s is UiSurface u && (u.Kind == UiSurfaceKinds.DataChart || u.Props.ContainsKey("chartSpec") || u.Props.ContainsKey("tree")));
            // Item 15 polish: assert "from telegram" context arrived via stamped causation on the UiSurface.
            Assert.Contains(flIncoming, s => s is UiSurface u && u.Props.TryGetValue("originChannel", out var oc) && oc?.ToString() == "telegram");
            // Additional: context used in title for visibility in rendered surface.
            Assert.Contains(flIncoming, s => s is UiSurface u && u.Props.TryGetValue("title", out var t) && (t?.ToString()?.Contains("(from Telegram)") ?? false));
            // E2E prep: full context flow includes channelContext from tg viz path.
            Assert.Contains(flIncoming, s => s is UiSurface u && u.Props.TryGetValue("channelContext", out var cc) && (cc?.ToString()?.Contains("tg") ?? false));
        }
        finally { await cluster.StopAllSilosAsync(); }
    }
}
