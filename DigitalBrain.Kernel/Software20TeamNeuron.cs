using DigitalBrain.Core;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel;

[GrainType("awesome.se.team20.v1")]
public class Software20TeamNeuron : Neuron, ISoftware20Team
{
    public Software20TeamNeuron(ILogger<Software20TeamNeuron> logger, NeuronJournals journals) : base(logger, journals) { }

    public async Task HandleAsync(CreateSimpleApp cmd)
    {
        var name = "Neuro" + cmd.Description.Replace(" ", "").Substring(0, Math.Min(12, cmd.Description.Length));
        string code;

        var chat = ServiceProvider.GetService<IChatClient>();
        if (chat != null)
        {
            var p = $"Create a clean minimal C# console or Neuron-style simple app for: {cmd.Description}. Make it modern, self-documenting, no legacy main if possible. Output only the code.";
            var response = await chat.GetResponseAsync(p);
            var acc = response.Text;
            code = acc.Trim().Length > 10 ? acc.Trim() : ModernTemplate(name, cmd.Description);
        }
        else
        {
            code = ModernTemplate(name, cmd.Description);
        }

        await FireAsync(new SimpleAppCreated(cmd.Team, name, code));
    }

    static string ModernTemplate(string n, string d) =>
        $"// Software20 (new) - DigitalBrain/LLM assisted\n[GrainType(\"app.{n.ToLower()}\")]\npublic class {n}App : Neuron {{\n  // Self-improving simple app for: {d}\n  public {n}App() {{ /* modern defaults */ }}\n}}";
}
