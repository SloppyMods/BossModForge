using BossModForge.Core.Emit;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Golden;

public sealed class SyntheticShapesGoldenTests
{
    [Fact]
    public void Pipeline_MatchesGoldenFiles()
    {
        using var stream = SyntheticShapes.Build().Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "SyntheticShapes.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticShapes.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, NullGameData.Instance);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, NullGameData.Instance);
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, NullGameData.Instance, new BuildOptions(ModuleName: "SyntheticShapes"));
        var json = EncounterModelJson.Serialize(em);
        Assert.Empty(EncounterModelJson.Validate(json));
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var emitted = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm));
        var cs = emitted.Files[0].Content;
        Assert.Contains("StandardAOEs", cs, StringComparison.Ordinal);
        Assert.Contains("AOEShapeCone", cs, StringComparison.Ordinal);
        Assert.Contains("AOEShapeCircle", cs, StringComparison.Ordinal);
        Assert.Contains("RaidwideCast", cs, StringComparison.Ordinal);
        GoldenPaths.AssertEqualOrUpdate("SyntheticShapes.em.json", json);
        GoldenPaths.AssertEqualOrUpdate("SyntheticShapes.vbm.cs", cs);
    }
}
