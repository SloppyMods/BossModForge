---
tags:
  - type/plan
  - project/bossmodforge
  - status/active
type: plan
project: bossmodforge
status: active
milestone: M5
aliases: []
---
# M5 builder brief — Dalamud plugin (`/forge`)

Tracer bullet: in game, `/forge` → Recordings lists `<pluginConfigs>/BossMod/replays/*.log` → pick one → **Analyze** runs parse → game-data snapshot → inference off the framework thread with a progress bar → Abilities table shows AIDs with confidence colours → change one shape → Export writes `<ModuleName>.em.json` + `.cs` files whose C# reflects the override → **Validate** shows the dry report → **Re-analyze** keeps the override.

Status going in: M0–M4 done, 124 tests green, `forge ingest|infer|emit|validate` works, goldens compile against the `awgil/ffxiv_bossmod@1391bc42` shim (`docs/plans/m4-status.md`).

## 0. Read first

- `docs/specs/2026-09-18-bossmodforge-design.md` §2 (project table), §4.3 (game data), §6 (`overrides` sentence), §9 (plugin), §11.
- `docs/plans/2026-09-18-bossmodforge-implementation.md` — ground rules, "Solution layout", M5.
- `AGENTS.md` (repo conventions). `docs/UPSTREAM.md` §8–9.
- Repo Dalamud conventions: `../TankCooldownHelper/TankCooldownHelper.csproj`, `TankCooldownHelper.cs` (ctor + `Dispose`), `TankCooldownHelper.json`, `Configuration.cs`, `UI/MainWindow.cs`.
- BossMod IPC presence check: `../MarkerMind/Core/Adapter/BossmodBridge.cs:26-39`. Top-down draw approach: `../MarkerMind/MarkerMind/Core/Renderer/MinimapRenderer.cs:43-77`.
- Core entry points this brief freezes on top of: `Replay/ReplayParser.cs` (`Parse(string|Stream, ParseOptions?, CancellationToken)`; `ParseOptions(IProgress<float>? Progress, bool KeepRawOps)`), `Inference/PullSegmenter.Segment`, `ParticipantClassifier.Classify`, `AbilityCataloger.Catalog`, `Model/EncounterModelBuilder.Build(recording, pulls, participants, catalog, gameData, BuildOptions?)`, `Model/EncounterModelJson.{Serialize,Deserialize,Validate}`, `Emit/ModuleEmitter.{Emit,ProfileFor,ResolveFlavorsDirectory}`, `Validation/DryValidator`, `GameData/IGameData.cs`, `Cli/Commands/InferCommand.cs:60-70` (the only place the stage chain exists today).

## 1. Scope

In:
1. Core additions (Linux-testable): `Pipeline/ForgePipeline.cs`, `Model/OverridesApplier.cs`, `GameData/CachedGameData.cs` (+ `GameDataKeys`), `Overrides.Empty`. CLI `infer` switches to the facade; byte-identical output.
2. Plugin project `src/BossModForge.Plugin/` — scaffold, `DalamudGameData`, `ReplayDiscovery`, `AnalysisRunner`, `ForgeSession`, six windows, `EvidencePlot`, `BossModIpc`, `/forge`.
3. `BossModForge.Plugin.slnx`; docs (`README.md` build matrix, `AGENTS.md` child index, spec §6 override-key sentence).

Out (do not build): own recorder; multi-recording merge (M6 — Analyze takes exactly one file); real BossMod-build validation (`--bossmod`, M4b); `LuminaGameData` for CLI (still deferred — `DalamudGameData` is the first Lumina implementation); LLM polish (M7); BMR IPC beyond a presence probe.

## 2. Build/CI constraint (read twice)

The plugin is **Windows-only** and **cannot compile on this Linux box** (no Dalamud libs, `Dalamud.NET.Sdk` pulls `Dalamud.dll` from `%AppData%\XIVLauncher\addon\Hooks\dev`).

- `BossModForge.slnx` stays exactly Core + Cli + Tests + UpstreamShim + GoldenCompile. **Do not add the plugin to it.** `scripts/build.sh`, `quality-gate.json`, CI on Linux compile only those.
- Add `BossModForge.Plugin.slnx` at repo root containing `src/BossModForge.Core/BossModForge.Core.csproj` and `src/BossModForge.Plugin/BossModForge.Plugin.csproj`. Windows build: `dotnet build BossModForge.Plugin.slnx -c Release`. Because two `.slnx` files now sit at root, every `dotnet build`/`test` invocation must name its solution (scripts already do; document in README).
- Add `scripts/build-plugin.ps1` (Windows): `dotnet build BossModForge.Plugin.slnx -c Release`; exits non-zero if `$env:APPDATA\XIVLauncher\addon\Hooks\dev\Dalamud.dll` is missing with a clear message.
- You still write plugin code to compile first time: every Dalamud/Lumina/ImGui member you use must appear in §8 (VERIFY table) or in the confirmed list in §7. No `dynamic`, no reflection to dodge signatures. Where a signature is `VERIFY`, isolate it behind a one-line private helper so a Windows fix is a one-liner.
- Acceptance for the plugin is therefore (a) Core-side tests for everything the plugin calls, (b) the "compiles on Windows" checklist in §10, executed by whoever has the Windows box; record the result in `docs/plans/m5-status.md`.

## 3. Files

```
BossModForge.Plugin.slnx                                     new
scripts/build-plugin.ps1                                     new
src/BossModForge.Core/Pipeline/ForgePipeline.cs              new  facade (§4.1)
src/BossModForge.Core/Model/OverridesApplier.cs              new  (§4.2)
src/BossModForge.Core/Model/EncounterModel.cs                edit add Overrides.Empty (§4.2)
src/BossModForge.Core/GameData/CachedGameData.cs             new  GameDataKeys + CachedGameData (§4.3)
src/BossModForge.Cli/Commands/InferCommand.cs                edit lines 60-70 → ForgePipeline (§4.1)
src/BossModForge.Plugin/BossModForge.Plugin.csproj           new  (§5)
src/BossModForge.Plugin/BossModForge.Plugin.json             new  manifest (§5)
src/BossModForge.Plugin/Plugin.cs                            new  entry, ECommons init, WindowSystem, /forge (§5)
src/BossModForge.Plugin/Configuration.cs                     new  (§5)
src/BossModForge.Plugin/Services/DalamudGameData.cs          new  (§4.3 plugin half)
src/BossModForge.Plugin/Services/ReplayDiscovery.cs          new  (§6.1)
src/BossModForge.Plugin/Services/AnalysisRunner.cs           new  (§4.4)
src/BossModForge.Plugin/Services/ForgeSession.cs             new  (§4.5)
src/BossModForge.Plugin/Services/BossModIpc.cs               new  presence probe (§6.1)
src/BossModForge.Plugin/Services/Exporter.cs                 new  (§6.5)
src/BossModForge.Plugin/UI/RecordingsWindow.cs               new
src/BossModForge.Plugin/UI/EncounterWindow.cs                new
src/BossModForge.Plugin/UI/AbilitiesWindow.cs                new
src/BossModForge.Plugin/UI/TimelineWindow.cs                 new
src/BossModForge.Plugin/UI/ExportWindow.cs                   new
src/BossModForge.Plugin/UI/SettingsWindow.cs                 new
src/BossModForge.Plugin/UI/EvidencePlot.cs                   new  scatter + top-down (§6.3)
src/BossModForge.Plugin/UI/Ui.cs                             new  colours, confidence badge, helpers
tests/BossModForge.Tests/Pipeline/ForgePipelineTests.cs      new
tests/BossModForge.Tests/Model/OverridesApplierTests.cs      new
tests/BossModForge.Tests/GameData/CachedGameDataTests.cs     new
tests/BossModForge.Tests/Fixtures/OverrideFixtures.cs        new  helpers building Overrides JSON
docs/plans/m5-status.md                                      new  (at end)
README.md, AGENTS.md, docs/specs/...design.md §6             edit one-liners
```

