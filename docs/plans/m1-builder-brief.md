---
tags:
  - type/plan
  - project/bossmodforge
  - status/draft
type: plan
project: bossmodforge
status: frozen
milestone: M1
spec: ../specs/2026-09-18-bossmodforge-design.md
plan: ./2026-09-18-bossmodforge-implementation.md
upstream: ../UPSTREAM.md
depends_on: ./m0-builder-brief.md
---

# M1 builder brief — segmentation, catalog, EM v1, trivial module

Tracer bullet: `forge infer a.log -o em.json && forge emit em.json --flavor vbm -o out/` produces `EncounterModel` JSON and a C# file with `OID`/`AID`/`SID`/`IconID`/`TetherID` enums, a `*States : StateMachineBuilder` calling `TrivialPhase()`, and a `[ModuleInfo]` `BossModule` with a placeholder `ArenaBoundsCircle(20)` that compiles inside the pinned vbm checkout.

Starts only after M0 is merged. All M0 contracts (`Recording`, ops, `ActorTimeline`, `IGameData`) are read-only inputs here. Algorithms are specified in the design spec §5.1–5.3 and §6 (EM); this brief fixes files and public APIs so two builders can split the work.

## 1. Scope

In: `PullSegmenter`, `ParticipantClassifier`, `AbilityCataloger` (abilities + statuses + tethers + icons tables, cast↔effect matching), `Confidence`/`Evidence`/`ReviewItem`, `EncounterModel` v1 + JSON schema + deterministic serializer, identifier sanitiser, `IEmitterProfile` + `VbmProfile` + `BmrProfile` (bmr = stub that emits a review item and the vbm text until verified), `ModuleEmitter` (enums + TrivialPhase + module), `FORGE.md` writer, `flavors/*.json`, CLI `infer` and `emit`.

Out: shape inference, bounds fitting, mechanic classes, timeline/state machine, validate harness, plugin, merge. **`LuminaGameData` / `--game-path` deferred to M1b** (Lumina column VERIFY in §5 is still open and neither builder has a game install); M1 runs on `NullGameData` only.

## 2. Files

```
src/BossModForge.Core/
  Inference/Confidence.cs            Confidence, ConfidenceBand, Evidence, ReviewItem, ReviewSeverity
  Inference/PullSegmenter.cs         + SegmenterOptions, Pull, PullOutcome
  Inference/ParticipantClassifier.cs + Participant, ParticipantRole
  Inference/AbilityCataloger.cs      + CatalogOptions, AbilityCatalog, Ability, AbilityInstance, AbilityHit, StatusEntry, TetherEntry, IconEntry
  Model/Identifiers.cs               CSharpIdentifier.Sanitise / Dedupe (namespace BossModForge.Core.Model; only the EM builder uses it)
  Model/EncounterModel.cs            EM records (schemaVersion 1)
  Model/EncounterModelJson.cs        Serialize/Deserialize (deterministic)
  Model/EncounterModelBuilder.cs     Recording + stage outputs → EM
  Emit/Flavor.cs                     Flavor enum, FlavorProfile (loaded from flavors/*.json)
  Emit/IEmitterProfile.cs
  Emit/VbmProfile.cs
  Emit/BmrProfile.cs
  Emit/ModuleEmitter.cs              + EmitOptions, EmitResult, EmittedFile
  Emit/ForgeMdWriter.cs
src/BossModForge.Cli/Commands/InferCommand.cs
src/BossModForge.Cli/Commands/EmitCommand.cs
flavors/vbm.json
flavors/bmr.json
schema/em.v1.schema.json
tests/BossModForge.Tests/Inference/PullSegmenterTests.cs
tests/BossModForge.Tests/Inference/ParticipantClassifierTests.cs
tests/BossModForge.Tests/Inference/AbilityCatalogerTests.cs
tests/BossModForge.Tests/Model/EncounterModelJsonTests.cs
tests/BossModForge.Tests/Model/EncounterModelBuilderTests.cs
tests/BossModForge.Tests/Emit/ModuleEmitterVbmTests.cs
tests/BossModForge.Tests/Cli/InferEmitCommandTests.cs
tests/BossModForge.Tests/Golden/SyntheticDungeon.em.json
tests/BossModForge.Tests/Golden/SyntheticDungeon.vbm.cs
```

