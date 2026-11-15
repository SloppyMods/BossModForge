# ADR-006: Validate by running the module in a real BossMod build against the recordings

**Status:** Proposed
**Date:** 2026-09-18

## Context
Only BossMod's own `StateMachine` and component code can prove that a generated module transitions on time and predicts hits correctly. Manual in-game testing is slow and cannot run in CI.

## Decision
`forge validate` copies emitted files into a pinned BossMod checkout, builds it, and runs a headless ReplayValidator host (modeled on upstream replay tooling) over the source recordings. It scores transition timing (p90 <= 1.0 s), AOE precision/recall (>= 0.95), coverage, and exceptions; results feed back into Encounter Model confidence.

## Consequences
- Builders need the BossMod checkout and the .NET SDK; no game client is required.
- Passing validation is the definition of done for a generated module.
- Compile errors map back to flavor-profile bugs and are caught before users see them.

## Alternatives rejected
- **Forge-internal simulator** — would re-implement BossMod semantics and drift from upstream.
- **Manual in-game verification only** — not automatable, not repeatable.