Core stays free of Dalamud/ImGui/BossMod.dll (`AGENTS.md`). `TreatWarningsAsErrors` is on for Core — keep it clean.

## 4. Frozen APIs

All Core types below are `public`, immutable, deterministic, and pure (no I/O except `ForgePipeline.Parse(string)` opening the file). Anything running off the framework thread in the plugin calls only these.

### 4.1 `BossModForge.Core.Pipeline.ForgePipeline`

The stage chain currently lives once, in `InferCommand.Run` (`ReplayParser.Parse → PullSegmenter.Segment → ParticipantClassifier.Classify → AbilityCataloger.Catalog → EncounterModelBuilder.Build`). The plugin would be a second copy, and it needs a seam between parse and inference for the game-data snapshot, so the chain moves into a facade both callers use.

```csharp
namespace BossModForge.Core.Pipeline;

public enum PipelineStage { Parsing, Segmenting, Classifying, Cataloging, Building, ApplyingOverrides, Done }

public sealed record PipelineProgress(PipelineStage Stage, float Fraction); // Fraction ∈ [0,1] overall

public sealed record PipelineOptions(
    ParseOptions? Parse = null,          // Progress inside is honoured during Parsing
    SegmenterOptions? Segmenter = null,
    CatalogOptions? Catalog = null,
    BuildOptions? Build = null);

public sealed record AnalysisResult(
    Recording Recording,
    ImmutableArray<Pull> Pulls,
    ImmutableArray<Participant> Participants,
    AbilityCatalog Catalog,
    EncounterModel Inferred,            // Build output, Overrides == Overrides.Empty
    EncounterModel Effective);          // OverridesApplier.Apply(Inferred with { Overrides = overrides })

public static class ForgePipeline
{
    public static Recording Parse(string path, PipelineOptions? options = null, CancellationToken ct = default);
    public static Recording Parse(Stream stream, string displayPath, PipelineOptions? options = null, CancellationToken ct = default);

    // Ids the game-data provider must be able to answer for this recording. Pure; O(ops).
    public static GameDataKeys CollectKeys(Recording recording);

    public static AnalysisResult Analyze(
        Recording recording,
        IGameData gameData,
        PipelineOptions? options = null,
        Overrides? overrides = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken ct = default);

    // Re-derive Effective from an existing result after an override edit; no inference re-run.
    public static AnalysisResult WithOverrides(AnalysisResult result, Overrides overrides);

    public static EmitResult Emit(EncounterModel effective, Flavor flavor, string flavorsDirectory, EmitOptions? options = null);
}
```

Rules:
- `Analyze` calls `ct.ThrowIfCancellationRequested()` between stages and reports `progress` at each stage boundary with fractions `0.15/0.30/0.45/0.60/0.85/1.0` (Parsing happens in `Parse`, so Analyze starts at 0.15). Stage functions themselves are not cancellable today; that is acceptable (each is sub-second on dungeon logs; note in status if a real log is slower than 2 s per stage).
- `Parse(string, …)` forwards `options.Parse ?? new ParseOptions(KeepRawOps: false)`. `KeepRawOps: false` is the default here (plugin and `infer` both want it); `ingest` keeps calling `ReplayParser.Parse(path)` directly.
- `Effective == Inferred` (reference-equal) when `overrides` is null or empty — no allocation, and tests assert it.
- `Emit` = `ModuleEmitter.Emit(effective, ModuleEmitter.ProfileFor(flavor, flavorsDirectory), options ?? new EmitOptions(flavor, effective.Encounter.Namespace))`. Callers pass an explicit `flavorsDirectory`; the plugin must never rely on `ResolveFlavorsDirectory()`'s `AppContext.BaseDirectory` walk (in Dalamud that is the game/Dalamud directory, not the plugin folder).
- `InferCommand.Run` becomes: `var rec = ForgePipeline.Parse(logPath); var r = ForgePipeline.Analyze(rec, NullGameData.Instance, new PipelineOptions(Build: new BuildOptions(module, namespaceName))); json = EncounterModelJson.Serialize(r.Effective);`. Golden EM JSON must not change by a byte.

### 4.2 `BossModForge.Core.Model.OverridesApplier` and the override key scheme

State of Core today: `EncounterModel.Overrides` is `Overrides(ImmutableSortedDictionary<string, JsonElement> Values)`, always constructed empty in `EncounterModelBuilder.cs:131`, round-tripped by `EncounterModelJson` (`overrides.values` object, ordinal-sorted keys), and **never read** by any stage or by the emitter. The spec's "three-way merge: inferred, overrides, effective" is implemented as: the EM's ordinary fields are the *effective* values; `Overrides.Values` holds the user's edits; *inferred* is recomputable (deterministic pipeline) and additionally kept in memory by the plugin (`AnalysisResult.Inferred`). Re-running inference and re-applying the same `Overrides` yields the same effective EM, which is exactly the M5 acceptance "inference re-run does not clobber it".

Keys are JSON-pointer-shaped strings addressing entities by **stable id, never by array index**:

