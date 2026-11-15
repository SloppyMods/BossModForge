---
tags:
  - type/status
  - project/bossmodforge
  - status/active
type: status
project: bossmodforge
status: active
milestone: completion
plan: ./2026-09-20-completion-plan.md
---

# Completion status — definition-of-done sweep (2026-09-21)

All 9 tasks of `2026-09-20-completion-plan.md` shipped. `./scripts/build.sh`: **197 passed, 0 failed** (149 pre-completion + 48 new; goldens byte-identical throughout).

## What shipped (per task)

- **Task 1 — VERIFY closure:** all 19 spec VERIFY tags resolved against the pins; `docs/UPSTREAM.md` is the single verification record (§9 "Closed VERIFY", §10 BMR record). Spec has zero open VERIFY items.
- **Task 2 — BMR flavor:** `flavors/bmr.json` pinned at `b919e0970d6559de280c40defb4117dd7275c961`, `verified: true`; component templates use `(uint)AID.X` / `SimpleAOEs`; `BmrProfile` honors the flag. +2 tests (`ModuleEmitterBmrTests`: API smoke + vbm/bmr diff guard).
- **Task 3 — M6 `forge merge`:** `Recording.Concat`, `ForgePipeline.Analyze(IReadOnlyList<Recording>)`, `MergeCommand` with `--out`/`--em`/`--select-boss`; associativity golden + door-boss fixture. +8 tests (`MergeTests` 3, `MergeCommandTests` 5).
- **Task 4 — M6 degraded + perf:** lenient v25+ parse clamped to v24 gates with a `Degraded` `UnsupportedVersion` issue; `< 10` still rejected. Streaming perf gate env-gated. +3 tests (`DegradedVersionTests` 2, `StreamingPerfTests` 1; perf test skips unless `FORGE_PERF=1`).
- **Task 5 — M7 `forge polish`:** `Polisher` (allowlist: `enumName`/`name`/`comment` + review `resolution` ∈ `Suggested`), `FilePatchProvider`/`HttpPatchProvider`, `PolishCommand`; ADR-008. +16 tests (`PolisherTests` 10, `PolishCommandTests` 6).
- **Task 6 — `forge conform`:** `ConformComparer`, `BossModReflection`, `ConformCommand` with `--bossmod-dll`/`--json`/`--selftest`; exit 0/3/4. +5 tests (`ConformTests`).
- **Task 7 — LuminaGameData:** `LuminaGameData : IGameData` in Cli (`Lumina 5.8.0` + `Lumina.Excel 7.2.3`), `--game-path` on `infer`, graceful degradation; ADR-009. +3 tests (`InferCommandGameDataTests`).
- **Task 8 — M4b `--bossmod`:** `BossmodBuildValidator`, `CompileErrorMapper`, ReplayValidator host template (embedded resource); `--bossmod` checkout gating + `--flavor` on `validate`. +11 tests (`CompileErrorMapperTests` 7, `ValidateCommandBossmodTests` 4).
- **Task 9 — docs sweep:** README (all 7 subcommands, `--game-path`, `--bossmod` gating, `FORGE_PERF=1`, two-solution build matrix), CONTEXT.md (Merge/Conform/Degraded parse glossary rows; no stale milestone claims), AGENTS.md (Purpose → M0–M7), UPSTREAM.md §11 extended with the plugin-compile entry, this status doc.

## Deviations / rulings

- **Merge associativity + Sources normalization:** merged EM equals concatenating recordings and analyzing once, modulo `sources[]` (one source per input recording by design). Actor instance ids must be distinct across recordings — fixtures guarantee it; the real-world collision caveat is recorded in `FORGE.md` output.
- **Exit codes on review/schema:** `forge merge` exits 2 on High-severity review items (per plan) and 3 if the merged EM fails schema validation; `forge polish` exits 3 if the polished EM fails schema validation (polish itself never fails on rejected patches — they are listed on stdout with reasons).
- **Lumina in Cli (ADR-009):** Core stays free of Lumina; Cli owns the only disk-based game-data implementation (`Lumina 5.8.0` + `Lumina.Excel 7.2.3`); the plugin uses `CachedGameData` via Dalamud.
- **Schema additions (ADR-008):** additive optional `comment` on abilities/participants and `resolution` on review items in `schema/em.v1.schema.json`; records serialize automatically.
- **Conform absent-DLL:** `forge conform` without `--bossmod-dll` prints the documented `error: conformance requires --bossmod-dll …` message and exits 4 (documented choice per M0 acceptance).
- **ModuleEmitter unverified-flavor review fix:** emitting against a flavor profile with `verified: false` produces a High review item instead of silently emitting (guards the bmr pre-pin state).
- **Degraded parse:** recordings newer than v24 parse leniently (v24 gates + `Degraded` issue) rather than returning zero ops; `< 10` remains rejected.

