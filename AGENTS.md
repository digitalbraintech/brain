# AGENTS.md — NeuroOS / DigitalBrain

**Reformed 2026-06-21 by applying Elon's "The Algorithm" (5 steps) in order.**

This file is the single, slim source of guidance for AI agents and contributors. Heavy prescriptive rules were questioned, duplicates deleted, and process waste removed.

## The 5 Steps (how we work)

1. **Make requirements less dumb** — Question every "always", "never", "must". Trace to real need.
2. **Delete** — Remove as much as possible. Duplication, blanket mandates, and "just in case" were cut first.
3. **Simplify** what remains.
4. **Accelerate cycle time** — fast feedback before anything else.
5. **Automate** last.

## Current rules (post-reform)

- **Fast inner loop (default)**: `dotnet build && dotnet test --filter "..."`. Do this for Protocol changes, unit logic, step defs, pure C# edits.
- **Aspire changes** (AppHost model, wiring, resource graph, observability): use the aspire MCP tools (list_apphosts, doctor, resource commands, logs) or `aspire` CLI. Prefer targeted resource commands over full restart.
- **Full distributed validation** (Ollama + replicas + end-to-end features): run intentionally before major PRs or when self-awareness / LLM flows are touched. Not after every edit.
- **Package versions**: Centralized in `Directory.Packages.props`. No more `Version="*"`. Updates are deliberate (not on every restore).
- **Context7 / docs lookup**: Use when the API surface or recent changes are unknown. Resolve first, limit queries. Not a tax on every line of code.
- **Docs in code**: Write meaningful XML docs on public API surface (DigitalBrain.Aspire etc.). Avoid vacuous repeat-the-signature comments.
- **Paths**: Use relative paths for workspace files. Avoid leaking user profile details.
- **No undefined "high severity" rituals**. Run the tests that are relevant.
- **Skills**: 
  - General .NET: full set from dotnet/skills (https://github.com/dotnet/skills) under .agents/skills.
  - Aspire: the official bundle (aspire, aspire-init, aspireify, aspire-orchestration, aspire-monitoring, aspire-deployment) from microsoft/aspire-skills. Added via `npx skills add microsoft/aspire-skills -y` and refreshed with `aspire agent init --non-interactive`.
  Local copies under .agents/skills take precedence for overrides. skills-lock.json pins sources/hashes. Use the npx or `aspire agent init` commands to refresh.

## Verification after changes

- `dotnet build`
- `dotnet test`
- `aspire doctor` (MCP or CLI)
- When the plan calls for it: targeted full run + feature specs.

See plan.md (session) for the detailed before/after analysis of the old rules.

Keep changes small, focused, and fast. Delete more than you add.