| Key | Value JSON | Effect |
|---|---|---|
| `/abilities/{aid}/shape` | `Shape` object exactly as `EncounterModelJson` serialises `AbilityModel.Shape`, or `null` | `Shape` replaced/cleared; `ShapeFit` set to `null`; `Confidence` → `Confidence.Of(1.0, "user override: shape")` |
| `/abilities/{aid}/class` | string, a `MechanicClass` name | `Class` replaced; confidence as above |
| `/abilities/{aid}/enumName` | string | `CSharpIdentifier.Sanitise(value, "Ability")`; applied to `AbilityModel.EnumName` **and** `Enums.Aid[aid]`. Collision with another member → not applied, `ReviewItem(High, "override", …)` |
| `/abilities/{aid}/ignore` | `true` | `Class = Unclassified`, `Shape = null`, `ShapeFit = null`; review items whose `Subject` equals the ability's inferred `Name` or `EnumName` are dropped. Ability stays in `Abilities`, `Enums.Aid` and the timeline (state machine unaffected; no component is planned — `ComponentPlanner` ignores `Unclassified`) |
| `/participants/{oid}/enumName` | string | `Sanitise(value, "Actor")`; `ParticipantModel.EnumName` and `Enums.Oid[oid]`; collision → review, not applied. `Boss` for the primary OID may not be renamed (emitter relies on it) → review |
| `/encounter/moduleName` | string | `Encounter.ModuleName = Sanitise(value, "Encounter")` |
| `/encounter/namespace` | string | `Encounter.Namespace = value` (trimmed, non-empty) |
| `/arena` | `ArenaModel` object as serialised | Whole `Arena` replaced; `Confidence.Of(1.0, "user override: arena")`, `Evidence` = `[Evidence("Override", "user")]` |

`{aid}` and `{oid}` are decimal `uint`. If an AID appears under more than one `SourceOid` in `Abilities`, an `/abilities/{aid}/…` override applies to all such entries (the `AID` enum is keyed by AID alone). Unknown key → `ReviewItem(Low, "override", "Unknown override key {key}", [])`, value ignored. Malformed value → `ReviewItem(High, "override", …)`, value ignored. Never throw on override content.

```csharp
namespace BossModForge.Core.Model;

public sealed record Overrides(ImmutableSortedDictionary<string, JsonElement> Values)
{
    public static readonly Overrides Empty = new(ImmutableSortedDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal));
    public bool IsEmpty => Values.Count == 0;
    public Overrides With(string key, JsonElement value);   // value.Clone()
    public Overrides Without(string key);
}

public static class OverridesApplier
{
    public static string AbilityKey(uint aid, string field);        // "/abilities/{aid}/{field}"
    public static string ParticipantKey(uint oid, string field);
    public const string ModuleNameKey = "/encounter/moduleName";
    public const string NamespaceKey = "/encounter/namespace";
    public const string ArenaKey = "/arena";

    // Pure, idempotent: Apply(Apply(em)) == Apply(em). Uses em.Overrides. Appends override-related ReviewItems
    // (sorted, deterministic) and never removes pre-existing ones except as the "ignore" row says.
    public static EncounterModel Apply(EncounterModel em);

    // Convenience for UIs: serialise a Shape / string / bool / ArenaModel into the JsonElement the key expects.
    public static JsonElement ToElement(Shape? shape);
    public static JsonElement ToElement(string value);
    public static JsonElement ToElement(bool value);
    public static JsonElement ToElement(ArenaModel arena);
}
```

Emitter contract to check while implementing (not upstream VERIFY, just read the code): `ModuleEmitter.cs:53,91` and `ComponentPlanner` read `Enums.Aid` / `AbilityModel.EnumName`; `StatesPlanner.Plan(em, aidEnumNames)` resolves step members from `Enums.Aid`. Confirm `TimelineStep.Name` is display-only (`EncounterTimelineBuilder.StepName`); if the emitter ever prints `TimelineStep.Name`, the applier must also rewrite it for renamed AIDs. Add one sentence to spec §6 after the existing `"overrides"` sentence naming this key scheme and pointing at `OverridesApplier`.

### 4.3 Game data: `GameDataKeys`, `CachedGameData` (Core) and `DalamudGameData` (Plugin)

Core half (pure, testable):

```csharp
namespace BossModForge.Core.GameData;

public sealed record GameDataKeys(
    ImmutableSortedSet<uint> ActionIds,      // ActionId.Type == Spell from ActorCastStartOp/ActorCastEventOp (all actors; classifier filters players later)
    ImmutableSortedSet<uint> BNpcNameIds,    // ActorTimeline.NameId != 0 for non-player actors, plus ActorRenameOp.NameId
    ImmutableSortedSet<uint> StatusIds,      // ActorStatusOp.Status?.Id
    ImmutableSortedSet<ushort> TerritoryTypes); // ZoneChangeOp.Zone

public sealed class CachedGameData : IGameData
{
    public CachedGameData(
        ImmutableSortedDictionary<uint, ActionRow> actions,
        ImmutableSortedDictionary<uint, string> bnpcNames,
        ImmutableSortedDictionary<uint, string> statusNames,
        ImmutableSortedDictionary<uint, (uint CfcId, string Name)> contentByTerritory,
        ImmutableSortedDictionary<uint, string> placeNames);
    public static readonly CachedGameData Empty;
    // IGameData: dictionary lookups; miss → null (same contract as NullGameData)
    public int Count { get; }
}
```

`ForgePipeline.CollectKeys` produces `GameDataKeys`. Nothing in Core reads Lumina.

Plugin half — runs **only on the framework thread**, produces the immutable `CachedGameData` that the background task then uses:

```csharp
namespace BossModForge.Plugin.Services;

public sealed class DalamudGameData
{
    public DalamudGameData(IDataManager data);      // Svc.Data
    public CachedGameData Snapshot(GameDataKeys keys); // framework thread only; never throws for a missing row
}
```

Sheet/column mapping (all `Lumina.Excel.Sheets.*`, Lumina ≥ 5 struct rows as shipped with Dalamud API 13+; see §8 for the VERIFY marks):

| `IGameData` member | Sheet | Columns → `ActionRow` / value |
|---|---|---|
| `Action(id)` | `Action` | `RowId`; `Name.ExtractText()`; `CastType` (byte); `EffectRange` (byte); `XAxisModifier` (byte); `Cast100ms` (ushort); `Omen.RowId` → `OmenId`; `Omen.ValueNullable?.Path.ExtractText()` → `OmenPath` (VERIFY-1, VERIFY-2) |
| `BNpcName(nameId)` | `BNpcName` | `Singular.ExtractText()` |
| `StatusName(id)` | `Status` | `Name.ExtractText()` |
| `ContentForTerritory(tt)` | `TerritoryType` → `ContentFinderCondition` | `ContentFinderCondition.RowId`, `.ValueNullable?.Name.ExtractText()` (VERIFY-3) |
| `PlaceName(tt)` | `TerritoryType` → `PlaceName` | `PlaceName.ValueNullable?.Name.ExtractText()` |

Lookup: `Svc.Data.GetExcelSheet<T>()` (may be null → treat as empty sheet) then `sheet.GetRowOrDefault(id)` (returns `T?`; confirmed in repo usage `ActionStacksEX/Modules/QueueMore.cs:58`, `ParseLord5/.../Text.cs:241`). Empty strings become `null`. Only ids in `keys` are read — no full-sheet enumeration.

