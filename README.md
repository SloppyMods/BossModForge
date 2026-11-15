# BossModForge

Generate BossMod / BossModReborn boss modules from recordings of fights that have no module yet.

**Status:** M0–M7 implemented (CLI pipeline: parse → infer → emit → validate → merge → polish, plus `conform` and Lumina-backed names). Emitted modules compile against a shim of the pinned BossMod API (`tests/BossModForge.UpstreamShim`); both flavors (`vbm`, `bmr`) are pinned and verified. The M5 plugin sources are in `src/BossModForge.Plugin/` (Windows-only; see Plugin below). See `docs/plans/2026-09-20-completion-status.md` for the full shipped/deviation record.

## Build

Two solutions, two machines:

- `BossModForge.slnx` — Core + Cli + tests. Built and tested here (Linux is fine).
- `BossModForge.Plugin.slnx` — Core + Plugin only. Windows-only (needs Dalamud libs), built via `scripts/build-plugin.ps1`; never added to `BossModForge.slnx`.

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
./scripts/build.sh
```

Or:

```bash
dotnet build BossModForge.slnx -c Release
dotnet test BossModForge.slnx -c Release
```

## CLI

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet run --project src/BossModForge.Cli -- ingest path/to/replay.log
dotnet run --project src/BossModForge.Cli -- infer path/to/replay.log -o encounter.em.json [--game-path "C:\Games\FFXIV"]
dotnet run --project src/BossModForge.Cli -- emit encounter.em.json --flavor vbm -o out/
dotnet run --project src/BossModForge.Cli -- validate --em encounter.em.json --recordings path/to/replay.log [--out dir] [--write-em] [--bossmod path/to/bossmod-checkout]
dotnet run --project src/BossModForge.Cli -- merge a.log b.log --out merged.em.json [--em prior.em.json] [--select-boss 0x4195]
dotnet run --project src/BossModForge.Cli -- polish encounter.em.json --provider file --patches patches.json --out polished.em.json
dotnet run --project src/BossModForge.Cli -- conform path/to/replay.log --bossmod-dll path/to/BossMod.dll [--json] [--selftest]
```

- `ingest` — encoding, version, per-tag op counts
- `infer` — Encounter Model JSON (pulls, actors, abilities, shapes, mechanic classes). `--game-path <dir>` enables Lumina-backed names from a FFXIV install (optional; a bad path degrades gracefully with a stderr warning). `--module-name` / `--namespace` override the emitted module identity.
- `emit` — C# module (`DeathPhase`/`SimplePhase` state machine, `Cast`/`CastMulti`/`Targetable` chain, standard components) and `FORGE.md`; `--flavor vbm|bmr`.
- `merge <log…> --out <em.json> [--em <em.json>] [--select-boss <OID>]` — analyze several recordings of one logical fight as one concatenated recording (actor instance ids must be distinct across files); `--em` carries prior overrides into the merged EM; `--select-boss` picks the primary OID for door bosses. Exit 0 ok / 2 High review items / 4 error.
- `polish <em.json> --provider file|http` — constrained LLM pass (ADR-007/ADR-008): patches may only touch ability/participant `enumName`/`name`/`comment` and review-item resolutions; everything else is rejected with a reason. `--provider file --patches p.json` applies an offline patch array; `--provider http --endpoint <url> [--model m] [--api-key-env VAR]` posts the prompt to an LLM endpoint. `--out` defaults to stdout. Exit 0 ok / 4 error.
- `validate --em x.em.json --recordings a.log…` — dry-replays recordings against the model: step timing p90 ≤ 1.0 s, AOE precision/recall ≥ 0.95, coverage; writes `validation-report.{json,md}`; exit 0 pass / 3 fail / 4 error. `--write-em` rewrites the EM, lowering confidence of failing abilities and appending review items. `--bossmod <checkout>` runs M4b real-build validation (stages the module into the checkout, `dotnet build`, maps compile errors back to EM items); it requires a BossMod/BMR checkout that builds on the machine — Dalamud libs, typically Windows (see `docs/UPSTREAM.md` §11).
- `conform <log…> --bossmod-dll <path> [--json] [--selftest]` — parser conformance (ADR-002): parses each recording with Forge and with BossMod's own parser (reflect-loaded from a `BossMod.dll` built from the pinned checkout) and diffs per-tag op counts; exit 0 = all tags equal, 3 = diffs, 4 = error (including a missing/unloadable DLL — without `--bossmod-dll` it prints the documented `error: conformance requires --bossmod-dll …` message). `--selftest` prints the resolved op-type → tag map from the given DLL. The happy path needs a BossMod build (typically a Windows box); see `docs/UPSTREAM.md` §11.

Set `FORGE_REAL_LOGS` to a directory of real BossMod `*.log` files for the smoke test. Set `FORGE_PERF=1` to enable the streaming-perf gate (parses a ~200 MB synthetic log; skipped by default so CI stays fast). Never commit real recordings.

## Layout

| Path | Purpose |
|---|---|
| `src/BossModForge.Core` | Parser, inference, Encounter Model, emitter, merge, polisher. No Dalamud, no Lumina. |
| `src/BossModForge.Cli` | `forge ingest \| infer \| emit \| validate \| merge \| polish \| conform` (Lumina game data lives here only) |
| `tests/BossModForge.Tests` | Synthetic fixtures and goldens |
| `tests/BossModForge.UpstreamShim` | Signature-accurate stub of the pinned BossMod API (no logic) |
| `tests/BossModForge.GoldenCompile` | Compiles every golden `.vbm.cs` against the shim on each build |
| `src/BossModForge.Plugin` | Dalamud `/forge` UI. Windows-only; built via `BossModForge.Plugin.slnx` |
| `flavors/` | vbm/bmr emit templates |
| `docs/` | Spec, plan, ADRs, builder briefs, upstream pin |

Plugin sources: `src/BossModForge.Plugin/` (not in `BossModForge.slnx`).

## Plugin (Windows)

The Dalamud plugin lives in `src/BossModForge.Plugin/` and is built with a separate solution so Linux CI stays unchanged.

Requires XIVLauncher/Dalamud (`%AppData%\XIVLauncher\addon\Hooks\dev\Dalamud.dll`) and a Windows box. Output: `%AppData%\XIVLauncher\devPlugins\BossModForge\`.

```powershell
.\scripts\build-plugin.ps1
```

That runs `dotnet build BossModForge.Plugin.slnx -c Release` (Core + Plugin only). Do not add the plugin to `BossModForge.slnx`.

In-game: `/forge` toggles the tabbed window (Recordings, Encounter, Abilities, Timeline, Export, Settings). `/forge recordings|encounter|abilities|timeline|export|settings` opens the matching pane. Enable it as a Dalamud dev plugin pointing at the `devPlugins\BossModForge` folder.

## Documents

- Design spec: `docs/specs/2026-09-18-bossmodforge-design.md`
- Implementation plan: `docs/plans/2026-09-18-bossmodforge-implementation.md`
- Completion plan + status: `docs/plans/2026-09-20-completion-plan.md`, `docs/plans/2026-09-20-completion-status.md`
- Builder briefs: `docs/plans/m0-builder-brief.md`, `m1-builder-brief.md`, `m2-builder-brief.md`, `m5-builder-brief.md`
- Upstream pins: `docs/UPSTREAM.md`
- Decisions: `docs/adr/`
- Glossary: `CONTEXT.md`
