# ADR-009: Lumina lives in the CLI — Core stays game-data-library-free

**Status:** Proposed
**Date:** 2026-09-20

## Context
Game-data lookups (ability names, CastType/EffectRange/XAxisModifier priors, omen paths, BNpcName/Status names, CFC/PlaceName for the encounter header) flow through the `IGameData` seam in `BossModForge.Core`. Three hosts need implementations: the CLI (reads from a game install on disk), the Dalamud plugin (reads through `IDataManager`, which already wraps Lumina), and tests (fake/in-memory implementations). If Core referenced Lumina directly, every host — including the plugin, which gets its data from Dalamud rather than a disk path, and every unit test — would inherit the dependency, and Core would no longer be a pure-.NET library (AGENTS.md boundary). The original design table even listed "Lumina (game data)" as a Core dependency, which drifted from the boundary rule once the seam existed.

## Decision
`BossModForge.Core` keeps only the `IGameData` interface, `ActionRow`, `NullGameData`, and `CachedGameData`. It never references Lumina.

`BossModForge.Cli` owns the only disk-based implementation, `BossModForge.Cli.GameData.LuminaGameData`, wired behind `forge infer --game-path <dir>`. It pins `Lumina 5.8.0` + `Lumina.Excel 7.2.3` (the struct-row sheet package paired with Lumina core 5.x) — Cli only; Core, the upstream shim, and the golden-compile project never gain these packages. The ctor never throws: any Lumina exception during construction or sheet probing sets `Available = false`, every member then returns null, and `InferCommand` prints a stderr warning and continues with `NullGameData` semantics. Individual lookups that fail mid-run also return null, so a partially readable install degrades per-lookup instead of aborting the analysis. Column mapping is pass-through to `ActionRow` (`Cast100ms` stays in 100 ms units; no consumer reads it yet — cast times come from the replay's `CST+` total). `OmenId`/`OmenPath` come from the `Omen` `RowRef` (`RowId` + `ValueNullable?.Path.ExtractText()`).

The plugin implements `IGameData` via Dalamud's `IDataManager` (Lumina already inside Dalamud; no package added there either) and pre-fetches into `CachedGameData` on the framework thread, per spec §9 threading rules.

## Consequences
- Core remains a pure `net10.0` classlib: unit tests fake `IGameData` in-memory; the golden-compile shim is untouched.
- `forge infer` without `--game-path` behaves byte-identically to before (golden EMs unchanged); with a bad path it warns and still produces the same EM as the `NullGameData` run (pinned by `Infer_WithBadGamePath_StillRuns`).
- The only new dependency weight (Lumina + BCnEncoder.Net + ObjectPool) lands on the CLI, the host that actually touches a game install.
- Runtime verification against a real FFXIV install is machine-gated (see `docs/UPSTREAM.md` §11 "LuminaGameData"); compile + graceful-degradation coverage is the acceptance on the Linux dev box.

## Alternatives rejected
- **Lumina in Core** — breaks the Core boundary (Dalamud/ImGui/BossMod-free); forces the dependency on tests, the shim, and the plugin for no benefit, since the plugin's data source is Dalamud, not a disk path.
- **Raw-row access (`RawExcelSheet`/`RawRow`) instead of Lumina.Excel** — avoids the second package but hardcodes column offsets for seven sheets; schema churn becomes silent breakage instead of a compile error. Typed struct rows fail loudly at compile time when the schema renames a column.
- **Fail `forge infer` when `--game-path` is invalid** — a names-only optimisation should never gate inference; warning + fallback preserves the deterministic NullGameData behaviour.