### 4.4 `AnalysisRunner` (Plugin) — state machine

```csharp
public enum RunnerState { Idle, Parsing, FetchingGameData, Inferring, Ready, Failed, Cancelled }

public sealed record RunnerSnapshot(
    RunnerState State,
    float Progress,                 // 0..1 overall: Parsing 0–0.15 (from ParseOptions.Progress), FetchingGameData 0.15, Inferring 0.15–1.0
    string StageText,               // "Parsing 43 MB…", "Fetching 212 action rows", "Inferring: Cataloging"
    string? Error,                  // Failed only: ex.Message (full ex logged via Svc.Log.Error)
    AnalysisResult? Result,         // Ready only
    string? SourcePath,
    DateTime StartedUtc, DateTime? FinishedUtc);

public sealed class AnalysisRunner : IDisposable
{
    public RunnerSnapshot Snapshot { get; }                 // Volatile.Read of an immutable record; safe from any thread
    public bool IsBusy => State is Parsing or FetchingGameData or Inferring;
    public void Start(string path, PipelineOptions options, Overrides carryOver);  // framework thread; throws InvalidOperationException if IsBusy
    public void Cancel();                                                          // framework thread; idempotent
    public event Action<RunnerSnapshot>? Completed;        // raised on the framework thread via IFramework.RunOnFrameworkThread
}
```

Transitions: `Idle|Ready|Failed|Cancelled --Start--> Parsing --> FetchingGameData --> Inferring --> Ready`; any active state `--Cancel-->` `Cancelled`; any exception → `Failed`. Only these edges; the snapshot is replaced wholesale (`Interlocked.Exchange`) — never mutated.

Threading contract:
1. `Start` runs on the framework thread (UI button). It captures `path`, `options`, `carryOver`, creates a `CancellationTokenSource`, publishes `Parsing`, then `Task.Run(RunAsync)`.
2. `RunAsync` (thread pool): `var rec = ForgePipeline.Parse(path, options with Parse progress → snapshot updates throttled to ≥ 50 ms, ct)`; `var keys = ForgePipeline.CollectKeys(rec)`; publish `FetchingGameData`.
3. Game data: `var cache = await Svc.Framework.RunOnFrameworkThread(() => gameData.Snapshot(keys)).ConfigureAwait(false);` (VERIFY-6 for the exact `IFramework.RunOnFrameworkThread<T>(Func<T>)` shape). This is the *only* game access in the whole analysis and it happens on the framework thread. Budget: log a warning if it takes > 8 ms (measure with `Stopwatch`); if real logs show > 8 ms for large key sets, chunk `keys` into batches of 256 across successive `RunOnFrameworkThread` calls.
4. Publish `Inferring`; `var result = ForgePipeline.Analyze(rec, cache, options, carryOver, progress, ct)` on the pool thread; publish `Ready` and raise `Completed` on the framework thread.
5. `OperationCanceledException` → `Cancelled`; anything else → `Failed`.
6. `Dispose` cancels and does not block on the task.

No `Framework.Update` handler is needed in the plugin at all: UI reads `Snapshot` inside `Window.Draw` (which runs on the framework thread), and the runner never touches game objects. Frame budget acceptance (plan M5): all `Draw` bodies stay < 2 ms on a 300-ability EM; the EvidencePlot draws at most 2 000 points (subsample deterministically by stride).

### 4.5 `ForgeSession` (Plugin) — the single mutable state, framework thread only

```csharp
public sealed class ForgeSession
{
    public AnalysisResult? Result { get; }        // from the runner
    public Overrides Overrides { get; }           // current edits (carried across re-analysis)
    public EncounterModel? Effective => Result?.Effective;
    public EmitResult? LastEmit { get; }
    public ValidationReport? LastValidation { get; }
    public string? LastOutputDir { get; }
    public IReadOnlyList<string> Messages { get; }  // last 20 toasts/log lines shown in Export

    public void Accept(AnalysisResult r);                          // Result = r (its Effective already has Overrides applied)
    public void SetOverride(string key, JsonElement value);        // Overrides = Overrides.With; Result = ForgePipeline.WithOverrides(Result, Overrides)
    public void ClearOverride(string key);                         // Overrides.Without; if Result.Inferred available → WithOverrides; else message "re-analyze to restore inferred value"
    public void ClearAllOverrides();
    public void LoadEncounterModel(string emJsonPath);             // Result = new AnalysisResult(recording: null?) — NO: see below
}
```

Loading a saved EM without its recording is out of scope for M5 (the runner needs the `Recording` for evidence plots and validation). Export writes EM JSON; the Recordings window re-analyzes from the `.log`. Overrides persistence across game sessions: `Configuration.OverridesBySha256[recordingSha256] = overrides.values JSON string`, saved on every `SetOverride`, loaded into `carryOver` when Analyze starts on a recording whose `Sources[0].Sha256` matches (compute SHA-256 of the file on the pool thread before parse; `SourceInfo.Sha256` is what the builder writes — use the same hex casing).

## 5. Plugin project

`src/BossModForge.Plugin/BossModForge.Plugin.csproj` — mirror `../TankCooldownHelper/TankCooldownHelper.csproj` exactly except as noted:

```xml
<Project Sdk="Dalamud.NET.Sdk/15.0.0">
  <PropertyGroup>
    <AssemblyName>BossModForge</AssemblyName>
    <RootNamespace>BossModForge.Plugin</RootNamespace>
    <Version>0.5.0.0</Version>
    <Description>Generate BossMod modules from replay recordings</Description>
    <Authors>…</Authors>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <Nullable>enable</Nullable>
    <LangVersion>13</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
    <OutputPath>$(AppData)\XIVLauncher\devPlugins\BossModForge\</OutputPath>
    <DalamudPackagerEnabled>false</DalamudPackagerEnabled>
    <InvariantGlobalization>false</InvariantGlobalization>   <!-- override Directory.Build.props; host owns runtime config -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ECommons" Version="3.1.0.13" />
    <ProjectReference Include="..\BossModForge.Core\BossModForge.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="..\..\flavors\*.json" Link="flavors\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <Target Name="CopyToDevPlugins" AfterTargets="Build"> … copy BossModForge.dll, BossModForge.json, ECommons.dll, BossModForge.Core.dll,
        JsonSchema.Net.dll and its transitive DLLs (copy $(OutputPath)*.dll), flavors\*.json … </Target>
</Project>
```

