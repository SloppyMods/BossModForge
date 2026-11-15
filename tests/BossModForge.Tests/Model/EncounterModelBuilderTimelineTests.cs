using System.Collections.Immutable;
using BossModForge.Core.Emit;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Model;

public sealed class EncounterModelBuilderTimelineTests
{
    [Fact]
    public void Build_SyntheticRotation_PopulatesTimeline()
    {
        var em = BuildRotation();
        Assert.NotNull(em.Timeline);
        var tl = em.Timeline!;
        Assert.NotEmpty(tl.Steps);
        Assert.Equal("0x00000000", tl.Steps[0].Id);
        Assert.Equal("0x00010000", tl.Steps[1].Id);
        Assert.Contains(tl.Phases[0].Activate, n => n.Contains("Circle", StringComparison.Ordinal));
        Assert.Equal("Cast", tl.Steps[0].Kind);
        Assert.False(tl.Steps[0].Activate.IsDefaultOrEmpty);
        Assert.False(tl.Steps[0].Deactivate.IsDefaultOrEmpty);
        var json = EncounterModelJson.Serialize(em);
        Assert.Empty(EncounterModelJson.Validate(json));
        var round = EncounterModelJson.Serialize(EncounterModelJson.Deserialize(json));
        Assert.Equal(json, round);
    }

    [Fact]
    public void PreM4Json_TimelineNull()
    {
        var json = EncounterModelJson.Serialize(HandBuiltEncounter.Create());
        Assert.DoesNotContain("\"timeline\"", json, StringComparison.Ordinal);
        var em = EncounterModelJson.Deserialize(json);
        Assert.Null(em.Timeline);
        Assert.Empty(EncounterModelJson.Validate(json));
    }

    private static EncounterModel BuildRotation()
    {
        using var stream = SyntheticRotation.Build().Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "rot.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticRotation.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, SyntheticRotation.GameData);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, SyntheticRotation.GameData);
        return EncounterModelBuilder.Build(rec, pulls, participants, catalog, SyntheticRotation.GameData);
    }
}
