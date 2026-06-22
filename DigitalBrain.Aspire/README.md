# DigitalBrain.Aspire

MVP Aspire hosting resource and SDK for DigitalBrain (core + marketplace + self-aware LLM).

## Usage

```csharp
// The SDK's AddDigitalBrain now wires redis + orleans + ollama (model from options)
var setup = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.LlmModel = "qwen2.5-coder:1.5b";
    options.UseLocalMarketplace = true;
});

// Use the returned context for references (3 replicas on kernel)
var silo = builder.AddProject<Projects.YourKernel>("kernel")
    .WithReference(setup.Orleans)
    .WithReference(setup.Llm)
    .WithReplicas(3);

var tui = builder.AddProject<Projects.YourTui>("tui")
    .WithReference(setup.Orleans.AsClient());
```

See AppHost.cs (in the AppHost project) for usage of AddDigitalBrain + returned context.

The resource + context enable encapsulation + future fluent With* for self-aware setup.

The resource enables future full encapsulation and self-awareness features (SystemStatusNeuron connects to own Aspire MCP for observability and auto-diagnosis).

## Self-awareness
- SystemStatusNeuron activates on kernel launch
- Attempts MCP connection to own Aspire instance (using pattern from AspireAgent examples)
- Uses local LLM (Qwen) to analyze issues
- Fires FixProposal and runs isolated simulations from journal checkpoints

This is part of preparing DigitalBrain for open source with excellent structure and demo state.