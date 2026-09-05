using DigitalBrain.Scripting.Startup;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class CSharpStartupScriptRunnerTests
{
    private readonly CSharpStartupScriptRunner runner = new();

    [Fact]
    public async Task Checked_in_github_review_handler_requires_a_typed_execution_input()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "examples", "github-pr-review.csx");
        var script = await StartupScript.ReadAsync(path, TestContext.Current.CancellationToken);
        var program = new DigitalBrain.Abstractions.Signals.BehaviorProgram(Guid.NewGuid(), script.Source, [], [],
            DigitalBrain.Abstractions.Signals.BehaviorValidation.Pending, [], DateTimeOffset.UtcNow);
        var behaviorRunner = new BehaviorProgramRunner();
        Assert.Empty(behaviorRunner.Validate(program));
        await Assert.ThrowsAsync<InvalidOperationException>(() => behaviorRunner.Run(program, new FakeDigitalBrain("alice"),
            new DigitalBrain.Abstractions.Signals.NewPost("untyped trigger"), "review", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Admitted_script_observes_host_supplied_revision_and_source_hash()
    {
        var revision = Guid.NewGuid();
        var script = StartupScript.FromSource("review", "return Behavior!.Name + \"/\" + Behavior.Revision + \"/\" + Behavior.SourceHash;");
        script = script with { Behavior = new ScriptBehavior("review", revision, script.Sha256) };
        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal($"review/{revision}/{script.Sha256}", result.Summary);
    }

    [Fact]
    public async Task Script_can_read_the_connected_brain_owner()
    {
        var script = StartupScript.FromSource("start.cs", "return Brain.Owner.Value;");

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", result.Summary);
    }

    [Fact]
    public async Task Script_can_see_chart_and_x_account_contracts()
    {
        var script = StartupScript.FromSource(
            "elon-chart.cs",
            "return typeof(IChart).Name + typeof(IXAccount).Name + typeof(NewPost).Name;");

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("IChartIXAccountNewPost", result.Summary);
    }

    [Fact]
    public async Task Elon_dashboard_script_compiles()
    {
        var script = StartupScript.FromSource(
            "elon-chart",
            """
            var chart = Brain.GetEntity<IChart>("elon-activity");
            await chart.Render(new ChartState("Elon on X", "line", Array.Empty<ChartPoint>()));
            await Brain.GetEntity<ISurface>("desk").Open(
                new SurfaceScene("chart:elon-activity", "Elon on X"),
                8);
            await foreach (var page in Brain.Get<IXAccount>("elon").WatchJournalAsync(
                JournalKind.Outgoing,
                0,
                CancellationToken))
            {
                foreach (var delivery in page.Delta)
                {
                    if (delivery.Signal is NewPost post)
                    {
                        await chart.Append(
                            new ChartPoint(post.Text, 1, EventId: delivery.SignalId.ToString()),
                            "Elon on X");
                    }
                }
            }
            """);

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not supported", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Personal_review_script_can_use_git_ai_chat_and_typed_subscriptions()
    {
        var script = StartupScript.FromSource("review", """
            Func<Task> review = async () =>
            {
                using var git = new Process();
                git.StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = @"D:\digitalbrain",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                git.StartInfo.ArgumentList.Add("diff");
                git.StartInfo.ArgumentList.Add("--no-ext-diff");
                git.Start();
                var diff = await git.StandardOutput.ReadToEndAsync(CancellationToken);
                await git.WaitForExitAsync(CancellationToken);
                var review = await Brain.Get<IAssistant>().RequestAsync(new AgentRequest(diff), CancellationToken);
                await Brain.Get<IChat>("alice/review").SendAsync(new Note(review.Text), CancellationToken);
                var source = Brain.Get<IChat>("alice/source");
                var chat = Brain.Get<IChat>("alice/review");
                await chat.SubscribeToAsync<IChat, IChat, Note>(source.Id, CancellationToken);
                await chat.UnsubscribeFromAsync<IChat, IChat, Note>(source.Id, CancellationToken);
            };
            return typeof(DigitalBrain.Time.ITimer).Name + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("diff")));
            """);

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Summary + string.Join(Environment.NewLine, result.Diagnostics));
        Assert.StartsWith("ITimer", result.Summary);
    }

    [Fact]
    public async Task Aspire_script_uses_inherited_agent_request_with_a_principal_scoped_connection()
    {
        var script = StartupScript.FromSource("aspire-status", """
            var principal = new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001"));
            var instance = PrincipalPartition.InstanceName(principal, "digitalbrain-local");
            Func<Task> inspect = async () =>
            {
                var aspire = Brain.Get<IAspire>(instance);
                AgentReply reply = await aspire.RequestAsync(
                    new AgentRequest("How many Aspire resources are healthy?"), CancellationToken);
            };
            return typeof(IAspire).Name + "|"
                + typeof(IHandle<AgentRequest>).IsAssignableFrom(typeof(IAspire)) + "|" + instance;
            """);

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Summary + string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("IAspire|True|0000dead000000000000000000000001.digitalbrain-local", result.Summary);
    }

    [Fact]
    public async Task Future_start_script_compiles_ino_inbox_subscribe_without_running_it()
    {
        var script = StartupScript.FromSource("start.cs", """
            Func<Task> wire = async () =>
            {
                var ino = Brain.Get<IAssistant>("assistant");
                var inbox = Brain.Get<IComposer>("inbox");
                await ino.SubscribeToAsync<IComposer, UserMessaged>(inbox.Id);
            };
            return typeof(IComposer).Name + "|"
                + typeof(IHandle<UserMessaged>).IsAssignableFrom(typeof(IAssistant));
            """);

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Summary + string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("IComposer|True", result.Summary);
    }

    [Fact]
    public async Task Compilation_errors_are_returned_as_diagnostics()
    {
        var script = StartupScript.FromSource("start.cs", "this is not C#;");

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task Provider_specialists_share_the_generic_agent_request_contract_in_scripts()
    {
        var script = StartupScript.FromSource("specialists", """
            var principal = new PrincipalId(Guid.Parse("0000dead-0000-0000-0000-000000000001"));
            Func<Task> inspect = async () =>
            {
                AgentReply mail = await Brain.Get<IGmail>(PrincipalPartition.InstanceName(principal, "default"))
                    .RequestAsync(new AgentRequest("Find recent release email"), CancellationToken);
                AgentReply crm = await Brain.Get<ISalesforce>(PrincipalPartition.InstanceName(principal, "default"))
                    .RequestAsync(new AgentRequest("Find open Acme opportunities"), CancellationToken);
            };
            return typeof(IHandle<AgentRequest>).IsAssignableFrom(typeof(IGmail)) + "|"
                + typeof(IHandle<AgentRequest>).IsAssignableFrom(typeof(ISalesforce));
            """);
        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Summary + string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("True|True", result.Summary);
    }

    [Fact]
    public async Task Runtime_errors_are_returned_without_terminating_the_worker()
    {
        var script = StartupScript.FromSource("start.cs", "throw new InvalidOperationException(\"boom\");");

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("boom", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Supplied_cancellation_is_propagated()
    {
        var script = StartupScript.FromSource("start.cs", "return 1;");
        var cancellationToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(
            script,
            new FakeDigitalBrain("alice"),
            cancellationToken));
    }
}
