using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.Execution;

[Alias("execution-context")]
public interface IExecutionContext : IEntity<ExecutionContextState>
{
    [Alias(nameof(Query))]
    Task<ContextEntry?> Query(ContextQuery query);

    [Alias(nameof(Ensure))]
    Task Ensure(ExecutionId executionId);

    [Alias(nameof(ApplyDelta))]
    Task ApplyDelta(ContextDelta delta);
}
