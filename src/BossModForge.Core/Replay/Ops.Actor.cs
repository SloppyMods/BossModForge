using System.Collections.Immutable;
using System.Numerics;

namespace BossModForge.Core.Replay;

public sealed record ActorCreateOp(
    DateTime Timestamp,
    ulong InstanceId,
    uint Oid,
    int SpawnIndex,
    string Name,
    uint NameId,
    ActorType Type,
    Class Class,
    int Level,
    PosRot PosRot,
    float HitboxRadius,
    HpMp HpMp,
    bool IsTargetable,
    bool IsAlly,
    ulong OwnerId,
    uint FateId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.ActPlus;

    /// <summary>Layout instance id; written from v26 (both lineages), 0 before.</summary>
    public uint LayoutId { get; init; }

    /// <summary>bmr-only render flags; written by bmr from v27, 0 otherwise.</summary>
    public int Renderflags { get; init; }
}

public sealed record ActorDestroyOp(DateTime Timestamp, ulong InstanceId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.ActMinus;
}

public sealed record ActorRenameOp(DateTime Timestamp, ulong InstanceId, string Name, uint NameId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Name;
}

public sealed record ActorClassChangeOp(DateTime Timestamp, ulong InstanceId, Class Class, int Level) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Clsr;
}

public sealed record ActorMoveOp(DateTime Timestamp, ulong InstanceId, PosRot PosRot) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Move;
}

public sealed record ActorSizeChangeOp(DateTime Timestamp, ulong InstanceId, float HitboxRadius) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Acsz;
}

public sealed record ActorHpMpOp(DateTime Timestamp, ulong InstanceId, HpMp HpMp) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Hp;
}

public sealed record ActorTargetableOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = Value
        ? global::BossModForge.Core.Replay.Tag.AtgPlus
        : global::BossModForge.Core.Replay.Tag.AtgMinus;
}

public sealed record ActorAllyOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Ally;
}

public sealed record ActorDeadOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = Value
        ? global::BossModForge.Core.Replay.Tag.DiePlus
        : global::BossModForge.Core.Replay.Tag.DieMinus;
}

public sealed record ActorCombatOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = Value
        ? global::BossModForge.Core.Replay.Tag.ComPlus
        : global::BossModForge.Core.Replay.Tag.ComMinus;
}

public sealed record ActorAggroPlayerOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Nenp;
}

public sealed record ActorModelStateOp(DateTime Timestamp, ulong InstanceId, byte ModelState, byte AnimState1, byte AnimState2) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Mdls;
}

public sealed record ActorEventStateOp(DateTime Timestamp, ulong InstanceId, byte State) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Evts;
}

public sealed record ActorTargetOp(DateTime Timestamp, ulong InstanceId, ulong TargetId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Targ;
}

public sealed record ActorMountOp(DateTime Timestamp, ulong InstanceId, uint MountId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Mntd;
}

public sealed record ActorForayOp(DateTime Timestamp, ulong InstanceId, byte Level, byte Element) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Fora;
}

public sealed record ActorTetherOp(DateTime Timestamp, ulong InstanceId, uint TetherId, ulong TargetId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = TetherId == 0 && TargetId == 0
        ? global::BossModForge.Core.Replay.Tag.TetMinus
        : global::BossModForge.Core.Replay.Tag.Teth;
}

public sealed record ActorCastStartOp(
    DateTime Timestamp,
    ulong InstanceId,
    ActionId Action,
    ulong TargetId,
    Vector3 Location,
    float ElapsedTime,
    float TotalTime,
    bool Interruptible,
    Angle Rotation) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.CstPlus;
}

public sealed record ActorCastStopOp(DateTime Timestamp, ulong InstanceId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.CstMinus;
}

public sealed record ActorCastEventOp(
    DateTime Timestamp,
    ulong InstanceId,
    ActionId Action,
    ulong MainTargetId,
    float AnimationLock,
    uint MaxTargets,
    Vector3 TargetPos,
    uint GlobalSequence,
    uint SourceSequence,
    Angle Rotation,
    ImmutableArray<CastTarget> Targets) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.CstBang;
}

public sealed record ActorEffectResultOp(DateTime Timestamp, ulong InstanceId, uint GlobalSequence, int TargetIndex) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Er;
}

public sealed record ActorStatusOp(DateTime Timestamp, ulong InstanceId, int Index, ActorStatus? Status, bool IsUpdate) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = Status is null
        ? global::BossModForge.Core.Replay.Tag.StaMinus
        : IsUpdate
            ? global::BossModForge.Core.Replay.Tag.StaBang
            : global::BossModForge.Core.Replay.Tag.StaPlus;
}

public sealed record ActorIncomingEffectOp(
    DateTime Timestamp,
    ulong InstanceId,
    int Index,
    uint GlobalSequence,
    int TargetIndex,
    ulong SourceId,
    ActionId Action,
    ActionEffects Effects,
    bool Added) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = Added
        ? global::BossModForge.Core.Replay.Tag.AiePlus
        : global::BossModForge.Core.Replay.Tag.AieMinus;
}

public sealed record ActorIconOp(DateTime Timestamp, ulong InstanceId, uint IconId, ulong TargetId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Icon;
}

public sealed record EventObjectStateOp(DateTime Timestamp, ulong InstanceId, ushort State) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Esta;
}

public sealed record EventObjectAnimationOp(DateTime Timestamp, ulong InstanceId, ushort Param1, ushort Param2) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Eanm;
}

public sealed record PlayActionTimelineOp(DateTime Timestamp, ulong InstanceId, ushort ActionTimelineId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Pate;
}

public sealed record NpcYellOp(DateTime Timestamp, ulong InstanceId, ushort YellId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Nyel;
}

public sealed record OpenTreasureOp(DateTime Timestamp, ulong InstanceId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Opnt;
}
