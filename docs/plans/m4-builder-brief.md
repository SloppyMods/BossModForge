---
tags:
  - type/plan
  - project/bossmodforge
  - status/draft
type: plan
project: bossmodforge
status: frozen
milestone: M4
spec: ../specs/2026-09-18-bossmodforge-design.md
plan: ./2026-09-18-bossmodforge-implementation.md
upstream: ../UPSTREAM.md
depends_on: ./m2-builder-brief.md
---

# M4 builder brief — timeline alignment, phases, state machine, dry validate

Tracer bullet: `forge infer a.log -o em.json && forge emit em.json --flavor vbm -o out/` now writes a `DeathPhase(0, Phase0)` whose body is a `Cast/CastMulti/Timeout/Targetable` chain aligned across pulls, with `ActivateOnEnter/DeactivateOnExit` placed per step, a folded rotation loop, and `forge validate --em em.json --recordings a.log` replays every pull against the EM and writes `validation-report.{json,md}` (exit 0/3).

Starts from M3 merged (80 tests green). Algorithms: spec §5.6 (phase/branch cues), §5.7 (alignment → state machine), §7.3 last paragraph (component placement), §7.4 (review markers), §8 (validation metrics), §10 (exit codes). This brief fixes files and public APIs; where it says "freeze" the signature is not negotiable, everything else is the builder's call as long as tests in §6 pass. All upstream `VERIFY`s in this scope are already resolved in §5 — do not re-verify, do not invent.

## 1. Scope

In: `TimelineAligner` (anchor extraction, progressive affine-gap NW, consensus with support and gap `{median, MAD}`, loop folding, branch → `CastMulti`), `PhaseDetector` (targetable spans → `Targetable` steps, HP-threshold casting-stop → `SimplePhase` split, DIE+/DIRU as evidence), `TimelineModel` in the EM (additive, schema v1 unchanged), `StatesPlan` + emission (`DeathPhase`/`SimplePhase` methods, state id scheme, loop method, tail state, placement rules, review comments), `DryValidator` + `forge validate` (timing p90, AOE precision/recall, coverage, report json+md, `--write-em`), fixtures `SyntheticRotation` (loop + branch + jitter) and `SyntheticPhases`, goldens.

Out: compiling against a live BossMod checkout (ADR-006 real validation = **M4b**; `--bossmod` is a stub that errors), `ComponentCondition` generation (Kind reserved, never produced), `CastStartFork`/`ConditionFork` (multi-step forks become per-column `CastMulti` + Review), `ActorCast*` for adds/helpers (their casts never become steps; they stay covered by components only), per-phase arena bounds (still Review), plugin, merge, LLM pass.

Ownership: **one builder (Grok)** owns every file in §2 and nothing else. Do not touch `Replay/**`, `PullSegmenter.cs`, `ParticipantClassifier.cs`, `AbilityCataloger.cs`, `MechanicClassifier.cs`, `BoundsFitter.cs`, `Shape.cs`, `Identifiers.cs`, `Confidence.cs`. The only edit to `ShapeInference.cs` is the one extraction in §3.6. The only edit to `ShapeCSharp.cs` is the `f`-suffix fix in §4.4.

## 2. Files

```
src/BossModForge.Core/
  Inference/Timeline/Anchors.cs             AnchorKind, AnchorEvent, AnchorExtractor
  Inference/Timeline/Alignment.cs           AlignmentColumn, ProfileAligner (affine-gap NW, deterministic traceback)
  Inference/Timeline/TimelineAligner.cs     TimelineAlignerOptions, ConsensusStep, Loop, Timeline, TimelineAligner.Align
  Inference/Timeline/PhaseDetector.cs       PhaseCue, PhaseSplit, PhaseDetector.Detect
  Model/EncounterModel.cs                   + TimelineModel, PhaseModel, TimelineStep, LoopModel; EncounterModel gains trailing `TimelineModel? Timeline = null`
  Model/EncounterModelBuilder.cs            calls PhaseDetector + TimelineAligner after classification; component placement; review items
  Model/EncounterModelJson.cs               no code change expected (nulls omitted); confirm round-trip
  Emit/StatesPlan.cs                        StatesPlan, PhasePlan, StepPlan, LoopPlan records (pure data, no templates)
  Emit/StatesPlanner.cs                     EncounterModel → StatesPlan (ids, offsets, unrolling, activate/deactivate resolution)
  Emit/IEmitterProfile.cs                   StatesClass(string moduleName, IEnumerable<string>) REPLACED by StatesClass(StatesPlan plan)
  Emit/VbmProfile.cs                        renders StatesPlan via templates (§4)
  Emit/BmrProfile.cs                        delegates as before
  Emit/ModuleEmitter.cs                     builds StatesPlan; passes component names → class names map to the planner
  Emit/ShapeCSharp.cs                       Num(): `f` suffix on non-integers (§4.4)
  Emit/ForgeMdWriter.cs                     "Timeline" section (phases, steps table, loop, branches, review)
  Validate/DryValidator.cs                  ValidateOptions, ValidationReport + parts, DryValidator.Validate/ToJson/ToMarkdown
  Validate/TimelineSimulator.cs             unrolls TimelineModel and walks one pull (shared by validator; internal)
src/BossModForge.Cli/Commands/ValidateCommand.cs   `forge validate`
src/BossModForge.Cli/Program.cs             register ValidateCommand
flavors/vbm.json, flavors/bmr.json          new templates (§4.1)
schema/em.v1.schema.json                    additive: timeline (§3.4)
docs/UPSTREAM.md                            §8.1 already appended by the architect; add only new facts you actually read
tests/BossModForge.Tests/
  Fixtures/SyntheticRotation.cs             3 pulls, opener + 6-step loop + branch + optional jitter (§6.1)
  Fixtures/SyntheticPhases.cs               3 pulls, HP-50 % untargetable split with new vocabulary (§6.2)
  Inference/AnchorExtractorTests.cs
  Inference/ProfileAlignerTests.cs          pure alignment on symbol/time arrays
  Inference/TimelineAlignerTests.cs         consensus, support, MAD, loop, branch, jitter → Timeout
  Inference/PhaseDetectorTests.cs
  Model/EncounterModelBuilderTimelineTests.cs
  Emit/StatesPlannerTests.cs                ids, unrolling, placement
  Emit/ModuleEmitterStatesTests.cs          exact text of the States class for a hand-built EM
  Validate/DryValidatorTests.cs             pass, wrong-radius fail, missing step, exit codes
  Cli/ValidateCommandTests.cs
  Golden/SyntheticDungeon.em.json / .vbm.cs   regenerated (1-step DeathPhase) — inspect the diff
  Golden/SyntheticShapes.em.json / .vbm.cs    regenerated (3-step DeathPhase, `7.5f`) — inspect the diff
  Golden/SyntheticRotation.em.json / .vbm.cs  new
  Golden/SyntheticPhases.em.json / .vbm.cs    new
  Golden/SyntheticRotation.validation.json    new (report golden, timestamps/paths normalised)
```

