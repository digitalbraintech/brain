namespace DigitalBrain.Protocol;

public enum TargetTier
{
    Run,
    Deploy
}

[GenerateSerializer]
public record GenerateCode(
    [property: Id(0)] string Spec,
    [property: Id(1)] TargetTier Tier,
    [property: Id(2)] string Hints = "") : Synapse(nameof(GenerateCode), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record CodeGenerated(
    [property: Id(0)] string Spec,
    [property: Id(1)] string Source,
    [property: Id(2)] TargetTier Tier,
    [property: Id(3)] IReadOnlyList<string> RequiredRefs) : Synapse(nameof(CodeGenerated), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record RunGeneratedCode(
    [property: Id(0)] string Source,
    [property: Id(1)] string Entrypoint = "Run",
    [property: Id(2)] IReadOnlyList<string>? Refs = null) : Synapse(nameof(RunGeneratedCode), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record CodeRunResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Output,
    [property: Id(2)] string Error = "") : Synapse(nameof(CodeRunResult), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record DeployGeneratedCode(
    [property: Id(0)] string Source,
    [property: Id(1)] string ModuleName,
    [property: Id(2)] IReadOnlyList<string>? Refs = null) : Synapse(nameof(DeployGeneratedCode), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record CodeBuilt(
    [property: Id(0)] string ModuleName,
    [property: Id(1)] bool Success,
    [property: Id(2)] string BuildLog) : Synapse(nameof(CodeBuilt), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record SiloRestartRequested(
    [property: Id(0)] string Reason,
    [property: Id(1)] string ModuleName) : Synapse(nameof(SiloRestartRequested), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record FoundryRequest(
    [property: Id(0)] string Spec,
    [property: Id(1)] TargetTier Tier,
    [property: Id(2)] bool AutoApply = true) : Synapse(nameof(FoundryRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record FoundryCheckpointed(
    [property: Id(0)] string Spec,
    [property: Id(1)] string CheckpointId) : Synapse(nameof(FoundryCheckpointed), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record FoundryCompleted(
    [property: Id(0)] string Spec,
    [property: Id(1)] TargetTier Tier,
    [property: Id(2)] string Outcome,
    [property: Id(3)] bool Applied) : Synapse(nameof(FoundryCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record FoundryRolledBack(
    [property: Id(0)] string Spec,
    [property: Id(1)] string Reason,
    [property: Id(2)] string CheckpointId) : Synapse(nameof(FoundryRolledBack), DateTimeOffset.UtcNow);

public interface ICodeGenNeuron : INeuron, IHandle<GenerateCode> { }
public interface ICodeRunNeuron : INeuron, IHandle<RunGeneratedCode> { }
public interface ICodeDeployNeuron : INeuron, IHandle<DeployGeneratedCode> { }
public interface ICodeFoundryLoopNeuron : INeuron, IHandle<FoundryRequest> { }
