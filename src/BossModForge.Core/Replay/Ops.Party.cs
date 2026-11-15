namespace BossModForge.Core.Replay;

public sealed record PartyModifyOp(DateTime Timestamp, int Slot, ulong ContentId, ulong InstanceId, bool InCutscene, string Name) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Par;
}

public sealed record PartyLimitBreakOp(DateTime Timestamp, int Current, int Max) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Lb;
}