## 3. Frozen public APIs

### 3.1 Anchors (`BossModForge.Core.Inference.Timeline`, `Anchors.cs`)

```csharp
public enum AnchorKind { Cast, TargetableOff, TargetableOn }

// Symbol identity for alignment = (Kind, Aid). Aid is 0 for non-Cast kinds.
public sealed record AnchorEvent(int PullIndex, DateTime T, AnchorKind Kind, uint Aid, float CastTime, DateTime? CastEnd, ulong SourceInstanceId);

public static class AnchorExtractor
{
    // One ordered sequence per pull (index = pull.Index). Pure over catalog + recording.
    public static ImmutableArray<ImmutableArray<AnchorEvent>> Extract(Recording recording, ImmutableArray<Pull> pulls, AbilityCatalog catalog, TimelineAlignerOptions? options = null);
}
```

Rules:
- Cast anchors = `AbilityInstance`s whose `SourceInstanceId == pull.PrimaryInstanceId`, `CastStart != null`, and the owning `Ability.CastTime >= AnchorMinCastTime` (0.5 s). If a pull yields zero such anchors, fall back to every primary-actor instance with `CastStart != null`. **Only the primary actor** — `Cast<AID>` binds to `Module.PrimaryActor` (confirmed §5), so helper/add casts are never anchors.
- Two Cast instances with the same `Aid` whose `CastStart`s are within `MultiCastWindowSeconds` (0.5 s) collapse into one anchor at the earlier time.
- Targetable anchors = primary-actor `ActorTimeline.Targetable` transitions inside `[pull.Start, pull.End]` (skip the initial value at `Created`). A false→true pair shorter than `MinUntargetableSeconds` (3 s) is dropped (both events).
- Order by `T`, then Kind (`Cast` < `TargetableOff` < `TargetableOn`), then `Aid`.

### 3.2 Alignment (`Alignment.cs`)

```csharp
public sealed record AlignmentColumn(ImmutableArray<AnchorEvent> Members /* ≤ 1 per pull, ordered by PullIndex */)
{
    public AnchorKind Kind { get; }         // majority kind; ties → Cast
    public uint ConsensusAid { get; }       // most frequent Aid among Cast members; ties → smallest Aid
    public int Support => Members.Length;
}

public static class ProfileAligner
{
    // Gotoh affine-gap global alignment of `sequence` against `profile`. Returns the new profile (columns
    // reordered/extended; unmatched sequence anchors become new single-member columns at their traceback position).
    public static ImmutableArray<AlignmentColumn> AlignInto(ImmutableArray<AlignmentColumn> profile, ImmutableArray<AnchorEvent> sequence, TimelineAlignerOptions options);
}
```

Scoring (integers ×1000 internally to stay deterministic; expose as `double` in options):
- `Match = 5` when `anchor.Kind == column.Kind && (Kind != Cast || anchor.Aid == column.ConsensusAid)`, else `Mismatch = -4`. Kind mismatch (Cast vs Targetable) is never aligned: score `-1000`.
- Timing bonus added to `Match` and `Mismatch` alike: `TimingBonusMax · max(0, 1 − |g_seq − g_col| / TimingBonusScaleSeconds)` with `TimingBonusMax = 1.0`, scale `2.0 s`, where `g_seq` = seconds from the previous anchor in the sequence (first anchor: from `pull.Start`) and `g_col` = median of the column members' same quantity (first column: from their pulls' `Start`).
- `GapOpen = -4`, `GapExtend = -1`, both directions, no free end gaps.
- Traceback tie order: diagonal, then gap-in-sequence (skip profile column), then gap-in-profile (insert new column). Fixed order = byte-identical output.
- Progressive order: pulls sorted by anchor count descending, then `PullIndex` ascending; the first is the initial profile.

### 3.3 TimelineAligner and PhaseDetector

```csharp
public sealed record TimelineAlignerOptions(
    float AnchorMinCastTime = 0.5f, float MultiCastWindowSeconds = 0.5f, float MinUntargetableSeconds = 3f,
    double Match = 5, double Mismatch = -4, double GapOpen = -4, double GapExtend = -1,
    double TimingBonusMax = 1.0, double TimingBonusScaleSeconds = 2.0,
    float MadTimeoutSeconds = 1.5f, double MinSupportFraction = 0.6,
    int MinLoopLength = 2, int MinLoopRepeats = 2,
    double HpThresholdTolerance = 0.03, float CastingStopSeconds = 8f, double VocabularyJaccardMax = 0.5)
{ public static readonly TimelineAlignerOptions Default = new(); }

public enum StepKind { Cast, CastMulti, Timeout, Targetable, ComponentCondition /* reserved, never produced in M4 */ }

public sealed record ConsensusStep(int Index, StepKind Kind, uint Aid, ImmutableArray<uint> Of /* CastMulti: sorted distinct AIDs, else [Aid] */,
                                   bool? Targetable, float DelayMedian, float DelayMad, float CastTime, int Support, int PullCount,
                                   ImmutableArray<AnchorEvent> Members, string? ReviewReason);

public sealed record Loop(int StartStep, int Length, int Repeats, int TrailingPartial, float EntryDelay);

public sealed record Timeline(ImmutableArray<ConsensusStep> Steps /* folded: prefix + one loop body */, Loop? Loop, int PullCount,
                              Confidence Confidence, ImmutableArray<Evidence> Evidence);

public static class TimelineAligner
{
    public static Timeline Align(ImmutableArray<ImmutableArray<AnchorEvent>> anchorsPerPull, ImmutableArray<Pull> pulls, IReadOnlyDictionary<uint, float> castTimeByAid, TimelineAlignerOptions? options = null);   // pure; tests use this
}

public enum PhaseCueKind { HpThresholdCastingStop, PrimaryDeath, DirectorOncePerPull }
public sealed record PhaseCue(PhaseCueKind Kind, double HpRatio /* rounded to 0.05 */, ImmutableArray<(int PullIndex, DateTime T)> Occurrences, Confidence Confidence);
public sealed record PhaseSplit(ImmutableArray<PhaseCue> Cues, ImmutableArray<double> HpThresholds /* descending, e.g. [0.5] */, ImmutableArray<Evidence> Evidence);

public static class PhaseDetector
{
    public static PhaseSplit Detect(Recording recording, ImmutableArray<Pull> pulls, ImmutableArray<ImmutableArray<AnchorEvent>> anchorsPerPull, TimelineAlignerOptions? options = null);
    // Splits each pull's anchors at the HP thresholds: phase p = anchors with T in [t_p, t_{p+1}) where t_p = first time primary HP ratio ≤ threshold_p in that pull.
    public static ImmutableArray<ImmutableArray<ImmutableArray<AnchorEvent>>> SplitByPhase(Recording recording, ImmutableArray<Pull> pulls, ImmutableArray<ImmutableArray<AnchorEvent>> anchorsPerPull, PhaseSplit split);   // [phase][pull][anchor]
}
```

