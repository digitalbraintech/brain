using DigitalBrain.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Chat;
using DigitalBrain.Memory;
using DigitalBrain.Product.Identity;
using DigitalBrain.Product.Interactions;
using System.Reflection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ContractOwnershipTests
{
    [Fact]
    public void KernelVocabularyHasNoLegacyDomainBuckets()
    {
        Assert.Equal("DigitalBrain.Abstractions.Signals", typeof(DigitalBrainActivated).Namespace);
        Assert.Equal("DigitalBrain.Abstractions.Signals", typeof(JournalProjectionAttribute).Namespace);
        AssertAlias<DigitalBrainActivated>("db.digitalbrain-activated");
        AssertAlias<SaveBehaviorScript>("db.behavior-save-script");
        AssertAlias<BehaviorStateChanged>("db.behavior-state-changed");
        AssertAlias<PublishPost>("db.publish-post");
        AssertAlias<NewPost>("db.new-post");
        Assert.Equal("db.behaviors", AliasOf(typeof(IBehaviors)));
        Assert.Equal("db.x-account", AliasOf(typeof(IXAccount)));
        AssertField<DigitalBrainActivated>(
            nameof(DigitalBrainActivated.Owner),
            0,
            typeof(OwnerId));

        var projectionUsage = typeof(JournalProjectionAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(projectionUsage);
        Assert.Equal(AttributeTargets.Class, projectionUsage.ValidOn);
        Assert.False(projectionUsage.Inherited);
        Assert.False(projectionUsage.AllowMultiple);
        Assert.Contains(
            typeof(Responded).GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(JournalProjectionAttribute));

        var retiredNamespaces = new HashSet<string>(StringComparer.Ordinal)
        {
            string.Concat("DigitalBrain.Abstractions.", "Messaging"),
            string.Concat("DigitalBrain.Abstractions.", "Interactions"),
            string.Concat("DigitalBrain.Abstractions.", "Execution"),
            string.Concat("DigitalBrain.Abstractions.", "Security"),
        };
        var retiredTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            string.Concat("Module", "Id"),
            string.Concat("Un", "routed"),
            "BehaviorDefinition", "AdmitBehavior", "BehaviorAdmitted", "ReportBehaviorStatus",
        };
        var types = typeof(INeuron).Assembly.GetTypes();

        Assert.DoesNotContain(types, type => retiredNamespaces.Contains(type.Namespace ?? string.Empty));
        Assert.DoesNotContain(types, type => retiredTypeNames.Contains(type.Name));
    }

    [Fact]
    public void ModuleValueTypesLiveWithTheirPublicModuleContracts()
    {
        Assert.Same(typeof(IExecution).Assembly, typeof(ExecutionId).Assembly);
        Assert.Same(typeof(IExecution).Assembly, typeof(ContextPath).Assembly);
        Assert.Same(typeof(IExecution).Assembly, typeof(ContextDigest).Assembly);
        Assert.Same(typeof(IVectorMemory).Assembly, typeof(ProtectedPayloadReference).Assembly);
        Assert.Equal("DigitalBrain.Execution", typeof(ExecutionId).Namespace);
        Assert.Equal("DigitalBrain.Execution", typeof(ContextPath).Namespace);
        Assert.Equal("DigitalBrain.Execution", typeof(ContextDigest).Namespace);
        Assert.Equal("DigitalBrain.Memory", typeof(ProtectedPayloadReference).Namespace);
    }

    [Fact]
    public void ModuleValueTypeWireIdentityRemainsStable()
    {
        AssertAlias<ExecutionId>("db.execution-id");
        AssertAlias<ContextPath>("db.context-path");
        AssertAlias<ContextDigest>("db.context-digest");
        AssertAlias<ProtectedPayloadReference>("db.protected-payload-reference");

        AssertField<ExecutionId>(nameof(ExecutionId.Value), 0, typeof(Guid));
        AssertField<ContextPath>(nameof(ContextPath.Value), 0, typeof(string));
        AssertField<ContextDigest>(nameof(ContextDigest.Sha256Hex), 0, typeof(string));
        AssertField<ProtectedPayloadReference>(nameof(ProtectedPayloadReference.Id), 0, typeof(Guid));
        AssertField<ProtectedPayloadReference>(
            nameof(ProtectedPayloadReference.ExpiresAt),
            1,
            typeof(DateTimeOffset?));

        var kernelContracts = typeof(INeuron).Assembly;
        var retiredExecutionNamespace = string.Concat("DigitalBrain.Abstractions.", "Execution");
        var retiredSecurityNamespace = string.Concat("DigitalBrain.Abstractions.", "Security");
        Assert.Null(kernelContracts.GetType($"{retiredExecutionNamespace}.ExecutionId"));
        Assert.Null(kernelContracts.GetType($"{retiredExecutionNamespace}.ContextPath"));
        Assert.Null(kernelContracts.GetType($"{retiredExecutionNamespace}.ContextDigest"));
        Assert.Null(kernelContracts.GetType($"{retiredSecurityNamespace}.ProtectedPayloadReference"));
    }

    [Fact]
    public void ProductInteractionTypesDoNotBelongToKernelContracts()
    {
        Assert.NotSame(typeof(INeuron).Assembly, typeof(CommandId).Assembly);
        Assert.Same(typeof(CommandId).Assembly, typeof(AgentTurnContext).Assembly);
        Assert.Equal("DigitalBrain.Product.Identity", typeof(CommandId).Namespace);
        Assert.Equal("DigitalBrain.Product.Interactions", typeof(AgentTurnContext).Namespace);
        Assert.DoesNotContain(
            typeof(INeuron).Assembly.GetTypes(),
            static type => type.Namespace?.Contains(".Interactions", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ProductContractWireIdentityRemainsStable()
    {
        var productAssembly = typeof(CommandId).Assembly;
        var publicTypes = new (Type Type, string Namespace)[]
        {
            (typeof(CommandId), "DigitalBrain.Product.Identity"),
            (typeof(AgentTurnContext), "DigitalBrain.Product.Interactions"),
            (typeof(UserActionRequest), "DigitalBrain.Product.Interactions"),
            (typeof(ITrustedUserCommandHandler), "DigitalBrain.Product.Interactions"),
            (typeof(IUntrustedContentScreen), "DigitalBrain.Product.Interactions"),
            (typeof(IUserActionContinuation), "DigitalBrain.Product.Interactions"),
            (typeof(IUserActionSource), "DigitalBrain.Product.Interactions"),
        };

        Assert.All(publicTypes, contract =>
        {
            Assert.Same(productAssembly, contract.Type.Assembly);
            Assert.Equal(contract.Namespace, contract.Type.Namespace);
        });

        AssertAlias<CommandId>("db.command-id");
        AssertAlias<AgentTurnContext>("db.agent-turn-context");
        AssertAlias<UserActionRequest>("db.user-action-request");

        AssertField<CommandId>(nameof(CommandId.Value), 0, typeof(Guid));
        AssertField<AgentTurnContext>(nameof(AgentTurnContext.Chat), 0, typeof(NeuronId));
        AssertField<AgentTurnContext>(nameof(AgentTurnContext.CommandId), 1, typeof(CommandId));
        AssertField<AgentTurnContext>(nameof(AgentTurnContext.Actor), 2, typeof(ActorContext));
        AssertField<AgentTurnContext>(nameof(AgentTurnContext.AllowedToolNames), 3, typeof(string[]));
        AssertField<UserActionRequest>(nameof(UserActionRequest.Id), 0, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.Provider), 1, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.DisplayName), 2, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.Message), 3, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.LoginUrl), 4, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.ExpiresAt), 5, typeof(DateTimeOffset));
        AssertField<UserActionRequest>(nameof(UserActionRequest.ResumeToolNames), 6, typeof(string[]));

        var kernelContracts = typeof(INeuron).Assembly;
        var retiredIdentityNamespace = string.Concat("DigitalBrain.Abstractions.", "Identity");
        var retiredInteractionsNamespace = string.Concat("DigitalBrain.Abstractions.", "Interactions");
        Assert.Null(kernelContracts.GetType($"{retiredIdentityNamespace}.CommandId"));
        Assert.Null(kernelContracts.GetType($"{retiredInteractionsNamespace}.AgentTurnContext"));
        Assert.Null(kernelContracts.GetType($"{retiredInteractionsNamespace}.UserActionRequest"));
    }

    [Fact]
    public void AgentTurnContextEnterRestoresNestedContextAndDisposeIsIdempotent()
    {
        var previous = AgentTurnContext.Current;
        var owner = new OwnerId("owner");
        var actor = new ActorContext(PrincipalId.New(), "user");
        var outer = new AgentTurnContext(
            new NeuronId("chat", owner, "outer"),
            CommandId.New(),
            actor);
        var inner = new AgentTurnContext(
            new NeuronId("chat", owner, "inner"),
            CommandId.New(),
            actor,
            ["tool"]);

        using (AgentTurnContext.Enter(outer))
        {
            Assert.Same(outer, AgentTurnContext.Current);
            var innerScope = AgentTurnContext.Enter(inner);
            Assert.Same(inner, AgentTurnContext.Current);

            innerScope.Dispose();
            innerScope.Dispose();

            Assert.Same(outer, AgentTurnContext.Current);
        }

        Assert.Same(previous, AgentTurnContext.Current);
    }

    private static void AssertAlias<T>(string expected)
        => Assert.Equal(expected, AliasOf(typeof(T)));

    private static string AliasOf(Type type)
    {
        var alias = Assert.Single(
            type.GetCustomAttributesData(),
            static attribute => attribute.AttributeType.Name == "AliasAttribute");

        return Assert.IsType<string>(Assert.Single(alias.ConstructorArguments).Value);
    }

    private static void AssertField<T>(string propertyName, int expectedId, Type expectedType)
    {
        var property = typeof(T).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedType, property.PropertyType);
        var id = Assert.Single(
            property.GetCustomAttributesData(),
            static attribute => attribute.AttributeType.Name == "IdAttribute");

        Assert.Equal(expectedId, Convert.ToInt32(Assert.Single(id.ConstructorArguments).Value));
    }
}
