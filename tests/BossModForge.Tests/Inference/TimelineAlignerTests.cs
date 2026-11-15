using System.Collections.Immutable;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Inference.Timeline;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class TimelineAlignerTests
{
    [Fact]
    public void SyntheticRotation_FoldedLoopAndCastMulti()
    {
        var timeline = AlignFixture(jitter: false);
        Assert.Equal(8, timeline.Steps.Length);
        Assert.Equal(StepKind.Cast, timeline.Steps[0].Kind);
        Assert.Equal(SyntheticRotation.Opener, timeline.Steps[0].Aid);
        Assert.Equal(StepKind.CastMulti, timeline.Steps[1].Kind);
        Assert.Equal<uint>([SyntheticRotation.Cone, SyntheticRotation.ConeAlt], timeline.Steps[1].Of);
        Assert.Contains("branch", timeline.Steps[1].ReviewReason, StringComparison.Ordinal);
        Assert.NotNull(timeline.Loop);
        var loop = timeline.Loop!;
        Assert.Equal(2, loop.StartStep);
        Assert.Equal(6, loop.Length);
        Assert.Equal(2, loop.Repeats);
        Assert.Equal(3, loop.TrailingPartial);
        Assert.Equal(6f, loop.EntryDelay, 1);
        Assert.Equal(3, timeline.Steps[0].Support);
        Assert.All(timeline.Steps, s => Assert.True(s.DelayMad <= 0.1f, s.Kind + " MAD " + s.DelayMad));
    }

    [Fact]
    public void Jitter_Circle2BecomesTimeout()
    {
        var timeline = AlignFixture(jitter: true);
        var circle2 = timeline.Steps.First(s => s.Of.Contains(SyntheticRotation.Circle2) || s.Aid == SyntheticRotation.Circle2);
        Assert.Equal(StepKind.Timeout, circle2.Kind);
        Assert.Equal(3f, circle2.DelayMad, 1);
        Assert.Contains("MAD", circle2.ReviewReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ABCABC", 0, 3, 2, 0)]
    [InlineData("XABCABCAB", 1, 3, 2, 2)]
    [InlineData("ABABAB", 0, 2, 3, 0)]
    [InlineData("AAAA", 0, 2, 2, 0)]
    public void LoopFolding_Patterns(string pattern, int start, int length, int repeats, int trailing)
    {
        var timeline = AlignPattern(pattern);
        if (pattern == "ABCD")
        {
            Assert.Null(timeline.Loop);
            return;
        }

        Assert.NotNull(timeline.Loop);
        var loop = timeline.Loop!;
        Assert.Equal(start, loop.StartStep);
        Assert.Equal(length, loop.Length);
        Assert.Equal(repeats, loop.Repeats);
        Assert.Equal(trailing, loop.TrailingPartial);
    }

    [Fact]
    public void Abcd_HasNoLoop()
    {
        Assert.Null(AlignPattern("ABCD").Loop);
    }

    [Fact]
    public void LowSupport_BecomesTimeout()
    {
        var t0 = new DateTime(2026, 1, 1);
        var pulls = ThreePulls(t0);
        var a = Casts(0, t0, [1u, 2u]);
        var b = Casts(1, t0, [1u]);
        var c = Casts(2, t0, [1u]);
        var timeline = TimelineAligner.Align([a, b, c], pulls, new Dictionary<uint, float> { [1] = 1, [2] = 1 });
        Assert.Contains(timeline.Steps, s => s.Aid == 2 && s.Kind == StepKind.Timeout && s.ReviewReason != null && s.ReviewReason.Contains("seen in", StringComparison.Ordinal));
    }

    private static Timeline AlignFixture(bool jitter)
    {
        using var stream = SyntheticRotation.Build(jitter).Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "rot.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticRotation.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, SyntheticRotation.GameData);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, SyntheticRotation.GameData);
        var anchors = AnchorExtractor.Extract(rec, pulls, catalog);
        var times = catalog.Abilities.ToDictionary(a => a.Action.Id, a => a.CastTime);
        return TimelineAligner.Align(anchors, pulls, times);
    }

    private static Timeline AlignPattern(string pattern)
    {
        var t0 = new DateTime(2026, 1, 1);
        var aids = pattern.Select(ch => (uint)(ch - 'A' + 1)).ToArray();
        var seq = Casts(0, t0, aids);
        var pulls = ImmutableArray.Create(new Pull(0, t0, t0.AddMinutes(5), PullOutcome.Kill, 1, 1, 1, 1, [], Confidence.Of(1), []));
        var times = aids.Distinct().ToDictionary(a => a, _ => 0f);
        return TimelineAligner.Align([seq], pulls, times);
    }

    private static ImmutableArray<Pull> ThreePulls(DateTime t0)
        => [
            new Pull(0, t0, t0.AddMinutes(2), PullOutcome.Kill, 1, 1, 1, 1, [], Confidence.Of(1), []),
            new Pull(1, t0, t0.AddMinutes(2), PullOutcome.Wipe, 1, 1, 1, 1, [], Confidence.Of(1), []),
            new Pull(2, t0, t0.AddMinutes(2), PullOutcome.Kill, 1, 1, 1, 1, [], Confidence.Of(1), []),
        ];

    private static ImmutableArray<AnchorEvent> Casts(int pull, DateTime t0, uint[] aids)
        => aids.Select((aid, i) => new AnchorEvent(pull, t0.AddSeconds(1 + i * 5), AnchorKind.Cast, aid, 0, t0.AddSeconds(1 + i * 5), 1)).ToImmutableArray();
}