`Align` rules (spec §5.7):
1. Build the profile progressively (§3.2). Each column → one `ConsensusStep` in profile order.
2. `Support` = members count; `PullCount` = pulls with ≥ 1 anchor in this phase.
3. Gap per member = `member.T − T_prev` where `T_prev` = the previous anchor **of that pull** in the profile (walk back to the nearest earlier column containing that pull; none → `pull.Start`). For `Cast` steps convert to builder delay: `delay_m = gap_m − prevCastTime_m` where `prevCastTime_m` is the previous anchor's `CastTime` (0 for Targetable/pull start), because `Cast`'s delay counts from the previous state's transition (previous cast **end**). `DelayMedian` = median of `delay_m`, `DelayMad` = median(|delay_m − DelayMedian|), both clamped ≥ 0, rounded to 0.1. `CastTime` = `castTimeByAid[Aid]` (CastMulti: median over `Of`).
4. Kind: column of Targetable members → `Targetable` with `Targetable = (Kind == TargetableOn)`. Cast column with one distinct Aid → `Cast`. Cast column with ≥ 2 distinct AIDs where every Aid has ≥ `MinBranchSupport` members → `CastMulti`, `Of` sorted ascending, `ReviewReason = "branch: pulls diverge (…)"`; `MinBranchSupport = PullCount >= 4 ? 2 : 1` (spec §5.6 relaxation for tiny corpora; `Confidence` Low when 1). Otherwise (minority Aid below `MinBranchSupport`) drop the minority members from the column, they become their own single-member columns → rule 5 usually turns them into Timeout.
5. Downgrade to `Timeout` when `DelayMad > MadTimeoutSeconds` (reason `"timing jitter MAD {mad}s > 1.5s"`) or `Support < MinSupportFraction · PullCount` (reason `"seen in {Support}/{PullCount} pulls"`). A Timeout step keeps `Aid/Of/CastTime` for reporting; emission duration = `DelayMedian + CastTime`.
6. Loop folding on the **unfolded** consensus symbol string `S` (symbol = `(Kind, Aid or Of)`), `n = |S|`: for `L` from `⌊n/2⌋` down to `MinLoopLength`, for `p` from `0` to `L−1`: let `body = S[n−p−L .. n−p)`, require `S[n−p .. n) == body[0..p)` (trailing partial), then count `k` = maximal consecutive occurrences of `body` ending at `n−p`. First `(L, p)` with `k ≥ MinLoopRepeats` wins (largest `L`, then smallest `p`). `Loop = (StartStep = n−p−kL, Length = L, Repeats = k, TrailingPartial = p, EntryDelay = Steps[StartStep].DelayMedian as computed in the first repeat)`. Folded `Steps` = `S[0..StartStep)` ++ body, where body step stats pool members from **all repeats** (support = total members; `DelayMedian/Mad` of body step 0 pooled over repeats ≥ 2 only when `k ≥ 2`, else equals `EntryDelay`). Re-index `Index` 0..m−1. No loop → `Loop = null`, `Steps = S`.
7. `Confidence.Of(min(1, meanSupportFraction) · (1 − timeoutFraction))` with reasons; `Evidence("Alignment", "…")` carries per-column members and gap tables as JSON; `Evidence("Loop", …)` when folded.

`Detect` rules (spec §5.6; keep it this small):
- `HpThresholdCastingStop`: for each pull, find idle spans between consecutive primary Cast anchors ≥ `CastingStopSeconds` (8 s) **or** any `TargetableOff` anchor; take the primary HP ratio (`HpMp.CurHP / MaxHP` from `ActorTimeline.HpMp` at that time, hold-last) at the span start, rounded to 0.05. A rounded ratio that appears in ≥ `MinSupportFraction · pulls` pulls, once per pull, with `|raw − rounded| ≤ HpThresholdTolerance`, **and** whose cast vocabulary before vs after (sets of Aids within the pull) has Jaccard ≤ `VocabularyJaccardMax`, is a threshold. Ratios ≥ 0.95 or ≤ 0.05 are ignored. Multiple thresholds allowed, sorted descending.
- `PrimaryDeath` (DIE+ of primary) and `DirectorOncePerPull` (a `(DirectorId, UpdateId, Param1)` triple seen exactly once in every pull) are recorded as cues + `Evidence` only; they never split phases in M4.
- Targetable spans that do **not** coincide with an HP threshold stay inline as `Targetable` steps in the current phase.

### 3.4 Encounter Model (`BossModForge.Core.Model`, additive; `schemaVersion` stays 1; nulls omitted on write)

```csharp
public sealed record EncounterModel(…existing 13 params…, TimelineModel? Timeline = null);   // trailing optional → no call-site breaks

public sealed record TimelineModel(ImmutableArray<PhaseModel> Phases, ImmutableArray<TimelineStep> Steps, LoopModel? Loop, int PullCount, Confidence Confidence, ImmutableArray<Evidence> Evidence);

public sealed record PhaseModel(int Index, string Name /* "Phase0" */, string Kind /* Death | HpThreshold */, double? HpThreshold /* HpThreshold only */,
                                int FirstStep, int StepCount, ImmutableArray<string> Activate /* component class names activated on phase entry */);

public sealed record TimelineStep(string Id /* "0x00010000" runtime state id, see §4.2 */, string Kind /* Cast|CastMulti|Timeout|Targetable|ComponentCondition */,
                                  uint? Aid, float Delay, float CastTime, float TimingMad, int Support, ImmutableArray<uint> Of, string Name,
                                  ImmutableArray<string> Activate, ImmutableArray<string> Deactivate, string? Review, bool? Targetable);

public sealed record LoopModel(int StartStep, int Length, int Repeats, int TrailingPartial, float EntryDelay);
```

