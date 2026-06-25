using System.Collections.Immutable;

namespace DigitalBrain.Core;

// Minimal structured spec for a company process. Output of crystallization.
// Used as input to skill pack synthesis. Narrow to refund-style processes first.
[GenerateSerializer]
public sealed record ProcessSpec(
    string ProcessName,
    ImmutableArray<string> TriggerSynapseTypes,
    ImmutableArray<string> Steps,
    ImmutableArray<DecisionPoint> DecisionPoints,
    ImmutableArray<string> ExceptionPaths,
    ImmutableArray<string> EmittedOutcomeTypes,
    ImmutableArray<string> RequiredCapabilities);

[GenerateSerializer]
public sealed record DecisionPoint(string Condition, string TruePath, string FalsePath);

[GenerateSerializer]
public record CrystallizeProcess(string ProcessName, ImmutableArray<string> SourceQueries) : Synapse(nameof(CrystallizeProcess), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ProcessSpecCrystallized(ProcessSpec Spec, ImmutableArray<string> EvidenceRefs) : Synapse(nameof(ProcessSpecCrystallized), DateTimeOffset.UtcNow);

// Canonical typed contracts for the first company skill vertical (RefundHandling).
// These are part of the living map: the skill emits them; journals capture with full causation.
[GenerateSerializer]
public sealed record RefundRequested(
    string RequestId,
    decimal Amount,
    string? Reason = null,
    string? CustomerId = null,
    int DaysSincePurchase = 0) : Synapse(nameof(RefundRequested), DateTimeOffset.UtcNow);

[GenerateSerializer]
public sealed record RefundApproved(string RequestId, decimal ApprovedAmount, string ReasonCode)
    : Synapse(nameof(RefundApproved), DateTimeOffset.UtcNow);

[GenerateSerializer]
public sealed record RefundDenied(string RequestId, string DenialReason)
    : Synapse(nameof(RefundDenied), DateTimeOffset.UtcNow);

// Orchestration commands for automated company skill creation (ingest -> crystallize -> synthesize -> verify -> install).
[GenerateSerializer]
public record CreateCompanySkill(string ProcessName) : Synapse(nameof(CreateCompanySkill), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record CompanySkillCreationResult(string ProcessName, string Version, bool Success, string Details) : Synapse(nameof(CompanySkillCreationResult), DateTimeOffset.UtcNow);

public interface ICompanySkillOrchestratorNeuron : INeuron, IHandle<CreateCompanySkill> { }
