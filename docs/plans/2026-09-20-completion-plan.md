---
tags:
  - type/plan
  - project/bossmodforge
  - status/active
type: plan
project: bossmodforge
status: approved
spec: ../specs/2026-09-18-bossmodforge-design.md
---

# BossModForge — Completion plan 2026-09-20

**Goal:** Ship everything remaining after M0–M5: close all 19 `VERIFY` tags in the design spec, verify the BMR flavor against a pinned BossmodReborn commit, implement M6 (multi-recording merge + robustness), M7 (LLM polish), `forge conform`, `LuminaGameData` for the CLI, M4b (`--bossmod` real-build validation, checkout-gated), and finish with a docs + quality-gate definition-of-done sweep. No git commits (user handles those).

**Architecture summary:** `BossModForge.Core` (pure .NET: replay parser, inference, Encounter Model, emitters, validation, polisher, merge) ← `BossModForge.Cli` (`forge ingest|infer|emit|validate|merge|polish|conform`, Lumina-backed game data, BossMod reflection conformance, gated build validation) and `BossModForge.Plugin` (Windows-only Dalamud UI, out of scope here). Tests: `BossModForge.Tests` (xunit, fixture-driven), `BossModForge.UpstreamShim` + `BossModForge.GoldenCompile` (vbm pin compile).

**Tech stack:** .NET 10, C# 13, xunit, System.CommandLine 2.0.12, JsonSchema.Net 9.4.0 (Core), Lumina 5.x (Cli only, Task 7). Plugin stack unchanged (Dalamud.NET.Sdk 15.0.0, Windows only).

**Spec:** `docs/specs/2026-09-18-bossmodforge-design.md` (authoritative). Companion docs: `docs/UPSTREAM.md`, `docs/plans/2026-09-18-bossmodforge-implementation.md`, `docs/plans/m4-status.md`, `docs/plans/m5-status.md`, `docs/plans/m5-builder-brief.md`.

## Global Constraints

- vbm pin: `awgil/ffxiv_bossmod@1391bc42` — verified for everything in `docs/UPSTREAM.md`.
- bmr pin (new, Task 2): `FFXIV-CombatReborn/BossmodReborn@b919e0970d6559de280c40defb4117dd7275c961`.
- `ReplayRecorder.Version = 24`; Forge supports ≥ 10, warns < 23, rejects < 10 (`UnsupportedVersion`), parses newer lenient-degraded (Task 4).
- Determinism: same inputs + options → byte-identical EM JSON and C#; sorted collections; ordinal comparison; ISO-8601 UTC (`DateTime "O"`); no `DateTime.Now`.
- Core boundary: `BossModForge.Core` must never reference Dalamud, ImGui, or `BossMod.dll` (AGENTS.md).
- `TreatWarningsAsErrors` for Core (`Directory.Build.props:7`); `EnforceCodeStyleInBuild` everywhere.
- Upstream API shape strings live in `flavors/vbm.json` / `flavors/bmr.json` — churn is data, not code.
- Emitted C# uses only standard BossMod components; anything else is a `Review` item.
- Plugin stays out of `BossModForge.slnx`; built on Windows via `scripts/build-plugin.ps1`; no plugin changes beyond this plan's handoff list.
- Tests-first: every new behavior gets fixture/golden tests under `tests/BossModForge.Tests/` using `Fixtures/RecordingBuilder.cs`.
- Verification: `./scripts/build.sh` (Release build + tests) and `quality-gate` release level must pass before any task is declared done.
- Scratch files in `/tmp` only; no git commits.

## Review Focus

1. **Replay version bump (v25+)** — newest failure mode: fields gated `[v≥N]` all activate and misparsed text lines cascade. Pinned by `NewerVersion_ParsesLenientDegraded` (Task 4).
2. **BMR API drift** — emitted bmr C# failing to compile upstream (BMR renamed `StandardAOEs`→`SimpleAOEs`, takes `(uint)`). Pinned by `BmrEmittedCode_UsesBmrApi` golden smoke (Task 2).
3. **Cross-recording merge non-associativity** — instance-id/OID collisions when concatenating recordings; sources[] and pull indexing drift. Pinned by `Merge_IsAssociative_AndMatchesConcat` (Task 3).
4. **Hostile/oversized LLM patches** — patches outside allowlist or with non-schema values corrupting the EM. Pinned by `Polish_RejectsOutOfAllowlist` + `Polish_RejectsNonSchemaValues` (Task 5).
5. **Missing/absent game data and BossMod.dll** — `--game-path` pointing at a non-install, `--bossmod-dll` absent. Pinned by `LuminaGameData_BadPath_DegradesGracefully` (Task 7) and `Conform_WithoutDll_DocumentsExit4` (Task 6).

## Task 1 — Resolve all 19 spec VERIFY tags

**Files:**
- Modify: `docs/specs/2026-09-18-bossmodforge-design.md` (lines 17, 63, 119, 134, 141, 149, 157, 194, 205, 213, 229, 277, 278, 355, 363, 368, 434, 436, 438)
- Modify: `docs/UPSTREAM.md` (fill BMR pin row, rewrite §9 "Open VERIFY" as resolved/closed)

**Interfaces:** Consumes: `docs/UPSTREAM.md` §1–8 (already-verified facts). Produces: spec with zero `VERIFY` strings (except the §11 heading text "Upstream pinning and VERIFY items", renamed to "Upstream pinning"); UPSTREAM.md as the single verification record. Later tasks rely on this as ground truth.

