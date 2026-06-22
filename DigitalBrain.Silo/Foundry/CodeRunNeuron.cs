using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Silo.Foundry;

[GrainType("digitalbrain.coderun.v1")]
public class CodeRunNeuron : Neuron, ICodeRunNeuron
{
    public CodeRunNeuron(ILogger<CodeRunNeuron> logger) : base(logger) { }

    public async Task HandleAsync(RunGeneratedCode cmd)
    {
        var executor = ServiceProvider.GetRequiredService<ICodeExecutor>();
        var result = executor.Execute(cmd.Source, cmd.Entrypoint);
        await FireAsync(new CodeRunResult(result.Success, result.Output, result.Error));
    }
}
