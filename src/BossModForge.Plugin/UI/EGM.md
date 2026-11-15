# UI

## Overview
ImGui windows (Dalamud `Window` subclasses) for the plugin: a tabbed `MainWindow` that hosts the content of six sibling windows (Recordings, Encounter, Abilities, Timeline, Export, Settings), plus shared helpers and an evidence plotter. Windows read state from `ForgeSession`/`AnalysisRunner` and edit it through `ForgeSession.SetOverride`/`ClearOverride` or `Configuration`.

## Interface
- `internal static Ui`: `SizeConstraints`, `Band`, `BandU32`, `ColorU32`, `ConfidenceBadge`, `DrawSessionBar(ForgeSession, AnalysisRunner)`, `InputText`, `InputTextEnter`, `Combo`, `ShapeSummary`, `NullElement` [Ui.cs]
- `MainWindow(runner, session, recordings, encounter, abilities, timeline, export, settings)` [MainWindow.cs]
- `RecordingsWindow(config, discovery, runner, session, ipc)` [RecordingsWindow.cs]
- `EncounterWindow(session, runner)` [EncounterWindow.cs]
- `AbilitiesWindow(session, runner)` [AbilitiesWindow.cs]
- `TimelineWindow(session, runner)` [TimelineWindow.cs]
- `ExportWindow(config, session, runner, exporter)` [ExportWindow.cs]
- `SettingsWindow(config, session, runner)` [SettingsWindow.cs]
- Each content window exposes `Draw()` and `DrawContent()`; `RecordingsWindow` also `OnOpen()` [AbilitiesWindow.cs:Draw, EncounterWindow.cs:Draw, ExportWindow.cs:Draw, RecordingsWindow.cs:OnOpen, SettingsWindow.cs:Draw, TimelineWindow.cs:Draw]
- `internal static EvidencePlot.Draw(AnalysisResult, AbilityModel, int instanceIndex, Action<int> setInstance)` [EvidencePlot.cs:Draw]

## Invariants
- Every window sets `RespectCloseHotkey = true` and `Ui.SizeConstraints` (min 480x320, max 4096x4096) [each window constructor].
- Windows needing analysis show "Analyze a recording first." when `ForgeSession.Effective`/`Result` is null (Encounter, Abilities, Timeline, Export) [EncounterWindow.cs:DrawContent, AbilitiesWindow.cs:DrawContent, TimelineWindow.cs:DrawContent, ExportWindow.cs:DrawContent].
- Edits to encounter data go through override keys (`OverridesApplier.ModuleNameKey`, `NamespaceKey`, `ArenaKey`, `AbilityKey(aid, field)`, `ParticipantKey(oid, field)`); the primary-OID participant's enum name is not editable ("Boss") [EncounterWindow.cs, AbilitiesWindow.cs].
- Recordings "Analyze" button is always drawn; the click is ignored unless a row is selected and the runner is not busy (`canAnalyze`), and only the hover tooltip reflects that state ("Analysis running" / "Select a recording..."). Overrides are carried over only when the selected file is the current one [RecordingsWindow.cs:DrawContent].
- Export: Emit is disabled when `flavors/vbm.json` is missing; the `DrawButton` buttons (Write EM JSON, Emit C#, Open output folder, Validate, Write report) are replaced by disabled text while `Exporter.IsBusy`, but "Copy to clipboard", "Use this output dir" and "Apply downgrades" are not busy-gated; "Write report" needs a `LastValidation`; "Apply downgrades" re-accepts the session result with `DryValidator.ApplyDowngrades` [ExportWindow.cs:DrawContent].
- `MainWindow.OnOpen()` delegates to `RecordingsWindow.OnOpen()` [MainWindow.cs:OnOpen].
- ExportWindow Maturity and Contributors are local state that only feed `EmitOptions`; they are not written to `Configuration` [ExportWindow.cs:DrawContent].
- The EncounterWindow Edit-arena popup writes the arena override via `OverridesApplier.ArenaKey` [EncounterWindow.cs:DrawContent].
- AbilitiesWindow reads/writes per-ability override keys `shape`, `class`, `enumName`, `ignore` (`OverridesApplier.AbilityKey(aid, field)`); its shape popup shows fields depending on the chosen `ShapeKind` [AbilitiesWindow.cs:DrawContent].
- `Ui.InputText` uses `InputTextWithHint` because plain `InputText` does not exist in the binding (per code comment) [Ui.cs:InputText].
- Timeline "amber" warn when `TimingMad > 1.5` or `Support < 0.6 * PullCount` [TimelineWindow.cs:DrawContent]. Evidence scatter subsamples to <= 2000 points [EvidencePlot.cs:Subsample].
- Settings Advanced section sets `AdvancedThresholds = true` whenever drawn expanded [SettingsWindow.cs:DrawContent].

## Side Effects
- `SettingsWindow` mutates `Configuration` and saves it via `Svc.PluginInterface` on each change; "Clear stored overrides" wipes `OverridesBySha256` and calls `ForgeSession.ClearAllOverrides` [SettingsWindow.cs].
- `ExportWindow` saves `Flavor`/`OutputDirectory` into `Configuration`, can set clipboard text, triggers `Exporter` operations (file writes, folder open) and `Directory.CreateDirectory` for the output folder [ExportWindow.cs].
- `RecordingsWindow` probes IPC and scans replay directories on `OnOpen`/Rescan, and starts `AnalysisRunner.Start` [RecordingsWindow.cs].
- `Ui.DrawSessionBar` can call `runner.Cancel()` [Ui.cs:DrawSessionBar].
- `AbilitiesWindow`/`EncounterWindow`/`TimelineWindow`: ImGui draw only plus session override/review mutations (`MarkReviewed`, `SetOverride`, `ClearOverride`); `EvidencePlot` draws only [AbilitiesWindow.cs:DrawContent, EncounterWindow.cs:DrawContent, TimelineWindow.cs:DrawContent, EvidencePlot.cs:EvidencePlot].

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