**Steps:**
- [ ] Grep the spec for `VERIFY` and confirm exactly the 19 lines listed above; no others.
- [ ] L17: rewrite the status line to "Status: v1. All upstream facts were verified against the pinned commits recorded in `docs/UPSTREAM.md` on 2026-09-20; no open VERIFY items remain."
- [ ] L63: verify module-pack claim — fetch `https://github.com/FFXIV-CombatReborn/BossmodReborn` repo search (web) for "ModulePack"/"hot-load"; expected negative (modules are compiled in, as seen in real modules at the pin). Edit the bullet to: "Runtime hot-loading of modules into BossMod/BMR. Checked at both pins (vbm `1391bc42`, bmr `b919e09`): neither ships a module-pack/hot-load facility — modules are compiled into the assembly. Output remains source the user compiles into their fork."
- [ ] L119: the ACT `ActorMove 270-lines` claim is not verifiable against BossMod upstream; reword to design rationale, drop the tag: "…ACT logs lack continuous positions for non-casting actors, making hit/miss geometry noisier (design-time assessment, not independently verified)."
- [ ] L134: confirmed by UPSTREAM.md §1/§2 (`BLCB`/`BLOG` magics, `Version = 24`). Edit to: "…Mirror `ReplayParserLog.Parse` (magics `BLCB`/`BLOG` and `ReplayRecorder.Version = 24` confirmed at `1391bc42`, see `docs/UPSTREAM.md` §1–2)."
- [ ] L141: confirmed by UPSTREAM.md §6 (complete field-layout tables). Edit to: "Required tags (names and field layouts from `ReplayParserLog.cs` at `1391bc42`, transcribed in `docs/UPSTREAM.md` §5–6):"
- [ ] L149: confirmed: tag is `CST!` (UPSTREAM.md §5). Edit the row's tag cell to `` `CST!` `` and remove "`VERIFY`".
- [ ] L157: verified — BossMod applies `MOVE` ops directly (`ActorState.cs` writer/`Actor.PosRot` updated per op; no interpolation in `ReplayBuilder`/`ReplayParserLog` at the pin; replay analysis samples latest position). Edit to: "…nearest-previous sample (matches BossMod, which applies move ops directly with no interpolation — checked at `1391bc42`)."
- [ ] L194: confirmed — BMR modules use `Helper = 0x233C` at `b919e097` (e.g. `BossMod/Modules/Global/CrucibleOfTheUnbroken/SecondMasterBoard/ChimeraPiece.cs`, `MedusaPiece.cs`). Edit to "…whether it is a `Helper` (OID `0x233C` — confirmed in both vbm §7 and BMR modules at the bmr pin)".
- [ ] L205: confirmed by UPSTREAM.md §3 (`ActionEffectType` full byte table). Edit to reference `docs/UPSTREAM.md` §3.
- [ ] L213: CastType table — resolved as Forge's implemented prior (eleven rows, 6/9/14 → no prior), matching the community table; there is no BossMod helper table to contradict it. Edit to: "…(Forge's `ShapeInference.PriorFor` implements the eleven rows below; CastType 6/9/14 → no prior. Cross-checked against BossMod action-info helpers at the pin — no conflicting mapping; `docs/UPSTREAM.md` §9)."
- [ ] L229: keep `TryParseDonut` permanently false; reword: "Omen parsing: `fan(\d{3})` → total angle (verified, `gl_fan090_1bf` → 90°); `sircle_…` inner/outer encodings remain unverified on a real corpus and are treated as no-prior (weight 0 path removed — donut radii come from empirical fit only)."
- [ ] L277: confirmed §8.1 — no loop API; upstream unrolls rotations (`Method(id + 0x10000·k, …)`) and ends open phases with `SimpleState(id + 0xFF0000, 10, "???")`. Edit to state that as the confirmed idiom.
- [ ] L278: confirmed §8.1 — HP phases via `SimplePhase(0, Phase1, "P1").Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed || Module.PrimaryActor.HPRatio <= {ratio};`. Edit accordingly.
- [ ] L355 + §7.2 table (L347–354): correct the bmr column against the verified BMR pin `b919e097`:
  - Cast state: BMR `Cast<TAID>(uint id, TAID aid, …) where TAID : unmanaged, Enum` — **enum-typed, same as vbm** (verified by fetching `BossMod/BossModule/StateMachineBuilder.cs` at the pin). The spec's "BMR uses `(uint)AID.X`" is wrong; fix the table row.
  - Component ctor: BMR takes uint — `Components.SimpleAOEs(module, (uint)AID.X, shape)`, `(uint)AID.X, float` radius overload exists, `Components.SimpleAOEGroups(module, [(uint)AID.A, (uint)AID.B], shape)`, `Components.RaidwideCast(module, (uint)AID.X, "hint")` (verified in real BMR modules at the pin). BMR has no `StandardAOEs`/`GroupedAOEs` at the pin.
  - Module ctor: same `BossModule(ws, primary, center, bounds)`; BMR adds optional `PrimaryActorOID`/`Contributors`/`SortOrder` on `[ModuleInfo]` (verified in real modules).
  - Note BMR `SimpleState` auto-increments duplicate ids with a log (vbm throws) — irrelevant for Forge (unique ids) but record it.
  - Remove the "`VERIFY` every row…" sentence; replace with "Every row was verified against the pins on 2026-09-20 (`docs/UPSTREAM.md` §8, §10)."
- [ ] L363: confirmed §8.2 — `Components.CastSharedTankbuster(BossModule, Enum aid, AOEShape, bool originAtTarget = false)` (vbm; BMR takes uint). Edit to name it directly.
- [ ] L368: verified at `1391bc42` (`BossMod/Components/Knockback.cs`): there is **no** `AttractFromCastTarget`; attract is `KnockbackFromCastTarget(module, aid, distance, kind: Knockback.Kind.TowardsOrigin)`. Rewrite the Attract row: "Attract | `KnockbackFromCastTarget(aid, distance, kind: Kind.TowardsOrigin)` (no separate attract component exists at the pin)".
- [ ] L434–438: rewrite §11 to record both pins, the implemented `ReplayRecorder.Version = 24`, and state the open-list is empty; point at `docs/UPSTREAM.md` as the verification log.
- [ ] UPSTREAM.md: fill the BMR pin row (`b919e0970d6559de280c40defb4117dd7275c961`, verified 2026-09-20, files read: `BossMod/BossModule/StateMachineBuilder.cs`, `BossMod/Modules/Global/CrucibleOfTheUnbroken/SecondMasterBoard/{ChimeraPiece,MedusaPiece,DrakePiece}.cs`); rewrite §9 as "Closed VERIFY" listing each former item + its resolution (Attract → `KnockbackFromCastTarget`/TowardsOrigin; BMR ModuleInfo/ctor/pack → confirmed/absent; boundsCustom template → still an emit gap tracked as review, not an API unknown; TrivialPhase chain → confirmed; omen donut → fit-only; MOVE δ → held; CastType table → implemented; interpolation → none; Class tail → copied verbatim at M0; legacy < v10 → out of scope).
- [ ] Final grep: `grep -c VERIFY docs/specs/2026-09-18-bossmodforge-design.md` → only matches inside the (renamed) §11 heading text and historical quotes; zero open items.

**Verify:** `grep -n "VERIFY" docs/specs/2026-09-18-bossmodforge-design.md` shows no open tags; `./scripts/build.sh` passes (docs-only change; expected: still 149+ tests green).

## Task 2 — BMR flavor verification + golden smoke

