using System.Collections.Immutable;
using System.Numerics;

namespace BossModForge.Core.Replay;

public sealed record FrameStartOp(DateTime Timestamp, FrameInfo Frame, TimeSpan PrevUpdateTime, ulong GaugeLow, ulong GaugeHigh, Angle CameraAzimuth) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Fram;
}

public sealed record ZoneChangeOp(DateTime Timestamp, ushort Zone, ushort CfcId) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Zone;
}

public sealed record DirectorUpdateOp(DateTime Timestamp, uint DirectorId, uint UpdateId, uint Param1, uint Param2, uint Param3, uint Param4) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Diru;
}

public sealed record EnvControlOp(DateTime Timestamp, byte Index, uint State) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Envc;
}

public sealed record RsvDataOp(DateTime Timestamp, string Key, string Value) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Rsv;
}

public sealed record UserMarkerOp(DateTime Timestamp, string Text) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Umrk;
}

public sealed record SystemLogOp(DateTime Timestamp, uint MessageId, ImmutableArray<int> Args) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = global::BossModForge.Core.Replay.Tag.Slog;
}

public sealed record WaymarkOp(DateTime Timestamp, byte Waymark, Vector3? Position) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = Position is null
        ? global::BossModForge.Core.Replay.Tag.WayMinus
        : global::BossModForge.Core.Replay.Tag.WayPlus;
}