Ownership: **Builder C** — `Inference/**` and `tests/…/Inference/**`, plus one addition to `tests/…/Fixtures/SyntheticDungeon.cs` (§4). **Builder D** — `Model/**`, `Emit/**`, `flavors/**`, `schema/**`, `src/BossModForge.Cli/**` (adds two `root.Subcommands.Add(...)` lines to `Program.cs`), `tests/…/Model/**`, `tests/…/Emit/**`, `tests/…/Cli/InferEmitCommandTests.cs`, `Golden/**`. `EncounterModelBuilder` is D's but consumes C's records, so **C's first commit is a contracts commit**: `Inference/Confidence.cs` verbatim from §3 and the `Pull`/`Participant`/`Ability*`/`*Entry`/`*Options` records with `PullSegmenter.Segment` / `ParticipantClassifier.Classify` / `AbilityCataloger.Catalog` throwing `NotImplementedException`. D builds against that commit; neither builder edits the other's files. Neither builder touches `Replay/**`, `IGameData.cs`, or `Directory.Build.props`.

## 3. Public APIs

Namespace `BossModForge.Core.Inference`:

```csharp
public enum ConfidenceBand { High, Medium, Low }           // High >= 0.85, Medium 0.6–0.85, Low < 0.6 (CONTEXT.md)
public sealed record Confidence(double Score, ConfidenceBand Band, ImmutableArray<string> Reasons)
{
    public static Confidence Of(double score, params string[] reasons);   // clamps, derives Band
    public static Confidence LowBecause(params string[] reasons);          // 0.3
}
public sealed record Evidence(string Kind, string Summary, JsonElement? Data = null);
public enum ReviewSeverity { High, Medium, Low }
public sealed record ReviewItem(string Id, ReviewSeverity Severity, string Subject, string Question, ImmutableArray<string> Suggested);   // Id "R1", "R2", … assigned by EncounterModelBuilder

public enum PullOutcome { Kill, Wipe, Abandoned, RecordingEnded }
public sealed record Pull(int Index, DateTime Start, DateTime End, PullOutcome Outcome, ushort Zone, ushort CfcId,
                          ulong PrimaryInstanceId, uint PrimaryOid, ImmutableArray<ulong> PlayerInstanceIds,
                          Confidence Confidence, ImmutableArray<Evidence> Evidence);
public sealed record SegmenterOptions(TimeSpan MinPullDuration, TimeSpan CombatGapToSplit)
{
    public static readonly SegmenterOptions Default = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(8));   // 8 s = spec §5.1 InCombat falling-edge hold
}
public static class PullSegmenter
{
    public static ImmutableArray<Pull> Segment(Recording recording, SegmenterOptions? options = null);
}

public enum ParticipantRole { Boss, Helper, Add, Caster, EventObject, Player, Pet, Unknown }
public sealed record Participant(uint Oid, string Name, uint NameId, ActorType Type, ParticipantRole Role, float HitboxRadius,
                                 int InstanceCount, int PullCount, bool SpawnsDuringFight, Confidence Confidence, ImmutableArray<Evidence> Evidence);
public static class ParticipantClassifier
{
    public static ImmutableArray<Participant> Classify(Recording recording, ImmutableArray<Pull> pulls, IGameData gameData);
}

public sealed record AbilityHit(ulong TargetInstanceId, Vector3 TargetPos, ActionEffects Effects);
public sealed record AbilityInstance(int PullIndex, ulong SourceInstanceId, DateTime? CastStart, DateTime? CastEnd, DateTime EffectTime,
                                     Vector3 Origin, Angle Rotation, ulong MainTargetId, Vector3 TargetPos, ImmutableArray<AbilityHit> Hits);
public sealed record Ability(uint SourceOid, ActionId Action, string Name, bool HasCastBar, float CastTime, bool Interruptible,
                             int InstanceCount, ImmutableArray<AbilityInstance> Instances, ActionRow? Sheet, Confidence Confidence, ImmutableArray<Evidence> Evidence);
public sealed record StatusEntry(uint StatusId, string Name, ImmutableArray<uint> SourceOids, int Applications, ImmutableArray<ushort> ExtraValues);
public sealed record TetherEntry(uint TetherId, ImmutableArray<(uint SourceOid, uint TargetOid)> Pairs, int Count);
public sealed record IconEntry(uint IconId, ImmutableArray<uint> TargetOids, int Count);
public sealed record AbilityCatalog(ImmutableArray<Ability> Abilities, ImmutableArray<StatusEntry> Statuses, ImmutableArray<TetherEntry> Tethers, ImmutableArray<IconEntry> Icons);
public sealed record CatalogOptions(TimeSpan MatchWindowBefore, TimeSpan MatchWindowAfter, bool IncludePlayerActions)
{
    public static readonly CatalogOptions Default = new(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(2.5), false);   // spec §5.3 window
}
public static class AbilityCataloger
{
    public static AbilityCatalog Catalog(Recording recording, ImmutableArray<Pull> pulls, ImmutableArray<Participant> participants, IGameData gameData, CatalogOptions? options = null);
}

```