- `Steps` is the folded list across all phases in phase order; `PhaseModel.FirstStep/StepCount` index into it. `Loop` may only live inside the **last** phase (if folding finds a loop in an earlier phase, ignore it — its steps stay unrolled — and add `Evidence("LoopIgnored")`).
- `Name`: `AbilityModel.Name` if non-empty and ≠ EnumName-style default, else `EnumName`; CastMulti → names joined with `" / "`; Targetable → `"Boss disappears"` / `"Boss reappears"`; Timeout keeps the underlying cast name.
- `Activate`/`Deactivate` hold **emitted component class names** (post-`Dedupe`), resolved by `EncounterModelBuilder` using the same grouping `ModuleEmitter.BuildComponents` uses — extract that grouping into `public static ImmutableArray<ComponentUnit> ComponentPlanner.Plan(EncounterModel em)` (`ComponentUnit(string ClassName, string Kind, ImmutableArray<uint> Aids)`) in `Emit/StatesPlanner.cs` and make `ModuleEmitter` consume it so both agree by construction. Rule (spec §7.3 last paragraph): for unit `C` with AID set `A`, `steps(C)` = indices of steps with `Aid ∈ A` or `Of ∩ A ≠ ∅` within a phase. If `steps(C)` is empty (AIDs never became anchors — sub-0.5 s casts, helper/add casts) **or** any of `steps(C)` lies in the loop body → phase-level `PhaseModel.Activate`. Else `Activate` on `min(steps(C))`, `Deactivate` on `max(steps(C))`. A unit active in ≥ 2 phases is phase-level in each.
- `Review`: the reason string from `ConsensusStep.ReviewReason`; `EncounterModelBuilder` also adds a `ReviewItem(Medium, "timeline", "step {Id}: {reason}")` per reviewed step, one `ReviewItem(Medium, "timeline", "HP {threshold:P0} phase split inferred from casting stop")` per HpThreshold phase, and `ReviewItem(Low, "timeline", "no timeline steps — TrivialPhase emitted")` when `Steps` is empty.
- `schema/em.v1.schema.json`: add optional `timeline` with the shapes above; `EncounterModelJson.Validate` must accept both old (no timeline) and new JSON.

`EncounterModelBuilder.Build` (signature unchanged): after arena/shape/classification, `anchors = AnchorExtractor.Extract(...)`, `split = PhaseDetector.Detect(...)`, `perPhase = SplitByPhase(...)`, one `TimelineAligner.Align` per phase, then `TimelineModel` assembly, id assignment (§4.2), placement, review. `Timeline` is **always** present after `Build` (possibly zero steps); `null` only appears in hand-built or pre-M4 JSON.

### 3.5 StatesPlan (`BossModForge.Core.Emit`, `StatesPlan.cs`)

```csharp
public sealed record StepPlan(uint Offset /* relative to the enclosing method's id */, string Kind, string? AidMember, ImmutableArray<string> OfMembers, bool? Targetable,
                              float Delay, float CastTime, float Duration /* Timeout */, string Name, ImmutableArray<string> Activate, ImmutableArray<string> Deactivate, string? ReviewComment);
public sealed record LoopPlan(string MethodName /* "Rotation0" */, ImmutableArray<StepPlan> Body, int Repeats, int TrailingPartial, float EntryDelay, uint FirstCallOffset, uint Stride /* 0x10000 * Body.Length */);
public sealed record PhasePlan(int SeqId, string MethodName /* "Phase0" */, string Kind /* Death | HpThreshold */, double? HpThreshold, string DisplayName,
                               ImmutableArray<string> Activate, ImmutableArray<StepPlan> Prefix, LoopPlan? Loop, ImmutableArray<StepPlan> Tail /* trailing partial, unrolled */);
public sealed record StatesPlan(string ModuleName, ImmutableArray<PhasePlan> Phases, ImmutableArray<string> TrivialActivate /* used only when Phases is empty */);

public static class StatesPlanner
{
    public const uint StepStride = 0x10000;
    public static StatesPlan Plan(EncounterModel em, IReadOnlyDictionary<uint, string> aidEnumNames);   // pure
}

public interface IEmitterProfile { … string StatesClass(StatesPlan plan); … }   // replaces StatesClass(string, IEnumerable<string>)
```

### 3.6 Validation (`BossModForge.Core.Validate`)

```csharp
public sealed record ValidateOptions(double TimingP90MaxSeconds = 1.0, double TimingMatchWindowSeconds = 10.0, double MaxMissedStepFraction = 0.4,
                                     double AoeMinPrecision = 0.95, double AoeMinRecall = 0.95)
{ public static readonly ValidateOptions Default = new(); }

public sealed record StepTiming(string StepId, string Kind, uint? Aid, int Expected /* pull-visits */, int Observed, int Missed, double MedianError, double P90Error, double MaxError);
public sealed record TimingReport(int Steps, int Samples, int Missed, double P50, double P90, double Max, bool Passed, ImmutableArray<StepTiming> PerStep);
public sealed record AoeReport(string Component, uint Aid, int Instances, int Tp, int Fp, int Fn, int Tn, double Precision, double Recall, bool Passed);
public sealed record CoverageReport(int DamagingCasts, int Covered, double Fraction, ImmutableArray<uint> UncoveredAids);
public sealed record ValidationReport(string EmPath, ImmutableArray<string> Recordings, int PullCount, TimingReport Timing, ImmutableArray<AoeReport> Aoe, CoverageReport Coverage,
                                      bool Passed, ImmutableArray<string> Failures, ImmutableArray<ReviewItem> SuggestedReview);

public static class DryValidator
{
    public static ValidationReport Validate(EncounterModel em, IReadOnlyList<(string Path, Recording Recording)> recordings, ValidateOptions? options = null);
    public static string ToJson(ValidationReport report);       // deterministic, indented, invariant culture, paths as given
    public static string ToMarkdown(ValidationReport report);
    public static EncounterModel ApplyDowngrades(EncounterModel em, ValidationReport report);   // failed AOE → ability Confidence Low + ReviewItem(High, "shape", …); appends SuggestedReview
}
```

Pulls come from `PullSegmenter.Segment(recording)` per recording (default options); participants/catalog via the same stages `forge infer` runs (`NullGameData`). Only pulls whose `PrimaryOid == em.Encounter.PrimaryOid` are validated.