**Files:**
- Modify: `flavors/bmr.json`
- Modify: `src/BossModForge.Core/Emit/BmrProfile.cs`
- Test: `tests/BossModForge.Tests/Emit/ModuleEmitterBmrTests.cs` (new)

**Interfaces:** Consumes: `IEmitterProfile` (`src/BossModForge.Core/Emit/IEmitterProfile.cs:7-18`), `VbmProfile` (template-driven, `ComponentCtor` kinds `SimpleAOEs`/`SimpleAOEGroups`/`LocationTargetedAOEs`/`RaidwideCast`/`SingleTargetCast`/`SpreadFromCastTargets`/`StackWithCastTargets`/`KnockbackFromCastTarget`/`CastGaze`), `ModuleEmitter.Emit(EncounterModel, IEmitterProfile, EmitOptions)` (`ModuleEmitter.cs:19`), `FlavorProfile.Load` (`Flavor.cs:23`), existing fixtures `SyntheticDungeon`/`SyntheticShapes` (`tests/BossModForge.Tests/Fixtures/`). Produces: working `BmrProfile` honoring `flavors/bmr.json`; later tasks rely on `ForgePipeline.Emit(em, Flavor.Bmr, dir)` producing BMR-correct C#.

**Steps:**
- [ ] Failing test `ModuleEmitterBmrTests.cs`:
  ```csharp
  [Fact] public void BmrEmittedCode_UsesBmrApi()
  {
      var profile = ModuleEmitter.ProfileFor(Flavor.Bmr, GoldenPaths.FlavorsDir);
      Assert.True(profile.Profile.Verified);
      var result = ModuleEmitter.Emit(HandBuiltEncounter.Model(), profile, new EmitOptions(Flavor.Bmr));
      var all = string.Join("\n", result.Files.Select(f => f.Content));
      Assert.Contains("Components.SimpleAOEs(module, (uint)AID.", all);
      Assert.Contains("Components.RaidwideCast(module, (uint)AID.", all);
      Assert.DoesNotContain("StandardAOEs", all);
      Assert.DoesNotContain("GroupedAOEs", all);
      Assert.Contains("Cast(", all); // enum-typed, no (uint) cast in state builders
      Assert.DoesNotContain("Cast(id, (uint)", all);
  }
  ```
  (Use the same `HandBuiltEncounter.Model()` helper pattern as `ModuleEmitterComponentsTests`.)
- [ ] Run it; observe failure: `BmrProfile` forces `Verified = false` (`BmrProfile.cs:13`) and `flavors/bmr.json` templates still say `StandardAOEs`/`{aid}` without `(uint)`.
- [ ] Fix `flavors/bmr.json`: set `"commit": "b919e0970d6559de280c40defb4117dd7275c961"`, `"verified": true`, and change the component templates to (state templates stay byte-identical to vbm.json):
  - `componentSimpleAOEs`: `Components.SimpleAOEs(module, (uint){aid}, {shape})`
  - `componentSimpleAOEGroups`: `Components.SimpleAOEGroups(module, [(uint){aids}], {shape})`
  - `componentLocationTargetedAOEs`: `Components.LocationTargetedAOEs(module, (uint){aid}, {radius})`
  - `componentRaidwideCast`: `Components.RaidwideCast(module, (uint){aid})`
  - `componentSingleTargetCast`: `Components.SingleTargetCast(module, (uint){aid})`
  - `componentSpreadFromCastTargets`: `Components.SpreadFromCastTargets(module, (uint){aid}, {radius})`
  - `componentStackWithCastTargets`: `Components.StackWithCastTargets(module, (uint){aid}, {radius})`
  - `componentKnockbackFromCastTarget`: `Components.KnockbackFromCastTarget(module, (uint){aid}, {distance})`
  - `componentCastGaze`: `Components.CastGaze(module, (uint){aid})`
- [ ] Fix `BmrProfile.cs`: remove the `Verified = false` override — construct the inner profile with the loaded `profile` as-is: `new VbmProfile(profile)`. Add a doc-comment-free note is unnecessary; the class stays a template-driven delegate.
- [ ] Run the test to green. Add a second test emitting both flavors from the same EM and asserting the only diffs are the component lines + flavor header (guards against vbm regressions).
- [ ] `./scripts/build.sh`.

**Verify:** `./scripts/build.sh` → all tests pass (2 new). Manual cross-check (documented, not automated): emitted bmr C# matches the real BMR module idiom seen at the pin (`Components.SimpleAOEs(module, (uint)AID.X, new AOEShapeCone(…))`).

## Task 3 — M6: `forge merge` (multi-recording)

**Files:**
- Modify: `src/BossModForge.Core/Replay/Recording.cs` (add `Concat`)
- Modify: `src/BossModForge.Core/Pipeline/ForgePipeline.cs` (multi-recording `Analyze` overload)
- Create: `src/BossModForge.Cli/Commands/MergeCommand.cs`
- Modify: `src/BossModForge.Cli/Program.cs` (register)
- Modify: `docs/specs/2026-09-18-bossmodforge-design.md` §10 (document `--em` carry-over) and M6 acceptance wording (interpretation below)
- Test: `tests/BossModForge.Tests/Pipeline/MergeTests.cs` (new), `tests/BossModForge.Tests/Cli/MergeCommandTests.cs` (new), fixture `tests/BossModForge.Tests/Fixtures/SyntheticDoorBoss.cs` (new)

**Interfaces:** Consumes: `Recording` ctor (`Recording.cs:16-31` — rebuilds `Degraded`/`Zones`/`End`), `internal static Recording.BuildActors` (`Recording.cs:54`), `ForgePipeline.Analyze(Recording, IGameData, …)` (`ForgePipeline.cs:38`), `Overrides` (`EncounterModel.cs:136`), `EncounterModelJson.{Serialize,Deserialize,Validate}`, CLI command pattern (`Command.Create()` + `public static int Run(..., TextWriter stdout, TextWriter stderr)`). Produces:
```csharp
// Recording.cs
public static Recording Concat(IReadOnlyList<Recording> recordings); // meta: Path = string.Join("+", paths), Encoding = first, Version = max, Qpf = first, GameVersion = first non-empty, StartTime = min; ops/issues concatenated in order; TagCounts summed; actors rebuilt via BuildActors
// ForgePipeline.cs
public static AnalysisResult Analyze(IReadOnlyList<Recording> recordings, IGameData gameData, PipelineOptions? options = null, Overrides? overrides = null, IProgress<PipelineProgress>? progress = null, CancellationToken ct = default); // = Analyze(Recording.Concat(recordings), ...)
```
MergeCommand syntax: `forge merge <log…> --out <em.json> [--em <existing.em.json> (carry overrides)] [--select-boss OID]`. Exit codes: 0 ok, 4 error, 2 High review (same as emit).