Types referenced above come from M0 as-is: `ActorType`, `ActionId` (`.Type`, `.Id`), `Angle`, `ActionEffects`, `Vector3` (`System.Numerics`) from `BossModForge.Core.Replay`/primitives; `ActionRow` from `BossModForge.Core.GameData`. `ActorTimeline` exposes `InstanceId, Incarnation, Oid, Name, NameId, Type, Class, Level, HitboxRadius, OwnerId, Created, Destroyed, PosRotSamples, Targetable, Dead, InCombat, HpMp, PosRotAt(t), IsTargetableAt(t), IsDeadAt(t), IsAliveAt(t)` — nothing about statuses, tethers, casts or targets; read those from `Recording.Ops` (`ActorCastStartOp`, `ActorCastEventOp` (`Targets: ImmutableArray<CastTarget(InstanceId, Effects)>`, `MainTargetId`, `TargetPos`, `Rotation`), `ActorStatusOp`, `ActorTetherOp`, `ActorIconOp`, `ZoneChangeOp`). Do not call `Recording.ActorAt` in loops — it scans all actors; build a `Dictionary<ulong, List<ActorTimeline>>` once. `AbilityHit.TargetPos` and `AbilityInstance.Origin` are `PosRotAt(EffectTime).Pos` of the target/source timeline (`Vector3.Zero` when no timeline).

Segmentation for C (spec §5.1, adapted to what M0 exposes):

1. **Start** = earliest, after the previous pull's end, of: `COM+` from an `ActorType.Enemy` actor; `CST+`/`CST!` from an `Enemy` actor whose target is a `Player`. Candidates of the pull = every `Enemy` actor that is `COM+` at any time between start and end.
2. **End** = earliest of: `ZONE` op → `Abandoned`; every `Player` alive in the pull is `DIE+` simultaneously → `Wipe`; every candidate is dead (`DIE+`) or out of combat and none of them returns to `COM+` within `CombatGapToSplit` after its `COM-` (look-ahead; `TimeSpan.Zero` means a `COM-` ends immediately) → `Kill` if the primary is dead at that instant, else `Wipe`; last op → `RecordingEnded`. Evaluate in that order when several fire on the same timestamp (a `DIE+` and `COM-` on the same op timestamp is a Kill).
3. **Primary** = candidate with the most `CST!` events in `[Start, End]`; tie → largest `HitboxRadius`; tie → lowest `InstanceId`. `PrimaryOid` is its `Oid`. `Zone`/`CfcId` = last `ZoneChangeOp` at or before `Start` (0/0 if none, with `Evidence("NoZone")`).
4. Pulls shorter than `MinPullDuration` are dropped; the next surviving pull carries `Evidence("DroppedPull", …)`. `PlayerInstanceIds` = `Player`-type actors alive at `Start`. `Confidence`: `Of(0.9)` when end was `DIE+`/`ZONE`, `Of(0.7, "combat-gap end")` for the `COM-` rule, `LowBecause("recording ended")` for `RecordingEnded`.

