# Grok CLI benchmark of the MCP surface

Run 2026-09-08 against the Silo started by `aspire start` (kernel at `http://localhost:5080`,
MCP at `/mcp`). Client: Grok CLI (`grok-4.6`, headless `-p`, `--no-subagents`,
`--disable-web-search`), registered with
`grok mcp add brain "http://localhost:5080/mcp?principal=grok" -t http -s project`.
The model was told only that four tools exist and to use nothing else. It had never seen this
repository. Each prompt ran as a brand-new session.

## Scorecard

| # | Prompt (verbatim intent) | Tool calls made, in order | Score |
|---|---|---|---|
| 1 | Remember that I always want tests run before a commit, and file it under a topic called git. | `fire` Note → `run-tests-before-commit`; `connect` git → run-tests-before-commit for Note | 2 |
| 2 | *(after `aspire stop` / `aspire start`)* What do you know about how I want to work with git? | `read` git; `read` run-tests-before-commit | 2 |
| 3 | Correction: only run the affected tests, not the whole suite. Update it; keep the old version discoverable. | `read` git; `read` run-tests-before-commit; `fire` Note → run-tests-before-commit **with the original correlation id**; `read` git synapses; `read` run-tests-before-commit | 2 |

Scoring: 0 = wrong or gave up, 1 = right after a retry or wrong tool first, 2 = right first time.

## What the graph looked like afterwards (read as principal `claude`)

- `git` has one synapse: `git --Note--> run-tests-before-commit`.
- `run-tests-before-commit` latest `Note` is the corrected text, from `grok`.
- Its incoming journal holds both notes, sequences 1 and 2, sharing a correlation id.
- `grok`'s outgoing journal shows the two fires; `claude`'s Session neuron has no synapses,
  which confirms per-principal Sessions over HTTP (`?principal=`).

## Observations

- Prompt 1: the model chose the exact intended shape (note on a named neuron, topic connected to
  it) from the tool descriptions alone. It did not try to invent a `create` or a `list`.
- Prompt 2: it followed the recall pattern written in the `read` description word for word.
- Prompt 3: unprompted, it reused the original signal's correlation id to tie the correction to
  the old note, and explained why it left the synapse in place.
- No error message was ever hit, so the membrane wording is untested by this run; the test suite
  covers it.
- Each run began with Grok's own `search_tool` to load MCP schemas; that is a Grok CLI mechanic,
  not a tool of ours.

## Description changes made as a result

None. All three prompts scored 2 on the first round, so the tool descriptions from Task 5 stand.
