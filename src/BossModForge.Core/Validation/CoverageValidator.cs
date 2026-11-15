using System.Collections.Immutable;
using System.Globalization;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Core.Validation;

public static class CoverageValidator
{
    public static CoverageReport Evaluate(EncounterModel em, IReadOnlyList<RecordingContext> contexts)
    {
        var participantOids = (em.Participants.IsDefaultOrEmpty ? [] : em.Participants)
            .Select(p => p.Oid)
            .ToHashSet();
        var planned = ComponentPlanner.Plan(em)
            .SelectMany(u => u.Aids.IsDefaultOrEmpty ? [] : u.Aids)
            .ToHashSet();
        var uncovered = new SortedSet<uint>();
        var damaging = 0;
        var covered = 0;
        var abilities = em.Abilities.IsDefaultOrEmpty ? [] : em.Abilities;
        foreach (var ability in abilities.OrderBy(a => a.ActionId))
        {
            if (!participantOids.Contains(ability.SourceOid))
                continue;
            if (!HasDamagingPlayerHit(ability.ActionId, contexts))
                continue;
            damaging++;
            if (planned.Contains(ability.ActionId))
                covered++;
            else
                uncovered.Add(ability.ActionId);
        }

        var fraction = damaging == 0 ? 1.0 : (double)covered / damaging;
        return new CoverageReport(damaging, covered, fraction, [.. uncovered]);
    }

    public static ReviewItem? CoverageReview(CoverageReport report)
    {
        if (report.Fraction >= 1)
            return null;
        var aids = string.Join(", ", report.UncoveredAids.Select(a => a.ToString(CultureInfo.InvariantCulture)));
        return new ReviewItem(
            "coverage",
            ReviewSeverity.Medium,
            "coverage",
            "damaging enemy casts not covered by a component: " + aids,
            ["Add a component or reclassify the ability"]);
    }

    private static bool HasDamagingPlayerHit(uint aid, IReadOnlyList<RecordingContext> contexts)
    {
        foreach (var ctx in contexts)
        {
            foreach (var ability in ctx.Catalog.Abilities.IsDefaultOrEmpty ? [] : ctx.Catalog.Abilities)
            {
                if (ability.Action.Id != aid)
                    continue;
                foreach (var inst in ability.Instances.IsDefaultOrEmpty ? [] : ability.Instances)
                {
                    if (inst.PullIndex < 0 || inst.PullIndex >= ctx.Pulls.Length)
                        continue;
                    if (ShapeObservations.HasPlayerDamage(inst, ctx.Pulls[inst.PullIndex]))
                        return true;
                }
            }
        }

        return false;
    }
}
