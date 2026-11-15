using System.Linq;
using System.Numerics;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class PullSegmenterTests
{
    [Fact]
    public void SyntheticDungeon_ThreePulls_WipeWipeKill()
    {
        var recording = Parse(SyntheticDungeon.Build(), "SyntheticDungeon.log");
        var pulls = PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions);

        Assert.Equal(3, pulls.Length);
        Assert.Equal([PullOutcome.Wipe, PullOutcome.Wipe, PullOutcome.Kill], pulls.Select(p => p.Outcome).ToArray());
        Assert.All(pulls, p =>
        {
            Assert.Equal(SyntheticDungeon.BossOid, p.PrimaryOid);
            Assert.Equal(SyntheticDungeon.BossId, p.PrimaryInstanceId);
            Assert.Equal((ushort)1234, p.Zone);
            Assert.Equal((ushort)826, p.CfcId);
            Assert.Equal(4, p.PlayerInstanceIds.Length);
        });
        Assert.Equal(0.7, pulls[0].Confidence.Score);
        Assert.Equal(ConfidenceBand.Medium, pulls[0].Confidence.Band);
        Assert.Contains("combat-gap end", pulls[0].Confidence.Reasons);
        Assert.Equal(0.9, pulls[2].Confidence.Score);
        Assert.Equal(ConfidenceBand.High, pulls[2].Confidence.Band);
    }

    [Fact]
    public void ShortPull_DroppedEvidenceOnNext()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var builder = new RecordingBuilder(start)
            .Frame()
            .Zone(10, 20)
            .Player(0x10, "Alice", Class.PLD, Vector3.Zero)
            .Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, Vector3.Zero, default, 5f, targetable: true, ally: false)
            .Advance(TimeSpan.FromSeconds(1))
            .Combat(0x1000, true)
            .Advance(TimeSpan.FromSeconds(1))
            .Combat(0x1000, false)
            .Advance(TimeSpan.FromSeconds(1))
            .Combat(0x1000, true)
            .Advance(TimeSpan.FromSeconds(6))
            .Dead(0x1000, true)
            .Combat(0x1000, false);

        var recording = Parse(builder, "dropped.log");
        var pulls = PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions);
        var pull = Assert.Single(pulls);
        Assert.Equal(PullOutcome.Kill, pull.Outcome);
        Assert.Contains(pull.Evidence, e => e.Kind == "DroppedPull");
        Assert.Equal((ushort)10, pull.Zone);
        Assert.Equal((ushort)20, pull.CfcId);
    }

    [Fact]
    public void ZoneChange_Abandoned()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var builder = new RecordingBuilder(start)
            .Frame()
            .Zone(1234, 826)
            .Player(0x10, "Alice", Class.PLD, Vector3.Zero)
            .Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, Vector3.Zero, default, 5f, targetable: true, ally: false)
            .Advance(TimeSpan.FromSeconds(1))
            .Combat(0x1000, true)
            .Advance(TimeSpan.FromSeconds(6))
            .Zone(99, 1);

        var recording = Parse(builder, "abandon.log");
        var pull = Assert.Single(PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions));
        Assert.Equal(PullOutcome.Abandoned, pull.Outcome);
        Assert.Equal((ushort)1234, pull.Zone);
        Assert.Equal((ushort)826, pull.CfcId);
        Assert.Equal(0.9, pull.Confidence.Score);
    }

    [Fact]
    public void OpenCombat_RecordingEnded()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var builder = new RecordingBuilder(start)
            .Frame()
            .Zone(1, 1)
            .Player(0x10, "Alice", Class.PLD, Vector3.Zero)
            .Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, Vector3.Zero, default, 5f, targetable: true, ally: false)
            .Advance(TimeSpan.FromSeconds(1))
            .Combat(0x1000, true)
            .Advance(TimeSpan.FromSeconds(6));

        var recording = Parse(builder, "open.log");
        var pull = Assert.Single(PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions));
        Assert.Equal(PullOutcome.RecordingEnded, pull.Outcome);
        Assert.Equal(0.3, pull.Confidence.Score);
        Assert.Equal(ConfidenceBand.Low, pull.Confidence.Band);
        Assert.Contains("recording ended", pull.Confidence.Reasons);
    }

    private static Recording Parse(RecordingBuilder builder, string name)
    {
        using var stream = builder.Open(ReplayEncoding.Text);
        return ReplayParser.Parse(stream, name);
    }
}