Timing (spec §8 row 1) — `TimelineSimulator` walks the unrolled steps of the EM per pull:
- Phase 0 starts at `pull.Start`; an `HpThreshold` phase starts at the first `t` where primary HP ratio ≤ threshold (missing → that phase is skipped for the pull and its steps count as `Missed`).
- Cursor `t_prev` = phase start. For `Cast`/`CastMulti`: `expected = t_prev + Delay`; `actual` = earliest primary-actor instance with `Aid ∈ Of`, `CastStart ≥ t_prev − 0.5 s`, not yet consumed, `|CastStart − expected| ≤ TimingMatchWindowSeconds`; record `|actual − expected|`; `t_prev = instance.CastEnd ?? CastStart + CastTime`. Not found → `Missed`, `t_prev = expected + CastTime`.
- `Timeout`: no sample, `t_prev += Duration (= Delay + CastTime)`. `Targetable`: `expected = t_prev + Delay`, `actual` = next primary targetable transition with that value; sample; `t_prev = actual` (missed → `t_prev = expected`).
- Loop: unroll `Repeats` full bodies + `TrailingPartial`; **additionally** keep consuming extra full bodies while the pull still has matching casts (a pull that lived longer than the consensus must not produce false misses; extra visits are counted as samples, not `Expected`).
- Pass: `P90 ≤ TimingP90MaxSeconds` over all samples **and** `Missed / (Samples + Missed) ≤ MaxMissedStepFraction`.

AOE precision/recall (spec §8 row 2): for every `AbilityModel` with `Shape` of kind Circle/Donut/Cone/Rect/Cross, for every instance with ≥ 1 damaging player hit, build the observation exactly as `ShapeInference.Infer` does at `t_snap = (CastEnd ?? EffectTime) + ShapeFit.LagSeconds` — **extract** that into `public static ImmutableArray<ShapeObservation> ShapeInference.Observe(Ability ability, Recording recording, ImmutableArray<Pull> pulls, float lagSeconds)` (behaviour-preserving refactor; `Infer` calls it). `predicted = Shape.Contains(player, origin, direction)`, `actual = hit with damage`. `Precision = Tp/(Tp+Fp)` (1 when denominator 0), `Recall = Tp/(Tp+Fn)`; pass when both ≥ 0.95. Abilities without a `Shape` are not reported.

Coverage (spec §8 row 3): damaging enemy casts = `AbilityModel`s from non-player participants with ≥ 1 instance dealing damage to a player; covered = `ComponentPlanner.Plan(em)` contains a unit with that Aid. Report only (no pass/fail) — spec gives no threshold; `Fraction < 1` adds `SuggestedReview(Medium, "coverage", …)`.

`Passed = Timing.Passed && Aoe.All(a => a.Passed)`; `Failures` lists human-readable reasons.

### 3.7 CLI

```
forge validate --em <em.json> --recordings <log…> [--out <dir>] [--write-em <path>] [--bossmod <checkout>] [--flavor vbm|bmr]
```

- Loads EM (`EncounterModelJson.Deserialize` + `Validate`; invalid → exit 4), parses each recording (parse error → exit 4), runs `DryValidator.Validate`, writes `<out>/validation-report.json` and `validation-report.md` (`--out` default = directory of `--em`), prints the markdown summary to stdout, `--write-em` writes `ApplyDowngrades(em)`.
- Exit codes (spec §10): `0` pass, `3` validation failed, `4` parse/argument error.
- `--bossmod <checkout>` → stderr `"NotImplemented: BossMod-build validation (ADR-006) is M4b; run without --bossmod for the Forge-internal dry validate"`, exit 4, nothing written. `--flavor` is accepted and ignored in M4 (reserved for M4b).
- `ValidateCommand.Run(string emPath, IReadOnlyList<string> recordings, string? outDir, string? writeEmPath, string? bossmodPath, TextWriter stdout, TextWriter stderr) : int` is public for tests, mirroring `InferCommand.Run`.

## 4. Emission (vbm; bmr delegates and keeps its High review item)

### 4.1 Templates (`flavors/vbm.json`; `bmr.json` copies them)

```json
"statesCtor":      "public {name}States(BossModule module) : base(module)",
"trivialPhase":    "TrivialPhase()",
"deathPhase":      "DeathPhase({seq}, {method})",
"simplePhase":     "SimplePhase({seq}, {method}, \"{name}\")",
"phaseHpUpdate":   ".Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed || Module.PrimaryActor.HPRatio <= {ratio}",
"phaseMethod":     "private void {method}(uint id)",
"loopMethod":      "private void {method}(uint id, float delay)",
"stateCast":       "Cast({id}, AID.{aid}, {delay}, {castTime}, \"{name}\")",
"stateCastMulti":  "CastMulti({id}, [{aids}], {delay}, {castTime}, \"{name}\")",
"stateTimeout":    "Timeout({id}, {duration}, \"{name}\")",
"stateTargetable": "Targetable({id}, {value}, {delay}, \"{name}\")",
"stateTail":       "SimpleState({id}, 10, \"???\")",
"activateOnEnter": ".ActivateOnEnter<{comp}>()",
"deactivateOnExit": ".DeactivateOnExit<{comp}>()"
```

`{aid}` is the **enum member** (`AID.HeavySlash`), never `IdCast` — `Cast<AID>` is constrained `where AID : Enum` (confirmed §5); `(uint)AID.X` would not compile. `{aids}` = comma-joined `AID.X` inside a collection expression (confirmed idiom `CastMulti(id + 0x10, [AID.A, AID.B], 2, 5)`).

### 4.2 State ids (confirmed against §5; supersedes spec §5.7's `0x0001_0000 * phaseIndex`)

- `SimplePhase/DeathPhase(seqID, build)` calls `build(seqID << 24)`, so the phase base is **`phaseIndex << 24`**, not `0x10000 * phaseIndex`. `PhasePlan.SeqId = phaseIndex`.
- Step `j` of a phase method uses offset `StepStride * j` = `0x10000 * j`; rendered `{id}` = `id` when offset is 0, else `id + 0x{offset:X}` (uppercase hex, no padding, e.g. `id + 0x20000`). `Cast` internally uses `id` (CastStart) and `id + 1` (CastEnd), so any stride ≥ 2 is legal; `0x10000` is the upstream idiom between mechanics and avoids `GroupWithNext` (set automatically when consecutive states share the high 16 bits — cosmetic UI grouping meant for sub-steps of one mechanic). The parent scope said `0x100 * step`; that value would group every step of a phase into one UI block. The stride is the single constant `StatesPlanner.StepStride` — change it there if the parent overrules.
- `TimelineStep.Id` = runtime id as 10-char hex string: `$"0x{(phaseIndex << 24) + StepStride * unfoldedIndex:X8}"` where `unfoldedIndex` counts prefix steps, then loop body steps of the **first** repeat, then trailing-partial steps are not separate (they reuse body ids + `Stride * Repeats`). A phase with > 255 unrolled states → truncate at 255, `ReviewItem(High, "timeline", "phase too long, truncated")`.
- Loop method call `r` (0-based) passes `id + 0x{FirstCallOffset + Stride * r:X}` and `delay` = `EntryDelay` for `r = 0`, else `Body[0].Delay`. Inside the loop method offsets restart at 0 (`Cast(id, …, delay, …)` for body step 0 — its delay is the `delay` parameter, not a literal). Trailing partial steps are rendered inline in the phase method after the last call with offsets continuing from `FirstCallOffset + Stride * Repeats`.
- Every phase method ends with the tail `SimpleState(id + 0xFF0000, 10, "???")` **when the phase has a loop or is the last phase** (upstream idiom: open-ended state, no exit condition; death ends the phase via `DeathPhase`). HpThreshold phases that are not last end without a tail (the phase `Update` transitions them).

