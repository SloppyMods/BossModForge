using System.Linq;
using System.Numerics;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Replay;

public sealed class EncodingParityTests
{
    [Fact]
    public void SyntheticDungeon_TextRawAndBrotli_AreEquivalent()
    {
        var builder = SyntheticDungeon.Build();
        using var textStream = builder.Open(ReplayEncoding.Text);
        using var rawStream = builder.Open(ReplayEncoding.Binary);
        using var brotliStream = builder.Open(ReplayEncoding.BinaryBrotli);

        var text = ReplayParser.Parse(textStream, "synthetic.txt");
        var raw = ReplayParser.Parse(rawStream, "synthetic.blog");
        var brotli = ReplayParser.Parse(brotliStream, "synthetic.blcb");

        Assert.Equal(ReplayEncoding.Text, text.Meta.Encoding);
        Assert.Equal(ReplayEncoding.Binary, raw.Meta.Encoding);
        Assert.Equal(ReplayEncoding.BinaryBrotli, brotli.Meta.Encoding);
        Assert.Equal(24, text.Meta.Version);
        Assert.Equal(24, raw.Meta.Version);

        var textOps = text.Ops.Where(op => op is not RawOp).ToArray();
        Assert.Equal(textOps.Length, raw.Ops.Count);
        Assert.Equal(textOps.Length, brotli.Ops.Count);
        OpEquivalence.AssertEqual(textOps, raw.Ops);
        OpEquivalence.AssertEqual(raw.Ops, brotli.Ops);
    }
}

internal static class OpEquivalence
{
    public static void AssertEqual(IReadOnlyList<Op> left, IReadOnlyList<Op> right)
    {
        Assert.Equal(left.Count, right.Count);
        for (var i = 0; i < left.Count; i++)
            AssertEqual(left[i], right[i], i);
    }

