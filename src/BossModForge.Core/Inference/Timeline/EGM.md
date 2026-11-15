# Timeline

## Overview
Infers a boss ability timeline from multiple pulls. `AnchorExtractor` turns each pull into ordered anchor events (casts, targetable on/off); `TimelineAligner` aligns those per-pull sequences with an affine-gap profile alignment (`ProfileAligner`), builds consensus steps (`ConsensusBuilder`), and folds repeated tails into a `Loop` (`LoopFolder`). `PhaseDetector` separately finds phase-split cues (HP thresholds, primary death, director updates) and can partition anchors by phase. Namespace is `BossModForge.Core.Inference.Timeline`; inputs `Pull`, `AbilityCatalog`, `Confidence`, `Evidence` come from the parent `Inference` namespace.

## Interface
- `TimelineAlignerOptions` record with defaults (`AnchorMinCastTime=0.5`, `MultiCastWindowSeconds=0.5`, `MinUntargetableSeconds=3`, `Match=5`, `Mismatch=-4`, `GapOpen=-4`, `GapExtend=-1`, `TimingBonusMax=1`, `TimingBonusScaleSeconds=2`, `MadTimeoutSeconds=1.5`, `MinSupportFraction=0.6`, `MinLoopLength=2`, `MinLoopRepeats=2`, `HpThresholdTolerance=0.03`, `CastingStopSeconds=8`, `VocabularyJaccardMax=0.5`) and static `Default` [TimelineTypes.cs:TimelineAlignerOptions]
- `StepKind` enum: `Cast, CastMulti, Timeout, Targetable, ComponentCondition` [TimelineTypes.cs:StepKind]
- `ConsensusStep`, `Loop(StartStep, Length, Repeats, TrailingPartial, EntryDelay)`, `Timeline(Steps, Loop, PullCount, Confidence, Evidence)` records [TimelineTypes.cs:ConsensusStep, TimelineTypes.cs:Loop, TimelineTypes.cs:Timeline]
- `TimelineAligner.Align(anchorsPerPull, pulls, castTimeByAid, options?) -> Timeline` [TimelineAligner.cs:TimelineAligner.Align]
- `AnchorKind` enum (`Cast, TargetableOff, TargetableOn`); `AnchorEvent` record [Anchors.cs:AnchorKind, Anchors.cs:AnchorEvent]
- `AnchorExtractor.Extract(Recording, pulls, AbilityCatalog, options?) -> ImmutableArray<ImmutableArray<AnchorEvent>>` [Anchors.cs:AnchorExtractor.Extract]
- `AlignmentColumn` record with computed `Kind`, `ConsensusAid`, `Support` [Alignment.cs:AlignmentColumn]
- `ProfileAligner.AlignInto(profile, sequence, options)` and overload with `(sequenceStart, pullStarts)` [Alignment.cs:ProfileAligner.AlignInto]
- `PhaseCueKind` enum (`HpThresholdCastingStop, PrimaryDeath, DirectorOncePerPull`); `PhaseCue`, `PhaseSplit` records [PhaseDetector.cs:PhaseCueKind, PhaseDetector.cs:PhaseCue, PhaseDetector.cs:PhaseSplit]
- `PhaseDetector.Detect(Recording, pulls, anchorsPerPull, options?) -> PhaseSplit` [PhaseDetector.cs:PhaseDetector.Detect]
- `PhaseDetector.SplitByPhase(Recording, pulls, anchorsPerPull, PhaseSplit) -> ImmutableArray<ImmutableArray<ImmutableArray<AnchorEvent>>>` (indexed `[phase][pull]`) [PhaseDetector.cs:PhaseDetector.SplitByPhase]
- Internal: `ConsensusBuilder.Build`, `ConsensusBuilder.MemberDelays` [ConsensusBuilder.cs:ConsensusBuilder]; `LoopFolder.Fold` [ConsensusBuilder.cs:LoopFolder.Fold]; `TimelineStats.Median/Round1` [Alignment.cs:TimelineStats]; `AnchorExtractor.Compare`; `PhaseDetector.HpRatioAt`
- Subdirectories: None

