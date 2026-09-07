namespace DigitalBrain.Abstractions.Execution;

public static class ExecutionStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class ExecutionFailureKind
{
    public const string Cancelled = "Cancelled";
    public const string DeadlineExpired = "DeadlineExpired";
    public const string UnavailableRecipient = "UnavailableRecipient";
    public const string WorkerUnavailable = "WorkerUnavailable";
    public const string RecoveryExhausted = "RecoveryExhausted";
    public const string ContractMismatch = "ContractMismatch";
    public const string ReplayMismatch = "ReplayMismatch";
    public const string Incomplete = "Incomplete";
    public const string ConflictingCompletion = "ConflictingCompletion";
}

[GenerateSerializer]
[Alias("db.ctx.execution-outcome")]
public sealed record ExecutionOutcome(
    [property: Id(0)] string Status,
    [property: Id(1)] string? ResultJson = null,
    [property: Id(2)] string? ResultType = null,
    [property: Id(3)] string? Error = null,
    [property: Id(4)] string? FailureKind = null);
