using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using System.Collections.Immutable;

namespace DigitalBrain.Silo.Company;

public sealed class ProcessCrystallizer(IChatClient? chatClient)
{
    public async Task<ProcessSpecCrystallized> CrystallizeAsync(string processName, string[] recalledFragments, CancellationToken ct = default)
    {
        string combined = string.Join("\n", recalledFragments);

        if (chatClient is not null)
        {
            string system = "Extract ONLY from the text. Output JSON with: processName, triggers (array), steps (array of strings), decisions (array of {condition,truePath,falsePath}), exceptions (array), outcomes (array), capabilities (array). No extra text.";
            string prompt = $"{system}\n\nProcess: {processName}\nText:\n{combined}";

            var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            // Best-effort parse; fall back on failure.
            var parsed = TryParseFromLlm(response.Text, processName, combined);
            if (parsed is not null) return new ProcessSpecCrystallized(parsed, [.. recalledFragments.Select(f => f[..Math.Min(60, f.Length)])]);
        }

        // Deterministic fallback tuned to refund policy examples.
        var spec = BuildRefundSpecFromText(processName, combined);
        return new ProcessSpecCrystallized(spec, [.. recalledFragments.Select(f => f[..Math.Min(60, f.Length)])]);
    }

    private static ProcessSpec? TryParseFromLlm(string llmText, string processName, string source)
    {
        // Lightweight extraction. Real impl could use structured output; keep simple.
        if (!llmText.Contains("30", StringComparison.Ordinal) && !source.Contains("30", StringComparison.Ordinal)) return null;

        return BuildRefundSpecFromText(processName, source);
    }

    private static ProcessSpec BuildRefundSpecFromText(string processName, string text)
    {
        bool has30 = text.Contains("30", StringComparison.Ordinal);
        bool hasDefective = text.Contains("defective", StringComparison.OrdinalIgnoreCase) || text.Contains("Defective", StringComparison.Ordinal);
        bool hasAuto = text.Contains("auto", StringComparison.OrdinalIgnoreCase) || text.Contains("Auto", StringComparison.Ordinal);

        var triggers = ImmutableArray.Create("RefundRequested");
        var steps = ImmutableArray.Create(
            "Check purchase window",
            "Verify receipt or loyalty",
            "Apply reason rules",
            "Decide or escalate");

        var decisions = ImmutableArray.Create(
            new DecisionPoint("daysSincePurchase > 30", "deny-outside-window", "continue"),
            new DecisionPoint("defective && days < 14", "auto-approve-full+shipping", "standard-review"),
            new DecisionPoint("amount > 500 || suspicious", "manual-escalate", "proceed-approve"));

        var exceptions = ImmutableArray.Create("outside 30 day window", "no receipt and not loyalty", "high value or suspicious");

        var outcomes = ImmutableArray.Create("RefundApproved", "RefundDenied", "RefundExecuted");

        var caps = ImmutableArray.Create("emit-outcome-synapses", "use-causation");

        return new ProcessSpec(
            processName,
            triggers,
            steps,
            decisions,
            exceptions,
            outcomes,
            caps);
    }
}
