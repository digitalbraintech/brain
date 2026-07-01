using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Foundry;

[GrainType("digitalbrain.coderun.v1")]
public class CodeRunNeuron : Neuron, ICodeRunNeuron
{
    public CodeRunNeuron(ILogger<CodeRunNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(RunGeneratedCode cmd)
    {
        var executor = ServiceProvider.GetRequiredService<ICodeExecutor>();
        var result = executor.Execute(cmd.Source, cmd.Entrypoint);
        await FireAsync(new CodeRunResult(result.Success, result.Output, result.Error));
    }
}

