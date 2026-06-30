using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Llm;

// Builds an IChatClient for a caller-supplied provider/key (the LLM config a user entered for a pack),
// distinct from the single global kernel IChatClient. Generic — carries no pack-specific meaning.
public interface IScopedChatClientFactory
{
    IChatClient Create(string provider, string? apiKey);
}
