# ADR-004: Shape inference combines sheet prior and empirical fit

**Status:** Proposed
**Date:** 2026-09-18

## Context
The game's `Action` sheet provides `CastType`, `EffectRange`, `XAxisModifier` and an Omen VFX path that encodes cone angle (e.g. `gl_fan090`). But ranges are sometimes zero or misleading, omens are absent for hidden AOEs, and cast type alone does not disambiguate targeting. Hit/miss geometry from player positions at the server snapshot is precise but noisy with few instances and an uncertain snapshot lag.

## Decision
Score candidate shapes as a weighted sum: prior match 0.35, fit consistency across instances 0.55, minus ambiguity 0.10. Search snapshot lag per ability over -0.2 to +1.0 s. Snap to standard radii and angles within tolerance. When prior and fit disagree, confidence drops and both are recorded as evidence.

## Consequences
- Lumina game data is required in both plugin and CLI.
- Every shape carries a confidence band and reviewable evidence.
- More pulls monotonically improve results.

## Alternatives rejected
- **Sheet-only** — wrong for many mechanics (zero ranges, hidden AOEs, target-relative shapes).
- **Fit-only** — underdetermined with one or two instances.
- **User draws shapes** — defeats the purpose of the tool.
