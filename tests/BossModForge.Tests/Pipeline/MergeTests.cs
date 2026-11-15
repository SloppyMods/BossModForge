using System.Collections.Immutable;
using System.Text.Json.Nodes;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Pipeline;

public sealed class MergeTests
{
    [Fact]
    public void Concat_MetaSummedAndActorsRebuilt()
    {
        var a = ReplayParser.Parse(new SyntheticSinglePull().WriteTemp(ReplayEncoding.Text));
        var b = ReplayParser.Parse(new SyntheticTwoPulls().WriteTemp(ReplayEncoding.Text));
        var joint = Recording.Concat([a, b]);

        Assert.Equal(a.Meta.Path + "+" + b.Meta.Path, joint.Meta.Path);
        Assert.Equal(a.Meta.Encoding, joint.Meta.Encoding);
        Assert.Equal(Math.Max(a.Meta.Version, b.Meta.Version), joint.Meta.Version);
        Assert.Equal(a.Meta.Qpf, joint.Meta.Qpf);
        Assert.Equal(string.IsNullOrEmpty(a.Meta.GameVersion) ? b.Meta.GameVersion : a.Meta.GameVersion, joint.Meta.GameVersion);
        Assert.Equal(a.Meta.StartTime, joint.Meta.StartTime);
        Assert.Equal(a.Ops.Count + b.Ops.Count, joint.Ops.Count);
        Assert.Equal(a.Issues.Count + b.Issues.Count, joint.Issues.Count);
        foreach (var (tag, count) in a.TagCounts)
            Assert.Equal(count + b.TagCounts.GetValueOrDefault(tag), joint.TagCounts[tag]);
        foreach (var (tag, count) in b.TagCounts)
            Assert.Equal(a.TagCounts.GetValueOrDefault(tag) + count, joint.TagCounts[tag]);
        Assert.Equal(a.Actors.Count + b.Actors.Count, joint.Actors.Count);
        Assert.False(joint.Degraded);
    }

    [Fact]
    public void Merge_IsAssociative_AndMatchesConcat()
    {
        var a = ReplayParser.Parse(new SyntheticSinglePull().WriteTemp(ReplayEncoding.Text));
        var bc = ReplayParser.Parse(new SyntheticTwoPulls().WriteTemp(ReplayEncoding.Text));
        var all = ReplayParser.Parse(new SyntheticThreePulls().WriteTemp(ReplayEncoding.Text));
        var options = new PipelineOptions(Segmenter: SyntheticMerge.PullOptions);

        var merge1 = ForgePipeline.Analyze([a, bc], NullGameData.Instance, options).Effective;
        var merge2 = ForgePipeline.Analyze([Recording.Concat([a, bc])], NullGameData.Instance, options).Effective;
        var joint = ForgePipeline.Analyze([all], NullGameData.Instance, options).Effective;

        // SourceInfo.Path/Sha256/Start/End and per-pull source paths necessarily differ between one
        // 3-pull file and a concat of 1+2; everything else must match byte-for-byte once normalized.
        Assert.Equal(Normalize(joint), Normalize(merge2));
        Assert.Equal(PullsJson(joint), PullsJson(merge2));
        Assert.Equal(3, merge1.Pulls.Length);
        Assert.Equal(2, merge1.Sources.Length);
        Assert.Single(merge2.Sources);
    }

    [Fact]
    public void DoorBoss_TwoOidsInOneCfc_YieldsTwoPulls()
    {
        var log = new SyntheticDoorBoss().WriteTemp(ReplayEncoding.Text);
        try
        {
            var rec = ForgePipeline.Parse(log);
            var pulls = PullSegmenter.Segment(rec, SyntheticDoorBoss.PullOptions);
            Assert.Equal(2, pulls.Length);
            Assert.Equal(
                [SyntheticDoorBoss.BossAOid, SyntheticDoorBoss.BossBOid],
                pulls.Select(p => p.PrimaryOid).ToArray());

            var em = ForgePipeline.Analyze(
                [rec],
                NullGameData.Instance,
                new PipelineOptions(Segmenter: SyntheticDoorBoss.PullOptions)).Effective;
            // Tied pull counts → deterministic min-OID pick, flagged for review.
            Assert.Equal(SyntheticDoorBoss.BossAOid, em.Encounter.PrimaryOid);
            Assert.Contains(em.Review, r => r.Subject == "encounter");
        }
        finally
        {
            File.Delete(log);
        }
    }

    private static string Normalize(EncounterModel em)
        => EncounterModelJson.Serialize(em with
        {
            Sources = [],
            Pulls = BlankSources(em.Pulls),
        });

    // PullSummary contains ImmutableArray<Confidence.Reasons>, whose Equals is reference-based, so
    // value-compare via the canonical serialization instead of record equality.
    private static string PullsJson(EncounterModel em)
        => JsonNode.Parse(Normalize(em))!["pulls"]!.ToJsonString();

    private static ImmutableArray<PullSummary> BlankSources(ImmutableArray<PullSummary> pulls)
        => [.. pulls.Select(p => p with { Source = "" })];
}