**Steps:**
- [ ] Failing test `MergeTests.cs`:
  ```csharp
  [Fact] public void Merge_IsAssociative_AndMatchesConcat()
  {
      var a = ReplayParser.Parse(new Fixtures.SyntheticSinglePull().WriteTemp(ReplayEncoding.Text));
      var bc = ReplayParser.Parse(new Fixtures.SyntheticTwoPulls().WriteTemp(ReplayEncoding.Text));
      var all = ReplayParser.Parse(new Fixtures.SyntheticThreePulls().WriteTemp(ReplayEncoding.Text));
      var merge1 = ForgePipeline.Analyze([a, bc], NullGameData.Instance).Effective;
      var merge2 = ForgePipeline.Analyze([Recording.Concat([a, bc])], NullGameData.Instance).Effective;
      var joint  = ForgePipeline.Analyze([all], NullGameData.Instance).Effective;
      Assert.Equal(EncounterModelJson.Serialize(joint), EncounterModelJson.Serialize(merge2));
      // pull set equal; sources[] differs by design (merge2 has 2 sources, joint 1)
      Assert.Equal(joint.Pulls.Length, merge1.Pulls.Length);
      Assert.Equal(2, merge1.Sources.Length);
  }
  ```
  Fixture rule: the three fixtures must be cut from one logical fight (same zone/cfc/OID, distinct actor instance ids across files, file A = pull 1, file B = pulls 2–3). Implement `SyntheticThreePulls` as a builder producing 3 pulls; `SyntheticSinglePull`/`SyntheticTwoPulls` reuse the same AIDs/timings with shifted instance ids.
- [ ] Run; fails (no `Concat`, no overload).
- [ ] Implement `Recording.Concat` in `Recording.cs` (uses existing `BuildActors`; TagCounts summed via `SortedDictionary<Tag,int>`).
- [ ] Implement the `Analyze(IReadOnlyList<Recording>)` overload delegating to `Analyze(Recording.Concat(recordings), …)`.
- [ ] Test passes.
- [ ] Failing CLI test `MergeCommandTests.cs`: write two temp logs via fixtures, run `MergeCommand.Run(new[]{"a.log","b.log"}, outPath, null, stdout, stderr)`, assert exit 0, file exists, `EncounterModelJson.Validate` clean, `Sources.Length == 2`; run with `--em` of a prior merge whose Overrides contains `/encounter/moduleName` and assert the new EM carries the override (`Effective.Encounter.ModuleName` renamed).
- [ ] Implement `MergeCommand` mirroring `InferCommand.Run` structure (parse each log with `ReplayParser.Parse(path, new ParseOptions(KeepRawOps: false))`, optional overrides from `--em`, `ForgePipeline.Analyze(list, …)`, serialize, write). Register in `Program.cs`.
- [ ] Door-boss fixture + test: one recording, two boss OIDs in the same CFC → two encounters; assert segmenter yields pulls for both and `MergeCommand` with `--select-boss` produces the requested primary OID (guards the encounter-identity rule §5.1-4).
- [ ] Spec §10: add `forge merge <log…> --out <em.json> [--em <em.json>]`; amend spec §M6 wording: "Merging recordings yields the EM of concatenating them and analyzing once, modulo `sources[]` (pulls are segmented per recording and concatenated; actor instance ids must be distinct across recordings — fixtures guarantee this; real-world collision caveat recorded in FORGE.md output)." Add that caveat sentence to `ForgeMdWriter` output.
- [ ] `./scripts/build.sh`.

**Verify:** `./scripts/build.sh` green with ≥4 new tests; `forge merge` on fixture logs in /tmp produces byte-identical EM across two runs (determinism assertion in `MergeCommandTests`).

## Task 4 — M6: degraded-version parsing + streaming performance

**Files:**
- Modify: `src/BossModForge.Core/Replay/OpReader.cs` (newer-than-known lenient path)
- Modify: `src/BossModForge.Core/Replay/ReplayParser.cs` (wire Degraded issue, remove early-return for newer versions)
- Test: `tests/BossModForge.Tests/Replay/DegradedVersionTests.cs` (new), `tests/BossModForge.Tests/Replay/StreamingPerfTests.cs` (new)

**Interfaces:** Consumes: `OpReader` (version gating; `Unsupported` flag), `ParseIssue(IssueSeverity, string Code, string Message, int? Line, long? ByteOffset)` (`Replay/ParseIssue.cs`), `Recording.Degraded` (`Recording.cs:36`), `RecordingBuilder(version:)` ctor arg (`Fixtures/RecordingBuilder.cs:22`). Produces: `public const int MaxSupportedVersion = 24;` on `OpReader`; recordings newer than 24 parse leniently with a Degraded issue instead of returning zero ops.

**Steps:**
- [ ] Failing test `DegradedVersionTests.cs`:
  ```csharp
  [Fact] public void NewerVersion_ParsesLenientDegraded()
  {
      var b = new RecordingBuilder(new DateTime(2026, 9, 19), version: 25)
          .Zone(1180, 826).Player(1, "P", Class.PLD, new(0, 0, 0)).Advance(TimeSpan.FromSeconds(2));
      var rec = ReplayParser.Parse(b.WriteTemp(ReplayEncoding.Text));
      Assert.True(rec.Degraded);
      Assert.Contains(rec.Issues, i => i.Severity == IssueSeverity.Degraded && i.Code == "UnsupportedVersion");
      Assert.NotEmpty(rec.Ops); // v24 layouts still recovered
  }
  [Fact] public void Version24_Unchanged() // golden guard: existing fixtures parse with zero Degraded issues
  {
      var rec = ReplayParser.Parse(new Fixtures.SyntheticDungeon().WriteTemp(ReplayEncoding.Text));
      Assert.False(rec.Degraded);
  }
  ```
