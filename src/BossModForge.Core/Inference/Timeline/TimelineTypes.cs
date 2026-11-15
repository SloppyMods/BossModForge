using System.Collections.Immutable;

namespace BossModForge.Core.Inference.Timeline;

public sealed record TimelineAlignerOptions(
    float AnchorMinCastTime = 0.5f,
    float MultiCastWindowSeconds = 0.5f,
    float MinUntargetableSeconds = 3f,
    double Match = 5,
    double Mismatch = -4,
    double GapOpen = -4,
    double GapExtend = -1,
    double TimingBonusMax = 1.0,
    double TimingBonusScaleSeconds = 2.0,
    float MadTimeoutSeconds = 1.5f,
    double MinSupportFraction = 0.6,
    int MinLoopLength = 2,
    int MinLoopRepeats = 2,
    double HpThresholdTolerance = 0.03,
    float CastingStopSeconds = 8f,
    double VocabularyJaccardMax = 0.5)
{
    public static readonly TimelineAlignerOptions Default = new();
}

public enum StepKind
{
    Cast,
    CastMulti,
    Timeout,
    Targetable,
    ComponentCondition,
}

public sealed record ConsensusStep(
    int Index,
    StepKind Kind,
    uint Aid,
    ImmutableArray<uint> Of,
    bool? Targetable,
    float DelayMedian,
    float DelayMad,
    float CastTime,
    int Support,
    int PullCount,
    ImmutableArray<AnchorEvent> Members,
    string? ReviewReason);

public sealed record Loop(int StartStep, int Length, int Repeats, int TrailingPartial, float EntryDelay);

public sealed record Timeline(
    ImmutableArray<ConsensusStep> Steps,
    Loop? Loop,
    int PullCount,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence);
