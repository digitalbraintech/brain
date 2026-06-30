using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Llm;

// Builds an IChatClient for a caller-supplied provider/key (the LLM config a user entered for a pack),
// distinct from the single global kernel IChatClient. Returns null when the input is insufficient
// (e.g. openai with no key) so callers can fall back to the global IChatClient gracefully.
public interface IScopedChatClientFactory
{
    IChatClient? Create(string provider, string? apiKey);
}