    public static void AssertEqual(Op left, Op right, int index)
    {
        Assert.True(left.GetType() == right.GetType(), $"[{index}] type {left.GetType().Name} vs {right.GetType().Name}");
        Assert.Equal(left.Tag, right.Tag);
        Assert.Equal(left.Timestamp.Ticks, right.Timestamp.Ticks);
        switch (left, right)
        {
            case (FrameStartOp a, FrameStartOp b):
                Assert.Equal(a.Frame.Qpc, b.Frame.Qpc);
                Assert.Equal(a.Frame.Index, b.Frame.Index);
                AssertFloat(a.Frame.DurationRaw, b.Frame.DurationRaw);
                AssertFloat(a.Frame.Duration, b.Frame.Duration);
                AssertFloat(a.Frame.TickSpeedMultiplier, b.Frame.TickSpeedMultiplier);
                Assert.Equal(a.GaugeLow, b.GaugeLow);
                Assert.Equal(a.GaugeHigh, b.GaugeHigh);
                Assert.True(Math.Abs(a.PrevUpdateTime.TotalMilliseconds - b.PrevUpdateTime.TotalMilliseconds) < 5e-4);
                AssertAngle(a.CameraAzimuth, b.CameraAzimuth);
                break;
            case (ZoneChangeOp a, ZoneChangeOp b):
                Assert.Equal(a.Zone, b.Zone);
                Assert.Equal(a.CfcId, b.CfcId);
                break;
            case (DirectorUpdateOp a, DirectorUpdateOp b):
                Assert.Equal(a.DirectorId, b.DirectorId);
                Assert.Equal(a.UpdateId, b.UpdateId);
                Assert.Equal(a.Param1, b.Param1);
                Assert.Equal(a.Param2, b.Param2);
                Assert.Equal(a.Param3, b.Param3);
                Assert.Equal(a.Param4, b.Param4);
                break;
            case (EnvControlOp a, EnvControlOp b):
                Assert.Equal(a.Index, b.Index);
                Assert.Equal(a.State, b.State);
                break;
            case (ActorCreateOp a, ActorCreateOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.Oid, b.Oid);
                Assert.Equal(a.SpawnIndex, b.SpawnIndex);
                Assert.Equal(a.Name, b.Name);
                Assert.Equal(a.NameId, b.NameId);
                Assert.Equal(a.Type, b.Type);
                Assert.Equal(a.Class, b.Class);
                Assert.Equal(a.Level, b.Level);
                AssertVec(a.PosRot.Pos, b.PosRot.Pos);
                AssertAngle(a.PosRot.Rot, b.PosRot.Rot);
                Assert.True(Math.Abs(a.HitboxRadius - b.HitboxRadius) < 5e-4);
                Assert.Equal(a.HpMp, b.HpMp);
                Assert.Equal(a.IsTargetable, b.IsTargetable);
                Assert.Equal(a.IsAlly, b.IsAlly);
                Assert.Equal(a.OwnerId, b.OwnerId);
                Assert.Equal(a.FateId, b.FateId);
                break;
            case (ActorDestroyOp a, ActorDestroyOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                break;
            case (ActorRenameOp a, ActorRenameOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.Name, b.Name);
                Assert.Equal(a.NameId, b.NameId);
                break;
            case (ActorMoveOp a, ActorMoveOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                AssertVec(a.PosRot.Pos, b.PosRot.Pos);
                AssertAngle(a.PosRot.Rot, b.PosRot.Rot);
                break;
            case (ActorHpMpOp a, ActorHpMpOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.HpMp, b.HpMp);
                break;
            case (ActorTargetableOp a, ActorTargetableOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.Value, b.Value);
                break;
            case (ActorDeadOp a, ActorDeadOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.Value, b.Value);
                break;
            case (ActorCombatOp a, ActorCombatOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.Value, b.Value);
                break;
            case (ActorTargetOp a, ActorTargetOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.TargetId, b.TargetId);
                break;
            case (ActorCastStartOp a, ActorCastStartOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.Action, b.Action);
                Assert.Equal(a.TargetId, b.TargetId);
                AssertVec(a.Location, b.Location);
                Assert.True(Math.Abs(a.ElapsedTime - b.ElapsedTime) < 5e-4);
                Assert.True(Math.Abs(a.TotalTime - b.TotalTime) < 5e-4);
                Assert.Equal(a.Interruptible, b.Interruptible);
                AssertAngle(a.Rotation, b.Rotation);
                break;
            case (ActorCastStopOp a, ActorCastStopOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                break;
            case (ActorCastEventOp a, ActorCastEventOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.Action, b.Action);
                Assert.Equal(a.MainTargetId, b.MainTargetId);
                Assert.True(Math.Abs(a.AnimationLock - b.AnimationLock) < 5e-4);
                Assert.Equal(a.MaxTargets, b.MaxTargets);
                AssertVec(a.TargetPos, b.TargetPos);
                Assert.Equal(a.GlobalSequence, b.GlobalSequence);
                Assert.Equal(a.SourceSequence, b.SourceSequence);
                AssertAngle(a.Rotation, b.Rotation);
                Assert.Equal(a.Targets.Length, b.Targets.Length);
                for (var i = 0; i < a.Targets.Length; i++)
                {
                    Assert.Equal(a.Targets[i].InstanceId, b.Targets[i].InstanceId);
                    Assert.Equal(a.Targets[i].Effects, b.Targets[i].Effects);
                }
                break;
            case (ActorStatusOp a, ActorStatusOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.Index, b.Index);
                Assert.Equal(a.IsUpdate, b.IsUpdate);
                Assert.Equal(a.Status is null, b.Status is null);
                if (a.Status is { } sa && b.Status is { } sb)
                {
                    Assert.Equal(sa.Id, sb.Id);
                    Assert.Equal(sa.Extra, sb.Extra);
                    Assert.Equal(sa.SourceId, sb.SourceId);
                    Assert.True(Math.Abs((sa.ExpireAt - sb.ExpireAt).TotalMilliseconds) <= 1);
                }
                break;
            case (ActorTetherOp a, ActorTetherOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.TetherId, b.TetherId);
                Assert.Equal(a.TargetId, b.TargetId);
                break;
            case (ActorIconOp a, ActorIconOp b):
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.IconId, b.IconId);
                Assert.Equal(a.TargetId, b.TargetId);
                break;
            case (PartyModifyOp a, PartyModifyOp b):
                Assert.Equal(a.Slot, b.Slot);
                Assert.Equal(a.ContentId, b.ContentId);
                Assert.Equal(a.InstanceId, b.InstanceId);
                Assert.Equal(a.InCutscene, b.InCutscene);
                Assert.Equal(a.Name, b.Name);
                break;
            case (RawOp a, RawOp b):
                Assert.Equal(a.RawTag, b.RawTag);
                Assert.Equal(a.Fields, b.Fields);
                break;
            case (OpaqueOp a, OpaqueOp b):
                Assert.Equal(a.OpaqueTag, b.OpaqueTag);
                break;
            default:
                throw new InvalidOperationException($"No comparison for {left.GetType().Name} at {index}.");
        }
    }

    private static void AssertVec(Vector3 a, Vector3 b)
        => Assert.True(Vector3.Distance(a, b) < 5e-4, $"{a} vs {b}");

    private static void AssertAngle(Angle a, Angle b)
        => Assert.True(Math.Abs(a.Rad - b.Rad) < 2e-5, $"{a.Rad} vs {b.Rad}");

    private static void AssertFloat(float a, float b)
        => Assert.Equal(a, b);
}
