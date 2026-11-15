using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Golden;

public sealed class SyntheticRotationGoldenTests
{
    [Fact]
    public void Pipeline_MatchesGoldenFiles()
    {
        using var stream = SyntheticRotation.Build().Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "SyntheticRotation.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticRotation.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, SyntheticRotation.GameData);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, SyntheticRotation.GameData);
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, SyntheticRotation.GameData, new BuildOptions(ModuleName: "SyntheticRotation"));
        var json = EncounterModelJson.Serialize(em);
        Assert.Empty(EncounterModelJson.Validate(json));
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var cs = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
        Assert.Contains("DeathPhase(0, Phase0)", cs, StringComparison.Ordinal);
        Assert.Contains("CastMulti(", cs, StringComparison.Ordinal);
        Assert.Contains("Rotation0(", cs, StringComparison.Ordinal);
        Assert.Contains("SimpleState(id + 0xFF0000, 10, \"???\")", cs, StringComparison.Ordinal);
        GoldenPaths.AssertEqualOrUpdate("SyntheticRotation.em.json", json);
        GoldenPaths.AssertEqualOrUpdate("SyntheticRotation.vbm.cs", cs);
    }
}

public sealed class SyntheticPhasesGoldenTests
{
    [Fact]
    public void Pipeline_MatchesGoldenFiles()
    {
        using var stream = SyntheticPhases.Build().Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "SyntheticPhases.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticPhases.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, SyntheticPhases.GameData);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, SyntheticPhases.GameData);
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, SyntheticPhases.GameData, new BuildOptions(ModuleName: "SyntheticPhases"));
        var json = EncounterModelJson.Serialize(em);
        Assert.Empty(EncounterModelJson.Validate(json));
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var cs = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
        Assert.Contains("SimplePhase(0, Phase0, \"P0\")", cs, StringComparison.Ordinal);
        Assert.Contains(".Raw.Update", cs, StringComparison.Ordinal);
        Assert.Contains("DeathPhase(1, Phase1)", cs, StringComparison.Ordinal);
        GoldenPaths.AssertEqualOrUpdate("SyntheticPhases.em.json", json);
        GoldenPaths.AssertEqualOrUpdate("SyntheticPhases.vbm.cs", cs);
    }
}
