using System.Collections.Immutable;

namespace BossModForge.Core.Replay;

public abstract record Op(DateTime Timestamp)
{
    public virtual Tag Tag { get; init; }
}

public sealed record RawOp(DateTime Timestamp, Tag RawTag, ImmutableArray<string> Fields) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = RawTag;
}

public sealed record OpaqueOp(DateTime Timestamp, Tag OpaqueTag) : Op(Timestamp)
{
    public override Tag Tag { get; init; } = OpaqueTag;
}