Notes: `Directory.Build.props` at repo root applies to the plugin (LangVersion 13, Nullable, ImplicitUsings, `EnforceCodeStyleInBuild`) — good; only `InvariantGlobalization` is overridden. Core is `net10.0` and is referenced from a `net10.0-windows` project — allowed. Core's `JsonSchema.Net` 9.4.0 (and `Json.More.Net`, `JsonPointer.Net` — VERIFY-9 the transitive list from `obj/project.assets.json` on Windows) must land in the plugin folder; Dalamud loads dependencies from the plugin directory.

`BossModForge.Plugin.json`: copy `TankCooldownHelper.json`, `InternalName` `BossModForge`, `Name` `BossModForge`, `DalamudApiLevel` 15 (VERIFY-4: keep equal to whatever `TankCooldownHelper.json` currently has on the Windows box — its `CLAUDE.md` says API 14 while its manifest says 15; the manifest wins), `Punchline` "Turn BossMod replays into boss modules".

`Plugin.cs` (`public sealed class Plugin : IDalamudPlugin`): mirror `TankCooldownHelper.cs:30-77` and `:109-124`:
- ctor: `ECommonsMain.Init(pluginInterface, this);` (no extra modules), `Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new()`, build services (`DalamudGameData(Svc.Data)`, `ReplayDiscovery`, `AnalysisRunner`, `ForgeSession`, `BossModIpc`, `Exporter`), `WindowSystem("BossModForge")` + six windows, `pluginInterface.UiBuilder.Draw += ws.Draw`, `UiBuilder.OpenMainUi += () => recordings.IsOpen = true`, `UiBuilder.OpenConfigUi += () => settings.IsOpen = true`, `Svc.Commands.AddHandler("/forge", new CommandInfo(OnCommand){ HelpMessage = "Open BossModForge (/forge recordings|encounter|abilities|timeline|export|settings)" })`.
- `OnCommand`: empty arg → toggle Recordings; otherwise open the named window; unknown → `Svc.Chat`-free: print help via `Svc.Log.Info` and open Recordings.
- `Dispose`: `Svc.Commands.RemoveHandler("/forge")`, `UiBuilder.Draw -=`, `OpenMainUi -=`, `OpenConfigUi -=`, `ws.RemoveAllWindows()`, `runner.Dispose()`, `ECommonsMain.Dispose()`.
- No `Framework.Update` subscription (see §4.4).

`Configuration : IPluginConfiguration` (`[Serializable]`, `Version = 1`, `Save(IDalamudPluginInterface)` like TCH): `List<string> ReplayDirectories` (empty = use defaults), `string? OutputDirectory` (null → `<ConfigDirectory>/output`), `string Flavor = "vbm"`, `string? DefaultNamespace`, `string Maturity = "WIP"`, `string? Contributors`, `bool OpenFolderAfterExport = true`, `Dictionary<string,string> OverridesBySha256`, `bool AdvancedThresholds`, and the numeric inference options mirrored from `ShapeInferenceOptions`/`BoundsFitterOptions`/`SegmenterOptions` as nullable overrides (null = Core default; build a `PipelineOptions` from them in `Settings.ToPipelineOptions()`).

## 6. Window-by-window behaviour

All windows: `Window` subclasses (`Dalamud.Interface.Windowing`), title `"BossModForge — X###ForgeX"`, `RespectCloseHotkey = true`, `SizeConstraints` min 480×320. Shared header line drawn by `Ui.DrawSessionBar(session, runner)`: current recording file name, state badge, progress bar while busy, **Cancel** while busy. Confidence colours (`Ui.Band(ConfidenceBand)`): High `#4CAF50`, Medium `#FFC107`, Low `#F44336`; render as a coloured square + score `0.83`. Everything reads `session`/`runner.Snapshot` only; no game object access anywhere in UI.

### 6.1 Recordings

- Top: replay directories from `ReplayDiscovery.Directories` (config list or defaults), each with an exists/missing tag and a **Rescan** button. Defaults: `Path.Combine(Svc.PluginInterface.ConfigDirectory.Parent!.FullName, "BossMod", "replays")` and `…"BossModReborn", "replays"` (VERIFY-7 dir names; VERIFY-8 file pattern).
- `ReplayDiscovery.Scan()` (framework thread, cheap): enumerate `*.log` (top level, plus one level of sub-dirs) → `ReplayFile(Path, SizeBytes, LastWriteUtc)` sorted by `LastWriteUtc` desc. Parsed meta (`RecordingMeta.Version`, `GameVersion`, `StartTime`, zone, boss guess, pull count, duration) is filled **only after** an analysis of that file and cached in a `Dictionary<string, RecordingSummary>` in memory for the session; unanalysed rows show `—`.
- Table columns: File · Size · Modified · Version · Game ver · Zone/CFC · Boss · Pulls · Duration. Single selection; **Analyze** button enabled when one row is selected and runner is idle. Multi-select is intentionally disabled with tooltip "Merging recordings arrives in M6".
- BossMod status line: `BossModIpc.Probe()` on window open and on **Rescan**: `Svc.PluginInterface.GetIpcSubscriber<uint, bool>("BossMod.HasModuleByDataId").InvokeFunc(0)` in try/catch exactly as `BossmodBridge.cs:26-39` → "BossMod IPC: available / not found". BMR probe uses the same IPC name (VERIFY-10). Text hint "Recording is controlled in BossMod's own settings (`/vbm` → Replays); Forge does not record." No recorder, no settings deep-link.

### 6.2 Encounter

- Identity block: `Encounter.BossName`, `DutyName`, `Zone`, `CfcId`, `PrimaryOid`, `ModuleName` (editable → `/encounter/moduleName`), `Namespace` (editable → `/encounter/namespace`), sources (`Sources[]` path/sha/version/game version/start-end).
- Pulls table from `Pulls[]`: index, start, duration, outcome, confidence.
- Participants table from `Participants[]`: OID (hex), Name, NameId, Type, Role, hitbox, instances, spawns-mid-fight, confidence, **Enum name** (inline `ImGui.InputText` → on Enter `/participants/{oid}/enumName`; primary OID row read-only "Boss"). Overridden cells show an "✎" marker and a **reset** button (`ClearOverride`).
- Arena block: kind/center/radius/half-extents/vertex count + confidence; **Edit arena…** opens a small popup editing `ArenaModel` fields → `/arena`.

### 6.3 Abilities (the review table) + evidence panel

