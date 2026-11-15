using BossModForge.Cli.Commands;
using BossModForge.Core.Emit;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Pipeline;

public sealed class ForgePipelineTests
{
    [Fact]
    public void Analyze_MatchesInferCommandGoldens()
    {
        AssertGolden("SyntheticDungeon", () => SyntheticDungeon.Build(), SyntheticDungeon.PullOptions, NullGameData.Instance, "SyntheticDungeon");
        AssertGolden("SyntheticShapes", () => SyntheticShapes.Build(), SyntheticShapes.PullOptions, NullGameData.Instance, "SyntheticShapes");
        AssertGolden("SyntheticRotation", () => SyntheticRotation.Build(), SyntheticRotation.PullOptions, SyntheticRotation.GameData, "SyntheticRotation");
        AssertGolden("SyntheticPhases", () => SyntheticPhases.Build(), SyntheticPhases.PullOptions, SyntheticPhases.GameData, "SyntheticPhases");

        var log = SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text);
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = InferCommand.Run(log, null, null, null, null, stdout, stderr);
            Assert.True(exit is 0 or 2, stderr.ToString());
            var rec = ForgePipeline.Parse(log);
            var analyzed = ForgePipeline.Analyze(
                rec,
                NullGameData.Instance,
                new PipelineOptions(Build: new BuildOptions(null, null)));
            Assert.Equal(EncounterModelJson.Serialize(analyzed.Effective), stdout.ToString());
        }
        finally
        {
            File.Delete(log);
        }
    }

    [Fact]
    public void Analyze_NoOverrides_EffectiveIsInferred()
    {
        using var stream = SyntheticDungeon.Build().Open(ReplayEncoding.Text);
        var rec = ForgePipeline.Parse(stream, "SyntheticDungeon.log");
        var result = ForgePipeline.Analyze(
            rec,
            NullGameData.Instance,
            new PipelineOptions(Segmenter: SyntheticDungeon.PullOptions, Build: new BuildOptions(ModuleName: "SyntheticDungeon")));
        Assert.True(ReferenceEquals(result.Inferred, result.Effective));
    }

    [Fact]
    public void Analyze_Progress_IsMonotonicAndEndsAtOne()
    {
        using var stream = SyntheticDungeon.Build().Open(ReplayEncoding.Text);
        var rec = ForgePipeline.Parse(stream, "SyntheticDungeon.log");
        var progress = new CollectingProgress();
        _ = ForgePipeline.Analyze(
            rec,
            NullGameData.Instance,
            new PipelineOptions(Segmenter: SyntheticDungeon.PullOptions),
            progress: progress);
        var reports = progress.Reports;
        Assert.NotEmpty(reports);
        Assert.Equal(1.0f, reports[^1].Fraction);
        Assert.Equal(PipelineStage.Done, reports[^1].Stage);
        for (var i = 1; i < reports.Count; i++)
            Assert.True(reports[i].Fraction >= reports[i - 1].Fraction, $"{reports[i - 1].Fraction} -> {reports[i].Fraction}");
        Assert.All(reports, p => Assert.InRange(p.Fraction, 0f, 1f));
    }

    [Fact]
    public void Analyze_Cancelled_Throws()
    {
        using var stream = SyntheticDungeon.Build().Open(ReplayEncoding.Text);
        var rec = ForgePipeline.Parse(stream, "SyntheticDungeon.log");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ForgePipeline.Analyze(
            rec,
            NullGameData.Instance,
            ct: cts.Token));
    }

    [Fact]
    public void CollectKeys_SyntheticDungeon()
    {
        using var stream = SyntheticDungeon.Build().Open(ReplayEncoding.Text);
        var rec = ForgePipeline.Parse(stream, "SyntheticDungeon.log");
        var keys = ForgePipeline.CollectKeys(rec);
        Assert.Contains(SyntheticDungeon.HeavySlash, keys.ActionIds);
        Assert.Contains(rec.Actors, a => a.Type == ActorType.Player);
        var spellIds = rec.Ops
            .Select(SpellId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();
        Assert.Subset(spellIds, keys.ActionIds.ToHashSet());
        Assert.Equal(spellIds, keys.ActionIds.ToHashSet());
        Assert.Equal([1234], keys.TerritoryTypes.ToArray());
        Assert.Contains(12716u, keys.BNpcNameIds);
        Assert.Contains(1234u, keys.StatusIds);
    }

    [Fact]
    public void Emit_UsesGivenFlavorsDirectory()
    {
        var em = HandBuiltEncounter.Create();
        var good = Directory.CreateTempSubdirectory("forge-flavors-").FullName;
        var missing = Directory.CreateTempSubdirectory("forge-flavors-missing-").FullName;
        try
        {
            File.Copy(Path.Combine(ModuleEmitter.ResolveFlavorsDirectory(), "vbm.json"), Path.Combine(good, "vbm.json"));
            var result = ForgePipeline.Emit(em, Flavor.Vbm, good);
            Assert.NotEmpty(result.Files[0].Content);
            Assert.Throws<FileNotFoundException>(() => ForgePipeline.Emit(em, Flavor.Vbm, missing));
        }
        finally
        {
            Directory.Delete(good, recursive: true);
            Directory.Delete(missing, recursive: true);
        }
    }

    private static void AssertGolden(
        string name,
        Func<RecordingBuilder> build,
        SegmenterOptions segmenter,
        IGameData gameData,
        string moduleName)
    {
        using var stream = build().Open(ReplayEncoding.Text);
        var rec = ForgePipeline.Parse(stream, name + ".log");
        var result = ForgePipeline.Analyze(
            rec,
            gameData,
            new PipelineOptions(Segmenter: segmenter, Build: new BuildOptions(ModuleName: moduleName)));
        var json = EncounterModelJson.Serialize(result.Effective);
        GoldenPaths.AssertEqualOrUpdate(name + ".em.json", json);
    }

    private static uint? SpellId(Op op) => op switch
    {
        ActorCastStartOp start when start.Action.Type == ActionType.Spell => start.Action.Id,
        ActorCastEventOp ev when ev.Action.Type == ActionType.Spell => ev.Action.Id,
        _ => null,
    };

    private sealed class CollectingProgress : IProgress<PipelineProgress>
    {
        public List<PipelineProgress> Reports { get; } = [];

        public void Report(PipelineProgress value) => Reports.Add(value);
    }
}