Classification order for C: `Type == Player` → Player; `Type is Pet or Chocobo` or `OwnerId` is a `Player` instance → Pet; `Type == Helper || Oid == 0x233C` → Helper (`UPSTREAM.md §7`); `Type == EventObj` → EventObject; `Oid == pull.PrimaryOid` for any pull → Boss; `Enemy` that casts but is never targetable → Caster; targetable `Enemy` → Add; else Unknown with Low confidence. One `Participant` per `Oid` (players in the synthetic fixture all have `Oid == 0` and collapse into one row — intended). `InstanceCount` = distinct `(InstanceId, Incarnation)`, `PullCount` = pulls in which any instance was alive, `SpawnsDuringFight` = first `Created` after any pull `Start`. `Name` = `gameData.BNpcName(NameId) ?? timeline.Name`.

Cataloger for C: key = `(SourceOid, Action)`; a `CST+` matches the first later `CST!` from the same `InstanceId` with the same `Action` whose timestamp is within `[castEnd − MatchWindowBefore, castEnd + MatchWindowAfter]`, `castEnd = CST+.Timestamp + (TotalTime − ElapsedTime)`. `InstanceCount` counts `CST!` only; a `CST+` with no match adds `Evidence("UnmatchedCast", …)` and still sets `HasCastBar = true`. `CastTime` = median `TotalTime` over matched casts (0 when none). Player-sourced actions are skipped unless `IncludePlayerActions`. Statuses/tethers/icons are counted per pull window only; `StatusEntry.SourceOids` resolves `ActorStatus.SourceId` through the timeline index (0 → omitted). `IconEntry.TargetOids` = `Oid` of the actor the `ICON` op is on (`InstanceId`), not `TargetId`.

Namespace `BossModForge.Core.Model` (D; JSON property names camelCase, sorted alphabetically on write, `schemaVersion` first):

```csharp
public static class CSharpIdentifier
{
    public static string Sanitise(string name, string fallbackPrefix);                 // PascalCase, strip non-identifier chars, prefix digits, keyword-safe; empty → fallbackPrefix
    public static ImmutableArray<string> Dedupe(IEnumerable<string> names);            // stable: append 1,2,3 in input order
}

public sealed record EncounterModel(int SchemaVersion, EncounterInfo Encounter, ImmutableArray<SourceInfo> Sources, ImmutableArray<PullSummary> Pulls,
                                    ImmutableArray<ParticipantModel> Participants, ImmutableArray<AbilityModel> Abilities, ImmutableArray<StatusModel> Statuses,
                                    ImmutableArray<TetherModel> Tethers, ImmutableArray<IconModel> Icons, EnumNames Enums, ArenaModel Arena,
                                    ImmutableArray<ReviewItem> Review, Overrides Overrides);
public sealed record EncounterInfo(ushort Zone, ushort CfcId, string? DutyName, uint PrimaryOid, string BossName, uint BossNameId, string ModuleName, string Namespace);
public sealed record SourceInfo(string Path, string Sha256, int Version, string GameVersion, DateTime Start, DateTime End);
public sealed record PullSummary(int Index, string Source, DateTime Start, DateTime End, string Outcome, double DurationSeconds, Confidence Confidence);
public sealed record ParticipantModel(uint Oid, string EnumName, string Name, uint NameId, string Type, string Role, float HitboxRadius, int InstanceCount, bool SpawnsDuringFight, Confidence Confidence, ImmutableArray<Evidence> Evidence);
public sealed record AbilityModel(uint SourceOid, uint ActionId, string ActionType, string EnumName, string Name, bool HasCastBar, float CastTime, bool Interruptible, int InstanceCount,
                                  string Class /* "Unclassified" in M1 */, Confidence Confidence, ImmutableArray<Evidence> Evidence);
public sealed record StatusModel(uint StatusId, string EnumName, string Name, int Applications);
public sealed record TetherModel(uint TetherId, string EnumName, int Count);
public sealed record IconModel(uint IconId, string EnumName, int Count);
public sealed record EnumNames(ImmutableSortedDictionary<uint,string> Oid, ImmutableSortedDictionary<uint,string> Aid, ImmutableSortedDictionary<uint,string> Sid,
                               ImmutableSortedDictionary<uint,string> TetherId, ImmutableSortedDictionary<uint,string> IconId);
public sealed record ArenaModel(string Kind /* "Circle" placeholder */, float CenterX, float CenterZ, float Radius, Confidence Confidence);   // M1: Circle r=20 at mean boss position, Low
public sealed record Overrides(ImmutableSortedDictionary<string, JsonElement> Values);   // empty in M1; key = JSON pointer into EM

public static class EncounterModelJson
{
    public static string Serialize(EncounterModel em);                 // indented (2 spaces), "\n", invariant, DateTime "O" as-is (no kind conversion), sorted keys, nulls omitted, trailing newline
    public static EncounterModel Deserialize(string json);             // DateTimeStyles.RoundtripKind
    public static ImmutableArray<string> Validate(string json);        // against schema/em.v1.schema.json via the JsonSchema.Net package (Core PackageReference, latest stable); empty == valid
}
public static class EncounterModelBuilder
{
    public static EncounterModel Build(Recording recording, ImmutableArray<Pull> pulls, ImmutableArray<Participant> participants, AbilityCatalog catalog, IGameData gameData, BuildOptions? options = null);
}
public sealed record BuildOptions(string? ModuleName = null, string? Namespace = null);
```