Left (table, `ImGuiTableFlags.Resizable|Sortable|ScrollY|RowBg`): AID · Name · Source (OID enum) · Cast (s) · Shape (`Circle r=7.5`, `Cone r=18 ±40°`, `Rect 40×4`, `Donut 5–20`, `—`) · Class · Confidence (coloured) · Instances · Actions.
Actions per row: **Accept** (sets `/abilities/{aid}/…` nothing; purely marks the row reviewed in session memory — greys the confidence colour), **Override shape…** (popup: kind combo over `ShapeKind`, numeric fields relevant to the kind, origin/direction combos; OK → `/abilities/{aid}/shape` via `OverridesApplier.ToElement(shape)`; "Clear shape" → `null` element), **Class** combo over `MechanicClass` → `/abilities/{aid}/class`, **Enum name** inline edit → `/abilities/{aid}/enumName`, **Ignore** checkbox → `/abilities/{aid}/ignore` (`true`) / `ClearOverride`, **Evidence** selects the row for the right panel. Filter box (name/AID), "hide Low-band" toggle, "only overridden" toggle. Overridden cells carry the ✎ marker + reset.

Right (`EvidencePlot`, drawn with `ImGui.GetWindowDrawList()` into a `Dummy` region):
1. **Scatter angle-vs-distance** for the selected ability: for each `AbilityInstance` in `Result.Catalog.Abilities` matching AID, for each `AbilityHit` compute target polar coords relative to the source at `EffectTime` (`Recording.ActorAt(SourceInstanceId, EffectTime)?.PosRotAt(EffectTime)` — the same inputs `ShapeInference.Infer` uses; if a helper in `ShapeObservations` already yields `(angleDeg, dist, hit)` pairs, call it rather than re-deriving — check `Inference/ShapeObservations.cs` first). X = relative angle −180..180°, Y = distance 0..max(arena diameter, 40). Hit points green, sampled non-hit players (from `Pull.PlayerInstanceIds` at `EffectTime`) grey. Overlay the **effective** shape as a translucent region: circle → horizontal band `[InnerRadius, Radius]`; cone → rectangle `|angle| ≤ HalfAngleDeg`, `dist ≤ Radius`; rect → polyline of `dist ≤ HalfWidth/|sin θ|`-style bound (approximate by sampling 90 θ values); donut → band. Axis ticks every 45° and 5 y.
2. **Top-down instance view** (MinimapRenderer approach: `center = origin + canvas/2`, `scale = canvas/2 / arenaRadius`, `AddCircle` for the arena, `AddCircleFilled` for actors, `AddLine` for facing): instance selector (`◄ 3/12 ►`), arena from `Effective.Arena` (circle/square/rect/custom vertices via `AddPolyline`), boss at its pos/rotation, players as dots (hit = red, not hit = grey), effective shape drawn in world space (circle/donut with `AddCircle`; cone with `PathArcTo`+`PathLineTo`+`PathFillConvex`; rect/cross as rotated quads with `AddQuadFilled`; VERIFY-5 for these `ImDrawListPtr` members). Tooltip on hover shows AID, instance time, source pos.

### 6.4 Timeline

- Header: `Timeline.PullCount`, `Timeline.Confidence`, loop summary (`Loop.StartStep/Length/Repeats/TrailingPartial`).
- Main table, one row per `TimelineStep` (`Id · Kind · AID enum · Delay · Cast · MAD · Support x/N · Activate · Deactivate · Review`), phase separators drawn from `Phases[]` (`Name`, `Kind`, `HpThreshold`), loop rows tinted. MAD > 1.5 s and Support < 60 % tinted amber (same thresholds M4 used to emit `Timeout`).
- **Consensus lanes** beneath (draw list): one horizontal lane per pull, x = time since pull start; each lane shows that pull's casts of the boss/primary as ticks (from `Result.Catalog.Abilities[*].Instances` where `PullIndex == lane`), coloured by AID hash; the consensus row on top shows steps at cumulative `Delay`. Click a step → side box lists, per pull, the raw casts within ±5 s of the step's consensus time (`Name`, offset). Zoom via `ImGui.SliderFloat` (px/s), scroll horizontally with the wheel.

### 6.5 Export

- Fields: Flavor (`vbm`/`bmr`; `bmr` shows the `flavors/bmr.json` `"verified": false` warning), Namespace (prefilled from `Effective.Encounter.Namespace`; editing sets `/encounter/namespace`), Class name (`ModuleName`; same override path), Maturity (`WIP/Contributed/Verified`), Contributors, Output directory (`Configuration.OutputDirectory ?? <ConfigDirectory>/output`, final path `<out>/<ModuleName>/`).
- Buttons (each runs on `Task.Run`, disabled while running, results into `session.Messages`):
  - **Write EM JSON** → `<out>/<ModuleName>/<ModuleName>.em.json` = `EncounterModelJson.Serialize(Effective)`; also `EncounterModelJson.Validate(json)` and show issues if any.
  - **Emit C#** → `ForgePipeline.Emit(Effective, flavor, FlavorsDir, new EmitOptions(flavor, ns, maturity, contributors))`; write each `EmittedFile.RelativePath` + `FORGE.md` (same layout as `EmitCommand.cs:108-111`); list files with sizes and the `EmitResult.Review` count.
  - **Copy to clipboard** → `ImGui.SetClipboardText` of the concatenated `.cs` files (or of the EM JSON when the toggle says so).
  - **Open output folder** → `Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true })`.
  - **Validate** → `DryValidator.Validate(Effective, [(path, Result.Recording)], validationOptions)` on the pool thread (pure; the `Recording` is already in memory); on completion `session.LastValidation = report`; **Write report** writes `validation-report.{json,md}` via `ValidationReportWriter` into the output dir.
- Report panel: `Passed` badge, `Timing.P50/P90/Max`, missed steps, per-ability `AoeReport` precision/recall table (red when < 0.95), `Coverage`, `Failures[]`, `SuggestedReview[]`. **Apply downgrades** → `session.Accept(result with { Effective = DryValidator.ApplyDowngrades(Effective, report) })` (session-only; not an override).
- `FlavorsDir = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, "flavors")`; if `vbm.json` is missing there, Export shows an error and disables Emit.

### 6.6 Settings

Replay directories (list editor: add via `InputText` + **Add**, remove, **Reset to defaults**), output directory, flavor default, namespace default, maturity, contributors, "open folder after export", **Advanced** collapsing header exposing the nullable inference thresholds (segmenter min pull duration / gap; `ShapeInferenceOptions` and `BoundsFitterOptions` numeric fields — read their current parameter lists from `Inference/ShapeInference.cs:9-25` and `BoundsFitter.cs:8-20` and expose exactly those), with per-field **reset**. **Clear stored overrides** (wipes `OverridesBySha256` after a confirm popup). Save on every change (`Configuration.Save(Svc.PluginInterface)`). Changing thresholds shows "takes effect on next Analyze".

## 7. Dalamud/ECommons/ImGui surface (exact members)

