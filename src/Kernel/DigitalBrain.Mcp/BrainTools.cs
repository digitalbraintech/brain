using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
public sealed class BrainTools(BrainOperations operations, SessionPrincipal session)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerTool(Name = "fire"), Description(
        "Fire a signal from your Session neuron. A signal is a `type` (letters only, vocabulary such as Note, Confirmed, Decision) "
        + "and a JSON `body` up to 64 KB. With `to`, it goes to exactly that neuron and creates the synapse if missing; "
        + "without `to`, it follows every synapse of that type you already have. Neurons exist as soon as they are named. "
        + "Put identity in the neuron name (run-tests-before-commit), never in the type. Returns the signal id, correlation and how many neurons received it.")]
    public Task<string> Fire(
        [Description("Signal type: letters only, e.g. Note")] string type,
        [Description("JSON body, e.g. {\"text\":\"run tests before commit\"}. Empty means {}.")] string body,
        [Description("Target neuron name, e.g. run-tests. Omit to follow all your synapses of this type.")] string? to = null,
        [Description("Optional correlation id (GUID) to tie this to an earlier signal.")] string? correlation = null,
        CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(
            await operations.FireAsync(session.Name, new(type, body, to, correlation), cancellationToken).ConfigureAwait(false), Json));

    [McpServerTool(Name = "connect"), Description(
        "Create a synapse: from one neuron to another for one signal type. Idempotent. Use it to build structure, "
        + "e.g. connect topic `git` to `run-tests-before-commit` for `Note`, then recall by reading `git`'s synapses and following them.")]
    public Task<string> Connect(
        [Description("Source neuron name")] string from,
        [Description("Target neuron name")] string to,
        [Description("Signal type the synapse carries, letters only")] string type,
        CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            await operations.ConnectAsync(new(from, to, type), cancellationToken).ConfigureAwait(false);
            return $"connected {from} --{type}--> {to}";
        });

    [McpServerTool(Name = "disconnect"), Description(
        "Remove a synapse: from one neuron to another for one signal type. Succeeds even if the synapse did not exist, "
        + "so it is safe to call twice. Nothing else about either neuron changes; state and journals are kept.")]
    public Task<string> Disconnect(
        [Description("Source neuron name")] string from,
        [Description("Target neuron name")] string to,
        [Description("Signal type the synapse carries, letters only")] string type,
        CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            await operations.DisconnectAsync(new(from, to, type), cancellationToken).ConfigureAwait(false);
            return $"disconnected {from} --{type}--> {to}";
        });

    [McpServerTool(Name = "read"), Description(
        "Read a neuron without changing anything. Returns its state (latest signal per type), its synapses, and its incoming and outgoing journals. "
        + "Recall pattern: read a topic's synapses, follow each target, read its state. "
        + "`what` narrows to state | synapses | incoming | outgoing. `after` is a journal sequence to resume from. "
        + "`timeoutSeconds` makes an incoming/outgoing read wait for the next entry. Your own Session neuron is named after your principal (default `claude`).")]
    public Task<string> Read(
        [Description("Neuron name, e.g. git or run-tests-before-commit")] string neuron,
        [Description("state | synapses | incoming | outgoing; omit for all four")] string? what = null,
        [Description("Journal sequence to read after; 0 for the retained window")] long after = 0,
        [Description("Seconds to wait for a new journal entry, at most 60; 0 returns immediately")] int timeoutSeconds = 0,
        CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(
            await operations.ReadAsync(new(neuron, what, after, timeoutSeconds), cancellationToken).ConfigureAwait(false), Json));

    // A rejection is advice, so the caller must see the kernel's own wording, not a generic
    // "an error occurred". McpException is the one exception the SDK relays verbatim.
    private static async Task<string> Guard(Func<Task<string>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new ModelContextProtocol.McpException(error.Message, error);
        }
    }
}
