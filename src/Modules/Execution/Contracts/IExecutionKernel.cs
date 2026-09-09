namespace DigitalBrain.Execution;

// In-silo return-value surface. Owners and scripts use IExecution + RequestAsync.
[Alias("execution.runtime")]
public interface IExecutionKernel : IGrainWithStringKey
{
    Task<ExecutionProjection> LoadProjection();
}
