# Services

## Overview
Non-UI plugin services: replay file discovery, background analysis runner with progress snapshots, a per-session state holder with persisted overrides, Dalamud/Lumina game-data snapshotting, export/validate file output, a BossMod IPC presence probe, and a post-fight recording-count toast. Core types (`ForgePipeline`, `Overrides`, `EncounterModel`, ...) come from `BossModForge.Core`.

## Interface
- `enum RunnerState { Idle, Parsing, FetchingGameData, Inferring, Ready, Failed, Cancelled }`; `record RunnerSnapshot(State, Progress, StageText, Error, Result, SourcePath, StartedUtc, FinishedUtc)` [AnalysisRunner.cs]
- `AnalysisRunner(DalamudGameData, Configuration) : IDisposable`: `Snapshot`, `IsBusy`, `event Action<RunnerSnapshot>? Completed`, `Start(string path, PipelineOptions, Overrides carryOver)`, `Cancel()`, `Dispose()`, `internal static FileSha256(string)` [AnalysisRunner.cs]
- `internal static OverrideJson.Serialize(Overrides)` / `Parse(string)` [AnalysisRunner.cs:OverrideJson]
- `ForgeSession(Configuration)`: `Result`, `Overrides`, `Effective`, `LastEmit`, `LastValidation`, `LastOutputDir`, `Messages`, `ReviewedAids`, `Accept`, `SetOverride`, `ClearOverride`, `ClearAllOverrides`, `LoadEncounterModel`, `MarkReviewed`, `IsOverridden`, `SetLastOutputDir`, `SetLastEmit`, `SetLastValidation`, `Message` [ForgeSession.cs]
- `DalamudGameData(IDataManager).Snapshot(GameDataKeys) -> CachedGameData` [DalamudGameData.cs:Snapshot]
- `Exporter(Configuration, ForgeSession)`: `IsBusy`, `FlavorsDir`, `OutputRoot`, `ModuleOutputDir(em)`, `FlavorsPresent()`, `WriteEm`, `EmitCsharp`, `Validate`, `WriteReport`, `static ConcatenateCsharp(EmitResult)`, `static OpenFolder(string)` [Exporter.cs]
- `BossModIpc`: `Available`, `Probe()` [BossModIpc.cs]
- `record ReplayFile(Path, SizeBytes, LastWriteUtc)`; `ReplayDiscovery(Configuration)`: `Directories`, `static DefaultDirectories()`, `Scan()` [ReplayDiscovery.cs]
- `RecordingCountToast(Configuration, ReplayDiscovery) : IDisposable` [RecordingCountToast.cs]

## Invariants
- `AnalysisRunner.Start` throws `InvalidOperationException` if already busy (Parsing/FetchingGameData/Inferring) [AnalysisRunner.cs:Start, IsBusy]. Each run has an incrementing `_runId`; results/progress from a stale run are dropped via `StillCurrent` [AnalysisRunner.cs:RunAsync, StillCurrent]. Snapshot is published with `Interlocked.Exchange` and read with `Volatile.Read` [AnalysisRunner.cs:Publish, Snapshot].
- If `carryOver.IsEmpty`, stored overrides are loaded from `Configuration.OverridesBySha256[sha256]` under `lock` [AnalysisRunner.cs:RunAsync, LoadStoredOverrides]. Parse progress publishes at most every 50 ms and scales to 0..0.15 [AnalysisRunner.cs:RunAsync].
- Game-data snapshot and the `Completed` event are executed on the framework thread; a snapshot over 8 ms logs a warning [AnalysisRunner.cs:SnapshotOnFramework, RaiseCompleted].
- Terminal states: `Ready`, `Cancelled` (on `OperationCanceledException`), `Failed` (error message, logged), each followed by `Completed`, except that a stale run (`StillCurrent` false) publishes nothing and raises no `Completed`, including for Failed/Cancelled [AnalysisRunner.cs:RunAsync].
- `FileSha256` returns "" for empty/missing path [AnalysisRunner.cs:FileSha256].
- `ForgeSession.Messages` keeps at most 20 entries under a lock [ForgeSession.cs:Message]. `PersistOverrides` is a no-op when the current SHA-256 (first source of `Result.Inferred.Sources`) is empty; empty overrides remove the stored entry [ForgeSession.cs:PersistOverrides, CurrentSha256]. `LoadEncounterModel` only posts a message ("out of scope for M5") [ForgeSession.cs:LoadEncounterModel]. `Accept` resets `LastEmit`/`LastValidation` and takes overrides from `r.Effective.Overrides` [ForgeSession.cs:Accept].
- `Exporter` allows one operation at a time (`_running` CAS); a concurrent call posts "already running" [Exporter.cs:Run]. Output root is `Configuration.OutputDirectory` or `<ConfigDirectory>/output`; module dir is `<root>/<ModuleName>` [Exporter.cs:OutputRoot, ModuleOutputDir]. Emit requires `flavors/vbm.json` next to the plugin DLL, otherwise posts a message and returns [Exporter.cs:FlavorsPresent, EmitCsharp]. `WriteEm` writes even when schema issues exist, reporting the count [Exporter.cs:WriteEm].
- `DalamudGameData.Snapshot` skips missing rows. Action rows are kept even when `ExtractName` is empty; the bnpc, status, content and place tables skip rows whose name is empty (`NullIfEmpty`) [DalamudGameData.cs:Snapshot]. A sheet-fetch failure logs a warning and skips that sheet [DalamudGameData.cs:Snapshot, Sheet].
- `ForgeSession.ClearOverride` removes the key; with a `Result` it re-applies overrides, otherwise it posts "re-analyze to restore inferred value"; it always persists [ForgeSession.cs:ClearOverride].
- `ReplayDiscovery.Directories` uses `ReplayDirectories` when non-empty, else `<ConfigDirectory parent>/BossMod/replays` and `.../BossModReborn/replays`; `Scan` looks at `*.log` in each directory and its immediate subdirectories, dedups case-insensitively and sorts newest first [ReplayDiscovery.cs].
- Toast fires only when combat ends, `ShowRecordingCountToast` is on, the player is `BoundByDuty`, fight length >= `ToastMinFightSeconds`, and the territory resolves to a non-empty content name; the count shown is `RecordingNames.CountFor(scanned paths, name)` from Core; errors are logged, not thrown [RecordingCountToast.cs:OnUpdate, ShowToast].

## Side Effects
- `AnalysisRunner`: reads and hashes the recording file; runs the pipeline on a thread-pool task; logs errors via `Svc.Log` [AnalysisRunner.cs].
- `ForgeSession`: mutates `Configuration.OverridesBySha256` and calls `Configuration.Save(Svc.PluginInterface)` (disk write of plugin config); logs each message via `Svc.Log.Info` [ForgeSession.cs:PersistOverrides, Message].
- `Exporter`: creates directories and writes `<Module>.em.json`, emitted files, `FORGE.md`, `validation-report.json/md`; launches `explorer.exe` for `OpenFolder`; runs work on a `Task.Run` thread [Exporter.cs].
- `DalamudGameData`: reads Excel sheets through `IDataManager` [DalamudGameData.cs].
- `BossModIpc.Probe`: invokes IPC `BossMod.HasModuleByDataId` with id 0; any exception sets `Available = false` [BossModIpc.cs:Probe].
- `ReplayDiscovery`: enumerates directories and stats files; logs warnings on failures [ReplayDiscovery.cs].
- `RecordingCountToast`: subscribes to `Svc.Framework.Update` (unsubscribed in `Dispose`), shows a Dalamud toast [RecordingCountToast.cs].

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
