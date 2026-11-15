# Architecture Decision Records — BossModForge

Format: one file per decision, `NNN-short-title.md`, sections *Context / Decision / Consequences / Alternatives rejected*. Status is `Proposed` until a builder confirms the relevant `VERIFY` items in the spec, then `Accepted`.

| ADR | Title | Status |
|---|---|---|
| [001](001-input-is-bossmod-replay-logs.md) | Input is BossMod replay logs; ACT logs v2; video out of scope | Proposed |
| [002](002-own-replay-parser.md) | Core owns its replay parser; conformance-tested against BossMod's | Proposed |
| [003](003-encounter-model-is-canonical.md) | Encounter Model JSON is canonical; C# emitted per flavor profile | Proposed |
| [004](004-shape-inference-prior-times-fit.md) | Shape inference combines sheet prior and empirical fit | Proposed |
| [005](005-standard-components-only.md) | Emit only standard BossMod components | Proposed |
| [006](006-closed-loop-validation.md) | Validate by running the module in a real BossMod build against the recordings | Proposed |
| [007](007-deterministic-first-llm-last.md) | Deterministic inference first; LLM assistance optional and constrained to EM edits | Proposed |
| [008](2026-09-20-adr-008-polish-field-allowlist.md) | `forge polish` field allowlist — names and comments only; geometry/mechanics/timeline untouched | Proposed |
| [009](2026-09-20-adr-009-lumina-in-cli.md) | Lumina lives in the CLI — Core stays game-data-library-free | Proposed |