- [ ] Run; `NewerVersion` fails (parser either treats 25 as unsupported or misgates — confirm actual behavior first and record it in the test comment).
- [ ] Implement: in `OpReader`, when `Version > MaxSupportedVersion`, clamp the gate version to 24 and expose a flag `NewerVersion`; in `ReplayParser.ParseInput`, replace the current `UnsupportedVersion` early-return so it only triggers for version < 10 (existing out-of-scope rule); for `NewerVersion` add one Degraded issue `"UnsupportedVersion"` with message "Replay version 25 is newer than supported 24; parsed with v24 gates." and continue.
- [ ] Tests green; all 4 goldens byte-identical (run `SyntheticDungeonGoldenTests` etc.).
- [ ] Failing perf test `StreamingPerfTests.cs` (env-gated so CI stays fast):
  ```csharp
  [Fact] public void LargeFile_ParsesWithinBudget()
  {
      if (Environment.GetEnvironmentVariable("FORGE_PERF") != "1") return; // skip silently
      var path = BuildLargeLog(Path.Combine(Path.GetTempPath(), $"forge-perf-{Guid.NewGuid():N}.log"), targetMb: 200);
      var before = Process.GetCurrentProcess().PeakWorkingSet64;
      var sw = Stopwatch.StartNew();
      var rec = ReplayParser.Parse(path);
      sw.Stop();
      File.Delete(path);
      Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"parse took {sw.Elapsed}");
      Assert.True(Process.GetCurrentProcess().PeakWorkingSet64 - before < 1L << 30, "peak +1 GB");
      Assert.False(rec.Degraded);
  }
  ```
  `BuildLargeLog`: `RecordingBuilder` with ~40 actors, `Frame()` + `Move()` every frame at 60 fps for ~35 min simulated (~126k frames → ≈200 MB text). If the builder is too slow generating it, emit the text directly with a `StreamWriter` loop in the test helper (same grammar: `timestamp|FRAM|…`, `timestamp|MOVE|…` per UPSTREAM.md §3).
- [ ] Run with `FORGE_PERF=1 dotnet test -c Release --filter LargeFile`. If parse > 10 s or > 1 GB peak, optimise the hot path first: `TextInput` line buffering / `Span` parsing of numbers (measure before/after; do not change op semantics). Re-run.
- [ ] `./scripts/build.sh` (perf test skips by default).

**Verify:** `./scripts/build.sh` green; `FORGE_PERF=1 dotnet test BossModForge.slnx -c Release --filter "FullyQualifiedName~StreamingPerf"` completes with both asserts passing; goldens unchanged.

## Task 5 — M7: `forge polish` (LLM pass, ADR-007)

**Files:**
- Create: `src/BossModForge.Core/Polish/Polisher.cs`, `src/BossModForge.Core/Polish/IPolishProvider.cs`
- Modify: `src/BossModForge.Core/Inference/Confidence.cs` (`ReviewItem` gains `string? Resolution = null`), `src/BossModForge.Core/Model/EncounterModel.cs` (`AbilityModel`/`ParticipantModel` gain `string? Comment = null` — append optional param), `schema/em.v1.schema.json` (additive: `comment`, `resolution`), `src/BossModForge.Core/Model/EncounterModelJson.cs` (no change expected — records serialize automatically; verify)
- Create: `src/BossModForge.Cli/Commands/PolishCommand.cs`, `src/BossModForge.Cli/Polish/FilePatchProvider.cs`, `src/BossModForge.Cli/Polish/HttpPatchProvider.cs`
- Modify: `src/BossModForge.Cli/Program.cs`
- Modify: `docs/adr/` — add `2026-09-20-adr-008-polish-field-allowlist.md` (records the allowlist decision; ADR-007 stays the parent decision)
- Test: `tests/BossModForge.Tests/Polish/PolisherTests.cs` (new), `tests/BossModForge.Tests/Cli/PolishCommandTests.cs` (new)

**Interfaces:** Consumes: `EncounterModel`, `OverridesApplier`-style sanitiser (`Model/Identifiers.cs` — reuse `CSharpIdentifier.Sanitise`), `EncounterModelJson.{Serialize,Deserialize,Validate}`. Produces:
```csharp
// Core/Polish/Polisher.cs
public sealed record PolishPatch(string Op, string Path, JsonElement Value); // Op: "replace" | "remove"
public sealed record PolishResult(EncounterModel Model, ImmutableArray<string> Accepted, ImmutableArray<string> Rejected);
public static class Polisher
{
    public static PolishResult Apply(EncounterModel em, IReadOnlyList<PolishPatch> patches); // pure, deterministic, never throws on patch content
}
// Core/Polish/IPolishProvider.cs
public interface IPolishProvider { string Name { get; } JsonElement Complete(JsonObject prompt); }
```
Allowlist (exact): path suffixes `enumName`, `name`, `comment` under `/abilities/{aid}/…` and `/participants/{oid}/…`, plus `/review/{reviewId}/resolution` where the value must be one of that review item's `Suggested`. Everything else → rejected with a reason string.