Builder rules (D): implement `Serialize` by writing to a `JsonNode`, sorting object keys recursively, then moving `schemaVersion` to the front; do not rely on record `==` for EM (records contain `ImmutableArray`, whose equality is by reference) — tests compare serialized strings. `SourceInfo.Path` = `recording.Meta.Path` verbatim; `Sha256` = lower-hex SHA-256 of the file when `File.Exists(Meta.Path)`, else `""` (goldens parse from a stream with `displayPath = "SyntheticDungeon.log"`). `EncounterInfo.PrimaryOid` = most frequent `Pull.PrimaryOid` (ties → lowest); if pulls disagree add `ReviewItem(Medium, "encounter", "Multiple primary OIDs …")`. `ModuleName` default = `CSharpIdentifier.Sanitise(BossName, "Encounter")`; `Namespace` default = `BossMod.Forge.<ModuleName>`. `Participants` in the EM exclude `Player`/`Pet` roles; `EnumNames.Oid` = participant OIDs with `Boss` → `"Boss"`, `Helper` → `"Helper"`, others sanitised+deduped by `Name`; `EnumNames.Aid` keyed by `Action.Id`, `ActionType.Spell` only (other types → `ReviewItem(Low, …)`, not emitted); `Sid`/`TetherId`/`IconId` = `Status{id}` / `Tether{id}` / `Icon{id}` when no name. `ArenaModel` = `Circle`, centre = mean of the primary's `PosRotSamples` across pulls, radius 20, `Confidence.LowBecause("placeholder")`. `ReviewItem.Id` = `R1…` in the order added; `ModuleEmitter` uses `E1…` for its own items so the two never collide.

Namespace `BossModForge.Core.Emit` (D):

