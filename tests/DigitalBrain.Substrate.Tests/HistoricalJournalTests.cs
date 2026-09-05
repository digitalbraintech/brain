using System.Text;
using System.Threading.Channels;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;
using Xunit;
using JournalEntry = DigitalBrain.Core.JournalEntry;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.signal000")]
public sealed record HistoricalFixtureSignal(string Text) : Signal;

[Alias("DigitalBrain.Substrate.Tests.IHistoricalJournalProbe")]
public interface IHistoricalJournalProbe : INeuron
{
    [Alias(nameof(Record))]
    Task Record(bool historical);

    [Alias(nameof(RetireLast))]
    Task RetireLast();

    [Alias(nameof(ReadEncoded))]
    Task<byte[][]> ReadEncoded();

    [Alias(nameof(DeserializeLast))]
    Task DeserializeLast();

    [Alias(nameof(CorruptLast))]
    Task CorruptLast(bool truncate);
}

internal sealed class HistoricalJournalProbe : Neuron, IHistoricalJournalProbe
{
    private readonly IDurableList<byte[]> _outgoing;
    private readonly Serializer<JournalEntry> _serializer;

    public HistoricalJournalProbe(NeuronRuntime runtime) : base(runtime)
    {
        _outgoing = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>("outgoing");
        _serializer = ServiceProvider.GetRequiredService<Serializer<JournalEntry>>();
    }

    public Task Record(bool historical) => RecordOutgoingAsync(historical
        ? new HistoricalFixtureSignal("private historical payload")
        : new NewPost("current payload"));

    public async Task RetireLast()
    {
        // Equal-length alias substitution models bytes saved by a removed contract
        // without adding the retired type back to the current serializer manifest.
        var encoded = _outgoing[^1].ToArray();
        var previous = Encoding.UTF8.GetBytes("db.test.signal000");
        var retired = Encoding.UTF8.GetBytes("db.admit-behavior");
        var offset = encoded.AsSpan().IndexOf(previous);
        if (offset < 0 || previous.Length != retired.Length)
        {
            throw new InvalidOperationException("Historical fixture alias was not encoded as expected.");
        }

        retired.CopyTo(encoded.AsSpan(offset));
        _outgoing[^1] = encoded;
        await WriteStateAsync().ConfigureAwait(true);
    }

    public Task<byte[][]> ReadEncoded() => Task.FromResult(_outgoing.Select(bytes => bytes.ToArray()).ToArray());

    public Task DeserializeLast()
    {
        _ = _serializer.Deserialize(_outgoing[^1]);
        return Task.CompletedTask;
    }

    public async Task CorruptLast(bool truncate)
    {
        _outgoing[^1] = truncate ? _outgoing[^1][..^1] : [0xff];
        await WriteStateAsync().ConfigureAwait(true);
    }
}

public sealed class HistoricalJournalTests
{
    [Fact]
    public async Task RemovedAliasSurvivesReadWatchAndReactivationWithoutRewritingHistory()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var id = new NeuronId("historicaljournalprobe", new OwnerId("owner"), "retired-contract");
        var probe = brain.Grains.GetGrain<IHistoricalJournalProbe>(id.ToGrainId());
        var query = brain.Grains.GetGrain<INeuronQuery>(id.ToGrainId());
        await probe.Record(false);
        await probe.Record(true);
        await probe.RetireLast();
        var originalBytes = await probe.ReadEncoded();

        // The ordinary serializer stays strict. Only historical journal reads are tolerant.
        await Assert.ThrowsAsync<FieldTypeMissingException>(probe.DeserializeLast);
        await brain.Grains.GetGrain<Orleans.Runtime.IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);

        var recovered = await query.ReadJournal(JournalKind.Outgoing, 0);
        Assert.Equal(2, recovered.ResumeSequence);
        Assert.IsType<NewPost>(Assert.Single(recovered.Delta).Signal);
        Assert.Equal(1, recovered.SequenceOf(0));
        var unavailable = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<UnknownJournalEntry>>(recovered.UnknownEntries));
        Assert.Equal(new UnknownJournalEntry(2, originalBytes[1].Length), unavailable);
        Assert.Null(recovered.ResetSnapshot);
        Assert.Equal(originalBytes, await probe.ReadEncoded());

        var observer = new HistoricalObserver();
        var reference = brain.Grains.CreateObjectReference<IJournalObserver>(observer);
        try
        {
            await query.Watch(JournalKind.Outgoing, 1, reference);
            var unknownOnly = await observer.Next();
            Assert.Equal(2, unknownOnly.ResumeSequence);
            Assert.Empty(unknownOnly.Delta);
            Assert.Equal(unavailable, Assert.Single(unknownOnly.UnknownEntries!));

            await probe.Record(false);
            var next = await observer.Next();
            Assert.Equal(3, next.ResumeSequence);
            Assert.Equal(3, next.SequenceOf(0));
            Assert.IsType<NewPost>(Assert.Single(next.Delta).Signal);
            Assert.Null(next.UnknownEntries);
        }
        finally
        {
            await query.Unwatch(reference);
            brain.Grains.DeleteObjectReference<IJournalObserver>(reference);
            GC.KeepAlive(observer);
        }

        var mixed = await query.ReadJournal(JournalKind.Outgoing, 0);
        Assert.Equal([1L, 3L], Enumerable.Range(0, mixed.Delta.Count).Select(mixed.SequenceOf));
        var suffix = await query.ReadJournal(JournalKind.Outgoing, 1);
        Assert.Equal(3, suffix.SequenceOf(0));
        Assert.Equal(originalBytes, (await probe.ReadEncoded()).Take(2));
        var reset = Assert.IsType<JournalSnapshot>((await query.ReadJournal(JournalKind.Outgoing, long.MaxValue)).ResetSnapshot);
        Assert.Equal((3L, 3), (reset.LastSequence, reset.RetainedCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedJournalBytesStillFailExplicitly(bool hasUnknownAlias)
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var id = new NeuronId("historicaljournalprobe", new OwnerId("owner"), "corrupt-contract");
        var probe = brain.Grains.GetGrain<IHistoricalJournalProbe>(id.ToGrainId());
        await probe.Record(hasUnknownAlias);
        if (hasUnknownAlias)
        {
            await probe.RetireLast();
        }

        await probe.CorruptLast(hasUnknownAlias);
        var originalBytes = await probe.ReadEncoded();
        var query = brain.Grains.GetGrain<INeuronQuery>(id.ToGrainId());
        var error = await Record.ExceptionAsync(() => query.ReadJournal(JournalKind.Outgoing, 0));
        Assert.NotNull(error);
        Assert.IsNotType<TypeLoadException>(error);
        Assert.Equal(originalBytes, await probe.ReadEncoded());
    }

    private sealed class HistoricalObserver : IJournalObserver
    {
        private readonly Channel<JournalRead> _pages = Channel.CreateUnbounded<JournalRead>();

        public Task ObserveAsync(JournalKind kind, JournalRead read)
        {
            _pages.Writer.TryWrite(read);
            return Task.CompletedTask;
        }

        internal async Task<JournalRead> Next() => await _pages.Reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
