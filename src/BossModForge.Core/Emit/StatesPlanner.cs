using System.Collections.Immutable;
using System.Globalization;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Core.Emit;

public static class StatesPlanner
{
    public const uint StepStride = 0x10000;

    public static StatesPlan Plan(EncounterModel em, IReadOnlyDictionary<uint, string> aidEnumNames)
    {
        var units = ComponentPlanner.Plan(em);
        var trivial = units.Select(u => u.ClassName).ToImmutableArray();
        if (em.Timeline is null || em.Timeline.Steps.IsDefaultOrEmpty)
            return new StatesPlan(em.Encounter.ModuleName, [], trivial);

        var timeline = em.Timeline;
        var phases = ImmutableArray.CreateBuilder<PhasePlan>();
        for (var p = 0; p < timeline.Phases.Length; p++)
        {
            var phase = timeline.Phases[p];
            var isLast = p == timeline.Phases.Length - 1;
            var slice = Slice(timeline.Steps, phase.FirstStep, phase.StepCount);
            LoopPlan? loopPlan = null;
            ImmutableArray<StepPlan> prefix;
            var tail = ImmutableArray<StepPlan>.Empty;
            if (isLast && timeline.Loop is { } loop
                && loop.StartStep >= phase.FirstStep
                && loop.StartStep < phase.FirstStep + phase.StepCount)
            {
                var localStart = loop.StartStep - phase.FirstStep;
                prefix = [.. slice.Take(localStart).Select((s, j) => ToStep(s, (uint)j * StepStride, aidEnumNames, em.Review))];
                var bodySteps = slice.Skip(localStart).Take(loop.Length).ToArray();
                var body = bodySteps.Select((s, j) => ToStep(s, (uint)j * StepStride, aidEnumNames, em.Review)).ToImmutableArray();
                var stride = StepStride * (uint)loop.Length;
                loopPlan = new LoopPlan(
                    "Rotation" + p.ToString(CultureInfo.InvariantCulture),
                    body,
                    loop.Repeats,
                    loop.TrailingPartial,
                    loop.EntryDelay,
                    StepStride * (uint)localStart,
                    stride);
                var tailOffset = loopPlan.FirstCallOffset + stride * (uint)loop.Repeats;
                tail = [.. bodySteps.Take(loop.TrailingPartial).Select((s, j) => ToStep(s, tailOffset + (uint)j * StepStride, aidEnumNames, em.Review))];
            }
            else
            {
                prefix = [.. slice.Select((s, j) => ToStep(s, (uint)j * StepStride, aidEnumNames, em.Review))];
            }

            var names = CSharpIdentifier.Dedupe(["Phase" + p.ToString(CultureInfo.InvariantCulture)]);
            phases.Add(new PhasePlan(
                p,
                names[0],
                phase.Kind,
                phase.HpThreshold,
                "P" + p.ToString(CultureInfo.InvariantCulture),
                phase.Activate.IsDefault ? [] : phase.Activate,
                prefix,
                loopPlan,
                tail));
        }

        return new StatesPlan(em.Encounter.ModuleName, phases.ToImmutable(), trivial);
    }

    private static ImmutableArray<TimelineStep> Slice(ImmutableArray<TimelineStep> steps, int first, int count)
    {
        if (steps.IsDefaultOrEmpty || first < 0 || first >= steps.Length || count <= 0)
            return [];
        var take = Math.Min(count, steps.Length - first);
        return [.. steps.Skip(first).Take(take)];
    }

    private static StepPlan ToStep(
        TimelineStep step,
        uint offset,
        IReadOnlyDictionary<uint, string> aidEnumNames,
        ImmutableArray<ReviewItem> review)
    {
        string? member = null;
        if (step.Aid is uint aid)
            member = aidEnumNames.GetValueOrDefault(aid, "Ability" + aid.ToString(CultureInfo.InvariantCulture));
        var ofMembers = (step.Of.IsDefault ? [] : step.Of)
            .Select(id => aidEnumNames.GetValueOrDefault(id, "Ability" + id.ToString(CultureInfo.InvariantCulture)))
            .ToImmutableArray();
        string? comment = null;
        if (!string.IsNullOrEmpty(step.Review))
        {
            var item = review.IsDefault
                ? null
                : review.FirstOrDefault(r => r.Subject == "timeline" && r.Question.Contains(step.Id, StringComparison.Ordinal));
            var id = item?.Id ?? "R?";
            var sev = item?.Severity.ToString() ?? "Medium";
            comment = $"TODO(forge:{id}, {sev}): {step.Review}";
        }

        return new StepPlan(
            offset,
            step.Kind,
            member,
            ofMembers,
            step.Targetable,
            step.Delay,
            step.CastTime,
            step.Delay + step.CastTime,
            step.Name,
            step.Activate.IsDefault ? [] : step.Activate,
            step.Deactivate.IsDefault ? [] : step.Deactivate,
            comment);
    }
}
