using System.Collections.Immutable;
using BossModForge.Core.Inference;
using BossModForge.Core.Inference.Timeline;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class ProfileAlignerTests
{
    [Fact]
    public void IdenticalSequences_Support2()
    {
        var t0 = new DateTime(2026, 1, 1);
        var a = Seq(0, t0, 1, 2, 3);
        var b = Seq(1, t0, 1, 2, 3);
        var profile = ProfileAligner.AlignInto([], a, TimelineAlignerOptions.Default, t0, Starts(t0));
        profile = ProfileAligner.AlignInto(profile, b, TimelineAlignerOptions.Default, t0, Starts(t0));
        Assert.Equal(3, profile.Length);
        Assert.All(profile, c => Assert.Equal(2, c.Support));
    }

    [Fact]
    public void Insertion_CreatesSupport1Column()
    {
        var t0 = new DateTime(2026, 1, 1);
        var a = Seq(0, t0, 1, 3);
        var b = Seq(1, t0, 1, 2, 3);
        var profile = ProfileAligner.AlignInto([], a, TimelineAlignerOptions.Default, t0, Starts(t0));
        profile = ProfileAligner.AlignInto(profile, b, TimelineAlignerOptions.Default, t0, Starts(t0));
        Assert.Contains(profile, c => c.ConsensusAid == 2 && c.Support == 1);
        Assert.Equal(2, profile.First(c => c.ConsensusAid == 1).Support);
    }

    [Fact]
    public void CloseTimingMismatch_SameColumn_FarTiming_TwoColumns()
    {
        var t0 = new DateTime(2026, 1, 1);
        var closeA = ImmutableArray.Create(Ev(0, t0.AddSeconds(5), 1), Ev(0, t0.AddSeconds(8), 2));
        var closeB = ImmutableArray.Create(Ev(1, t0.AddSeconds(5), 1), Ev(1, t0.AddSeconds(8.4), 9));
        var close = ProfileAligner.AlignInto(
            ProfileAligner.AlignInto([], closeA, TimelineAlignerOptions.Default, t0, Starts(t0)),
            closeB, TimelineAlignerOptions.Default, t0, Starts(t0));
        Assert.Equal(2, close.Length);

        var farA = ImmutableArray.Create(Ev(0, t0.AddSeconds(5), 1), Ev(0, t0.AddSeconds(8), 2));
        var farB = ImmutableArray.Create(Ev(1, t0.AddSeconds(5), 1), Ev(1, t0.AddSeconds(16), 9));
        var far = ProfileAligner.AlignInto(
            ProfileAligner.AlignInto([], farA, TimelineAlignerOptions.Default, t0, Starts(t0)),
            farB, TimelineAlignerOptions.Default, t0, Starts(t0));
        Assert.DoesNotContain(far, c => c.Members.Any(m => m.Aid == 2) && c.Members.Any(m => m.Aid == 9));
    }

    [Fact]
    public void ReversedPullOrder_SameSymbols()
    {
        var t0 = new DateTime(2026, 1, 1);
        var p0 = Seq(0, t0, 1, 2, 3);
        var p1 = Seq(1, t0, 1, 2, 3);
        var pulls = ImmutableArray.Create(
            new Pull(0, t0, t0.AddMinutes(2), PullOutcome.Kill, 1, 1, 1, 1, [], Confidence.Of(1), []),
            new Pull(1, t0, t0.AddMinutes(2), PullOutcome.Kill, 1, 1, 1, 1, [], Confidence.Of(1), []));
        var times = new Dictionary<uint, float> { [1] = 1, [2] = 1, [3] = 1 };
        var fwd = TimelineAligner.Align([p0, p1], pulls, times);
        var rev = TimelineAligner.Align([p1, p0], pulls, times);
        Assert.Equal(fwd.Steps.Select(s => (s.Kind, s.Aid)).ToArray(), rev.Steps.Select(s => (s.Kind, s.Aid)).ToArray());
    }

    private static ImmutableArray<AnchorEvent> Seq(int pull, DateTime t0, params uint[] aids)
        => aids.Select((aid, i) => Ev(pull, t0.AddSeconds(2 + i * 6), aid)).ToImmutableArray();

    private static AnchorEvent Ev(int pull, DateTime t, uint aid)
        => new(pull, t, AnchorKind.Cast, aid, 1, t.AddSeconds(1), 1);

    private static Dictionary<int, DateTime> Starts(DateTime t0) => new() { [0] = t0, [1] = t0 };
}
