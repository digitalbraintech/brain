using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Concurrency;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class ContractShapeTests
{
    [Fact]
    public void BrainNeuronRenamesTheOwnerRootWithoutChangingItsDurableAddress()
    {
        var owner = new OwnerId("owner");

        Assert.Equal("sessionneuron", IBrainNeuron.GrainTypeName);
        Assert.Equal("session", IBrainNeuron.InstanceName);
        Assert.Equal(
            new NeuronId("sessionneuron", owner, "session"),
            IBrainNeuron.ForOwner(owner));
        Assert.Equal(
            "db.v2.brain-neuron",
            Assert.Single(typeof(IBrainNeuron).GetCustomAttributes<AliasAttribute>()).Alias);
        var grainType = Assert.IsType<GrainTypeAttribute>(
            typeof(BrainNeuron).GetCustomAttribute<GrainTypeAttribute>());
        using var services = new ServiceCollection().BuildServiceProvider();
        Assert.Equal(
            "sessionneuron",
            grainType.GetGrainType(services, typeof(BrainNeuron)).ToString());
        var retiredRootTypeName = string.Concat("Session", "Neuron");
        Assert.DoesNotContain(
            typeof(INeuron).Assembly.GetTypes(),
            type => type.Name.Contains(retiredRootTypeName, StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(Neuron).Assembly.GetTypes(),
            type => type.Name.Contains(retiredRootTypeName, StringComparison.Ordinal));
    }

    [Fact]
    public void Neuron_RequiresRuntimeCompositionBoundary()
    {
        _ = Assert.Single(
            typeof(Neuron).GetMembers(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
            static candidate => candidate is { MemberType: MemberTypes.Constructor, Name: ".ctor" });
        var constructor = typeof(Neuron).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(NeuronRuntime)],
            modifiers: null);

        Assert.NotNull(constructor);
        Assert.True(constructor.IsFamily);
        Assert.Null(typeof(Neuron).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null));
    }

    [Fact]
    public void TypedSendIsTheOnlyPublicFireOnANeuronReference()
    {
        var fires = typeof(NeuronReference<>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name is "SendAsync" or "DeliverAsync")
            .ToArray();
        Assert.Empty(fires);

        var send = typeof(NeuronReferenceExtensions).GetMethod(
            nameof(NeuronReferenceExtensions.SendAsync),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(send);
        Assert.Contains(
            send.GetGenericArguments()[0].GetGenericParameterConstraints(),
            constraint => constraint.IsGenericType
                && constraint.GetGenericTypeDefinition() == typeof(IHandle<>));
    }

    [Fact]
    public void NeuronContractsSeparateDeliveryFromObservation()
    {
        Assert.DoesNotContain(
            typeof(INeuron).GetMethods(),
            static method => method.Name is nameof(INeuronGrain.Deliver)
                or nameof(INeuronGrain.BindOutgoing)
                or nameof(INeuronGrain.UnbindOutgoing));
        Assert.Contains(
            typeof(INeuronGrain).GetMethods(),
            static method => method.Name == nameof(INeuronGrain.Deliver));
        Assert.Equal(
            typeof(Task<DeliveryOutcome>),
            typeof(INeuronGrain).GetMethod(nameof(INeuronGrain.Deliver))!.ReturnType);
        Assert.Equal(
            [
                nameof(INeuronQuery.ReadJournal),
                nameof(INeuronQuery.ReadSynapses),
                nameof(INeuronQuery.Unwatch),
                nameof(INeuronQuery.Watch),
            ],
            typeof(INeuronQuery).GetMethods().Select(static method => method.Name).Order().ToArray());
    }

    [Fact]
    public void DeliveryOutcomeIsAnAliasedWireType()
    {
        Assert.NotNull(typeof(DeliveryOutcome).GetCustomAttribute<GenerateSerializerAttribute>());
        var alias = Assert.Single(typeof(DeliveryOutcome).GetCustomAttributes<AliasAttribute>());
        Assert.Equal("db.v2.delivery-outcome", alias.Alias);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(DeliveryOutcome)));
        Assert.Equal((byte)0, (byte)DeliveryOutcome.Handled);
        Assert.Equal((byte)1, (byte)DeliveryOutcome.Unhandled);
        Assert.Equal((byte)2, (byte)DeliveryOutcome.Refused);
    }

    [Fact]
    public void SignalDeliveryResultPreservesEnvelopeAndOutcomeOnTheWire()
    {
        Assert.NotNull(typeof(SignalDeliveryResult).GetCustomAttribute<GenerateSerializerAttribute>());
        Assert.Equal(
            "db.v2.signal-delivery-result",
            Assert.Single(typeof(SignalDeliveryResult).GetCustomAttributes<AliasAttribute>()).Alias);

        Assert.Equal(0u, FieldId(typeof(SignalDeliveryResult), nameof(SignalDeliveryResult.Delivery)));
        Assert.Equal(1u, FieldId(typeof(SignalDeliveryResult), nameof(SignalDeliveryResult.Outcome)));
    }

    [Fact]
    public void UnknownHistoryHasMetadataWithoutALiveCompatibilitySignal()
    {
        Assert.DoesNotContain(
            typeof(Signal).Assembly.GetTypes(),
            static type => type.GetCustomAttributes<AliasAttribute>()
                .Any(static alias => alias.Alias == "db.unrouted"));
        Assert.False(typeof(Signal).IsAssignableFrom(typeof(UnknownJournalEntry)));
        Assert.NotNull(typeof(UnknownJournalEntry).GetCustomAttribute<GenerateSerializerAttribute>());
        Assert.Equal(3u, FieldId(typeof(JournalRead), nameof(JournalRead.UnknownEntries)));
    }

    [Fact]
    public void NeuronPortsUseTheIntentionalV2Aliases()
    {
        Assert.Equal(
            "db.v2.neuron",
            Assert.Single(typeof(INeuron).GetCustomAttributes<AliasAttribute>()).Alias);
        Assert.Equal(
            "db.v2.neuron-grain",
            Assert.Single(typeof(INeuronGrain).GetCustomAttributes<AliasAttribute>()).Alias);
        Assert.Equal(
            "db.v2.neuron-query",
            Assert.Single(typeof(INeuronQuery).GetCustomAttributes<AliasAttribute>()).Alias);
    }

    [Fact]
    public void SignalDeliveryFactoryRequiresTheClockBeforeOptionalLineage()
    {
        var create = Assert.Single(
            typeof(SignalDelivery).GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => method.Name == nameof(SignalDelivery.Create));
        var parameters = create.GetParameters();

        Assert.Equal(
            [
                typeof(Signal),
                typeof(NeuronId),
                typeof(long),
                typeof(TimeProvider),
                typeof(SignalDelivery),
                typeof(CorrelationId),
                typeof(PrincipalId),
                typeof(SignalId),
                typeof(long),
                typeof(string),
                typeof(long),
            ],
            parameters.Select(static parameter => Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType));
        Assert.False(parameters[3].IsOptional);
        Assert.All(parameters[4..], static parameter => Assert.True(parameter.IsOptional));
    }

    [Fact]
    public void SignalDeliveryFactoryRejectsANullClock()
    {
        var caller = new NeuronId("caller", new OwnerId("owner"), "source");

        Assert.Throws<ArgumentNullException>(
            () => SignalDelivery.Create(new Ping("ping"), caller, 1, null!));
    }

    [Fact]
    public void QueryReadsInterleaveWhileObserverManagementRemainsSerialized()
    {
        AssertInterleavable(nameof(INeuronQuery.ReadJournal));
        AssertInterleavable(nameof(INeuronQuery.ReadSynapses));
        AssertSerialized(nameof(INeuronQuery.Watch));
        AssertSerialized(nameof(INeuronQuery.Unwatch));
    }

    private static void AssertInterleavable(string methodName)
    {
        var method = typeof(INeuronQuery).GetMethod(methodName)!;
        Assert.NotNull(method.GetCustomAttribute<ReadOnlyAttribute>());
        Assert.NotNull(method.GetCustomAttribute<AlwaysInterleaveAttribute>());
    }

    private static uint FieldId(Type declaringType, string propertyName)
    {
        var attribute = declaringType
            .GetProperty(propertyName)!
            .GetCustomAttributesData()
            .Single(static candidate => candidate.AttributeType == typeof(IdAttribute));
        return Convert.ToUInt32(Assert.Single(attribute.ConstructorArguments).Value);
    }

    private static void AssertSerialized(string methodName)
    {
        var method = typeof(INeuronQuery).GetMethod(methodName)!;
        Assert.Null(method.GetCustomAttribute<ReadOnlyAttribute>());
        Assert.Null(method.GetCustomAttribute<AlwaysInterleaveAttribute>());
    }
}
