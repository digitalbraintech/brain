# Task 7d Report — Telegram Transport Hardening + Hygiene

**Branch:** `spec/telegram-llm-experience`
**Date:** 2026-06-30

## Changes Made

### 1. FUNCTIONAL — Transport pack identity wired (Item 1)

**File:** `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` — `WireTelegramTransport`

Added two env var injections at the end of `WireTelegramTransport`:

```csharp
transport = transport
    .WithEnvironment("Telegram__PackName", "DigitalBrain.Telegram.Responder")
    .WithEnvironment("Telegram__ConfigScope", "default");
```

**Why `Telegram__PackName` (not `DigitalBrain__PackName`):** `TelegramTransportOptions` is bound from the `"Telegram"` config section (`builder.Configuration.GetSection("Telegram")`), so the env key prefix is `Telegram__`. The default value in `TelegramTransportOptions.PackName` was `"TelegramResponderNeuron"` — the wrong name. The marketplace pack name from `MarketplaceSeeds` and the `ConfigPack` constant inside the pack code is `"DigitalBrain.Telegram.Responder"`.

### 2. HYGIENE — `ScopedChatClientFactory` graceful failure (Item 2a)

**Files:** `DigitalBrain.Kernel/Llm/IScopedChatClientFactory.cs`, `DigitalBrain.Kernel/Llm/ScopedChatClientFactory.cs`, `DigitalBrain.Kernel/LlmResponderNeuron.cs`

- Changed `IScopedChatClientFactory.Create` return type from `IChatClient` to `IChatClient?`.
- `ScopedChatClientFactory.Create`: replaced the `throw new InvalidOperationException(...)` for missing openai key with `logger.LogWarning(...); return null;` (no key material logged).
- `LlmResponderNeuron.ResolveChatClientAsync`: when `factory.Create` returns `null`, immediately falls back to `ServiceProvider.GetService<IChatClient>()` without caching the null entry.

Result: empty/whitespace openai key → warning logged, reply still produced via global Ollama client. No silence.

### 3. HYGIENE — `NeuronTestSiloConfigurator` test-safety (Item 2b)

**File:** `DigitalBrain.Tests/TestSupport/NeuronTestSiloConfigurator.cs`

Replaced `services.AddSingleton<IScopedChatClientFactory, ScopedChatClientFactory>()` (real network-dependent factory) with `services.AddSingleton<IScopedChatClientFactory, NoOpScopedChatClientFactory>()`.

`NoOpScopedChatClientFactory` always returns `null`, delegating to the global `IChatClient`. Tests that need the recording factory (e.g. `ScopedLlmResponderSiloConfigurator`) already override this via their own `ISiloConfigurator`.

### 4. CLEANUP — `Program.cs` egress comment corrected (Item 3a)

**File:** `DigitalBrain.Kernel/Program.cs`

Removed the inaccurate claim "the timeline is a silo-local MemoryStream — a gRPC client connected to one replica observes only Signals broadcast on that replica." Replaced with the proven behavior: Orleans MemoryStream explicit subscriptions deliver cluster-wide (as proven by `HomeFeedCrossSiloTests.Broadcast_On_Silo0_Received_On_Silo1`).

### 5. `AddTelegramBot` references

Only in historical plan/spec docs (`docs/superpowers/`), not in live code or API comments. Skipped per task instructions (would sprawl into docs-only files).

## Tests Added

**File:** `DigitalBrain.Tests/Kernel/LlmResponderScopedConfigTests.cs`

Added `NullScopedChatClientFactory`, `NullScopedLlmResponderSiloConfigurator`, and test:

`AskLlm_scoped_factory_returns_null_falls_back_to_global_client` — stores `{llm_provider: "openai", llm_key: ""}`, broadcasts a scoped `AskLlm`, asserts the reply is `"ANSWER:hi"` from the global `AnswerPrefixChatClient` (not silence), and that the factory was called once.

## Test Results

- `--filter "FullyQualifiedName~LlmResponder"`: **4/4 passed** (including new fallback test)
- `--filter "FullyQualifiedName~Telegram"`: **9/9 passed**
- `--filter "FullyQualifiedName~Gateway"`: **17/17 passed**, 1 skipped (pre-existing skip)
- `dotnet build Brain.slnx`: **0 errors**, 17 pre-existing warnings (unchanged)

## Key Decision

The transport config keys must be `Telegram__PackName` / `Telegram__ConfigScope` (not `DigitalBrain__PackName`) because `TelegramTransportOptions` is bound from `builder.Configuration.GetSection("Telegram")`. The `InternalServiceKey` already uses the `DigitalBrain__` prefix because it's bound separately via `builder.Configuration["DigitalBrain:InternalServiceKey"]`.
