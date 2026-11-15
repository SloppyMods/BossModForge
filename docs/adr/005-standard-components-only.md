# ADR-005: Emit only standard BossMod components

**Status:** Proposed
**Date:** 2026-09-18

## Context
BossMod ships reusable components — `SimpleAOEs`, `RaidwideCast`, `SingleTargetCast`/`BaitAwayCast`, `SpreadFromCastTargets`, `StackWithCastTargets`/`StackWithIcon`, `KnockbackFromCastTarget`, `CastGaze`, `GenericTowers`, `StandardChasingAOEs` — that cover most dungeon, trial and raid mechanics. Generated bespoke component code would be unverifiable and unmaintainable.

## Decision
The emitter maps mechanic classes only onto standard components via the flavor profile. Anything else becomes a `// TODO(forge:Rn)` comment plus a review item in the Encounter Model. `--strict` refuses to emit while High-severity review items remain.

## Consequences
- The validation coverage metric shows exactly what is not handled.
- Output is idiomatic and readable to BossMod maintainers.
- Complex mechanics remain manual work, explicitly listed in `FORGE.md`.

## Alternatives rejected
- **Generate custom component classes** — unverifiable, unmaintainable.
- **Generic "everything is a circle" fallback** — produces unsafe hints.