```csharp
public enum Flavor { Vbm, Bmr }
public sealed record FlavorProfile(Flavor Flavor, string Upstream, string Commit, bool Verified, string RootNamespace, string HelperComment,
                                   ImmutableSortedDictionary<string, string> Templates);   // loaded from flavors/{vbm,bmr}.json
public sealed record ModuleInfoArgs(string Maturity, string GroupType, uint GroupId, uint NameId, string? Contributors, uint? PrimaryActorOid);
public interface IEmitterProfile
{
    Flavor Flavor { get; }
    FlavorProfile Profile { get; }
    string ModuleInfoAttribute(ModuleInfoArgs args);
    string ModuleDeclaration(string moduleName, string center, string bounds);   // e.g. "public class X(WorldState ws, Actor primary) : BossModule(ws, primary, new(80, 53), new ArenaBoundsCircle(20));"
    string ArenaBounds(ArenaModel arena);
    string StatesClass(string moduleName, IEnumerable<string> componentTypeNames);   // TrivialPhase().ActivateOnEnter<...>() chain; empty chain in M1
    string EnumDeclaration(string enumName, string underlying, IEnumerable<(string Name, uint Value, string? Comment)> members);
    string ComponentCtor(string kind, IReadOnlyDictionary<string, string> args);    // M1: throws NotSupportedException — populated in M2/M3
    string IdCast(string enumName, string member);                                  // vbm: "(uint)AID.X" — VERIFY per use site in M2
}
public sealed record EmitOptions(Flavor Flavor, string? Namespace = null, string Maturity = "WIP", string? Contributors = null);
public sealed record EmittedFile(string RelativePath, string Content);
public sealed record EmitResult(ImmutableArray<EmittedFile> Files, string ForgeMd, ImmutableArray<ReviewItem> Review);
public static class ModuleEmitter
{
    public static EmitResult Emit(EncounterModel em, IEmitterProfile profile, EmitOptions options);
    public static IEmitterProfile ProfileFor(Flavor flavor, string flavorsDirectory);
}
```

Vbm output for M1 (confirmed shapes, `UPSTREAM.md §8`): file `<ModuleName>.cs` with `namespace <Namespace>;`, `public enum OID : uint { Boss = 0x…, Helper = 0x233C, … }` with `// R{radius:f3}, x{count}` comments like upstream, `public enum AID : uint`, `public enum SID : uint`, `public enum IconID : uint`, `public enum TetherID : uint` (omit empty enums except OID/AID), `class <ModuleName>States : StateMachineBuilder { public <ModuleName>States(BossModule module) : base(module) { TrivialPhase(); } }`, then `[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.CFC, GroupID = <cfc>, NameID = <bossNameId>)]` and `public class <ModuleName>(WorldState ws, Actor primary) : BossModule(ws, primary, new(<cx:f0>, <cz:f0>), new ArenaBoundsCircle(20));`. Default namespace `BossMod.Forge.<ModuleName>`; because vbm infers `Expansion`/`Category` from namespace levels 2–3, the emitter must also set them explicitly in the attribute **once the enum member lists are verified** (see §5) — until then, emit a `ReviewItem` "Set Expansion/Category" and a `// TODO(forge:Rn)` comment.

`flavors/vbm.json`:

```json
{ "flavor": "vbm", "upstream": "awgil/ffxiv_bossmod", "commit": "1391bc42", "verified": true,
  "rootNamespace": "BossMod", "helperOid": "0x233C",
  "templates": {
    "moduleInfo": "[ModuleInfo(BossModuleInfo.Maturity.{maturity}, GroupType = BossModuleInfo.GroupType.{groupType}, GroupID = {groupId}, NameID = {nameId})]",
    "moduleDecl": "public class {name}(WorldState ws, Actor primary) : BossModule(ws, primary, new({cx}, {cz}), {bounds});",
    "boundsCircle": "new ArenaBoundsCircle({radius})",
    "statesCtor": "public {name}States(BossModule module) : base(module)",
    "trivialPhase": "TrivialPhase()"
  } }
```

`flavors/bmr.json`: same keys, `"commit": ""`, `"verified": false`, templates copied from vbm. `BmrProfile` must add `ReviewItem(High, "flavor", "BMR API unverified — output uses vbm shapes")` to every emit.

CLI (mirror `IngestCommand`: `static Command Create()` + testable `static int Run(..., TextWriter stdout, TextWriter stderr)`): `forge infer <log> [-o em.json] [--module-name X] [--namespace Y]` (always `NullGameData` in M1; names fall back to `ACT+`/`NAME` names and `Ability{id}`; `-o` default `<log>.em.json`); `forge emit <em.json> --flavor vbm|bmr [-o dir] [--flavors <dir>]` writes `<Module>.cs` and `FORGE.md` (`--flavors` default: `flavors/` next to the executable, falling back to the current directory). Exit codes per spec §10: `0` ok, `2` any `High` review item (output is still written), `3` `Validate` failed on the input EM, `4` parse error / unreadable input. `ReplayParser.Parse` runs with `KeepRawOps = false`. Register both in `Program.cs`.

