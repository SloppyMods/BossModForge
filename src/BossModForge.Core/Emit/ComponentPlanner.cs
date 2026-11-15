using System.Collections.Immutable;
using System.Globalization;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Core.Emit;

public static class ComponentPlanner
{
    public static ImmutableArray<ComponentUnit> Plan(EncounterModel em)
    {
        var units = new List<(uint FirstId, string Name, string Kind, ImmutableArray<uint> Aids)>();
        foreach (var ability in em.Abilities.Where(a => a.Class == MechanicClass.Raidwide).OrderBy(a => a.ActionId))
            units.Add((ability.ActionId, ability.EnumName, "RaidwideCast", [ability.ActionId]));

        var aoes = em.Abilities
            .Where(a => (a.Class == MechanicClass.TelegraphedAOE || a.Class == MechanicClass.HiddenAOE) && a.Shape is not null)
            .GroupBy(a => a.Shape!)
            .Select(g => g.OrderBy(a => a.ActionId).ToArray())
            .OrderBy(g => g[0].ActionId);
        foreach (var group in aoes)
        {
            var names = group.Select(a => a.EnumName).ToArray();
            var aids = group.Select(a => a.ActionId).ToImmutableArray();
            if (group.Length == 1)
            {
                var shape = group[0].Shape!;
                var kind = shape.Kind == ShapeKind.Circle && shape.Origin == ShapeOrigin.CastLocation
                    ? "LocationTargetedAOEs"
                    : "SimpleAOEs";
                units.Add((group[0].ActionId, names[0], kind, aids));
            }
            else
            {
                var className = group.Length <= 3 ? string.Join("And", names) : names[0] + "AndOthers";
                units.Add((group[0].ActionId, className, "SimpleAOEGroups", aids));
            }
        }

        foreach (var ability in em.Abilities.OrderBy(a => a.ActionId))
        {
            switch (ability.Class)
            {
                case MechanicClass.Tankbuster:
                    units.Add((ability.ActionId, ability.EnumName, "SingleTargetCast", [ability.ActionId]));
                    break;
                case MechanicClass.Spread:
                    units.Add((ability.ActionId, ability.EnumName, "SpreadFromCastTargets", [ability.ActionId]));
                    break;
                case MechanicClass.Stack:
                    units.Add((ability.ActionId, ability.EnumName, "StackWithCastTargets", [ability.ActionId]));
                    break;
                case MechanicClass.Knockback:
                    units.Add((ability.ActionId, ability.EnumName, "KnockbackFromCastTarget", [ability.ActionId]));
                    break;
                case MechanicClass.Gaze:
                    units.Add((ability.ActionId, ability.EnumName, "CastGaze", [ability.ActionId]));
                    break;
            }
        }

        units.Sort((a, b) => a.FirstId.CompareTo(b.FirstId));
        var deduped = CSharpIdentifier.Dedupe(units.Select(u => u.Name));
        var result = ImmutableArray.CreateBuilder<ComponentUnit>(units.Count);
        for (var i = 0; i < units.Count; i++)
            result.Add(new ComponentUnit(deduped[i], units[i].Kind, units[i].Aids));
        return result.ToImmutable();
    }

    public static EncounterModel Place(EncounterModel em)
    {
        if (em.Timeline is null)
            return em;
        var units = Plan(em);
        var timeline = em.Timeline;
        var steps = timeline.Steps.ToArray();
        var phases = timeline.Phases.ToArray();
        var loopStart = timeline.Loop?.StartStep ?? int.MaxValue;
        var loopEnd = timeline.Loop is null ? int.MinValue : timeline.Loop.StartStep + timeline.Loop.Length;

        foreach (var unit in units)
        {
            var aidSet = unit.Aids.ToHashSet();
            var hitsByPhase = new List<int>[phases.Length];
            for (var p = 0; p < phases.Length; p++)
            {
                hitsByPhase[p] = [];
                var phase = phases[p];
                for (var s = phase.FirstStep; s < phase.FirstStep + phase.StepCount && s < steps.Length; s++)
                {
                    if (StepUses(steps[s], aidSet))
                        hitsByPhase[p].Add(s);
                }
            }

            var hitPhases = Enumerable.Range(0, phases.Length).Where(p => hitsByPhase[p].Count > 0).ToArray();
            var neverAnchored = hitPhases.Length == 0;
            var inLoop = hitPhases.SelectMany(p => hitsByPhase[p]).Any(i => i >= loopStart && i < loopEnd);
            var phaseLevel = neverAnchored || inLoop || hitPhases.Length >= 2;
            if (phaseLevel)
            {
                var targets = neverAnchored ? Enumerable.Range(0, phases.Length) : hitPhases.AsEnumerable();
                foreach (var p in targets)
                {
                    var activate = phases[p].Activate.IsDefault ? ImmutableArray<string>.Empty : phases[p].Activate;
                    if (!activate.Contains(unit.ClassName))
                        phases[p] = phases[p] with { Activate = activate.Add(unit.ClassName) };
                }

                continue;
            }

            var hits = hitsByPhase[hitPhases[0]];
            var min = hits.Min();
            var max = hits.Max();
            steps[min] = WithActivate(steps[min], unit.ClassName);
            steps[max] = WithDeactivate(steps[max], unit.ClassName);
        }

        for (var p = 0; p < phases.Length; p++)
        {
            if (!phases[p].Activate.IsDefaultOrEmpty)
                phases[p] = phases[p] with { Activate = [.. phases[p].Activate.OrderBy(n => n, StringComparer.Ordinal)] };
        }

        return em with { Timeline = timeline with { Phases = [.. phases], Steps = [.. steps] } };
    }

    private static bool StepUses(TimelineStep step, HashSet<uint> aids)
    {
        if (step.Aid is uint aid && aids.Contains(aid))
            return true;
        if (step.Of.IsDefaultOrEmpty)
            return false;
        return step.Of.Any(aids.Contains);
    }

    private static TimelineStep WithActivate(TimelineStep step, string name)
    {
        var list = step.Activate.IsDefault ? ImmutableArray<string>.Empty : step.Activate;
        if (list.Contains(name))
            return step;
        return step with { Activate = list.Add(name).Sort(StringComparer.Ordinal) };
    }

    private static TimelineStep WithDeactivate(TimelineStep step, string name)
    {
        var list = step.Deactivate.IsDefault ? ImmutableArray<string>.Empty : step.Deactivate;
        if (list.Contains(name))
            return step;
        return step with { Deactivate = list.Add(name).Sort(StringComparer.Ordinal) };
    }
}

file static class ImmutableArrayStringExtensions
{
    public static ImmutableArray<string> Sort(this ImmutableArray<string> values, StringComparer comparer)
        => [.. values.OrderBy(v => v, comparer)];
}
