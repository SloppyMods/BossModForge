# ADR-007: Deterministic inference first; LLM assistance optional and constrained to EM edits

**Status:** Proposed
**Date:** 2026-09-18

## Context
Reproducibility and golden tests require identical output for identical input. LLMs are useful for naming mechanics, choosing among ambiguous suggestions and writing comments, but not for safety-relevant geometry.

## Decision
All inference is deterministic and tested. An optional `forge polish` step sends the Encounter Model plus evidence summaries to a model and accepts only a schema-validated JSON patch restricted to `enumName`, `name`, `comment`, and selecting among `suggested[]` options on review items. Patches touching other fields are rejected.

## Consequences
- `forge validate` never depends on an LLM; polish is fully skippable.
- Accepted patches form an audit trail.
- Builder agents can use an LLM pass without compromising determinism of the core pipeline.

## Alternatives rejected
- **LLM writes the C# directly** — unverifiable and non-deterministic.
- **LLM chooses shapes** — hallucination risk on geometry that drives player-safety hints.