Confirmed in this repo (TankCooldownHelper, MarkerMind on the same SDK line) — use as-is:
- `Dalamud.Plugin.IDalamudPlugin`, `IDalamudPluginInterface` (`GetPluginConfig()`, `SavePluginConfig(IPluginConfiguration)`, `UiBuilder.Draw`, `GetIpcSubscriber<T1,TRet>(string).InvokeFunc(...)`); `Dalamud.Configuration.IPluginConfiguration`; `Dalamud.Game.Command.CommandInfo(HandlerDelegate){ HelpMessage, ShowInHelp }`; `Dalamud.Interface.Windowing.WindowSystem(string)` (`AddWindow`, `Draw`), `Window(string name)` (`IsOpen`, `RespectCloseHotkey`, `Draw()`); `Dalamud.Plugin.Services.IDataManager`, `IFramework`, `ICommandManager`, `IPluginLog`.
- ECommons 3.1.0.13: `ECommonsMain.Init(pluginInterface, plugin)`, `ECommonsMain.Dispose()`, `ECommons.DalamudServices.Svc.{PluginInterface, Data, Framework, Commands, Log}`.
- ImGui: `using Dalamud.Bindings.ImGui;` — the namespace in this SDK line is `Dalamud.Bindings.ImGui` (not `ImGuiNET`), confirmed by `TankCooldownHelper/UI/MainWindow.cs:8` and `MarkerMind/.../MinimapRenderer.cs:1`. Members already used in-repo: `ImGui.TextDisabled/Separator/Begin/End/SetNextWindowSize/GetCursorScreenPos/GetContentRegionAvail/GetWindowDrawList`, `ImGuiCond`, `ImGuiWindowFlags`, `ImDrawListPtr.AddCircle(Vector2,float,uint,int,float)`, `AddCircleFilled(Vector2,float,uint,int)`, `AddLine(Vector2,Vector2,uint,float)`.

Used by M5 and **not** yet seen in-repo (VERIFY-5, all standard Dear ImGui 1.9x names; if a member is missing, the fix is local to `EvidencePlot`/`Ui`): `ImGui.BeginTable/EndTable/TableSetupColumn/TableSetupScrollFreeze/TableHeadersRow/TableNextRow/TableNextColumn/TableSetColumnIndex`, `ImGuiTableFlags`, `ImGuiTableColumnFlags`, `ImGui.Selectable`, `ImGui.InputText(string, ref string, uint maxLength, ImGuiInputTextFlags)`, `ImGui.InputFloat`, `ImGui.SliderFloat`, `ImGui.Checkbox`, `ImGui.BeginCombo/EndCombo`, `ImGui.Button`, `ImGui.SameLine`, `ImGui.ProgressBar(float, Vector2, string)`, `ImGui.TextColored(Vector4,string)`, `ImGui.PushStyleColor(ImGuiCol, Vector4)/PopStyleColor`, `ImGui.OpenPopup/BeginPopupModal/EndPopup/CloseCurrentPopup`, `ImGui.CollapsingHeader`, `ImGui.Dummy`, `ImGui.InvisibleButton`, `ImGui.IsItemHovered`, `ImGui.SetTooltip`, `ImGui.GetIO().MouseWheel`, `ImGui.SetClipboardText(string)`, `ImGui.ColorConvertFloat4ToU32`, `ImDrawListPtr.AddRectFilled/AddRect/AddText/AddPolyline/AddQuadFilled/AddTriangleFilled/PathArcTo/PathLineTo/PathFillConvex/PathStroke/PushClipRect/PopClipRect`.

## 8. VERIFY table (resolve on the Windows box before/while writing the code that uses them; remove tags once confirmed, record in `docs/UPSTREAM.md` §9 or a new §10 "Dalamud/Lumina at API 15")

| # | Item | Where used | Best guess | How to confirm |
|---|---|---|---|---|
| 1 | `Lumina.Excel.Sheets.Action` columns `Name` (`ReadOnlySeString`, `.ExtractText()`), `CastType`, `EffectRange`, `XAxisModifier` (byte), `Cast100ms` (ushort), `Omen` (`RowRef<Omen>`) | `DalamudGameData` | as listed; `CastType/EffectRange/Cast100ms` confirmed in-repo (`ParseLord5/.../Debug.cs:825-1361`), `XAxisModifier`/`Omen` not | Decompile `Lumina.Excel.dll` shipped with Dalamud or F12 in the IDE |
| 2 | `Omen` sheet has `Path` (`ReadOnlySeString`) and the string looks like `gl_fan090_1bf` (what `OmenParser.TryParseFan` expects) | `DalamudGameData` | `Omen.Path`, also `PathAlly`, `Type` | Same; then check one known fan action in-game |
| 3 | `TerritoryType.ContentFinderCondition` is a `RowRef<ContentFinderCondition>` with `.RowId` / `.ValueNullable?.Name`; `TerritoryType.PlaceName.ValueNullable?.Name` | `DalamudGameData` | yes (`PlaceName.Value.Name` seen in `ParseLord5/.../Debug.cs:353`) | Decompile |
| 4 | Manifest `DalamudApiLevel` matching `Dalamud.NET.Sdk/15.0.0` | `BossModForge.Plugin.json` | `15` (TankCooldownHelper.json) | Load in devPlugins; Dalamud refuses a mismatched level |
| 5 | ImGui members listed in §7 "not yet seen in-repo" exist in `Dalamud.Bindings.ImGui` with the given shapes (esp. `InputText` overload with `ref string`, `ProgressBar`, `PathArcTo`) | `Ui`, `EvidencePlot`, all windows | yes | Compile |
| 6 | `IFramework.RunOnFrameworkThread<T>(Func<T>) : Task<T>` (and `RunOnFrameworkThread(Action) : Task`) | `AnalysisRunner` step 3, `Completed` marshalling | yes | F12 on `IFramework` |
| 7 | Replay dir names: `<pluginConfigs>/BossMod/replays` for vbm; BossModReborn's `InternalName` → `<pluginConfigs>/BossModReborn/replays` | `ReplayDiscovery` defaults | vbm confirmed by spec §4.1; BMR unverified | Check `ReplayManagementConfig` in both repos / look at a live install |
| 8 | Replay file pattern: `*.log` (text, binary, Brotli all use `.log`?) and name format `World_yyyyMMdd_HHmmss.log` | `ReplayDiscovery` | `*.log`; parser magic-detects encoding | `ReplayRecorder.cs` at `1391bc42` |
| 9 | Transitive DLL set of `JsonSchema.Net` 9.4.0 to copy next to the plugin | csproj copy target | `JsonSchema.Net.dll`, `Json.More.Net.dll`, `JsonPointer.Net.dll` | `obj/project.assets.json` after restore |
| 10 | BMR exposes `BossMod.HasModuleByDataId` under the same IPC name | `BossModIpc` | probably (fork) | grep BMR source; else probe both names |
| 11 | `IDalamudPluginInterface.ConfigDirectory` (`DirectoryInfo`) and `.AssemblyLocation` (`FileInfo`) exist with these names | paths in §6.1/§6.5 | yes | F12 |

