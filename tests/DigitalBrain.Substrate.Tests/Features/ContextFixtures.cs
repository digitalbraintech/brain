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
    [Alias(nameof(LastExecution))]
    Task<ExecutionIdentity?> LastExecution();
}

[Alias("DigitalBrain.Substrate.Tests.ISender")]
public interface ISender : INeuron, IHandle<Compose>
{
    [Alias(nameof(Aim))]
    Task Aim(string targetType, string targetName);
}

[Alias("DigitalBrain.Substrate.Tests.IPublisher")]
public interface IPublisher : INeuron, IHandle<Compose>;

[Alias("DigitalBrain.Substrate.Tests.IIncomplete")]
public interface IIncomplete : INeuron, IHandle<Compose>;

[Alias("DigitalBrain.Substrate.Tests.IHolding")]
public interface IHolding : INeuron, IHandle<Compose>
{
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
    private ExecutionIdentity? _last;

    public Task<ExecutionIdentity?> LastExecution() => Task.FromResult(_last);

    public async Task HandleAsync(Compose signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Context.Current;
        _last = context.Identity;
        var echo = context.Get<IEcho>("echo");
        var pong = await context.RequestAsync("echo", echo, new Ping(signal.Text), context.CancellationToken)
            .ConfigureAwait(true);
        await context.CompleteAsync(pong).ConfigureAwait(true);
    }
}

[GrainType("sender")]
internal sealed class Sender(NeuronRuntime runtime) : Neuron(runtime), ISender, IHandle<Compose>
{
    private string _targetType = "echo";
    private string _targetName = "echo";

    public Task Aim(string targetType, string targetName)
    {
        _targetType = targetType;
        _targetName = targetName;
        return Task.CompletedTask;
    }

    public async Task HandleAsync(Compose signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Context.Current;
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

        await context.CompleteAsync(new Pong(signal.Text)).ConfigureAwait(true);
    }
}

[GrainType("publisher")]
internal sealed class Publisher(NeuronRuntime runtime) : Neuron(runtime), IPublisher, IHandle<Compose>
{
    public async Task HandleAsync(Compose signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Context.Current;
        await context.PublishAsync("notice", new Notice(signal.Text), context.CancellationToken)
            .ConfigureAwait(true);
        await context.CompleteAsync(new Pong(signal.Text)).ConfigureAwait(true);
    }
}

[GrainType("incomplete")]
internal sealed class Incomplete(NeuronRuntime runtime) : Neuron(runtime), IIncomplete, IHandle<Compose>
{
    public async Task HandleAsync(Compose signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Context.Current;
        await context.PublishAsync("notice", new Notice(signal.Text), context.CancellationToken)
            .ConfigureAwait(true);
    }
}

[GrainType("holding")]
internal sealed class Holding(NeuronRuntime runtime) : Neuron(runtime), IHolding, IHandle<Compose>
{
    public Task RetryUnfinished()
        => throw new InvalidOperationException("Retry is not implemented.");

    public async Task HandleAsync(Compose signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var context = Context.Current;
        var echo = context.Get<IEcho>("echo");
        _ = await context.RequestAsync("echo", echo, new Ping(signal.Text), context.CancellationToken)
            .ConfigureAwait(true);
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
