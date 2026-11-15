using System.Collections.Immutable;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Model;

public sealed class EncounterModelBuilderShapeTests
{
    [Fact]
    public void Build_PlantedSheetCone_TelegraphedAOE()
    {
        using var stream = SyntheticShapes.Build().Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "shapes.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticShapes.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, NullGameData.Instance);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, NullGameData.Instance);
        var cone = catalog.Abilities.Single(a => a.Action.Id == SyntheticShapes.ConeAid);
        catalog = catalog with
        {
            Abilities = catalog.Abilities.Select(a =>
                a.Action.Id == SyntheticShapes.ConeAid
                    ? a with { Sheet = new ActionRow(a.Action.Id, a.Name, 3, 40, 0, 20, 12, "gl_fan090_1bf") }
                    : a).ToImmutableArray(),
        };
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, NullGameData.Instance);
        var model = em.Abilities.Single(a => a.ActionId == SyntheticShapes.ConeAid);
        Assert.Equal(MechanicClass.TelegraphedAOE, model.Class);
        Assert.NotNull(model.Shape);
        Assert.Equal(ShapeKind.Cone, model.Shape!.Kind);
        Assert.Equal(40f, model.Shape.Radius, 0.5);
        Assert.Equal(45f, model.Shape.HalfAngleDeg, 3);
        var json = EncounterModelJson.Serialize(em);
        Assert.Empty(EncounterModelJson.Validate(json));
        Assert.Equal(json, EncounterModelJson.Serialize(EncounterModelJson.Deserialize(json)));
        _ = cone;
    }

    [Fact]
    public void Build_MismatchedPrior_ReviewItemLow()
    {
        using var stream = SyntheticShapes.Build().Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "shapes.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticShapes.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, NullGameData.Instance);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, NullGameData.Instance);
        catalog = catalog with
        {
            Abilities = catalog.Abilities.Select(a =>
                a.Action.Id == SyntheticShapes.CircleAid
                    ? a with { Sheet = new ActionRow(a.Action.Id, a.Name, 3, 40, 0, 20, 12, "gl_fan090_1bf") }
                    : a).ToImmutableArray(),
        };
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, NullGameData.Instance);
        var model = em.Abilities.Single(a => a.ActionId == SyntheticShapes.CircleAid);
        Assert.Equal(ConfidenceBand.Low, model.Confidence.Band);
        Assert.Contains(em.Review, r => r.Subject == "shape");
    }
}
