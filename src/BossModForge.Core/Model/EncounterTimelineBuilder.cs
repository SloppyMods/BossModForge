using System.Collections.Immutable;
using System.Globalization;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Inference.Timeline;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Model;

internal static class EncounterTimelineBuilder
{
    public static EncounterModel Attach(
        EncounterModel em,
        Recording recording,
        ImmutableArray<Pull> pulls,
        AbilityCatalog catalog,
        ImmutableArray<ReviewItem>.Builder review)
    {
        var anchors = AnchorExtractor.Extract(recording, pulls, catalog);
        var split = PhaseDetector.Detect(recording, pulls, anchors);
        var perPhase = PhaseDetector.SplitByPhase(recording, pulls, anchors, split);
        // The same action id can be cast by several OIDs (one AbilityModel per (SourceOid, ActionId)); first entry wins.
        var castTimeByAid = em.Abilities.GroupBy(a => a.ActionId).ToDictionary(g => g.Key, g => g.First().CastTime);
        var aidNames = em.Enums.Aid;
        var phases = ImmutableArray.CreateBuilder<PhaseModel>();
        var steps = ImmutableArray.CreateBuilder<TimelineStep>();
        var evidence = ImmutableArray.CreateBuilder<Evidence>();
        LoopModel? loopModel = null;
        Confidence? conf = null;
        var pullCount = 0;

        for (var p = 0; p < perPhase.Length; p++)
        {
            var aligned = TimelineAligner.Align(perPhase[p], pulls, castTimeByAid);
            pullCount = Math.Max(pullCount, aligned.PullCount);
            evidence.AddRange(aligned.Evidence.IsDefault ? [] : aligned.Evidence);
            var isLast = p == perPhase.Length - 1;
            Loop? loop = aligned.Loop;
            var alignedSteps = aligned.Steps;
            if (loop is not null && !isLast)
            {
                evidence.Add(new Evidence("LoopIgnored", $"phase {p} loop ignored (not last phase)"));
                alignedSteps = UnrollIgnoredLoop(aligned);
                loop = null;
            }

            if (loop is not null && isLast)
                loopModel = new LoopModel(loop.StartStep + steps.Count, loop.Length, loop.Repeats, loop.TrailingPartial, loop.EntryDelay);

            var first = steps.Count;
            var hp = p < split.HpThresholds.Length ? split.HpThresholds[p] : (double?)null;
            var kind = hp is not null ? "HpThreshold" : "Death";
            for (var s = 0; s < alignedSteps.Length; s++)
            {
                if (steps.Count - first >= 255)
                {
                    review.Add(new ReviewItem("", ReviewSeverity.High, "timeline", "phase too long, truncated", []));
                    break;
                }

                steps.Add(ToStep(alignedSteps[s], p, s, aidNames, em.Abilities));
            }

            phases.Add(new PhaseModel(p, "Phase" + p.ToString(CultureInfo.InvariantCulture), kind, hp, first, steps.Count - first, []));
            conf = aligned.Confidence;
            if (hp is not null)
            {
                review.Add(new ReviewItem(
                    "",
                    ReviewSeverity.Medium,
                    "timeline",
                    $"HP {hp.Value.ToString("P0", CultureInfo.InvariantCulture)} phase split inferred from casting stop",
                    []));
            }
        }

        evidence.AddRange(split.Evidence.IsDefault ? [] : split.Evidence);
        if (steps.Count == 0)
        {
            review.Add(new ReviewItem("", ReviewSeverity.Low, "timeline", "no timeline steps — TrivialPhase emitted", []));
            phases.Clear();
        }

        var timeline = new TimelineModel(
            phases.ToImmutable(),
            steps.ToImmutable(),
            loopModel,
            pullCount,
            conf ?? Confidence.Of(0),
            evidence.ToImmutable());
        var withTimeline = em with { Timeline = timeline };
        withTimeline = ComponentPlanner.Place(withTimeline);
        foreach (var step in withTimeline.Timeline!.Steps)
        {
            if (!string.IsNullOrEmpty(step.Review))
            {
                review.Add(new ReviewItem(
                    "",
                    ReviewSeverity.Medium,
                    "timeline",
                    $"step {step.Id}: {step.Review}",
                    []));
            }
        }

        return withTimeline;
    }

    private static ImmutableArray<ConsensusStep> UnrollIgnoredLoop(Timeline aligned)
    {
        if (aligned.Loop is null)
            return aligned.Steps;
        var loop = aligned.Loop;
        var prefix = aligned.Steps.Take(loop.StartStep);
        var body = aligned.Steps.Skip(loop.StartStep).Take(loop.Length).ToArray();
        var unrolled = new List<ConsensusStep>();
        unrolled.AddRange(prefix);
        for (var r = 0; r < loop.Repeats; r++)
            unrolled.AddRange(body);
        unrolled.AddRange(body.Take(loop.TrailingPartial));
        return unrolled.Select((s, i) => s with { Index = i }).ToImmutableArray();
    }

    private static TimelineStep ToStep(
        ConsensusStep step,
        int phaseIndex,
        int unfoldedIndex,
        ImmutableSortedDictionary<uint, string> aidNames,
        ImmutableArray<AbilityModel> abilities)
    {
        var id = $"0x{(phaseIndex << 24) + (int)StatesPlanner.StepStride * unfoldedIndex:X8}";
        var name = StepName(step, aidNames, abilities);
        uint? aid = step.Kind == StepKind.Targetable ? null : step.Aid;
        var review = step.ReviewReason;
        if (review is not null && aidNames.Count > 0)
        {
            foreach (var (num, enumName) in aidNames.OrderByDescending(kv => kv.Key.ToString(CultureInfo.InvariantCulture).Length))
                review = review.Replace(num.ToString(CultureInfo.InvariantCulture), enumName, StringComparison.Ordinal);
        }

        return new TimelineStep(
            id,
            step.Kind.ToString(),
            aid,
            step.DelayMedian,
            step.CastTime,
            step.DelayMad,
            step.Support,
            step.Of.IsDefault ? [] : step.Of,
            name,
            [],
            [],
            review,
            step.Targetable);
    }

    internal static string StepName(
        ConsensusStep step,
        ImmutableSortedDictionary<uint, string> aidNames,
        ImmutableArray<AbilityModel> abilities)
    {
        if (step.Kind == StepKind.Targetable)
            return step.Targetable == true ? "Boss reappears" : "Boss disappears";
        string NameOf(uint id)
        {
            var model = abilities.FirstOrDefault(a => a.ActionId == id);
            var enumName = aidNames.GetValueOrDefault(id, "Ability" + id.ToString(CultureInfo.InvariantCulture));
            if (model is null)
                return enumName;
            if (string.IsNullOrEmpty(model.Name) || model.Name == enumName || model.Name == "Ability" + id.ToString(CultureInfo.InvariantCulture))
                return enumName;
            return model.Name;
        }

        if (step.Kind == StepKind.CastMulti)
            return string.Join(" / ", step.Of.Select(NameOf));
        return NameOf(step.Aid);
    }
}