**Steps:**
- [ ] Failing tests `PolisherTests.cs`: `Polish_RenamesEnumName_UpdatesEnums` (patch replace `/abilities/{aid}/enumName` "Heavy Slash" → EM `Enums.Aid[aid]` and `AbilityModel.EnumName` updated, sanitised via `CSharpIdentifier.Sanitise`); `Polish_RejectsOutOfAllowlist` (patch on `/abilities/{aid}/shape` and `/abilities/{aid}/castTime` → in `Rejected`, model unchanged); `Polish_ResolutionMustBeSuggested` (resolution "Foo" rejected; a `Suggested[0]` value accepted → `ReviewItem.Resolution` set); `Polish_IsDeterministic` (two runs byte-identical serialised); `Polish_BadAid_Rejected` (`/abilities/999999/enumName` → rejected, no throw).
- [ ] Run; all fail (types don't exist).
- [ ] Add `Comment` optional params to `AbilityModel`/`ParticipantModel` and `Resolution` to `ReviewItem`; extend `schema/em.v1.schema.json` additively (`comment: {"type":"string"}`, `resolution: {"type":"string"}` on the respective objects); confirm `EncounterModelJsonTests` still pass (round-trip).
- [ ] Implement `Polisher.Apply`: parse patch path with regex `^/(abilities|participants)/(\d+)/(enumName|name|comment)$` and `^/review/([^/]+)/resolution$`; per match apply with the rules above; enumName changes must also update `Enums.Aid`/`Enums.Oid` and must reject on collision (mirroring `OverridesApplier` collision rule); collect accepted/rejected reason strings (`"{path}: {reason}"`, ordinal-sorted).
- [ ] Tests green.
- [ ] Failing CLI test `PolishCommandTests.cs`: `forge polish em.json --provider file --patches patches.json --out out.json` with a patches file containing one accepted + one rejected patch → exit 0, out.json valid per `EncounterModelJson.Validate`, stdout lists rejected patch with reason; `--provider http` without `--endpoint` → exit 4.
- [ ] Implement `FilePatchProvider` (`JsonSerializer.Deserialize<List<PolishPatch>>` from file — used by tests and for offline patch review), `HttpPatchProvider` (System.Net.Http only: POST `{prompt}` JSON to `--endpoint` with `Authorization: Bearer $env[--api-key-env]`, read back a JSON array of patches; 30 s timeout), `PolishCommand` (`--provider file|http`, `--patches`, `--endpoint`, `--model`, `--api-key-env`, `--out`; prompt = serialised EM + review items + allowlist text). Register in `Program.cs`.
- [ ] Write ADR-008 (allowlist = enumName/name/comment/resolution; patches never touch geometry/mechanics/timeline; rejected patches logged, never fatal).
- [ ] `./scripts/build.sh`.

**Verify:** `./scripts/build.sh` green (≥9 new tests); spec §10 gains `forge polish <em.json> --provider …` line and §M7 acceptance is met.

## Task 6 — `forge conform` (parser conformance)

**Files:**
- Create: `src/BossModForge.Cli/Commands/ConformCommand.cs`, `src/BossModForge.Cli/Conformance/BossModReflection.cs`, `src/BossModForge.Cli/Conformance/ConformComparer.cs`
- Modify: `src/BossModForge.Cli/Program.cs`, `docs/specs/2026-09-18-bossmodforge-design.md` §8 (absent-DLL behaviour), `README.md` (conform usage)
- Test: `tests/BossModForge.Tests/Cli/ConformTests.cs` (new)

**Interfaces:** Consumes: `ReplayParser.Parse` → `Recording.TagCounts` (`IReadOnlyDictionary<Tag,int>`), `Tag.ToString()` four-char names. Produces:
```csharp
// Cli/Conformance/ConformComparer.cs
public sealed record TagCountDiff(string Tag, int ForgeCount, int BossModCount);
public static class ConformComparer
{
    public static ImmutableArray<TagCountDiff> Compare(IReadOnlyDictionary<string, int> forgeCounts, IReadOnlyDictionary<string, int> bossModCounts);
}
// Cli/Conformance/BossModReflection.cs
public static class BossModReflection
{
    public static IReadOnlyDictionary<string, int> CountOps(string bossModDllPath, string logPath); // Assembly.LoadFrom + Replay parser via reflection
}
```
`forge conform <log…> --bossmod-dll <path> [--json]`. Exit codes: 0 all tags equal, 3 diffs found, 4 error (incl. DLL absent/unloadable). Absent `--bossmod-dll`: stderr `error: conformance requires --bossmod-dll (a build of BossMod.dll from the pinned checkout); see docs/UPSTREAM.md` + exit 4 (documented choice per M0 acceptance).

**Steps:**
- [ ] Failing tests `ConformTests.cs`: `Compare_FindsDiffs` (synthetic dictionaries `{CST+:3,CST!:2}` vs `{CST+:4,CST!:2,MOVE:1}` → diffs for `CST+` and `MOVE`, `CST!` absent); `Compare_Identical_Empty`; `Run_WithoutDll_Exit4` (message contains `--bossmod-dll`); `Run_BadDll_Exit4`; `Run_MissingLog_Exit4`.
- [ ] Run; fail (no code).
- [ ] Implement `ConformComparer.Compare` (sorted union of keys; zero counts included; ordinal order).
- [ ] Implement `BossModReflection.CountOps`: load assembly; find type `BossMod.Replay.Replay` (fallback `BossMod.Replay.ReplayParserLog`); instantiate with the log path; read the `Ops` list; map op runtime types to tags: first try the parser's private static `_parsers` dictionary (`FourCC`→handler) to build reverse map, else the built-in table transcribed from `docs/UPSTREAM.md` §5 (e.g. `ActorCastStart`→`CST+`, `ActorCastEvent`→`CST!`, `ActorMove`→`MOVE`, `ActorCreate`→`ACT+`, `ActorDestroy`→`ACT-`, `ActorStatus`→`STA+`/`STA-` by flag, `ActorCast`→`CST+`/`CST-`, …); count by tag name. Wrap everything in try/catch → `InvalidOperationException` with a clear message (exit 4 upstream).
- [ ] Implement `ConformCommand` (parse each log with Forge, compare, print per-tag table or `--json`, exit per codes). Register in `Program.cs`.
- [ ] Unit tests green (comparer + CLI error paths are fully testable here). The happy reflection path is **gated**: no BossMod.dll exists on this machine — record in `docs/UPSTREAM.md` §11 "Conformance" that `--bossmod-dll` execution is verified manually on a machine with a BossMod build, and add a `--selftest` hidden option that reflect-loads the given DLL and prints the resolved op→tag map so the first real run is one command.
- [ ] Spec §8 step 5: add the absent-DLL sentence. `./scripts/build.sh`.

**Verify:** `./scripts/build.sh` green (≥5 new tests); `forge conform x.log` (no DLL) prints the documented message and exits 4; `forge conform x.log --bossmod-dll /nonexistent` exits 4.

## Task 7 — `LuminaGameData` for the CLI (`--game-path`)

**Files:**
- Modify: `src/BossModForge.Cli/BossModForge.Cli.csproj` (add `<PackageReference Include="Lumina" Version="5.6.1" />` — builder checks nuget.org for the current stable 5.x at implementation time and pins the exact version)
- Create: `src/BossModForge.Cli/GameData/LuminaGameData.cs`
- Modify: `src/BossModForge.Cli/Commands/InferCommand.cs` (add `--game-path`)
- Modify: `docs/specs/2026-09-18-bossmodforge-design.md` §2 project table (Core "Lumina" → "— (Lumina lives in Cli)"), §4.3
- Create: `docs/adr/2026-09-20-adr-009-lumina-in-cli.md`
- Test: `tests/BossModForge.Tests/Cli/InferCommandGameDataTests.cs` (new)

**Interfaces:** Consumes: `IGameData` (`src/BossModForge.Core/GameData/IGameData.cs:13-20`), `ActionRow` record (same file), `ForgePipeline.Analyze(recording, gameData, …)`. Produces:
```csharp
// Cli/GameData/LuminaGameData.cs
public sealed class LuminaGameData : IGameData
{
    public LuminaGameData(string gamePath); // ctor never throws; unavailable → all members null
    public bool Available { get; }
    public ActionRow? Action(uint id);
    public string? BNpcName(uint nameId);
    public string? StatusName(uint id);
    public (uint CfcId, string Name)? ContentForTerritory(uint territoryType);
    public string? PlaceName(uint territoryType);
}
```

**Steps:**
- [ ] Failing test `InferCommandGameDataTests.cs`: `LuminaGameData_BadPath_DegradesGracefully` — `new LuminaGameData("/nonexistent/" + Guid.NewGuid())` → `Available == false`, all members return null, no throw; `Infer_WithBadGamePath_StillRuns` — `InferCommand.Run(log, gamePath: badPath, …)` succeeds with EM identical to the `NullGameData` run except nothing (names fall back); `FakeGameData_ChangesNames` — implement a private `FakeGameData : IGameData` in the test returning `ActionRow(40001,"Heavy Slash",3,40,0,50,0,"gl_fan090_1bf")` and `BNpcName` "Drowsie", assert the EM ability name / enum name reflect it (proves the wiring is live end-to-end without a game install).
- [ ] Run; fail (no `--game-path`, no class).
- [ ] Add the Lumina package to the Cli csproj; `dotnet restore`.
- [ ] Implement `LuminaGameData` against Lumina 5 struct-row sheets: `Action` (`Name.ExtractText()`, `CastType`, `EffectRange`, `XAxisModifier`, `Cast100ms`, `Omen.RowId` + `Omen.ValueNullable?.Path.ExtractText()`), `BNpcName.Singular`, `Status.Name`, `TerritoryType.ContentFinderCondition`/`PlaceName` (`ValueNullable?.Name.ExtractText()`); catch all Lumina exceptions in the ctor → `Available = false`.
- [ ] Wire `--game-path` into `InferCommand` (present → `LuminaGameData`, absent → `NullGameData.Instance`; print a warning to stderr when the path yields `Available == false` and continue).
- [ ] Tests green; spec §2/§4.3 updated; ADR-009 written (Core stays free of Lumina; Cli owns the only disk-based game-data implementation; plugin uses `CachedGameData` via Dalamud).
- [ ] `./scripts/build.sh`.

**Verify:** `./scripts/build.sh` green (3 new tests). **Not possible on this machine:** any test against a real FFXIV install — explicitly recorded in `docs/UPSTREAM.md` §11 "LuminaGameData": runtime verification requires `--game-path` pointing at a game install (Windows box); compile + graceful-degradation coverage is the acceptance here.

## Task 8 — M4b: `--bossmod` real-build validation (checkout-gated)

**Files:**
- Create: `src/BossModForge.Cli/Validation/BossmodBuildValidator.cs`, `src/BossModForge.Cli/Validation/CompileErrorMapper.cs`, `src/BossModForge.Cli/ReplayValidator/` (host project template: `ForgeReplayValidator.csproj`, `Program.cs`)
- Modify: `src/BossModForge.Cli/Commands/ValidateCommand.cs` (replace the stub at lines 71–75)
- Modify: `docs/specs/2026-09-18-bossmodforge-design.md` §8 (gated-environment note), `docs/UPSTREAM.md` §11
- Test: `tests/BossModForge.Tests/Cli/CompileErrorMapperTests.cs` (new), `tests/BossModForge.Tests/Validation/ValidateCommandBossmodTests.cs` (new)

**Interfaces:** Consumes: `Validator.Validate` + `ValidationReportWriter` (`ValidationReport.cs`), `EmitResult.Files`, existing dry path in `ValidateCommand.Run`. Produces:
```csharp
// Cli/Validation/CompileErrorMapper.cs
public sealed record CompileErrorItem(string File, int Line, string Code, string Message, string? EmTarget);
public static class CompileErrorMapper
{
    public static ImmutableArray<CompileErrorItem> Map(string buildOutput, EncounterModel em); // pure
}
// Cli/Validation/BossmodBuildValidator.cs
public sealed record BossmodBuildResult(bool BuildSucceeded, ImmutableArray<CompileErrorItem> Errors, string? ValidatorHostOutput);
public static class BossmodBuildValidator
{
    public static BossmodBuildResult Run(string moduleDir, string bossmodCheckout, EncounterModel em, IReadOnlyList<string> recordings, string scratchNamespace = "BossMod.ForgeScratch");
}
```

**Steps:**
- [ ] Failing tests `CompileErrorMapperTests.cs`: feed sample `dotnet build` output lines — `…/ForgeScratch/D012/D012States.cs(26,42): error CS0103: The name 'AID.HeavySlsh' does not exist in the current context` → `EmTarget == "ability:40001"` when `HeavySlsh` is within Levenshtein distance 3 of a known `EnumName`; `…: error CS1729: 'StandardAOEs' does not contain a constructor that takes 3 arguments` → `EmTarget == "flavor-profile"`; unrelated file → `EmTarget == null`.
- [ ] Run; fail.
- [ ] Implement `CompileErrorMapper.Map`: regex `\((?<line>\d+),(?<col>\d+)\): error (?<code>CS\d+): (?<msg>.+)` per line; extract the token after `The name '` / `constructor of '` / `' does not contain…`; match against `em.Enums.Aid`/`Oid` values (case-insensitive prefix + Levenshtein ≤ 3, ordinal-sorted first match); map component-name errors to `"flavor-profile"`; everything else null. Deterministic, no I/O.
- [ ] Failing CLI tests `ValidateCommandBossmodTests.cs`: `--bossmod` pointing at a non-existent dir → exit 4 with stderr containing `--bossmod`; `--bossmod` pointing at an empty temp dir (no `.sln`/`.slnx`, no `BossMod/`) → exit 4 with "does not look like a BossMod checkout".
- [ ] Implement `BossmodBuildValidator.Run`: verify checkout shape (`BossMod/` dir + a solution file; else throw `InvalidOperationException` with the message above); copy `moduleDir` files to `<checkout>/BossMod/Modules/ForgeScratch/<ModuleName>/` (deterministic, `File.Copy`, overwrite); `Process.Start("dotnet", $"build \"{sln}\" -c Release")` with output capture; `CompileErrorMapper.Map` on failure → return; on success locate/run the ReplayValidator host (copy `src/BossModForge.Cli/ReplayValidator/` into the checkout, `dotnet run -- <module assembly-qualified name> <log…>`, capture JSON) and merge into the dry report. All Process calls wrapped; any failure → `BossmodBuildResult(false, …)` with the message, never an unhandled exception.
- [ ] Write the ReplayValidator host template: minimal console program that references BossMod, loads a recording through BossMod's own parser, instantiates the emitted module for the encounter, steps frames, and prints transition/AOE JSON on stdout (model on upstream `UIDev`; keep it < 200 lines; it cannot be compiled here — that's expected).
- [ ] Wire into `ValidateCommand.Run`: replace lines 71–75 — non-empty `--bossmod` → `BossmodBuildValidator.Run(...)`; on `!BuildSucceeded` print mapped errors (with `EmTarget`) to stderr, still write `validation-report.{json,md}` including a `failures` entry, exit 3; success → exit per dry report.
- [ ] Tests green; spec §8 + UPSTREAM.md §11 record: "End-to-end `--bossmod` execution requires a BossMod/BMR checkout that builds on the machine (Dalamud libs; typically Windows). Everything except the in-checkout `dotnet build`/host run is implemented and unit-tested on Linux."
- [ ] `./scripts/build.sh`.

