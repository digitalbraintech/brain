using DigitalBrain.Core;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.llm.qwen.v1")]
public class LlmNeuron : Neuron, ILlmNeuron
{
    public LlmNeuron(ILogger<LlmNeuron> logger, NeuronJournals journals)
        : base(logger, journals)
    {
    }

    public async Task HandleAsync(LlmPrompt prompt)
    {
        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat == null)
        {
            await FireAsync(new LlmResponse(prompt.Prompt, "[no local llm client]", "none"));
            return;
        }

        var options = string.IsNullOrWhiteSpace(prompt.PreferredModel)
            ? null
            : new Microsoft.Extensions.AI.ChatOptions { ModelId = prompt.PreferredModel };
        var response = await chat.GetResponseAsync(prompt.Prompt, options);
        await FireAsync(new LlmResponse(prompt.Prompt, response.Text.Trim(), prompt.PreferredModel ?? "qwen2.5-coder:1.5b"));
    }
}


