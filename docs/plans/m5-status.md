---
tags:
  - type/status
  - project/bossmodforge
  - status/active
type: status
project: bossmodforge
status: active
milestone: M5
brief: ./m5-builder-brief.md
---

# M5 status — Core side done, plugin written but unbuilt (2026-09-20)

- Core: `Pipeline/ForgePipeline` (Parse → CollectKeys → Analyze → WithOverrides → Emit), `Model/OverridesApplier` (id-keyed JSON-pointer overrides, idempotent, bad keys → review items), `GameData/GameDataKeys` + `CachedGameData`. `forge infer` now runs through the facade. `./scripts/build.sh`: 149 tests, goldens byte-identical.
- Plugin: `src/BossModForge.Plugin/**` written (Plugin, Configuration, ReplayDiscovery, BossModIpc, DalamudGameData snapshot, AnalysisRunner state machine, six windows, Exporter). Lives in `BossModForge.Plugin.slnx`; **not** in `BossModForge.slnx`.
- The plugin has **never been compiled**: this machine has no Dalamud libs. First Windows build via `scripts/build-plugin.ps1` will surface the `// VERIFY:` items (Lumina column names, ImGui overloads, `RunOnFrameworkThread`, replay dir names, IPC name on BMR).
- A compile-by-inspection pass cross-checked every plugin → Core call against real signatures; the Dalamud/ImGui side is checked only against `TankCooldownHelper` conventions.
- Still deferred: `LuminaGameData` for the CLI (`--game-path`), `--bossmod` real-build validation (M4b), BMR flavor verification, `forge conform`, `forge merge`/`diff`, LLM polish (M7).

## Plugin compiled on Linux (2026-09-21)

`dotnet build BossModForge.Plugin.slnx -c Release` succeeds on the Linux dev box and deploys straight to `~/.xlcore/devPlugins/BossModForge/` (the csproj gained `<EnableWindowsTargeting>true</EnableWindowsTargeting>` and an OS-conditioned OutputPath/DevPluginsPath for non-Windows). Fixes from the first compile:
- This Dalamud binding (API 15, `ImU8String`-based) has **no plain `ImGui.InputText`**; the `Ui.InputText` helper now calls `InputTextWithHint(label, "", ref string, int maxLength, ImGuiInputTextFlags)`.
- Manifest renamed `BossModForge.Plugin.json` → `BossModForge.json` (must equal `AssemblyName`; DalamudPackager validates it on every build).

§8 VERIFY items are compile-confirmed; remaining verification is the §10 in-game checklist where the game runs (this box, xlcore/Wine): analyze a real recording, names from Lumina, override → re-emit, validate report, unload/reload. Runtime-only risks left: `RunOnFrameworkThread<T>` behavior, ImGui widget behavior (existence ≠ correct rendering), replay dir/file-pattern assumptions, BMR IPC name.
