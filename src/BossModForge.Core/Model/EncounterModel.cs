using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using BossModForge.Core.Inference;

namespace BossModForge.Core.Model;

public sealed record EncounterModel(
    int SchemaVersion,
    EncounterInfo Encounter,
    ImmutableArray<SourceInfo> Sources,
    ImmutableArray<PullSummary> Pulls,
    ImmutableArray<ParticipantModel> Participants,
    ImmutableArray<AbilityModel> Abilities,
    ImmutableArray<StatusModel> Statuses,
    ImmutableArray<TetherModel> Tethers,
    ImmutableArray<IconModel> Icons,
    EnumNames Enums,
    ArenaModel Arena,
    ImmutableArray<ReviewItem> Review,
    Overrides Overrides,
    TimelineModel? Timeline = null);

public sealed record TimelineModel(
    ImmutableArray<PhaseModel> Phases,
    ImmutableArray<TimelineStep> Steps,
    LoopModel? Loop,
    int PullCount,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence);

public sealed record PhaseModel(
    int Index,
    string Name,
    string Kind,
    double? HpThreshold,
    int FirstStep,
    int StepCount,
    ImmutableArray<string> Activate);

public sealed record TimelineStep(
    string Id,
    string Kind,
    uint? Aid,
    float Delay,
    float CastTime,
    float TimingMad,
    int Support,
    ImmutableArray<uint> Of,
    string Name,
    ImmutableArray<string> Activate,
    ImmutableArray<string> Deactivate,
    string? Review,
    bool? Targetable);

public sealed record LoopModel(int StartStep, int Length, int Repeats, int TrailingPartial, float EntryDelay);

public sealed record EncounterInfo(
    ushort Zone,
    ushort CfcId,
    string? DutyName,
    uint PrimaryOid,
    string BossName,
    uint BossNameId,
    string ModuleName,
    string Namespace);

public sealed record SourceInfo(string Path, string Sha256, int Version, string GameVersion, DateTime Start, DateTime End);

public sealed record PullSummary(
    int Index,
    string Source,
    DateTime Start,
    DateTime End,
    string Outcome,
    double DurationSeconds,
    Confidence Confidence);

public sealed record ParticipantModel(
    uint Oid,
    string EnumName,
    string Name,
    uint NameId,
    string Type,
    string Role,
    float HitboxRadius,
    int InstanceCount,
    bool SpawnsDuringFight,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence,
    string? Comment = null);

public sealed record AbilityModel(
    uint SourceOid,
    uint ActionId,
    string ActionType,
    string EnumName,
    string Name,
    bool HasCastBar,
    float CastTime,
    bool Interruptible,
    int InstanceCount,
    MechanicClass Class,
    Shape? Shape,
    ShapeFitSummary? ShapeFit,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence,
    string? Comment = null);

public sealed record ShapeFitSummary(double Score, double FitConsistency, float MinMargin, float LagSeconds, int Instances);

public sealed record StatusModel(uint StatusId, string EnumName, string Name, int Applications);

public sealed record TetherModel(uint TetherId, string EnumName, int Count);

public sealed record IconModel(uint IconId, string EnumName, int Count);

public sealed record EnumNames(
    ImmutableSortedDictionary<uint, string> Oid,
    ImmutableSortedDictionary<uint, string> Aid,
    ImmutableSortedDictionary<uint, string> Sid,
    ImmutableSortedDictionary<uint, string> TetherId,
    ImmutableSortedDictionary<uint, string> IconId);

public sealed record ArenaModel(
    string Kind,
    float CenterX,
    float CenterZ,
    float Radius,
    float HalfWidth,
    float HalfHeight,
    ImmutableArray<PointModel> Vertices,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence);

public sealed record PointModel(float X, float Z);

public sealed record Overrides(ImmutableSortedDictionary<string, JsonElement> Values)
{
    public static readonly Overrides Empty = new(
        ImmutableSortedDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal));

    [JsonIgnore]
    public bool IsEmpty => Values.Count == 0;

    public Overrides With(string key, JsonElement value)
        => new(Ordinal(Values).SetItem(key, value.Clone()));

    public Overrides Without(string key)
        => new(Ordinal(Values).Remove(key));

    private static ImmutableSortedDictionary<string, JsonElement> Ordinal(
        ImmutableSortedDictionary<string, JsonElement> values)
        => ReferenceEquals(values.KeyComparer, StringComparer.Ordinal)
            ? values
            : values.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}

public sealed record BuildOptions(
    string? ModuleName = null,
    string? Namespace = null,
    ShapeInferenceOptions? Shape = null,
    BoundsFitterOptions? Bounds = null);
