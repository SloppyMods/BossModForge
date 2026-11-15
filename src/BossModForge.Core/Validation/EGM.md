# Validation

## Overview
Dry-validates an `EncounterModel` against recordings. Each recording is segmented into pulls for the model's primary OID, then three checks run: timeline timing (`TimelineSimulator` walks the model's timeline against observed primary casts), AOE shape precision/recall (`AoeValidator`), and component coverage of damaging casts (`CoverageValidator`). Results are aggregated into a `ValidationReport`, which can be rendered as deterministic JSON/Markdown or used to downgrade failing abilities.

## Interface
- `ValidationOptions(TimingP90MaxSeconds=1.0, TimingMatchWindowSeconds=10.0, MaxMissedStepFraction=0.4, AoeMinPrecision=0.95, AoeMinRecall=0.95, Segmenter, GameData)` plus `ValidationOptions.Default` [Validation/ValidationReport.cs:ValidationOptions]
- Report records: `StepTiming`, `TimingReport`, `AoeReport`, `CoverageReport`, `PullValidation`, `RecordingValidation`, `ValidationThresholds`, `ValidationReport` [Validation/ValidationReport.cs]
- `ValidationReportWriter.ToJson(ValidationReport)`, `ToMarkdown(ValidationReport)` [Validation/ValidationReport.cs:ValidationReportWriter]
- `RecordingContext(Recording, Pulls, Catalog)` [Validation/Validator.cs:RecordingContext]
- `Validator.Validate(EncounterModel, IReadOnlyList<Recording>, ValidationOptions? = null)` -> `ValidationReport`; `Validator.ApplyDowngrades(EncounterModel, ValidationReport)` -> `EncounterModel` [Validation/Validator.cs:Validator]
- `DryValidator.Validate(EncounterModel, IReadOnlyList<(string Path, Recording Recording)>, ValidationOptions?)`, `ToJson`, `ToMarkdown`, `ApplyDowngrades` (wrappers) [Validation/Validator.cs:DryValidator]
- `TimelineSimulator.Walk(EncounterModel, Recording, Pull, IReadOnlyList<PrimaryCast>, ValidationOptions)` -> `SimulatedPull`; `TimelineSimulator.CastLookbackSeconds = 0.5`; records `PrimaryCast`, `SimulatedVisit`, `SimulatedPull` [Validation/TimelineSimulator.cs:TimelineSimulator]
- `AoeValidator.Evaluate(EncounterModel, IReadOnlyList<RecordingContext>, ValidationOptions)` -> `ImmutableArray<AoeReport>`; `AoeValidator.ShapeReview(AoeReport)` -> `ReviewItem` [Validation/AoeValidator.cs:AoeValidator]
- `CoverageValidator.Evaluate(EncounterModel, IReadOnlyList<RecordingContext>)` -> `CoverageReport`; `CoverageValidator.CoverageReview(CoverageReport)` -> `ReviewItem?` [Validation/CoverageValidator.cs:CoverageValidator]
- `Validator.Percentile` is `internal` [Validation/Validator.cs:Validator.Percentile]
- Subdirectories: None