## Invariants
- `Align`, `Extract`, `Detect`, `SplitByPhase` normalize default (uninitialized) `ImmutableArray` inputs to empty [TimelineAligner.cs:TimelineAligner.Align, Anchors.cs:AnchorExtractor.Extract, PhaseDetector.cs:PhaseDetector.Detect, PhaseDetector.cs:PhaseDetector.SplitByPhase]
- Sequences are aligned in descending anchor-count order (ties by pull index); a sequence's pull index is taken from its first anchor when non-empty [TimelineAligner.cs:TimelineAligner.Align]
- Timeline confidence = `Min(1, meanSupport) * (1 - timeoutFraction)` [TimelineAligner.cs:TimelineAligner.Align]
- Cast anchors are only taken from instances where `PullIndex == pull.Index`, `SourceInstanceId == pull.PrimaryInstanceId` and `CastStart` non-null; casts with `CastTime >= AnchorMinCastTime` are preferred, all are used only if none qualify; same-Aid casts within `MultiCastWindowSeconds` collapse to the first [Anchors.cs:AnchorExtractor.ExtractCasts, Anchors.cs:AnchorExtractor.CollapseMultiCast]
- Targetable off→on pairs shorter than `MinUntargetableSeconds` are dropped; transitions outside `[pull.Start, pull.End]` and samples at `actor.Created` are ignored [Anchors.cs:AnchorExtractor.ExtractTargetable]
- Anchor ordering is by `T`, then `Kind`, then `Aid` [Anchors.cs:AnchorExtractor.Compare]
- Cast-vs-targetable pairing scores `-1000*Scale`; non-matching pairs whose timing gaps differ by `>= 3*TimingBonusScaleSeconds` also score `-1000*Scale`; scores are integers scaled by 1000 [Alignment.cs:ProfileAligner.PairScore]
- `AlignmentColumn.ConsensusAid` is the most frequent Cast Aid, smallest Aid on ties, 0 if no casts [Alignment.cs:AlignmentColumn.ConsensusAid]
- Branch expansion: when ≥2 Aids in a column and any has fewer than `minBranch` members (`2` if `pullCount >= 4`, else `1`), low-count casts are split into single-member columns [ConsensusBuilder.cs:ConsensusBuilder.ExpandBranches]
- Cast/CastMulti steps become `Timeout` when `delayMad > MadTimeoutSeconds` or members `< MinSupportFraction * pullCount` [ConsensusBuilder.cs:ConsensusBuilder.Build]. The MAD `ReviewReason` text hard-codes "> 1.5s" regardless of `MadTimeoutSeconds` [ConsensusBuilder.cs:ConsensusBuilder.Build]
- Loop folding requires `n >= MinLoopLength * MinLoopRepeats` and `repeats >= MinLoopRepeats`; best loop maximizes coverage (`k*length+p`), then longer length, then smaller trailing partial; folded step indices are renumbered [ConsensusBuilder.cs:LoopFolder.Fold]
- HP threshold cues: HP ratio rounded to 0.05, excluded if `>= 0.95` or `<= 0.05` or differing from rounded by more than `HpThresholdTolerance`; kept only if supported by `>= MinSupportFraction * pulls.Length` pulls and mean before/after cast-vocabulary Jaccard `<= VocabularyJaccardMax` [PhaseDetector.cs:PhaseDetector.DetectHpThresholds, PhaseDetector.cs:PhaseDetector.VocabularyOk]
- HP threshold candidates come from two sources per pull: a casting stop (gap between a cast's end and the next cast's start `>= CastingStopSeconds`, taken at the earlier cast's end) and every `TargetableOff` anchor. A pull votes at most once per rounded threshold; accepted thresholds are emitted in descending order [PhaseDetector.cs:PhaseDetector.DetectHpThresholds]
- `PrimaryDeath` cue: per pull, the first `ActorDeadOp` with `Value == true` for `PrimaryInstanceId` inside `[pull.Start, pull.End]`; emitted only if at least one pull has one, with confidence `occ.Count / Max(1, pulls.Length)` [PhaseDetector.cs:PhaseDetector.DetectPrimaryDeath]
- Primary actor lookup: `ActorAt(PrimaryInstanceId, pull.Start)`, falling back to the last actor with that instance id (`Anchors` also requires `Created <= pull.End`); with no actor there are no targetable anchors. The sample at `actor.Created` sets the initial state and is not a transition. Only adjacent off→on pairs shorter than the minimum are dropped [Anchors.cs:AnchorExtractor.ExtractTargetable, PhaseDetector.cs:PhaseDetector.DetectHpThresholds]
- `Align` throws (duplicate key from `ToDictionary`) when two pulls share a `Pull.Index` [TimelineAligner.cs:TimelineAligner.Align]
- `HpRatioAt` returns 1 when there are no HP samples or `MaxHp == 0` [PhaseDetector.cs:PhaseDetector.HpRatioAt]
- Director cues are `DirectorUpdateOp` keys `(DirectorId, UpdateId, Param1)` occurring exactly once in every pull [PhaseDetector.cs:PhaseDetector.DetectDirector]

## Side Effects
- None for I/O, network, or static mutable state; all functions are pure over their inputs (`Recording` is only read) [Anchors.cs:AnchorExtractor, TimelineAligner.cs:TimelineAligner, PhaseDetector.cs:PhaseDetector]
- `TimelineStats.Median` sorts the passed `List<double>`/`List<float>` in place [Alignment.cs:TimelineStats.Median]
- Allocates O(n·m) DP matrices per `AlignInto` call [Alignment.cs:ProfileAligner.AlignInto]
- Emits `Evidence` records (`Alignment`, `Loop`, `HpThreshold`, `PrimaryDeath`, `DirectorOncePerPull`) with JSON data in `Alignment` evidence [TimelineAligner.cs:TimelineAligner.ColumnEvidence, PhaseDetector.cs:PhaseDetector.Detect]

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from repo root)
