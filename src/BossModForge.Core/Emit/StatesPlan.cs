using System.Collections.Immutable;

namespace BossModForge.Core.Emit;

public sealed record StepPlan(
    uint Offset,
    string Kind,
    string? AidMember,
    ImmutableArray<string> OfMembers,
    bool? Targetable,
    float Delay,
    float CastTime,
    float Duration,
    string Name,
    ImmutableArray<string> Activate,
    ImmutableArray<string> Deactivate,
    string? ReviewComment);

public sealed record LoopPlan(
    string MethodName,
    ImmutableArray<StepPlan> Body,
    int Repeats,
    int TrailingPartial,
    float EntryDelay,
    uint FirstCallOffset,
    uint Stride);

public sealed record PhasePlan(
    int SeqId,
    string MethodName,
    string Kind,
    double? HpThreshold,
    string DisplayName,
    ImmutableArray<string> Activate,
    ImmutableArray<StepPlan> Prefix,
    LoopPlan? Loop,
    ImmutableArray<StepPlan> Tail);

public sealed record StatesPlan(
    string ModuleName,
    ImmutableArray<PhasePlan> Phases,
    ImmutableArray<string> TrivialActivate);

public sealed record ComponentUnit(string ClassName, string Kind, ImmutableArray<uint> Aids);
