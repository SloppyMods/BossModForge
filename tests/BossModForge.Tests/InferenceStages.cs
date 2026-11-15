using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit.Sdk;

namespace BossModForge.Tests;

internal static class InferenceStages
{
    public static void SkipIfUnimplemented()
    {
        try
        {
            using var stream = SyntheticDungeon.Build().Open(ReplayEncoding.Text);
            var rec = ReplayParser.Parse(stream, "SyntheticDungeon.log");
            var pulls = PullSegmenter.Segment(rec, SyntheticDungeon.PullOptions);
            var participants = ParticipantClassifier.Classify(rec, pulls, NullGameData.Instance);
            _ = AbilityCataloger.Catalog(rec, pulls, participants, NullGameData.Instance);
        }
        catch (NotImplementedException)
        {
            throw SkipException.ForSkip("waiting on Inference");
        }
    }
}
