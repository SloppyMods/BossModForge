using System.Linq;
using System.Numerics;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Replay;

public sealed class BinaryParserTests
{
    [Fact]
    public void UnknownBinaryTag_DegradedAndKeepsPartialOps()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var builder = new RecordingBuilder(start).Frame().Zone(1234, 826);
        var prefix = builder.ToBinary(brotli: false);

        using var ms = new MemoryStream();
        ms.Write(prefix);
        ms.Write(BitConverter.GetBytes(Tag.Parse("QQQQ").Value));
        ms.Write(new byte[] { 1, 2, 3, 4 });
        ms.Position = 0;

        var recording = ReplayParser.Parse(ms, "partial.blog");
        Assert.True(recording.Degraded);
        Assert.Contains(recording.Issues, i => i.Code == "UnknownBinaryTag" && i.Severity == IssueSeverity.Degraded);
        Assert.Contains(recording.Ops, op => op is FrameStartOp);
        Assert.Contains(recording.Ops, op => op is ZoneChangeOp);
        Assert.DoesNotContain(recording.Ops, op => op is RawOp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void CastEvent_TargetCounts(int count)
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var targets = new List<CastTarget>();
        if (count >= 1)
            targets.Add(new CastTarget(0x10, ActionEffects.From([Effects.Damage(9), 0, Effects.Miss()])));
        if (count >= 3)
        {
            targets.Add(new CastTarget(0x11, ActionEffects.From([Effects.Heal(4)])));
            targets.Add(new CastTarget(0x12, ActionEffects.Empty));
        }

        var builder = new RecordingBuilder(start)
            .Frame()
            .CastEvent(0x1000, ActionId.Spell(40001), 0x10, new Vector3(1, 2, 3), Angle.FromDeg(45), [.. targets]);
        using var stream = builder.Open(ReplayEncoding.Binary);
        var recording = ReplayParser.Parse(stream, "cst.blog");
        var ev = Assert.Single(recording.Ops.OfType<ActorCastEventOp>());
        Assert.Equal((uint)count, ev.MaxTargets);
        Assert.Equal(count, ev.Targets.Length);
        if (count >= 1)
        {
            Assert.Equal(
                new[] { ActionEffectType.Damage, ActionEffectType.Miss },
                ev.Targets[0].Effects.NonEmpty().Select(e => e.Type).ToArray());
        }
    }
}
