using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Scripting.Startup;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class BehaviorProgramRunnerTests
{
    [Fact]
    public async Task Ordinary_top_level_program_returns_typed_output_and_connect_borrows_host_context()
    {
        var program = Program("""
            await using IDigitalBrain connected = await DigitalBrainClient.ConnectAsync(args);
            var post = (NewPost)Input!;
            return new Note(connected.Owner.Value + ": " + post.Text);
            """);
        var runner = new BehaviorProgramRunner();
        Assert.Empty(runner.Validate(program));
        var types = runner.DeclaredTypes(program);
        Assert.Contains(nameof(NewPost), types.Input);
        Assert.Contains(nameof(Note), types.Output);
        var result = await runner.Run(program, new FakeDigitalBrain("alice"), new NewPost("hello"), "test", TestContext.Current.CancellationToken);
        Assert.Equal("alice: hello", Assert.IsType<Note>(result).Text);
    }

    [Fact]
    public void Unresolved_draft_and_non_signal_results_cannot_validate()
    {
        var runner = new BehaviorProgramRunner();
        Assert.NotEmpty(runner.Validate(Program("var token = \"__MISSING_BINDING__\"; return null;")));
        Assert.NotEmpty(runner.Validate(Program("return \"not a signal\";")));
    }

    [Fact]
    public async Task Early_return_does_not_produce_a_publication()
    {
        var runner = new BehaviorProgramRunner();
        var program = Program("if (Input is NewPost post && post.Text == \"pending\") return null; return new Note(\"ready\");");
        Assert.Empty(runner.Validate(program));
        Assert.Null(await runner.Run(program, new FakeDigitalBrain("alice"), new NewPost("pending"), "gate", TestContext.Current.CancellationToken));
        Assert.IsType<Note>(await runner.Run(program, new FakeDigitalBrain("alice"), new NewPost("green"), "gate", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("github-pr-review", "PullRequestChanged")]
    [InlineData("personal-code-review", "Note")]
    public async Task Checked_in_behavior_examples_compile_and_infer_exact_contracts(string name, string input)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "examples", $"{name}.csx"), TestContext.Current.CancellationToken);
        var runner = new BehaviorProgramRunner();
        var program = Program(source);
        Assert.Empty(runner.Validate(program));
        var types = runner.DeclaredTypes(program);
        Assert.Equal([input], types.Input);
        Assert.Equal([nameof(Note)], types.Output);
    }

    [Fact]
    public void Manifest_inference_uses_input_expressions_and_typed_return_variables()
    {
        var runner = new BehaviorProgramRunner();
        var program = Program("""
            var input = (NewPost)Input!;
            object metadata = "value";
            if (metadata is string description) { _ = description.Length; }
            var note = new Note(input.Text);
            return note;
            """);
        Assert.Empty(runner.Validate(program));
        var types = runner.DeclaredTypes(program);
        Assert.Equal([nameof(NewPost)], types.Input);
        Assert.Equal([nameof(Note)], types.Output);
    }

    private static BehaviorProgram Program(string source) => new(Guid.NewGuid(), source, [], [], BehaviorValidation.Pending, [], DateTimeOffset.UtcNow);
}