### 4.3 States class shape (exact text the tests assert; 4-space indent, `\n` endings)

```csharp
class TestBossStates : StateMachineBuilder
{
    public TestBossStates(BossModule module) : base(module)
    {
        SimplePhase(0, Phase0, "P0")
            .ActivateOnEnter<Raidwide>()
            .Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed || Module.PrimaryActor.HPRatio <= 0.5f;
        DeathPhase(1, Phase1)
            .ActivateOnEnter<Raidwide>();
    }

    private void Phase0(uint id)
    {
        Cast(id, AID.Opener, 5.1f, 5, "Opener");
        // TODO(forge:R4, Low): branch: pulls diverge (Cone 2/3, ConeAlt 1/3)
        CastMulti(id + 0x10000, [AID.Cone, AID.ConeAlt], 3.2f, 4, "Cone / ConeAlt")
            .ActivateOnEnter<ConeAndConeAlt>()
            .DeactivateOnExit<ConeAndConeAlt>();
    }

    private void Phase1(uint id)
    {
        Targetable(id, true, 12.3f, "Boss reappears");
        Rotation0(id + 0x10000, 6.2f);
        Rotation0(id + 0x70000, 5.5f);
        Cast(id + 0xD0000, AID.Circle, 5.5f, 3, "Circle")
            .ActivateOnEnter<Circle>()
            .DeactivateOnExit<Circle>();
        SimpleState(id + 0xFF0000, 10, "???");
    }

    private void Rotation0(uint id, float delay)
    {
        Cast(id, AID.Circle, delay, 3, "Circle")
            .ActivateOnEnter<Circle>()
            .DeactivateOnExit<Circle>();
        // TODO(forge:R5, Medium): timing jitter MAD 3.0s > 1.5s — emitted Timeout
        Timeout(id + 0x10000, 9.5f, "Raidwide");
        …
    }
}
```

Rules: chain links each on their own line indented 12 spaces, statement ends `;`. Order of links on a state: `ActivateOnEnter*` (ascending class name), then `DeactivateOnExit*` (ascending). Phase-level links follow the same order; `phaseHpUpdate` is always the **last** link and only on `HpThreshold` phases. A `TimelineStep.Review` renders as `// TODO(forge:{ReviewId}, {Severity}): {reason}` on the line above the state (spec §7.4; `ReviewId` = the `R`-numbered item the builder created for it). With zero steps everywhere: unchanged M2 output `TrivialPhase()` + `.ActivateOnEnter<…>()` chain (`TrivialActivate`). Phase display names: `"P{index}"`. Method names `Phase{index}`, `Rotation{phaseIndex}`; dedupe against component class names via `CSharpIdentifier.Dedupe`.

### 4.4 Numbers

`ShapeCSharp.Num(float)`: unchanged formatting (invariant, ≤ 1 decimal, no trailing `.0`) **plus** an `f` suffix whenever the rendered value contains a `.` (`5` stays `5`, `5.7` → `5.7f`, `7.5` → `7.5f`). Reason: `AOEShapeCircle(float)`/`Cast(…, float delay, float castTime, …)` take `float`; a bare `7.5` is a `double` literal and does not implicitly convert — the current `SyntheticShapes` golden (`new AOEShapeCircle(7.5)`) would not compile. Regenerate both M2/M3 goldens and inspect. HP ratio in `phaseHpUpdate` uses the same `Num` (→ `0.5f`).

### 4.5 ModuleEmitter / ForgeMd

