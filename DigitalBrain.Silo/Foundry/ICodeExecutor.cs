namespace DigitalBrain.Silo.Foundry;

public record CodeExecutionResult(bool Success, string Output, string Error);

public interface ICodeExecutor
{
    CodeExecutionResult Execute(string source, string entrypoint);
}