**Verify:** `./scripts/build.sh` green (≥6 new tests); gated steps explicitly documented; `forge validate --em x --recordings y --bossmod /empty` exits 4 with the checkout message.

## Task 9 — Definition-of-done sweep (docs + quality-gate)

**Files:**
- Modify: `README.md` (command list incl. `merge|polish|conform`, `--game-path`, `--bossmod` gating, two-solution build matrix), `CONTEXT.md` (glossary: merge, polish, conform, degraded parse, flavor), `AGENTS.md` (Purpose: M0–M5 → M0–M7 shipped scope; deferred items list), `docs/UPSTREAM.md` (pins table final, §10 bmr verification record, §11 gated-verification ledger), `docs/plans/2026-09-20-completion-status.md` (new status doc mirroring m4/m5 format: shipped, test counts, deviations, gated items)
- No source changes.

**Steps:**
- [ ] Run `./scripts/build.sh` — expect zero failures; record final test count in the status doc.
- [ ] Run `quality-gate` at release level per `quality-gate.json` (`changed-files`, `build`, `test`); attach/record output; fix any red level before proceeding.
- [ ] README: verify every `forge` subcommand in `Program.cs` is documented with an example; document `FORGE_PERF=1`; document the Windows-only checklist pointers (`scripts/build-plugin.ps1`, M4b `--bossmod`, conform `--bossmod-dll`).
- [ ] CONTEXT.md: glossary matches spec §0 terms + new ones; no stale milestone claims.
- [ ] AGENTS.md: update the Purpose paragraph (merge/polish/conform shipped; plugin handoff list below), keep Local Contracts and Child DOX index current (add none — no new subfolder AGENTS.md).
- [ ] UPSTREAM.md: pins table shows both commits `verified`; §9 "Open VERIFY" renamed "Closed VERIFY"; §11 ledger lists what remains machine-gated (plugin compile, M4b e2e, conform reflection run, Lumina runtime) and where each is verified.
- [ ] Write `docs/plans/2026-09-20-completion-status.md`: shipped list per task, deviations (merge associativity interpretation; Lumina in Cli ADR-009; ReviewItem.Resolution/Comment schema additions; conform absent-DLL exit-4 choice), test count, quality-gate result, and the plugin handoff list below copied verbatim.
- [ ] Final `./scripts/build.sh` + grep the repo for `TODO(forge)` placeholders that this plan was supposed to resolve; each remaining one must be a deliberate `Review` item, not a plan gap.

