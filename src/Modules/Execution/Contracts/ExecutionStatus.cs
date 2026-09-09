namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.status")]
public enum ExecutionStatus : byte
{
    Running = 0,
    Completed = 1,
    Failed = 2,
}
