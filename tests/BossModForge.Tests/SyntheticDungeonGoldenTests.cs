using BossModForge.Core.Emit;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Golden;

public sealed class SyntheticDungeonGoldenTests
{
    [Fact]
    public void Pipeline_MatchesGoldenFiles()
    {
        InferenceStages.SkipIfUnimplemented();
        using var stream = SyntheticDungeon.Build().Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "SyntheticDungeon.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticDungeon.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, NullGameData.Instance);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, NullGameData.Instance);
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, NullGameData.Instance, new BuildOptions(ModuleName: "SyntheticDungeon"));
        var json = EncounterModelJson.Serialize(em);
        Assert.Empty(EncounterModelJson.Validate(json));

        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var emitted = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm));
        GoldenPaths.AssertEqualOrUpdate("SyntheticDungeon.em.json", json);
        GoldenPaths.AssertEqualOrUpdate("SyntheticDungeon.vbm.cs", emitted.Files[0].Content);
    }
}
