using System.Collections.Immutable;
using System.Numerics;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class ShapeInferenceTests
{
    private static readonly ArenaFit Arena20 = new(ArenaKind.Circle, Vector2.Zero, 20, 0, 0, [], 200, Confidence.Of(0.9), []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovers_Circle6(bool withPrior) => Recover(Shape.Circle(6), 15, withPrior, 0.5f, 3f);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovers_Donut(bool withPrior) => Recover(Shape.Donut(5, 12), 20, withPrior, 0.5f, 3f);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovers_Cone(bool withPrior) => Recover(Shape.Cone(30, 45), 40, withPrior, 0.5f, 3f);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovers_Rect(bool withPrior) => Recover(Shape.Rect(40, 3), 50, withPrior, 0.5f, 3f);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovers_Cross(bool withPrior) => Recover(Shape.Cross(40, 3), 50, withPrior, 0.5f, 3f);

    [Fact]
    public void MatchingPrior_IsHigh()
    {
        var shape = Shape.Circle(6);
        var prior = ShapeFixture.PriorFor(shape);
        var high = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var obs = ShapeFixture.Planted(shape, seed, fieldRadius: 15);
            var fit = ShapeInference.Fit(obs, prior, 20);
            if (fit is { Confidence.Band: ConfidenceBand.High })
                high++;
        }

        Assert.True(high >= 196, "High " + high);
    }

    [Fact]
    public void Ambiguity_NeverHigh()
    {
        var shape = Shape.Circle(6);
        for (var seed = 0; seed < 40; seed++)
        {
            var obs = ShapeFixture.Planted(shape, seed, fieldRadius: 15, clearBoundary: true, ambiguityOutside: 0.6f);
            var fit = ShapeInference.Fit(obs, ShapeFixture.PriorFor(shape), 20);
            Assert.NotNull(fit);
            Assert.NotEqual(ConfidenceBand.High, fit!.Confidence.Band);
        }
    }

    [Fact]
    public void Raidwide_AllHit_High()
    {
        var obs = ShapeFixture.Raidwide(1, 10, 8, 10);
        var fit = ShapeInference.Fit(obs, null, 20);
        Assert.NotNull(fit);
        Assert.Equal(ShapeKind.RaidwideNoShape, fit!.Shape.Kind);
        Assert.Equal(ConfidenceBand.High, fit.Confidence.Band);
    }

    [Fact]
    public void Raidwide_EightOfTen_NotHigh()
    {
        var obs = ShapeFixture.Raidwide(2, 10, 8, 8);
        var fit = ShapeInference.Fit(obs, null, 20);
        Assert.NotNull(fit);
        Assert.Equal(ShapeKind.RaidwideNoShape, fit!.Shape.Kind);
        Assert.NotEqual(ConfidenceBand.High, fit.Confidence.Band);
    }

    [Fact]
    public void Raidwide_OneObservation_CappedMedium()
    {
        var obs = ShapeFixture.Raidwide(3, 1, 8, 1);
        var fit = ShapeInference.Fit(obs, null, 20);
        Assert.NotNull(fit);
        Assert.Equal(ShapeKind.RaidwideNoShape, fit!.Shape.Kind);
        Assert.Equal(ConfidenceBand.Medium, fit.Confidence.Band);
    }

    [Fact]
    public void Classify_SheetNull_Cone_Telegraphed()
    {
        var ability = Ability(true, null);
        var fit = ConeFit();
        Assert.Equal(MechanicClass.TelegraphedAOE, ShapeInference.Classify(ability, fit, Arena20));
    }

    [Fact]
    public void Classify_HiddenWhenNoOmen()
    {
        var sheet = new ActionRow(1, "x", 3, 40, 0, 50, 0);
        var ability = Ability(true, sheet);
        Assert.Equal(MechanicClass.HiddenAOE, ShapeInference.Classify(ability, ConeFit(), Arena20));
    }

    [Fact]
    public void Classify_OmenFan_Telegraphed_Prior45()
    {
        var sheet = new ActionRow(1, "x", 3, 40, 0, 50, 12, "gl_fan090_1bf");
        var prior = ShapeInference.PriorFor(sheet);
        Assert.NotNull(prior);
        Assert.Equal(45f, prior!.HalfAngleDeg);
        Assert.Equal(MechanicClass.TelegraphedAOE, ShapeInference.Classify(Ability(true, sheet), ConeFit(), Arena20));
    }

    [Fact]
    public void Classify_LargeCircle_Raidwide()
    {
        var shape = Shape.Circle(19);
        var fit = new ShapeFit(shape, shape, 0.9, 1, 0, 0, 2, 0, 3, Confidence.Of(0.9), []);
        Assert.Equal(MechanicClass.Raidwide, ShapeInference.Classify(Ability(true, null), fit, Arena20));
    }

    [Fact]
    public void Classify_NoCastBar_Unclassified()
    {
        Assert.Equal(MechanicClass.Unclassified, ShapeInference.Classify(Ability(false, null), ConeFit(), Arena20));
    }

    [Fact]
    public void Classify_SingleTarget_Unclassified()
    {
        var fit = new ShapeFit(Shape.SingleTarget, Shape.SingleTarget, 0.9, 1, 1, 0, 9, 0, 1, Confidence.Of(0.9), []);
        Assert.Equal(MechanicClass.Unclassified, ShapeInference.Classify(Ability(true, new ActionRow(1, "x", 1, 0, 0, 0, 0)), fit, Arena20));
    }

    [Fact]
    public void PriorFor_CastTypeTable()
    {
        Assert.Equal(ShapeKind.SingleTarget, ShapeInference.PriorFor(new ActionRow(1, "a", 1, 0, 0, 0, 0))!.Kind);
        Assert.Null(ShapeInference.PriorFor(new ActionRow(1, "a", 6, 0, 0, 0, 0)));
        Assert.Null(ShapeInference.PriorFor(new ActionRow(1, "a", 9, 0, 0, 0, 0)));
        Assert.Null(ShapeInference.PriorFor(new ActionRow(1, "a", 14, 0, 0, 0, 0)));
        Assert.Equal(ShapeKind.Circle, ShapeInference.PriorFor(new ActionRow(1, "a", 2, 8, 0, 0, 0))!.Kind);
        Assert.Equal(ShapeKind.Cone, ShapeInference.PriorFor(new ActionRow(1, "a", 3, 40, 0, 0, 0))!.Kind);
        Assert.Equal(ShapeKind.Rect, ShapeInference.PriorFor(new ActionRow(1, "a", 4, 40, 8, 0, 0))!.Kind);
        Assert.Equal(ShapeOrigin.TargetActor, ShapeInference.PriorFor(new ActionRow(1, "a", 5, 6, 0, 0, 0))!.Origin);
        Assert.Equal(ShapeOrigin.CastLocation, ShapeInference.PriorFor(new ActionRow(1, "a", 7, 6, 0, 0, 0))!.Origin);
        Assert.Equal(ShapeDirection.ToTarget, ShapeInference.PriorFor(new ActionRow(1, "a", 8, 0, 8, 0, 0))!.Direction);
        Assert.Equal(ShapeKind.Donut, ShapeInference.PriorFor(new ActionRow(1, "a", 10, 12, 0, 0, 0))!.Kind);
        Assert.Equal(ShapeKind.Cross, ShapeInference.PriorFor(new ActionRow(1, "a", 11, 40, 6, 0, 0))!.Kind);
        Assert.Equal(ShapeKind.Rect, ShapeInference.PriorFor(new ActionRow(1, "a", 12, 40, 8, 0, 0))!.Kind);
        Assert.Equal(ShapeDirection.ToTarget, ShapeInference.PriorFor(new ActionRow(1, "a", 13, 40, 0, 0, 0))!.Direction);
    }

    [Fact]
    public void Infer_LagSearch()
    {
        AssertLag(0.3f);
        AssertLag(0.0f);
    }

    private static void AssertLag(float planted)
    {
        var t0 = new DateTime(2026, 9, 19, 21, 0, 0, DateTimeKind.Unspecified);
        var b = new RecordingBuilder(t0);
        var origin = new Vector3(0, 0, 0);
        var shape = Shape.Circle(6);
        b.Frame().Zone(1, 1);
        for (var i = 0; i < 8; i++)
            b.Player(0x10 + (ulong)i, "P" + i, Class.PLD, new Vector3(i - 3.5f, 0, i < 4 ? 2 : 12)).Party(i, 1 + (ulong)i, 0x10 + (ulong)i, "P" + i);
        b.Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, origin, default, 5f, targetable: true, hpmp: new HpMp(100, 100, 0, 10000, 10000))
            .Combat(0x1000, true);
        for (var i = 0; i < 8; i++)
            b.Combat(0x10 + (ulong)i, true);
        b.Advance(TimeSpan.FromSeconds(6));
        b.CastStart(0x1000, ActionId.Spell(7), 0x10, origin, 1f, default);
        const int pre = 10;
        for (var s = 0; s < pre; s++)
        {
            b.Advance(TimeSpan.FromSeconds(0.1));
            PlaceLagPlayers(b, (s + 1) * 0.1f - 1f, planted);
        }

        var lagSteps = (int)MathF.Round(planted / 0.1f);
        for (var s = 0; s < lagSteps; s++)
        {
            b.Advance(TimeSpan.FromSeconds(0.1));
            PlaceLagPlayers(b, (s + 1) * 0.1f, planted);
        }

        var labels = new List<CastTarget>();
        for (var i = 0; i < 8; i++)
        {
            var pos = LagPos(i, planted, planted);
            var hit = shape.Contains(new Vector2(pos.X, pos.Z), Vector2.Zero, default);
            labels.Add(new CastTarget(0x10 + (ulong)i, ActionEffects.From([hit ? Effects.Damage(1) : Effects.Miss()])));
        }

        b.CastEvent(0x1000, ActionId.Spell(7), 0x10, origin, default, [.. labels]).CastStop(0x1000);
        b.Advance(TimeSpan.FromSeconds(1)).Dead(0x1000, true).Combat(0x1000, false).Destroy(0x1000);
        using var stream = b.Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "lag.log");
        var pulls = PullSegmenter.Segment(rec, new SegmenterOptions(TimeSpan.FromSeconds(5), TimeSpan.Zero));
        var participants = ParticipantClassifier.Classify(rec, pulls, NullGameData.Instance);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, NullGameData.Instance);
        var ability = catalog.Abilities.Single(a => a.Action.Id == 7);
        var fit = ShapeInference.Infer(ability, rec, pulls, Arena20);
        Assert.NotNull(fit);
        if (planted == 0)
            Assert.True(MathF.Abs(fit!.LagSeconds) <= 0.1f, "lag " + fit.LagSeconds);
        else
            Assert.True(MathF.Abs(fit!.LagSeconds - planted) <= 0.1f, "lag " + fit.LagSeconds);
    }

    private static void Recover(Shape planted, float field, bool withPrior, float yTol, float angTol)
    {
        var prior = withPrior ? ShapeFixture.PriorFor(planted) : null;
        var ok = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var obs = ShapeFixture.Planted(planted, seed, fieldRadius: field);
            var fit = ShapeInference.Fit(obs, prior, 40);
            if (fit is null || fit.Shape.Kind != planted.Kind)
                continue;
            if (MathF.Abs(fit.Shape.Radius - planted.Radius) > yTol)
                continue;
            if (planted.Kind == ShapeKind.Donut && MathF.Abs(fit.Shape.InnerRadius - planted.InnerRadius) > yTol)
                continue;
            if (planted.Kind == ShapeKind.Cone && MathF.Abs(fit.Shape.HalfAngleDeg - planted.HalfAngleDeg) > angTol)
                continue;
            if (planted.Kind is ShapeKind.Rect or ShapeKind.Cross && MathF.Abs(fit.Shape.HalfWidth - planted.HalfWidth) > yTol)
                continue;
            ok++;
        }

        Assert.True(ok >= 196, planted.Kind + " recovered " + ok + " prior=" + withPrior);
    }

    private static void PlaceLagPlayers(RecordingBuilder b, float tFromCastEnd, float tTrue)
    {
        for (var i = 0; i < 8; i++)
            b.Move(0x10 + (ulong)i, LagPos(i, tFromCastEnd, tTrue), default);
    }

    private static Vector3 LagPos(int i, float tFromCastEnd, float tTrue)
    {
        var dt = tFromCastEnd - tTrue;
        var x = 0f;
        float z;
        if (i <= 2)
            z = 3f;
        else if (i == 3)
            z = 5.5f + 10f * dt;
        else if (i == 4)
            z = 5.5f - 10f * dt;
        else
            z = 12f;
        return new Vector3(x, 0, z);
    }

    private static Ability Ability(bool castBar, ActionRow? sheet)
        => new(1, ActionId.Spell(1), "X", castBar, 5, false, 1, [], sheet, Confidence.Of(0.9), []);

    private static ShapeFit ConeFit()
    {
        var s = Shape.Cone(40, 45);
        return new ShapeFit(s, s, 0.95, 1, 1, 0, 4, 0, 3, Confidence.Of(0.95), []);
    }
}
