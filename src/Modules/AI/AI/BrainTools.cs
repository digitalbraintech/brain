using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using McpOperations = DigitalBrain.Mcp.BrainOperations;
using McpReadRequest = DigitalBrain.Mcp.ReadRequest;

namespace DigitalBrain.AI;

/// <summary>
/// The four brain operations as <see cref="AIFunction"/>s bound to one agent's identity, so a
/// model inside a neuron talks to the graph exactly as an MCP client does. The descriptions
/// are the MCP server's, word for word: the same tools should read the same everywhere.
/// </summary>
internal static class BrainTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static IEnumerable<AIFunction> For(AgentNeuron agent, McpOperations reads)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(reads);

        yield return AIFunctionFactory.Create(
            (
                [Description("Signal type: letters only, e.g. Note")] string type,
                [Description("JSON body, e.g. {\"text\":\"run tests before commit\"}. Empty means {}.")] string body,
                [Description("Target neuron name, e.g. run-tests. Omit to follow all your synapses of this type.")] string? to = null,
                [Description("Optional correlation id (GUID) to tie this to an earlier signal.")] string? correlation = null)
                => agent.FireFromToolAsync(type, body, to, correlation),
            "fire",
            "Fire a signal from your Session neuron. A signal is a `type` (letters only, vocabulary such as Note, Confirmed, Decision) "
            + "and a JSON `body` up to 64 KB. With `to`, it goes to exactly that neuron and creates the synapse if missing; "
            + "without `to`, it follows every synapse of that type you already have. Neurons exist as soon as they are named. "
            + "Put identity in the neuron name (run-tests-before-commit), never in the type. Returns the signal id, correlation and how many neurons received it.");

        yield return AIFunctionFactory.Create(
            (
                [Description("Source neuron name")] string from,
                [Description("Target neuron name")] string to,
                [Description("Signal type the synapse carries, letters only")] string type)
                => agent.ConnectFromToolAsync(from, to, type, connect: true),
            "connect",
            "Create a synapse: from one neuron to another for one signal type. Idempotent. Use it to build structure, "
            + "e.g. connect topic `git` to `run-tests-before-commit` for `Note`, then recall by reading `git`'s synapses and following them.");

        yield return AIFunctionFactory.Create(
            (
                [Description("Source neuron name")] string from,
                [Description("Target neuron name")] string to,
                [Description("Signal type the synapse carries, letters only")] string type)
                => agent.ConnectFromToolAsync(from, to, type, connect: false),
            "disconnect",
            "Remove a synapse: from one neuron to another for one signal type. Succeeds even if the synapse did not exist, "
            + "so it is safe to call twice. Nothing else about either neuron changes; state and journals are kept.");

        yield return AIFunctionFactory.Create(
            async (
                [Description("Neuron name, e.g. git or run-tests-before-commit")] string neuron,
                [Description("state | synapses | incoming | outgoing; omit for all four")] string? what = null,
                [Description("Journal sequence to read after; 0 for the retained window")] long after = 0)
                // A reaction may read but never wait: the timeout an MCP client may pass is 0 here.
                => JsonSerializer.Serialize(await reads.ReadAsync(new McpReadRequest(neuron, what, after, 0)).ConfigureAwait(false), Json),
            "read",
            "Read a neuron without changing anything. Returns its state (latest signal per type), its synapses, and its incoming and outgoing journals. "
            + "Recall pattern: read a topic's synapses, follow each target, read its state. "
            + "`what` narrows to state | synapses | incoming | outgoing. `after` is a journal sequence to resume from. "
            + "A read inside a turn never waits.");
    }
}
