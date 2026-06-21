using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace DigitalBrain.Silo.Ino;

// INO: ultra-context personal assistant neuron.
// Uses dual journals as primary memory (recent + full history), spawns KernelTasks for actions,
// can drive checkpoints/branches for planning. Context is multi-scale via recency + LLM summary.
[GrainType("ino.personal.v1")]
public class InoNeuron : Neuron, IInoNeuron
{
    private readonly List<string> _activeTasks = new();
    private string _focus = string.Empty;

    public InoNeuron(ILogger<InoNeuron> logger) : base(logger) { }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        // Bootstrap context from own recent out journal on activate.
        var recent = OutgoingJournal.TakeLast(5).OfType<InoResponse>().LastOrDefault();
        if (recent != null) _focus = recent.Prompt;
    }

    public async Task HandleAsync(InoRequest req)
    {
        var ctx = BuildContext(req.Prompt);
        var reply = await ReasonWithLlmAsync(req.Prompt, ctx);

        var taskIds = await OrchestrateActionsIfNeededAsync(req.Prompt, reply);

        // Enrich response with any fresh task results for better context to caller.
        var enriched = reply;
        if (taskIds.Count > 0)
        {
            var infos = new List<string>();
            foreach (var tid in taskIds.Where(t => !t.StartsWith("branch")))
            {
                try
                {
                    var kt = GrainFactory.GetGrain<IKernelTask>(tid);
                    var inf = await kt.GetInfoAsync();
                    if (inf.Result != null) infos.Add($"{tid}={inf.Result}");
                }
                catch { }
            }
            if (infos.Count > 0) enriched += " | " + string.Join("; ", infos);
        }

        await FireAsync(new InoResponse(req.Prompt, enriched, taskIds.ToArray()));
        _focus = req.Prompt;
    }

    public async Task<string> AskAsync(string prompt)
    {
        await FireAsync(new InoRequest(prompt));
        await Task.Delay(120);
        var tl = await GetOutgoingTimelineAsync();
        var last = tl.OfType<InoResponse>().LastOrDefault();
        return last?.Response ?? "processed";
    }

    private string BuildContext(string prompt)
    {
        // Multi-scale: own dual journals (recent out/in) + task results as long-term memory.
        var recentOut = OutgoingJournal.TakeLast(8).Select(s => s.Type + ":" + (s as dynamic)?.ToString() ?? s.Type).ToList();
        var recentIn = IncomingJournal.TakeLast(5).Select(s => "in:" + s.Type).ToList();
        var taskCtx = string.Join(";", _activeTasks.Select(tid => "task:" + tid));
        return $"focus:{_focus}\nprompt:{prompt}\nrecent-out:{string.Join(";", recentOut)}\nrecent-in:{string.Join(";", recentIn)}\ntasks:{taskCtx}";
    }

    private async Task<string> ReasonWithLlmAsync(string prompt, string context)
    {
        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        if (llm == null) return $"[no-llm] INO would act on: {prompt} (ctx len {context.Length})";

        llm.SelectedModel = "qwen2.5-coder:1.5b";
        var sys = "You are INO, DigitalBrain's personal OS assistant. Use provided context from neuron journals. Be concise, propose kernel tasks or branches when useful. Output action if any as 'TASK: desc' or 'BRANCH: whatif'.";
        var full = sys + "\nCTX:\n" + context + "\nUSER: " + prompt;
        var acc = "";
        await foreach (var ch in llm.GenerateAsync(full))
            if (ch?.Response is string t) acc += t;
        return acc.Trim();
    }

    private async Task<List<string>> OrchestrateActionsIfNeededAsync(string prompt, string reply)
    {
        var created = new List<string>();
        if (reply.Contains("TASK:", StringComparison.OrdinalIgnoreCase))
        {
            var taskDesc = reply.Split("TASK:", 2)[1].Split('\n')[0].Trim();
            var tid = "task-" + Guid.NewGuid().ToString("N")[..8];
            var kt = GrainFactory.GetGrain<IKernelTask>(tid);
            await kt.FireAsync(new RunKernelTask(tid, taskDesc));
            // Capture result for context (poll briefly in prototype)
            await Task.Delay(80);
            var info = await kt.GetInfoAsync();
            if (info.Result != null)
            {
                await FireAsync(new KernelTaskProgress(tid, "result:" + info.Result));
            }
            created.Add(tid);
            _activeTasks.Add(tid);
        }
        if (reply.Contains("BRANCH:", StringComparison.OrdinalIgnoreCase) || prompt.Contains("what if", StringComparison.OrdinalIgnoreCase))
        {
            var cp = await CreateCheckpointAsync();
            var bid = await BranchAsync(cp);
            await FireAsync(new KernelTaskProgress("branch", "created:" + bid.Value));
            created.Add("branch:" + bid.Value);
        }
        return created;
    }
}