**Verify:** `./scripts/build.sh` green; `quality-gate` release level passes; `grep -rn "VERIFY" docs/specs/2026-09-18-bossmodforge-design.md` shows no open items.

## Plugin handoff (documented, not executed here)

`src/BossModForge.Plugin/**` stays untouched. The Windows builder resolves the `// VERIFY:` items from `docs/plans/m5-builder-brief.md` §8 (items 1–11: Lumina `Action`/`Omen`/`ContentFinderCondition` columns, `DalamudApiLevel` 15, ImGui member set, `IFramework.RunOnFrameworkThread<T>`, BMR replay dir name, replay file pattern, `JsonSchema.Net` transitive DLLs, BMR IPC name, `ConfigDirectory`/`AssemblyLocation`) plus the `m5-status.md` note that the plugin has never been compiled. Verification path: `scripts/build-plugin.ps1` on a Windows box + the §10 checklist of the m5 brief, recorded in `docs/plans/m5-status.md`.

## Self-Review

**Spec/milestone coverage:** VERIFY tags → Task 1; BMR flavor + deferred M1 item → Task 2; M6 merge → Task 3; M6 degraded/streaming → Task 4; M7 polish (ADR-007) → Task 5; M0 conform stub → Task 6; deferred LuminaGameData (M1) → Task 7; M4b `--bossmod` → Task 8; definition-of-done (implementation plan final section) → Task 9. Non-goals respected: no plugin code changes, no recorder, no video/ACT adapter, no custom components.

**Placeholder scan:** every step names real files, real test code or exact template strings, and a verification command; no "add error handling" style steps. Gated work (BossMod checkout, FFXIV install, Dalamud libs) always has a Linux-executable seam test plus a documented manual gate — no task is unverifiable-by-design.

**Type consistency:** `Recording.Concat(IReadOnlyList<Recording>)` → `Recording` consumed by the new `ForgePipeline.Analyze(IReadOnlyList<Recording>, IGameData, …)` overload returning the existing `AnalysisResult`; `Polisher.Apply` returns `PolishResult` consumed only by `PolishCommand`; `ConformComparer.Compare(string→int)` consumes `Recording.TagCounts` mapped via `Tag.ToString()`; `LuminaGameData : IGameData` slots into the `InferCommand` call site that today passes `NullGameData.Instance`; `CompileErrorMapper.Map` is pure and consumed by `BossmodBuildValidator.Run` + `ValidateCommand`. Schema additions (`comment`, `resolution`) are optional fields on existing records — additive per the v1 schema convention used for `TimelineModel`.

| # | Deliverable | Depends on | Verify via |
|---|---|---|---|
| 1 | Spec VERIFY-free; UPSTREAM.md closed | — | grep + build.sh |
| 2 | bmr.json pinned/verified; BmrProfile honors it; bmr golden smoke | 1 | build.sh |
| 3 | `forge merge` + `Recording.Concat`; associativity golden | — | build.sh + MergeTests |
| 4 | Lenient v25+ parse; 200 MB perf gate | — | build.sh + FORGE_PERF run |
| 5 | `forge polish` (allowlist, providers, ADR-008) | — | build.sh + PolisherTests |
| 6 | `forge conform` (comparer, reflection seam, exit-4 doc) | — | build.sh + ConformTests |
| 7 | `LuminaGameData` + `--game-path` (ADR-009) | — | build.sh + graceful tests |
| 8 | `--bossmod` orchestration + compile-error mapper (gated e2e) | 1, 2 | build.sh + mapper/CLI tests |
| 9 | Docs sweep, status doc, quality-gate release | 1–8 | build.sh + quality-gate |