Items 1–3 gate `DalamudGameData`; 5–6 gate the runner and UI; the rest are defaults that fail soft.

## 9. Core-side tests (run on Linux; these are the acceptance we can execute here)

`tests/BossModForge.Tests/Pipeline/ForgePipelineTests.cs`
- `Analyze_MatchesInferCommandGoldens`: for each fixture with a golden EM (`SyntheticDungeon`, `SyntheticShapes`, `SyntheticRotation`, `SyntheticPhases`), `Serialize(ForgePipeline.Analyze(Parse(fixture), NullGameData).Effective)` equals the golden byte-for-byte; `InferCommand.Run` output unchanged (existing `Cli/InferEmitCommandTests` keep passing).
- `Analyze_NoOverrides_EffectiveIsInferred`: `ReferenceEquals(r.Inferred, r.Effective)`.
- `Analyze_Progress_IsMonotonicAndEndsAtOne`, `Analyze_Cancelled_Throws` (pre-cancelled token → `OperationCanceledException` before any stage).
- `CollectKeys_SyntheticDungeon`: `ActionIds` equals the set of boss AIDs the fixture plants; no player AIDs? — players *are* included by design; assert the boss AIDs ⊆ set and the set contains only `Spell`-type ids; `TerritoryTypes` == fixture zone; `BNpcNameIds` contains the boss NameId.
- `Emit_UsesGivenFlavorsDirectory`: passing a temp dir with copied `vbm.json` works; passing a dir without it throws `FileNotFoundException` (surface as message in plugin).

`tests/BossModForge.Tests/Model/OverridesApplierTests.cs`
- `ShapeOverride_ChangesEmAndCSharp`: SyntheticShapes; set `/abilities/{aid}/shape` to `Shape.Circle(12)`; `Effective.Abilities[aid].Shape.Radius == 12`, `ShapeFit == null`, confidence High with reason `user override: shape`; emitted `.cs` contains `AOEShapeCircle(12)` for that AID and the inferred value is gone.
- `Reanalyze_PreservesOverrides`: `WithOverrides(Analyze(rec, …, overrides), overrides).Effective` serialises identically to `Analyze(rec, …, overrides).Effective`, and to a fresh `Analyze` of the same recording with the same overrides (the M5 acceptance "re-run does not clobber").
- `Apply_IsIdempotent`: `Apply(Apply(em)) == Apply(em)` (serialised).
- `ClassOverride`, `EnumNameOverride_UpdatesEnumsAndComponents` (emitted `enum AID` and component line use the new member), `EnumNameCollision_ReviewNotApplied`, `Ignore_RemovesComponentKeepsEnumAndTimeline` (no component line for the AID; `enum AID` still has it; timeline step count unchanged; that ability's review items dropped), `ParticipantEnumName`, `BossRename_Rejected`, `ModuleNameAndNamespace`, `ArenaOverride_EmitsNewBounds`, `UnknownKey_LowReview`, `MalformedValue_HighReview_NoThrow`, `NullShape_ClearsShape`.
- `JsonRoundTrip_KeepsOverrides`: `Deserialize(Serialize(effective)).Overrides.Values` equals the input (ordinal order), and `Apply` on the round-tripped EM is a no-op (serialised equal).
- `Determinism_TwoRunsByteIdentical`.

`tests/BossModForge.Tests/GameData/CachedGameDataTests.cs`
- Lookup hits/misses mirror `NullGameData` on misses; `Empty.Count == 0`; a `CachedGameData` with one fan `Action` row (`CastType 3`, `OmenPath "gl_fan090_1bf"`) makes `ShapeInference.PriorFor` return the cone prior (ties the plugin's first real game-data path to existing M2 behaviour).

Existing 124 tests stay green; `scripts/build.sh` green; `quality-gate` `release` level passes.

## 10. "Compiles on Windows" checklist (record results in `docs/plans/m5-status.md`)

1. `dotnet build BossModForge.Plugin.slnx -c Release` succeeds with 0 warnings-as-errors from Core, and the devPlugins folder contains `BossModForge.dll`, `BossModForge.json`, `BossModForge.Core.dll`, `ECommons.dll`, `JsonSchema.Net.dll` (+ transitive), `flavors/vbm.json`, `flavors/bmr.json`.
2. Dalamud loads the plugin (dev plugin, API level accepted); `/forge` opens Recordings; `/forge settings` opens Settings.
3. Recordings lists the default BossMod replay dir with at least one `.log`; BossMod IPC line reads "available" when vbm is loaded, "not found" when unloaded.
4. Analyze a real recording: state goes `Parsing → FetchingGameData → Inferring → Ready`; progress bar moves; **Cancel** mid-parse yields `Cancelled` within one second; no exception in `/xllog`.
5. Frame budget: with Dalamud's `/xlstats` (or a `Stopwatch` around `WindowSystem.Draw` logged when > 4 ms) no Forge frame exceeds 16 ms during analysis and while the Abilities window is open with the evidence panel drawing.
6. Abilities shows real names (Lumina `Action.Name`) and boss name (`BNpcName.Singular`) — proves VERIFY-1..3; a fan AID shows a Cone prior.
7. Override a shape → Export → Emit C# → the `.cs` contains the overridden constructor; **Analyze** the same file again → override still present (✎ marker, same C#).
8. Validate produces a report; **Write report** creates `validation-report.json/md`; **Open output folder** opens Explorer; **Copy to clipboard** pastes the C#.
9. Unload/reload plugin twice: no leaked command handler, no duplicate windows, no `Framework.Update` handlers (there are none).
10. `dotnet build BossModForge.slnx` and `dotnet test BossModForge.slnx` on the same Windows box still pass (plugin excluded, Linux parity).

## 11. Determinism and safety reminders

- All Core additions: sorted dictionaries/sets, ordinal string comparison, no `DateTime.Now`, no `Dictionary` enumeration into output.
- `OverridesApplier` never throws on user data; it converts problems into `ReviewItem`s.
- Plugin never blocks the framework thread on the analysis task (`Task.Run` + `RunOnFrameworkThread` marshalling only); never accesses `Svc.Data`/`Svc.Objects`/`Svc.ClientState` from the pool thread.
- Scratch files outside the repo. Update `AGENTS.md` child index with `src/BossModForge.Plugin/ — Dalamud UI; Windows-only; built via BossModForge.Plugin.slnx` and README's build section with the two-solution matrix.

## 12. Status report expected (`docs/plans/m5-status.md`)

Shipped list, test count, deviations from this brief (with reasons), the resolved VERIFY table (what each turned out to be), the Windows checklist results (or "not yet executed — Linux only" per item), and anything punted to M6.