## 4. Tests / acceptance

- `SyntheticDungeon` (M0 fixture) has three ~7 s pulls separated by 1 s, so it does not fit `SegmenterOptions.Default`. Builder C adds `public static readonly SegmenterOptions PullOptions = new(TimeSpan.FromSeconds(5), TimeSpan.Zero);` to `SyntheticDungeon.cs` (the only edit to that file); every M1 test and both goldens use it. Expected: pulls `Wipe, Wipe, Kill`, all `PrimaryOid == 0x4321`, `Zone 1234 / CfcId 826`; roles Boss (0x4321), Helper (0x233C), Player (Oid 0, `InstanceCount 4`); one ability `(0x4321, Spell 40001)` with `HasCastBar = true`, `CastTime 5`, `InstanceCount 3`, 4 hits per instance; status 1234 ×3 from source 0x4321; tether 7 ×3 (0x233C → 0); icon 50 ×3 on Oid 0. Unmatched cast start (no `CST!`) yields `HasCastBar = true`, `InstanceCount` counts effects only, with `Evidence("UnmatchedCast")` — build that case with a fresh `RecordingBuilder`.
- `EncounterModelJson.Serialize(Deserialize(x)) == x` byte-exact; `Validate` passes on the golden; key order, `schemaVersion` first and `\n` endings asserted.
- Golden: `SyntheticDungeon.em.json` and `SyntheticDungeon.vbm.cs` byte-exact (update via `FORGE_UPDATE_GOLDEN=1`), produced by `ReplayParser.Parse(builder.Open(ReplayEncoding.Text), "SyntheticDungeon.log")` → `PullSegmenter.Segment(rec, SyntheticDungeon.PullOptions)` → `Classify` → `Catalog` → `Build(…, NullGameData.Instance)` → `ModuleEmitter.Emit(em, ProfileFor(Vbm, "flavors"), new EmitOptions(Flavor.Vbm))`. Until C's stages land, D's golden test may be `[Fact(Skip = "waiting on Inference")]` but `EncounterModelJsonTests`/`ModuleEmitterVbmTests` must run against a hand-built `EncounterModel`.
- CLI: `InferEmitCommandTests` runs `infer` on a temp text recording then `emit --flavor vbm` into a temp dir and asserts exit `0`, both files exist, and `emit --flavor bmr` exits `2`.
- Manual, documented in `docs/UPSTREAM.md`: drop the emitted file into a checkout of `awgil/ffxiv_bossmod@1391bc42` under `BossMod/Modules/Forge/` and `dotnet build BossMod/BossMod.csproj` succeeds (automated in M4).

## 5. VERIFY still open for M1 (resolve before coding the affected line, record in `UPSTREAM.md`)

| Item | Blocks |
|---|---|
| `BossModuleInfo.Expansion` and `Category` full member lists (`BossModuleInfo.cs:16-55`) | explicit `Expansion=`/`Category=` in `[ModuleInfo]` |
| BMR: pin commit; `ModuleInfo` shape; `BossModule` ctor (`SharedBounds`, `ArenaBoundsComplex`); whether `Helper = 0x233C` convention holds; module-pack loading | everything in `BmrProfile` |
| Component ctor signatures for both flavors (`SimpleAOEs`, `RaidwideCast`, …) | `IEmitterProfile.ComponentCtor` (M2/M3 — keep `NotSupportedException` in M1) |
| `IdCast` idiom per call site (`(uint)AID.X` vs `ActionID.MakeSpell(AID.X)`) | M2 |
| Lumina sheet columns for `ActionRow` (`CastType`, `EffectRange`, `XAxisModifier`, `Cast100ms`, `Omen`) in the Lumina version chosen | `LuminaGameData` (M1b, out of this brief) |
| Whether `DIE+` on primary is reliable for Kill vs. `ACT-`/`ATG-` first (check a real log via `FORGE_REAL_LOGS`) | `PullSegmenter` outcome rules |
