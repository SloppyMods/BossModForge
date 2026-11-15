# M4 status — timeline alignment, state-machine emission, dry validate, shim compile

Part A shipped 2026-09-20. `dotnet test BossModForge.slnx -c Release`: **106 passed** (part B below brings it to 124).

## What shipped

- Anchor extraction, progressive Gotoh profile alignment, consensus steps (median/MAD, CastMulti branches, Timeout when MAD > 1.5s or support < 60%).
- Longest-coverage repeating-block fold, unrolled as `Rotation{n}` calls + trailing partial + `SimpleState(id + 0xFF0000, 10, "???")`.
- HP-threshold phase split (casting-stop / untargetable); PrimaryDeath and DIRU-once-per-pull are cues only.
- `TimelineModel` on the Encounter Model (schema v1 additive); `DeathPhase` / `SimplePhase`+`HPRatio` / `Cast(AID.X)` emission; `ShapeCSharp.Num` `f` suffix.
- Fixtures `SyntheticRotation` / `SyntheticPhases` and goldens (plus regenerated `SyntheticDungeon` / `SyntheticShapes`).

## Deviations

- Loop pick maximizes coverage (`k·L+p`), then largest `L`, then smallest `p`, so `XABCABCAB` matches the brief example (strict “largest L then smallest p” would pick a shorter suffix).
- Folding symbols ignore Cast vs Timeout (same AID still loops); otherwise support-1 trailing Timeout columns blocked the rotation fold.
- Last-phase `CDCD` in SyntheticPhases folds to `Rotation1` × 2 (4 timeline steps after Targetable pair) instead of staying 6 unrolled steps.
- Second `Rotation0` delay is `6` (pooled Circle delay), not the brief’s illustrative `5.5f`.
- Component **classes** still emit `SimpleAOEs` + `(uint)AID.X` (M2). Pin actually has `Enum aid` and no `SimpleAOEs.cs` (see `docs/UPSTREAM.md` §8.1).

## Part B outcome — `forge validate` + shim compile (2026-09-20)

`dotnet test BossModForge.slnx -c Release`: **124 passed** (106 part A + 18 part B).

- `Validation/` (`Validator`, `DryValidator`, `TimelineSimulator`, `AoeValidator`, `CoverageValidator`, `ValidationReportWriter`) and `forge validate --em --recordings [--out] [--write-em]`: timing p90 ≤ 1.0 s, missed steps ≤ 40 %, AOE precision/recall ≥ 0.95; writes `validation-report.{json,md}`; exit 0 pass / 3 fail / 4 error. `--bossmod` (real BossMod build, ADR-006) stubbed → exit 4, deferred to M4b.
- `tests/BossModForge.UpstreamShim` is a signature-accurate, logic-free stub of `awgil/ffxiv_bossmod@1391bc42` (`StateMachineBuilder`, `BossModule`, `ArenaBounds*`, `AOEShape*`, `Components/*`); `tests/BossModForge.GoldenCompile` compiles all four `Golden/*.vbm.cs` against it on every build — so the part A compile gap is closed.
- Component emission fixed against the pin: `Components.StandardAOEs(module, AID.X, new AOEShapeCone(18, 40.Degrees()))`, `RaidwideCast`, etc. (`SimpleAOEs`/`(uint)AID.X` gone); `docs/UPSTREAM.md` §8.2–8.3 record the confirmed ctors.
- Still open: BMR pin (`flavors/bmr.json` `"verified": false`), `ArenaBoundsCustom` WPos-list template vs pin ctor, `Attract` component name (`docs/UPSTREAM.md` §9).

Dalamud plugin not started → M5 (`docs/plans/m5-builder-brief.md`).
