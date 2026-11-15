# ADR-008: `forge polish` field allowlist — names and comments only; geometry/mechanics/timeline untouched

**Status:** Proposed
**Date:** 2026-09-20
**Parent:** [ADR-007](007-deterministic-first-llm-last.md)

## Context
ADR-007 allows an optional LLM pass over the Encounter Model, constrained to schema-validated patches. An unconstrained patch language (e.g. full JSON Patch) would let a model — or a hostile/buggy endpoint — rewrite shapes, mechanic classes, or the timeline, corrupting safety-relevant geometry that the deterministic pipeline inferred and the goldens pin. At the same time, renaming abilities/participants and picking among `suggested[]` review options is exactly where an LLM adds value over deterministic inference.

## Decision
`Polisher.Apply` accepts only these patch paths:

- `/abilities/{actionId}/enumName`, `/abilities/{actionId}/name`, `/abilities/{actionId}/comment`
- `/participants/{oid}/enumName`, `/participants/{oid}/name`, `/participants/{oid}/comment`
- `/review/{reviewId}/resolution` — the value must be one of that review item's `suggested[]` values

`enumName` values are sanitised with `CSharpIdentifier.Sanitise` and rejected on collision with an existing AID/OID (mirroring `OverridesApplier`, including the rule that the primary OID keeps the name `Boss`). Everything outside the allowlist, every non-string value where a string is required, and every unknown id is **rejected with a reason string** — never fatal, never applied. Accepted and rejected reason strings are ordinal-sorted; the function is pure, deterministic, and never throws on patch content. The polisher does not touch geometry (`shape`/`shapeFit`), mechanic `class`, arena bounds, or the timeline.

`forge polish <em.json> --provider file|http` fetches patches (a JSON array from `--patches`, or a 30 s `POST` from `--endpoint` with `Authorization: Bearer $env[--api-key-env]`), applies them, writes the polished EM (`--out`), lists accepted/rejected patches on stdout, and exits `0` even when every patch is rejected — rejected patches are a normal outcome, not an error (exit `4` is reserved for unusable input: missing files, malformed patch JSON, missing `--endpoint`/`--patches`).

## Consequences
- Hostile or hallucinated patches degrade to a log line; the EM stays schema-valid and deterministic-pipeline sound.
- The audit trail is complete: accepted patches are listed with before/after path, rejected ones with reasons.
- The LLM prompt is simple and stable: serialised EM + review items + the allowlist text; no few-shot geometry examples can leak in.
- New polishable fields require extending the allowlist in code, the schema, and this ADR — deliberate friction, not an oversight.

## Alternatives rejected
- **Full JSON Patch (RFC 6902)** — expressive enough to rewrite `shape.radius` or delete timeline steps; the allowlist is the whole point.
- **Reject-the-batch on any bad patch** — one hallucinated patch would discard all good ones; per-patch accept/reject keeps the useful signal.
- **LLM edits emitted C# directly** — already rejected in ADR-007; restated here because polish is where that temptation recurs.
