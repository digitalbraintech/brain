using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Scripting.Startup;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Binding]
public sealed class BehaviorSteps
{
    private BrainSimulation? _brain;
    private IDigitalBrain? _client;
    private BehaviorExecutionWorker? _worker;
    private readonly ActorContext _actor = new(PrincipalId.New(), "owner");

    [Given("a running brain")]
    public async Task GivenARunningBrain()
    {
        _brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(UIModule)]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
        });
        _client = DigitalBrainClient.Connect(_brain.Grains, _brain.Brain.Owner.Value, _actor);
    }

    [Given("DigitalBrain is activated")]
    public async Task GivenDigitalBrainIsActivated()
        => await Client.ActivateAsync(TestContext.Current.CancellationToken);

    [When(@"the user requests a behavior that charts new posts from X account ""(.*)"" onto chart ""(.*)""")]
    public async Task WhenTheUserRequestsAChartingBehavior(string account, string chart)
    {
        // Composition persists the chart and source subscription once. Each new
        // source fact invokes the saved body and returns without watching a journal.
        await Client.GetEntity<IChart>(chart).Render(new ChartState("Elon on X", "line", []));
        await Client.GetEntity<ISurface>(ISurface.DefaultInstanceName).Open(new SurfaceScene($"chart:{chart}", "Elon on X"), 8);
        var source = $$"""
            await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
            var post = digitalBrain.Input<NewPost>();
            var chart = digitalBrain.GetEntity<IChart>("{{chart}}");
            await chart.Append(new ChartPoint(post.Text, 1), "Elon on X");
            return null;
            """;
        var runner = new BehaviorProgramRunner();
        _worker = new BehaviorExecutionWorker(Brain.Brain, Brain.Grains, runner, NullLogger<BehaviorExecutionWorker>.Instance);
        await _worker.StartAsync(TestContext.Current.CancellationToken);
        var behavior = Client.Get<IBehavior>($"{account}-chart");
        var saved = await behavior.SaveScriptAsync(source, TestContext.Current.CancellationToken);
        Assert.Equal(_actor.PrincipalId, saved.Principal);
        await behavior.SubscribeAsync<NewPost>(Client.Get<IXAccount>(account), TestContext.Current.CancellationToken);
        await behavior.ActivateAsync(TestContext.Current.CancellationToken);

        await WaitUntilAsync(async () =>
        {
            var state = await Client.GetEntity<IChart>(chart).Read();
            return state is not null;
        });
    }

    [When(@"X account ""(.*)"" publishes ""(.*)""")]
    public async Task WhenXAccountPublishes(string account, string text)
    {
        var outcome = await Client.Get<IXAccount>(account).SendAsync(
            new PublishPost(text),
            TestContext.Current.CancellationToken);
        Assert.Equal(DeliveryOutcome.Handled, outcome);
    }

    [Then(@"chart ""(.*)"" has a point labeled ""(.*)""")]
    public async Task ThenChartHasAPointLabeled(string chart, string label)
    {
        await WaitUntilAsync(async () =>
        {
            var state = await Client.GetEntity<IChart>(chart).Read();
            return state?.Points.Any(point => string.Equals(point.Label, label, StringComparison.Ordinal)) == true;
        });
    }

    [Then(@"the dashboard includes chart ""(.*)""")]
    public async Task ThenTheDashboardIncludesChart(string chart)
    {
        var surface = await Client.GetEntity<ISurface>(ISurface.DefaultInstanceName).Read();
        Assert.NotNull(surface);
        Assert.Contains(
            surface.Scenes,
            scene => string.Equals(scene.SurfaceKey, $"chart:{chart}", StringComparison.Ordinal));
    }

    [AfterScenario]
    public async Task AfterScenario()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker = null;
        }

        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        if (_brain is not null)
        {
            await _brain.DisposeAsync();
            _brain = null;
        }
    }

    private BrainSimulation Brain
        => _brain ?? throw new InvalidOperationException("Given a running brain first.");

    private IDigitalBrain Client
        => _client ?? throw new InvalidOperationException("Given a running brain first.");

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token,
            TestContext.Current.CancellationToken);
        while (!await condition().ConfigureAwait(false))
        {
            await Task.Delay(50, linked.Token).ConfigureAwait(false);
        }
    }
}
