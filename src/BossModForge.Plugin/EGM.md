# BossModForge.Plugin

## Overview
Dalamud plugin (`IDalamudPlugin` named "BossModForge") that wires configuration, services and ImGui windows around `BossModForge.Core`. `Plugin.cs` is the composition root; `Configuration.cs` is the persisted settings model.

## Local Interface & Invariants
- `Plugin(IDalamudPluginInterface)`: initializes ECommons, loads `Configuration` (or defaults), constructs `DalamudGameData`, `ReplayDiscovery`, `AnalysisRunner`, `ForgeSession`, `BossModIpc`, `RecordingCountToast`, `Exporter`, the seven windows and a `WindowSystem("BossModForge")` [Plugin.cs:Plugin]. `Name => "BossModForge"` [Plugin.cs].
- `/forge [recordings|encounter|abilities|timeline|export|settings|config]` command: empty toggles main window, known keys open the matching window, unknown logs a hint and opens main [Plugin.cs:OnCommand].
- `OnRunnerCompleted` calls `ForgeSession.Accept` only when state is `Ready` and `Result` is non-null [Plugin.cs:OnRunnerCompleted].
- `Dispose` unregisters `/forge`, `Draw`/`OpenMainUi`/`OpenConfigUi` handlers and the `Completed` handler, removes windows, disposes toast and runner, then `ECommonsMain.Dispose()` [Plugin.cs:Dispose].
- `Configuration : IPluginConfiguration` (`Version = 1`) with settings: `ReplayDirectories`, `OutputDirectory`, `Flavor` ("vbm"), `DefaultNamespace`, `Maturity` ("WIP"), `Contributors`, `OpenFolderAfterExport` (true), `OverridesBySha256`, `AdvancedThresholds`, `ShowRecordingCountToast`, `ToastMinFightSeconds` (45), and nullable segmenter/shape/bounds threshold overrides [Configuration.cs].
- `Configuration.ToPipelineOptions()` builds `PipelineOptions` with `SegmenterOptions`/`ShapeInferenceOptions`/`BoundsFitterOptions` only if at least one corresponding override is set (otherwise null); a `BuildOptions` is created only if `DefaultNamespace` is non-blank or shape/bounds options exist; unset fields fall back to the `.Default` values [Configuration.cs:ToPipelineOptions].
- `Configuration.Save(IDalamudPluginInterface)` => `SavePluginConfig(this)` [Configuration.cs:Save].

## Local Side Effects
- Registers a Dalamud chat command and UI builder event handlers; subscribes to `Svc.Framework.Update` via `RecordingCountToast` [Plugin.cs].
- `ECommonsMain.Init/Dispose`; plugin config persistence through `SavePluginConfig` [Plugin.cs, Configuration.cs].

## Subdirectories & System Boundaries
- Services: [./Services/EGM.md](./Services/EGM.md) - analysis runner, session state, exporter, replay discovery, game-data snapshot, IPC probe, toast.
- UI: [./UI/EGM.md](./UI/EGM.md) - ImGui windows, shared helpers, evidence plot.

## Global Invariants
- Analysis completion flows `AnalysisRunner.Completed` -> `ForgeSession.Accept` -> windows read `ForgeSession` [Plugin.cs; Services/EGM.md; UI/EGM.md].
- Overrides are persisted per recording SHA-256 in `Configuration.OverridesBySha256` [Services/EGM.md].

## Global Side Effects
- Config file writes, output file writes, `explorer.exe` launch, plugin IPC call, Dalamud toasts/commands (see child contracts).

## System Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
