using System.Collections.Immutable;
using BossModForge.Core.Inference;
using BossModForge.Core.Inference.Timeline;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class PhaseDetectorTests
{
    [Fact]
    public void SyntheticPhases_Hp50Split()
    {
        using var stream = SyntheticPhases.Build().Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "phases.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticPhases.PullOptions);
        Assert.Equal(3, pulls.Length);
        var participants = ParticipantClassifier.Classify(rec, pulls, SyntheticPhases.GameData);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, SyntheticPhases.GameData);
        var anchors = AnchorExtractor.Extract(rec, pulls, catalog);
        var split = PhaseDetector.Detect(rec, pulls, anchors);
        Assert.Equal<double>([0.5], split.HpThresholds);
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, SyntheticPhases.GameData);
        Assert.NotNull(em.Timeline);
        var tl = em.Timeline!;
        Assert.Equal(2, tl.Phases.Length);
        Assert.Equal("HpThreshold", tl.Phases[0].Kind);
        Assert.Equal(0.5, tl.Phases[0].HpThreshold);
        Assert.Equal(4, tl.Phases[0].StepCount);
        Assert.Equal("Death", tl.Phases[1].Kind);
        Assert.True(tl.Phases[1].StepCount >= 4);
        Assert.Equal("Targetable", tl.Steps[tl.Phases[1].FirstStep].Kind);
        Assert.False(tl.Steps[tl.Phases[1].FirstStep].Targetable);
        Assert.Equal("Targetable", tl.Steps[tl.Phases[1].FirstStep + 1].Kind);
        Assert.True(tl.Steps[tl.Phases[1].FirstStep + 1].Targetable);
    }

    [Fact]
    public void InconsistentIdle_NoThreshold_AndHighHpIgnored()
    {
        var t0 = new DateTime(2026, 1, 1);
        var pulls = ImmutableArray.Create(
            new Pull(0, t0, t0.AddMinutes(2), PullOutcome.Kill, 1, 1, 0x1000, 0x1, [], Confidence.Of(1), []),
            new Pull(1, t0.AddMinutes(3), t0.AddMinutes(5), PullOutcome.Kill, 1, 1, 0x1000, 0x1, [], Confidence.Of(1), []));
        var a0 = ImmutableArray.Create(
            new AnchorEvent(0, t0.AddSeconds(2), AnchorKind.Cast, 1, 1, t0.AddSeconds(3), 0x1000),
            new AnchorEvent(0, t0.AddSeconds(40), AnchorKind.Cast, 2, 1, t0.AddSeconds(41), 0x1000));
        var a1 = ImmutableArray.Create(
            new AnchorEvent(1, t0.AddMinutes(3).AddSeconds(2), AnchorKind.Cast, 1, 1, t0.AddMinutes(3).AddSeconds(3), 0x1000),
            new AnchorEvent(1, t0.AddMinutes(3).AddSeconds(40), AnchorKind.Cast, 2, 1, t0.AddMinutes(3).AddSeconds(41), 0x1000));
        var rec = EmptyBoss(t0, hpRatio: 0.97);
        var split = PhaseDetector.Detect(rec, pulls, [a0, a1]);
        Assert.Empty(split.HpThresholds);
    }

    [Fact]
    public void DirectorOncePerPull_CueWithoutSplit()
    {
        var t0 = new DateTime(2026, 9, 20, 6, 0, 0, DateTimeKind.Unspecified);
        var b = new RecordingBuilder(t0);
        b.Frame().Zone(1, 1).Actor(0x1000, 0x1, "Boss", ActorType.Enemy, new System.Numerics.Vector3(100, 0, 100), default, 5f, targetable: true);
        for (var pull = 0; pull < 2; pull++)
        {
            b.Advance(TimeSpan.FromSeconds(1)).Combat(0x1000, true)
                .DirectorUpdate(0xABC, 0x10, 0x1)
                .Advance(TimeSpan.FromSeconds(20))
                .Dead(0x1000, true).Combat(0x1000, false).Dead(0x1000, false);
        }

        using var stream = b.Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "diru.log");
        var pulls = PullSegmenter.Segment(rec, new SegmenterOptions(TimeSpan.FromSeconds(5), TimeSpan.Zero));
        var anchors = AnchorExtractor.Extract(rec, pulls, new AbilityCatalog([], [], [], []));
        var split = PhaseDetector.Detect(rec, pulls, anchors);
        Assert.Contains(split.Cues, c => c.Kind == PhaseCueKind.DirectorOncePerPull);
        Assert.Contains(split.Evidence, e => e.Kind == "DirectorOncePerPull");
        Assert.Empty(split.HpThresholds);
    }

    private static Recording EmptyBoss(DateTime t0, double hpRatio)
    {
        var b = new RecordingBuilder(t0);
        var hp = (uint)(100000 * hpRatio);
        b.Frame().Zone(1, 1)
            .Actor(0x1000, 0x1, "Boss", ActorType.Enemy, new System.Numerics.Vector3(100, 0, 100), default, 5f, targetable: true, hpmp: new HpMp(hp, 100000, 0, 1, 1))
            .Advance(TimeSpan.FromSeconds(1)).Combat(0x1000, true)
            .Advance(TimeSpan.FromSeconds(90)).Dead(0x1000, true);
        using var stream = b.Open(ReplayEncoding.Text);
        return ReplayParser.Parse(stream, "hp.log");
    }
}
