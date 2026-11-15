using System.Numerics;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class BoundsFitterTests
{
    [Fact]
    public void Fit_PlantedCircle_SnapsHigh()
    {
        var samples = ArenaFixture.UniformDisc(1, 500, 20, new Vector2(10, 20));
        var fit = BoundsFitter.Fit(samples, new Vector2(10, 20));
        Assert.Equal(ArenaKind.Circle, fit.Kind);
        Assert.Equal(20f, fit.Radius);
        Assert.True(Vector2.Distance(fit.Center, new Vector2(10, 20)) < 0.5f);
        Assert.Equal(ConfidenceBand.High, fit.Confidence.Band);
    }

    [Fact]
    public void Fit_PlantedSquare()
    {
        var samples = ArenaFixture.UniformRect(2, 500, 20, 20, Vector2.Zero);
        var fit = BoundsFitter.Fit(samples, Vector2.Zero);
        Assert.Equal(ArenaKind.Square, fit.Kind);
        Assert.InRange(fit.HalfWidth, 19f, 21f);
    }

    [Fact]
    public void Fit_PlantedRect()
    {
        var samples = ArenaFixture.UniformRect(3, 500, 20, 15, Vector2.Zero);
        var fit = BoundsFitter.Fit(samples, Vector2.Zero);
        Assert.Equal(ArenaKind.Rect, fit.Kind);
    }

    [Fact]
    public void Fit_LShape_CustomLow()
    {
        var samples = ArenaFixture.LShape(4, 500, Vector2.Zero);
        var fit = BoundsFitter.Fit(samples, Vector2.Zero);
        Assert.Equal(ArenaKind.Custom, fit.Kind);
        Assert.True(fit.Vertices.Length >= 6);
        Assert.Equal(ConfidenceBand.Low, fit.Confidence.Band);
    }

    [Fact]
    public void Fit_TenSamples_Fallback()
    {
        var samples = ArenaFixture.UniformDisc(5, 10, 8, new Vector2(3, 4));
        var fit = BoundsFitter.Fit(samples, new Vector2(1, 2));
        Assert.Equal(ArenaKind.Circle, fit.Kind);
        Assert.Equal(20f, fit.Radius);
        Assert.Equal(new Vector2(1, 2), fit.Center);
        Assert.Equal(ConfidenceBand.Low, fit.Confidence.Band);
    }

    [Fact]
    public void Fit_Recording_DropsKnockbackWindow()
    {
        var t0 = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var b = new RecordingBuilder(t0);
        var origin = new Vector3(0, 0, 0);
        b.Frame().Zone(1, 1)
            .Player(0x10, "A", Class.PLD, origin)
            .Party(0, 1, 0x10, "A")
            .Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, origin, default, 5f, targetable: true, hpmp: new HpMp(100, 100, 0, 10000, 10000))
            .Combat(0x1000, true).Combat(0x10, true);
        for (var i = 0; i < 20; i++)
        {
            b.Advance(TimeSpan.FromSeconds(0.5));
            b.Move(0x10, new Vector3(i * 0.2f, 0, 0), default);
        }

        b.CastEvent(0x1000, ActionId.Spell(1), 0x10, origin, default, new CastTarget(0x10, ActionEffects.From([Effects.Knockback(10, 0)])));
        for (var i = 0; i < 10; i++)
        {
            b.Advance(TimeSpan.FromSeconds(0.5));
            b.Move(0x10, new Vector3(80, 0, 0), default);
        }

        b.Advance(TimeSpan.FromSeconds(1)).Dead(0x1000, true).Combat(0x1000, false).Destroy(0x1000);
        using var stream = b.Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "kb.log");
        var pulls = PullSegmenter.Segment(rec, new SegmenterOptions(TimeSpan.FromSeconds(5), TimeSpan.Zero));
        var withKb = BoundsFitter.Fit(rec, pulls, 0x4321);
        Assert.DoesNotContain(withKb.Evidence, e => e.Summary.Contains("80", StringComparison.Ordinal));
        Assert.True(withKb.SampleCount > 0);
        Assert.True(withKb.Radius < 50f);
    }
}
