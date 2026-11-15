using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BossModForge.Core.Inference.Timeline;

public static class TimelineAligner
{
    public static Timeline Align(
        ImmutableArray<ImmutableArray<AnchorEvent>> anchorsPerPull,
        ImmutableArray<Pull> pulls,
        IReadOnlyDictionary<uint, float> castTimeByAid,
        TimelineAlignerOptions? options = null)
    {
        options ??= TimelineAlignerOptions.Default;
        if (pulls.IsDefault)
            pulls = [];
        if (anchorsPerPull.IsDefault)
            anchorsPerPull = [];

        var pullStarts = pulls.ToDictionary(p => p.Index, p => p.Start);
        var pullByIndex = pulls.ToDictionary(p => p.Index);
        var sequences = new List<(int PullIndex, ImmutableArray<AnchorEvent> Anchors)>();
        for (var i = 0; i < anchorsPerPull.Length; i++)
        {
            var seq = anchorsPerPull[i].IsDefault ? ImmutableArray<AnchorEvent>.Empty : anchorsPerPull[i];
            var pullIndex = i < pulls.Length ? pulls[i].Index : i;
            if (seq.Length > 0)
                pullIndex = seq[0].PullIndex;
            sequences.Add((pullIndex, seq));
        }

        sequences.Sort((a, b) =>
        {
            var c = b.Anchors.Length.CompareTo(a.Anchors.Length);
            return c != 0 ? c : a.PullIndex.CompareTo(b.PullIndex);
        });

        var profile = ImmutableArray<AlignmentColumn>.Empty;
        foreach (var (pullIndex, seq) in sequences)
        {
            pullStarts.TryGetValue(pullIndex, out var start);
            profile = ProfileAligner.AlignInto(profile, seq, options, start, pullStarts);
        }

        var pullCount = anchorsPerPull.Count(s => !s.IsDefaultOrEmpty);
        if (pullCount == 0)
            pullCount = pulls.Length;

        var unfolded = ConsensusBuilder.Build(profile, pullByIndex, pullStarts, pullCount, castTimeByAid, options);
        var evidence = ImmutableArray.CreateBuilder<Evidence>();
        for (var c = 0; c < profile.Length && c < unfolded.Length; c++)
            evidence.Add(ColumnEvidence(profile[c], unfolded[c]));

        var (steps, loop) = LoopFolder.Fold(unfolded, options, pullCount);
        if (loop is not null)
        {
            evidence.Add(new Evidence(
                "Loop",
                $"start={loop.StartStep} length={loop.Length} repeats={loop.Repeats} trailing={loop.TrailingPartial} entry={loop.EntryDelay.ToString("0.0", CultureInfo.InvariantCulture)}"));
        }

        var timeoutFraction = steps.Length == 0 ? 0 : steps.Count(s => s.Kind == StepKind.Timeout) / (double)steps.Length;
        var meanSupport = steps.Length == 0 ? 0 : steps.Average(s => pullCount == 0 ? 0 : s.Support / (double)pullCount);
        var conf = Confidence.Of(Math.Min(1, meanSupport) * (1 - timeoutFraction));
        return new Timeline(steps, loop, pullCount, conf, evidence.ToImmutable());
    }

    private static Evidence ColumnEvidence(AlignmentColumn column, ConsensusStep step)
    {
        var data = new JsonObject
        {
            ["kind"] = step.Kind.ToString(),
            ["aid"] = step.Aid,
            ["support"] = step.Support,
            ["delayMedian"] = step.DelayMedian,
            ["delayMad"] = step.DelayMad,
            ["members"] = new JsonArray([.. column.Members.Select(m => new JsonObject
            {
                ["pull"] = m.PullIndex,
                ["t"] = m.T.ToString("O", CultureInfo.InvariantCulture),
                ["aid"] = m.Aid,
            })]),
        };
        return new Evidence(
            "Alignment",
            $"{step.Kind} aid={step.Aid} support={step.Support}/{step.PullCount}",
            JsonSerializer.SerializeToElement(data));
    }
}