## Invariants
- `ValidationReport.Passed` = `timing.Passed && all AoeReport.Passed`; coverage does not affect `Passed` [Validation/Validator.cs:Validator.Validate]
- `Validator.Validate` returns `EmPath` as `""`; `DryValidator.Validate` overwrites `Recordings` with the supplied paths [Validation/Validator.cs:Validator.Validate, DryValidator.Validate]
- Only pulls whose `PrimaryOid == em.Encounter.PrimaryOid` are validated; options default to `ValidationOptions.Default`, `SegmenterOptions.Default` and `NullGameData.Instance` [Validation/Validator.cs:Validator.Validate]
- Timing `Passed` = `P90 <= TimingP90MaxSeconds && missedFraction <= MaxMissedStepFraction`; `P90` = max(overall sample P90, largest per-step P90); missed fraction = `missed / (samples + missed)`, 0 when the denominator is 0 [Validation/Validator.cs:Validator.BuildTiming]
- `Percentile` returns 0 for an empty list and uses linear interpolation on a sorted list [Validation/Validator.cs:Validator.Percentile]
- Timing steps are grouped and ordered by `StepId` with ordinal comparison [Validation/Validator.cs:Validator.BuildTiming]
- Walk returns no visits when the timeline is null or has no steps; when `Phases` is empty it synthesizes one phase covering all steps [Validation/TimelineSimulator.cs:TimelineSimulator.Walk]
- Phase 0 starts at `pull.Start`; a phase with `HpThreshold` starts at the first HP sample at or below the threshold within the pull, else the whole phase is recorded as expected-but-missed (Timeout steps skipped); a non-zero phase without threshold starts at `pull.Start` [Validation/TimelineSimulator.cs:Walker.TryPhaseStart, TryHpTime, MissWholePhase]
- Each `PrimaryCast` is matched at most once (`_consumed`). A match requires the AID to match (`step.Of` contains it; `step.Aid` is compared only when `step.Of` is empty), start >= previous time − 0.5 s, and |start − expected| <= `TimingMatchWindowSeconds`; the earliest-starting candidate wins [Validation/TimelineSimulator.cs:Walker.FindCast, AidMatches]
- Step kind `Targetable` is matched against the primary actor's `Targetable` samples (`sample.Value == step.Targetable`) with the same 0.5 s lookback and `TimingMatchWindowSeconds`; its visit has `Aid = null`, the first match wins, and it never consumes a `PrimaryCast` [Validation/TimelineSimulator.cs:Walker.VisitTargetable, FindTargetable]
- Time advance: a matched cast sets the previous time to `CastEnd ?? CastStart + step.CastTime`; an unmatched `expected: true` step advances by `Delay + CastTime` from the previous time; an unmatched `expected: false` step does not advance [Validation/TimelineSimulator.cs:Walker.VisitCast]
- The cast pool is only casts with `PullIndex == pull.Index`, `SourceInstanceId == pull.PrimaryInstanceId` and non-null `CastStart` [Validation/Validator.cs:Validator.CollectPrimaryCasts]
- Loop handling: pre-loop steps, `Repeats` full loop bodies, `TrailingPartial` steps, then up to 32 extra body iterations recorded with `expected: false` while the first body step still has a matching cast, then post-loop steps [Validation/TimelineSimulator.cs:Walker.WalkPhase]
- Timeout steps produce no `SimulatedVisit`; they only advance time [Validation/TimelineSimulator.cs:Walker.Visit, AdvanceTimeout]
- AOE evaluation covers only abilities with a shape of kind Circle, Donut, Cone, Rect or Cross, ordered by `ActionId`; precision and recall default to 1.0 when their denominators are 0; passes when both meet `AoeMinPrecision`/`AoeMinRecall` [Validation/AoeValidator.cs:AoeValidator.CountedKinds, EvaluateAbility]
- AOE: `AoeReport.Component` is the `ComponentPlanner.Plan` unit containing the AID (fallback `ability.EnumName`); observations use `ability.ShapeFit?.LagSeconds ?? 0`; origin and direction are overridden per observation via `AoeFitters.OriginOf`/`DirectionOf` [Validation/AoeValidator.cs:AoeValidator.EvaluateAbility]
- Coverage: an ability is covered when its `ActionId` is in some `ComponentPlanner.Plan(em)` unit's `Aids`; the rest are listed in `UncoveredAids`, sorted [Validation/CoverageValidator.cs:CoverageValidator.Evaluate]
- `ValidationReport.Failures` holds `"timing p90 X missed M/N"` (when timing fails) and `"aoe <Component> AID <aid> P=.. R=.."` (per failed AOE); Markdown renders a "Failures" section [Validation/Validator.cs:Validator.Validate, Validation/ValidationReport.cs:ValidationReportWriter]
- `ValidationReport.PerRecording` and `Thresholds` are populated; each `PullValidation` carries `TimingDeltas`, `ObservedSteps`, `MissedSteps` [Validation/Validator.cs:Validator.Validate]
- Coverage counts only abilities whose `SourceOid` is a participant and that have at least one player-damage hit in a recording instance (out-of-range `PullIndex` instances are skipped); `Fraction` is 1.0 when there are none; `CoverageReview` returns `null` when `Fraction >= 1` [Validation/CoverageValidator.cs:CoverageValidator.Evaluate, HasDamagingPlayerHit, CoverageReview]
- `ApplyDowngrades` sets `Confidence.LowBecause("dry-validate shape precision/recall")` on abilities whose AID failed AOE, and appends `report.SuggestedReview` to `em.Review`; the input model is not mutated [Validation/Validator.cs:Validator.ApplyDowngrades]
- Review items: AOE failure -> id `"shape-" + AID as X8`, severity High; coverage -> id `"coverage"`, severity Medium [Validation/AoeValidator.cs:AoeValidator.ShapeReview, Validation/CoverageValidator.cs:CoverageValidator.CoverageReview]
- JSON output: camelCase, nulls omitted, indented, string enums (integers disallowed), `DateTime` as round-trip `"O"`, object keys recursively sorted ordinally, LF newlines, trailing newline. Markdown uses invariant culture, 3-decimal formatting, LF newlines [Validation/ValidationReport.cs:ValidationReportWriter]
- `ToJson` throws `InvalidOperationException` if the report does not serialize to a JSON object [Validation/ValidationReport.cs:ValidationReportWriter.ToJson]
- `Validator.Validate` does not null-check `em` or `recordings` [Validation/Validator.cs:Validator.Validate]

## Side Effects
- None to disk, network or static state; validation is pure over its inputs. The only statics are the readonly `ValidationOptions.Default`, `AoeValidator.CountedKinds` and the cached `JsonSerializerOptions` [Validation/ValidationReport.cs:ValidationOptions.Default, ValidationReportWriter.SerializerOptions, Validation/AoeValidator.cs:AoeValidator.CountedKinds]
- Re-segments and re-catalogs each recording via `PullSegmenter.Segment`, `ParticipantClassifier.Classify` and `AbilityCataloger.Catalog` (cost, no persisted state) [Validation/Validator.cs:Validator.Validate]

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from repo root)