## Quality-gate ruling

The external `quality-gate` CLI is **not installed on this machine**; `quality-gate.json` is a config convention whose `report_dir` (`icm/stages/04-verify/output`) points at an external pipeline. Equivalent release-level verification performed here:

- `changed-files` — this status doc is the changed-files inventory for the sweep (README.md, CONTEXT.md, AGENTS.md, docs/UPSTREAM.md, docs/plans/2026-09-20-completion-status.md; no source changes in Task 9).
- `build` — `dotnet build BossModForge.slnx -c Release`: succeeded, 0 warnings, 0 errors.
- `test` — `dotnet test BossModForge.slnx -c Release`: **197 passed, 0 failed, 0 skipped**.

## TODO(forge) disposition

`grep -rn "TODO(forge)" src/ tests/` → **2 hits**, both deliberate: `src/BossModForge.Core/Emit/ModuleEmitter.cs:166` emits `// TODO(forge) …` into generated C# for mechanic classes with no standard component (the Review-marker mechanism), and `tests/BossModForge.Tests/Emit/ModuleEmitterComponentsTests.cs:50` asserts that marker. No plan-gap placeholders remain.

## Machine-gated verification

See `docs/UPSTREAM.md` §11 (M4b `--bossmod` e2e, conform reflection happy-path, LuminaGameData runtime). Recorded acceptance everywhere: seams implemented + unit-tested on Linux; runtime verification on a machine with a game install/building checkout. Update 2026-09-21: the plugin **was compiled on the Linux dev box** (Dalamud.NET.Sdk against the local xlcore Dalamud) and deployed to `~/.xlcore/devPlugins/BossModForge/`; the Windows-box claim above is stale for plugin compile. Remaining plugin risk is runtime, not compile: the m5 brief §8 items are compile-confirmed but must be exercised in-game (analyze a real recording, override, re-emit).

## Plugin handoff (documented, not executed here)

`src/BossModForge.Plugin/**` stays untouched. The Windows builder resolves the `// VERIFY:` items from `docs/plans/m5-builder-brief.md` §8 (items 1–11: Lumina `Action`/`Omen`/`ContentFinderCondition` columns, `DalamudApiLevel` 15, ImGui member set, `IFramework.RunOnFrameworkThread<T>`, BMR replay dir name, replay file pattern, `JsonSchema.Net` transitive DLLs, BMR IPC name, `ConfigDirectory`/`AssemblyLocation`) plus the `m5-status.md` note that the plugin has never been compiled. Verification path: `scripts/build-plugin.ps1` on a Windows box + the §10 checklist of the m5 brief, recorded in `docs/plans/m5-status.md`.

## Final review (fresh-context reviewer)

A fresh-context whole-run review was dispatched against the full file inventory (see `.superpowers/sdd/2026-09-20-completion-plan/`). Verdict: **With fixes** — 0 Critical, 2 Important, 7 Minor.

- **Fixed (Important):** the ReplayValidator host template instantiated the emitted module with a nonexistent 4-arg ctor (`ws, primary, center, ArenaBoundsCircle(20)`); the emitted ctor is 2-arg `(WorldState ws, Actor primary)` (center/bounds are embedded by the moduleDecl template). First real `--bossmod` run would have thrown `MissingMethodException`. Now a 2-arg call; pinned by `ReplayValidatorHostTemplateTests.HostTemplate_InstantiatesModuleWithEmittedCtorShape` (RED→GREEN). Suite: **198/198**.
- **Refuted (Important):** "ledger artifacts missing" — the `.superpowers` ledger exists; hidden-directory visibility issue in reviewer tooling. Substance duplicated in this doc.
- **Deferred minors** (recorded in the ledger, not fixed): binary-path v25+ test (text-only today); `validate` exits 4 vs `emit/infer/merge/polish` exit 3 on schema-invalid EM (spec §10 favors 3 — align later); spec §10 `infer` CLI sketch mismatches shipped flags; `catch (NotImplementedException) { throw; }` tripwires in Merge/InferCommand; no length cap on polished `name`/`comment`; ConformTests bad-DLL branch covers nonexistent path only; merged per-pull `Source` is the joined path for every pull (no per-pull file provenance).