- `ModuleEmitter.Emit`: components come from `ComponentPlanner.Plan(em)` (same lines as today, same order); `StatesPlanner.Plan(em, aidEnumNames)`; `profile.StatesClass(plan)`. File layout unchanged (single `<Module>.cs`; §7.1's split into `Enums/States` files stays out of scope as in M1–M3).
- `ForgeMdWriter`: "Timeline" section — phases table (index, kind, threshold, steps, activate), steps table (id, kind, AID(s), delay, cast, MAD, support `k/n`, activate, deactivate, review), loop line (`Rotation0: steps 1–6 × 2 + 3 trailing`), timeline confidence. Deterministic order = EM order.

## 5. Confirmed upstream API (`awgil/ffxiv_bossmod@1391bc42`, `BossMod/BossModule/StateMachineBuilder.cs`) — recorded in `docs/UPSTREAM.md` §8.1

| Item | Confirmed |
|---|---|
| `Phase SimplePhase(uint seqID, Action<uint> buildState, string name, float dur = -1)` | calls `buildState(seqID << 24)`; throws if nested or if no states; `phase.Exit` clears non-`KeepOnPhaseChange` components |
| `Phase DeathPhase(uint seqID, Action<uint> buildState)` | `SimplePhase(seqID, buildState, "Boss death")` + `Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed \|\| Module.PrimaryActor.HPMP.CurHP == 0` |
| `Phase TrivialPhase(uint seqID = 0, float enrage = 10000)` | `DeathPhase(seqID, id => SimpleState(id, enrage, "Enrage"))` |
| `State SimpleState(uint id, float duration, string name)` | raw state, **no exit condition**; links `_lastState.NextStates = [state]`; duplicate id throws; same high 16 bits as previous → `GroupWithNext` |
| `State Timeout(uint id, float duration, string name = "")` | transitions when `timeSinceTransition >= duration`; no hints |
| `State Cast<AID>(uint id, AID aid, float delay, float castTime, string name = "", bool interruptible = false) where AID : Enum` | = `CastStart(id, aid, delay, "")` + `CastEnd(id + 1, castTime, name, interruptible)`; binds `Module.PrimaryActor`; hints `BossCastStart`/`BossCastEnd` |
| `State CastMulti<AID>(uint id, IEnumerable<AID> aids, float delay, float castTime, string name = "", bool interruptible = false)` | same shape, `id` / `id + 1`; call-site idiom `CastMulti(id + 0x10, [AID.A, AID.B], 2, 5)` |
| `State CastStart<AID>(uint id, AID aid, float delay, string name = "")`, `State CastEnd(uint id, float castTime, string name = "", bool interruptible = false)` | primary-actor wrappers over `ActorCastStart/ActorCastEnd` |
| `State Targetable(uint id, bool targetable, float delay, string name = "", float checkDelay = 0)` | sets `DowntimeEnd` (true) / `DowntimeStart` (false) |
| `State ComponentCondition<T>(uint id, float expected, Func<T, bool> condition, string name = "", float maxOverdue = 1, float checkDelay = 0) where T : BossComponent` | reserved for later milestones |
| `Phase/State .ActivateOnEnter<C>()`, `.DeactivateOnExit<C>()`, `State.ExecOnEnter<C>(Action<C>)`, `Phase.DeactivateOnEnter<C>()` | all return the same wrapper type → chainable; `Phase.Raw` / `State.Raw` public; `StateMachine.Phase.Update` is `Func<bool>?` |
| Loop | **no loop API**; `NextStates` is forward-only; upstream unrolls repeats and ends every open phase with `SimpleState(id + 0xFF0000, 10, "???")` (e.g. `A10RhalgrEmissaryStates.cs:21`, `A22AlthykNymeiaStates.cs:43`) |
| HP-based phase | idiom `SimplePhase(0, Phase1, "P1").Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed \|\| …` (`Ex3SpheneStates.cs:10-13`, `A22AlthykNymeiaStates.cs:9-11` with `.ActivateOnEnter<>()` before `.Raw.Update`); `Actor.HPRatio => (float)HPMP.CurHP / HPMP.MaxHP` (`Data/Actor.cs:137`); `ActorHPMP(uint CurHP, uint MaxHP, uint Shield, uint CurMP, uint MaxMP)` |
| Placement idiom | `Cast(id, AID.X, delay, 8, "Name").ActivateOnEnter<X>().DeactivateOnExit<X>();` (`A10RhalgrEmissaryStates.cs:26-28`); mechanic stride `id + 0x10000`, sub-steps `id + 0x10` |
| Delay literal | `5.7f` (float suffix on non-integers, bare ints allowed) |

Still open (not needed for M4; do not block): whether `BossModule.ActivateComponent` replays in-progress casts to a freshly activated component (upstream's `.ActivateOnEnter` on the `Cast` return proves the idiom works in practice).

## 6. Tests / acceptance

### 6.1 `Fixtures/SyntheticRotation.cs`

`Build(bool jitter = false, DateTime? start = null)` → `RecordingBuilder`; constants `BossOid = 0x4444`, AIDs `Opener = 40101 (raidwide, ct 5)`, `Circle = 40102 (r 6 at boss, ct 3)`, `Cone = 40103 (r 20, 45°, ct 4)`, `ConeAlt = 40108 (r 20, 45°, ct 4)`, `Raidwide2 = 40104 (ct 4)`, `Circle2 = 40105 (r 6 at boss, ct 3)`, `Buster = 40106 (single target, ct 4)`, `Raidwide3 = 40107 (ct 5)`. 4 players **static** at distances 4, 5, 8, 10 y from the boss (two inside r 6, two outside) with one inside-cone/one outside-cone at 45°: the two inside-circle players are at bearings 0° and 90° from a boss facing 0° so the cone (half-angle 45°) hits exactly one of them — assert the fixture geometry in a test. Every hit uses `Effects.Damage(...)`; misses are absent from `CastEvent`.

Per pull (delays are **from previous cast end**, castTime as above): opener `P = [Opener +2.0, Cone|ConeAlt +3.2]` (pulls 0,1 cast `Cone`, pull 2 casts `ConeAlt`), loop body `B = [Circle +6.0, Raidwide2 +4.5, Cone +5.0, Circle2 +7.0, Buster +4.0, Raidwide3 +5.5]`. Pull 0: `P B B B[0..3)` then boss dies; pull 1: `P B B B[0..1)` then wipe (`Combat-` without DIE+); pull 2: `P B B` then boss dies. Fixed timestamps, `Frame()` per op group as in `SyntheticDungeon`. `jitter: true` perturbs `Circle2`'s delay by `+3.0, −3.0, 0.0` s across its three occurrences in every pull (MAD = 3.0 > 1.5 → Timeout) and nothing else. Also provide `PullOptions`.

Expected: `Timeline.Steps` = 8 folded steps (`Opener`, `CastMulti[Cone, ConeAlt]`, then body 6), `Loop = (StartStep 2, Length 6, Repeats 2, TrailingPartial 3, EntryDelay 6.0)`, support `3/3` on every step except body steps pooled (`Circle` 8, `Raidwide2` 7, `Cone` 7, `Circle2` 6, `Buster` 6, `Raidwide3` 6), every `DelayMad ≤ 0.1`, `DelayMedian` within 0.1 s of planted values, `CastMulti` step `Review` mentions "branch". With jitter: `Circle2` step `Kind == Timeout`, `TimingMad ≈ 3.0`, `Review` mentions "MAD", others unchanged.

### 6.2 `Fixtures/SyntheticPhases.cs`

3 pulls; phase 1 vocabulary `[A 40201 ct 4, B 40202 ct 4]` × 2; boss HP drops to 50 % (`HpMp`) then `Targetable(false)` for 12 s, `Targetable(true)`, phase 2 vocabulary `[C 40203 ct 5, D 40204 ct 4]` × 2, death. Expected: `PhaseSplit.HpThresholds == [0.5]`, `Phases.Length == 2`; phase 0 `Kind HpThreshold, HpThreshold 0.5, 4 steps`; phase 1 `Kind Death` with 6 steps: the split time is the HP time, so the `TargetableOff` anchor (at or after it) lands in phase 1 as step 0 (`Targetable(false)`), `TargetableOn` as step 1 (`Targetable(true)`), then `C, D, C, D` — assert exactly this. Emitted: `SimplePhase(0, Phase0, "P0")…Raw.Update = … HPRatio <= 0.5f;` then `DeathPhase(1, Phase1)`; phase 0 has no tail state, phase 1 ends with the tail.

### 6.3 Unit tests

- `AnchorExtractorTests`: cast-time filter (0.4 s cast excluded, 0.5 s included), fallback when nothing qualifies, helper casts excluded, same-AID 0.3 s duplicates collapse, targetable span < 3 s dropped, ordering.
- `ProfileAlignerTests` (pure symbol/time arrays): identical sequences → all support 2; one insertion → gap column with support 1; single mismatch with close timing → same column (branch), far timing (> 6 s) → two columns; determinism (same input twice → identical columns; reversed pull order → identical consensus symbol string).
- `TimelineAlignerTests`: §6.1 expectations; loop folding edge cases on hand-built symbol strings: `ABCABC` → `(0,3,2,0)`, `XABCABCAB` → `(1,3,2,2)`, `ABAB AB` with `MinLoopRepeats 2` → `(0,2,3,0)`, `ABCD` → null, `AAAA` → `MinLoopLength` respected (null since L ≥ 2 needs `AA AA` → `(0,2,2,0)` — assert that); support < 60 % → Timeout with reason.
- `PhaseDetectorTests`: §6.2; a 30 s idle span at inconsistent HP (0.7/0.5/0.3) → no threshold; ratio 0.97 ignored; DIRU once per pull → cue with `Evidence`, no split.
- `EncounterModelBuilderTimelineTests`: `Timeline` present; ids `0x00000000, 0x00010000, …`; placement — component for `Circle`/`Circle2` (same shape → `SimpleAOEGroups` unit) is phase-level because its steps lie in the loop body; `Opener` raidwide unit is `Activate` on step 0 and `Deactivate` on step 0; a sub-0.5 s helper cast unit is phase-level; `Serialize(Deserialize(x)) == x`; `Validate` passes; pre-M4 golden JSON without `timeline` still deserialises with `Timeline == null`.
- `StatesPlannerTests`: offsets `0, 0x10000, …`; loop call offsets `FirstCallOffset + Stride*r`; trailing partial offsets continue; tail present only on loop/last phase; > 255 states truncation + review.
- `ModuleEmitterStatesTests` (hand-built EM): assert the exact §4.3 text for (a) zero steps → M2 `TrivialPhase` chain unchanged, (b) one `Cast` step with activate+deactivate, (c) `CastMulti` with review comment, (d) `Timeout` with review comment, (e) loop with 2 repeats + 1 trailing partial, (f) HpThreshold + Death phases. `Cast` args must be `AID.X` not `(uint)AID.X`; delays render `5.1f`, integers bare.
- `DryValidatorTests`: `SyntheticRotation` EM + its own recording → `Passed`, `Timing.P90 ≤ 0.2`, every `AoeReport` `Precision == Recall == 1.0`, `Coverage.Fraction == 1.0` (Buster is `Tankbuster` → `SingleTargetCast` unit); EM with `Circle` radius changed to 3 → that `AoeReport.Recall < 0.95`, `Passed == false`, `ApplyDowngrades` sets the ability `Confidence.Band == Low` and adds a `High` "shape" review; EM with an extra planted step nobody casts → `Missed` counted and, when `MaxMissedStepFraction` is exceeded, `Timing.Passed == false`; jitter recording validated against the **non-jitter** EM → p90 > 1.0 → fail (proves the metric bites); report JSON deterministic across two runs.
- `ValidateCommandTests`: exit 0 on pass with both report files written; exit 3 on the wrong-radius EM; exit 4 on missing EM / unparsable log / `--bossmod x`; `--write-em` writes a valid EM.
- Goldens: `SyntheticDungeon.vbm.cs` now `DeathPhase(0, Phase0)` + `Phase0` with exactly one `Cast(id, AID.Ability40001, 0, 5, "Ability40001").ActivateOnEnter<Ability40001>().DeactivateOnExit<Ability40001>();` + tail; `SyntheticShapes.vbm.cs` 3-step `Phase0`, `7.5f`; `SyntheticRotation.vbm.cs` shows `DeathPhase(0, Phase0)`, `CastMulti(`, `Rotation0(id + 0x20000, 6)` (integer delay renders bare), `Rotation0(id + 0x80000, 5.5f)`, tail; `SyntheticPhases.vbm.cs` shows `SimplePhase` + `.Raw.Update` + `DeathPhase(1, Phase1)`; `SyntheticRotation.validation.json` golden with `EmPath`/`Recordings` normalised to basenames. Regenerate via `FORGE_UPDATE_GOLDEN=1`, **inspect every diff**.
- Manual, recorded in `UPSTREAM.md`: all four `.vbm.cs` goldens compile inside `awgil/ffxiv_bossmod@1391bc42` under `BossMod/Modules/Forge/` (the one place a checkout is used in M4; if unavailable, say so in the status file — do not claim it).
- `scripts/build.sh` green; M0–M3 tests pass unchanged except the two regenerated goldens; determinism: two `infer`+`emit`+`validate` runs on the same log are byte-identical.

## 7. VERIFY table

| Item | Status | Blocks |
|---|---|---|
| `Cast`/`CastMulti`/`CastStart`/`CastEnd`/`Timeout`/`Targetable`/`SimpleState`/`ComponentCondition` signatures | **Confirmed** (§5) | `state*` templates |
| `Cast` uses `id` and `id + 1`; stride ≥ 2 legal | **Confirmed** | `StepStride` |
| `SimplePhase` base `seqID << 24`; `DeathPhase` exit lambda | **Confirmed** | §4.2 ids, `deathPhase` template |
| No loop API; tail idiom `SimpleState(id + 0xFF0000, 10, "???")` | **Confirmed** | loop unrolling, `stateTail` |
| HP-based phase via `SimplePhase(...).Raw.Update = …HPRatio <= x` | **Confirmed** | `simplePhase` + `phaseHpUpdate` templates |
| `Phase`/`State` `.ActivateOnEnter<>()`/`.DeactivateOnExit<>()` chainable, `.Raw` public | **Confirmed** | placement rendering |
| `AID` enum passed directly (generic `where AID : Enum`) | **Confirmed** | `{aid}` = `AID.X` |
| Float literals need `f` | **Confirmed** (C# language rule + upstream `5.7f`) | `Num` fix, golden regen |
| Real BossMod-build validation (ADR-006), `ReplayValidator` host, parser conformance in `validate` | **Deferred to M4b** | `--bossmod` stub only |
| `BossModule.ActivateComponent` replay of in-progress casts | Open, not blocking | none |

## 8. Definition of done

All §6 tests green (`scripts/build.sh`), goldens regenerated and reviewed, `docs/plans/m4-status.md` written (what shipped, what deviated, the `.vbm.cs` compile check result), `docs/UPSTREAM.md` §8.1 extended only with facts actually read, no `VERIFY` tag left in code paths this brief covers, no new dependency added to `BossModForge.Core`.
