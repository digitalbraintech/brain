using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.ping")]
public sealed record Ping(string Text) : Signal<Pong>;

[GenerateSerializer]
[Alias("db.test.pong")]
public sealed record Pong(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.compose")]
public sealed record Compose(string Text) : Signal<Pong>;

[GenerateSerializer]
[Alias("db.test.notice")]
public sealed record Notice(string Text) : Signal;

public enum ComposerScript
{
    RequestEcho,
    SendPing,
    PublishNotice,
    PublishWithoutComplete,
    HoldEchoBeforeComplete,
}

[Alias("DigitalBrain.Substrate.Tests.IEcho")]
public interface IEcho : INeuron, IHandle<Ping>
{
    [Alias(nameof(HandledCount))]
    Task<int> HandledCount();

    [Alias(nameof(RepeatIdenticalComplete))]
    Task RepeatIdenticalComplete();

    [Alias(nameof(ConflictComplete))]
    Task ConflictComplete(string text);

    [Alias(nameof(LastExecution))]
    Task<ExecutionIdentity?> LastExecution();
}

[Alias("DigitalBrain.Substrate.Tests.IComposer")]
public interface IComposer : INeuron, IHandle<Compose>
{
    [Alias(nameof(Use))]
    Task Use(ComposerScript script);

    [Alias(nameof(Aim))]
    Task Aim(string targetType, string targetName);

    [Alias(nameof(LastExecution))]
    Task<ExecutionIdentity?> LastExecution();

    [Alias(nameof(LastChild))]
    Task<ExecutionIdentity?> LastChild();

    [Alias(nameof(RetryUnfinished))]
    Task RetryUnfinished();
}

[Alias("DigitalBrain.Substrate.Tests.IBoard")]
public interface IBoard : INeuron, IHandle<Notice>;

[GrainType("echo")]
internal sealed class Echo(NeuronRuntime runtime) : Neuron(runtime), IEcho, IHandle<Ping>
{
    private int _handled;
    private bool _repeatIdentical;
    private ExecutionIdentity? _last;

    public Task<int> HandledCount() => Task.FromResult(_handled);

    public Task<ExecutionIdentity?> LastExecution() => Task.FromResult(_last);

    public Task RepeatIdenticalComplete()
    {
        _repeatIdentical = true;
        return Task.CompletedTask;
    }

    public Task ConflictComplete(string text)
        => throw new InvalidOperationException("No execution is bound.");

    public async Task HandleAsync(Ping signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        _handled++;
        var context = Context.Current;
        _last = context.Identity;
        var input = context.Input<Ping>();
        var pong = new Pong(input.Text);
        await context.CompleteAsync(pong).ConfigureAwait(true);
        if (_repeatIdentical)
        {
            await context.CompleteAsync(pong).ConfigureAwait(true);
        }
    }
}

[GrainType("composer")]
internal sealed class Composer(NeuronRuntime runtime) : Neuron(runtime), IComposer, IHandle<Compose>
{
    private ComposerScript _script = ComposerScript.RequestEcho;
    private string _targetType = "echo";
    private string _targetName = "echo";
    private ExecutionIdentity? _last;
    private ExecutionIdentity? _child;

    public Task Use(ComposerScript script)
    {
        _script = script;
        return Task.CompletedTask;
    }

    public Task Aim(string targetType, string targetName)
    {
        _targetType = targetType;
        _targetName = targetName;
        return Task.CompletedTask;
    }

    public Task<ExecutionIdentity?> LastExecution() => Task.FromResult(_last);

    public Task<ExecutionIdentity?> LastChild() => Task.FromResult(_child);

    public Task RetryUnfinished()
        => throw new InvalidOperationException("Retry is not implemented.");

    public async Task HandleAsync(Compose signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Context.Current;
        _last = context.Identity;
        switch (_script)
        {
            case ComposerScript.SendPing:
            {
                if (_targetType == "composer")
                {
                    var other = context.Get<IComposer>(_targetName);
                    await context.SendAsync("ping", other, new Ping(signal.Text), context.CancellationToken)
                        .ConfigureAwait(true);
                }
                else
                {
                    var echo = context.Get<IEcho>(_targetName);
                    await context.SendAsync("ping", echo, new Ping(signal.Text), context.CancellationToken)
                        .ConfigureAwait(true);
                }

                await context.CompleteAsync().ConfigureAwait(true);
                return;
            }
            case ComposerScript.PublishNotice:
            {
                await context.PublishAsync("notice", new Notice(signal.Text), context.CancellationToken)
                    .ConfigureAwait(true);
                await context.CompleteAsync(new Pong(signal.Text)).ConfigureAwait(true);
                return;
            }
            case ComposerScript.PublishWithoutComplete:
            {
                await context.PublishAsync("notice", new Notice(signal.Text), context.CancellationToken)
                    .ConfigureAwait(true);
                return;
            }
            case ComposerScript.HoldEchoBeforeComplete:
            {
                var echo = context.Get<IEcho>("echo");
                var pong = await context.RequestAsync("echo", echo, new Ping(signal.Text), context.CancellationToken)
                    .ConfigureAwait(true);
                _child = context.Identity;
                _ = pong;
                return;
            }
            default:
            {
                var echo = context.Get<IEcho>("echo");
                var pong = await context.RequestAsync("echo", echo, new Ping(signal.Text), context.CancellationToken)
                    .ConfigureAwait(true);
                _child = context.Identity;
                await context.CompleteAsync(pong).ConfigureAwait(true);
                return;
            }
        }
    }
}

[GrainType("board")]
internal sealed class Board(NeuronRuntime runtime) : Neuron(runtime), IBoard, IHandle<Notice>
{
    public Task HandleAsync(Notice signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
