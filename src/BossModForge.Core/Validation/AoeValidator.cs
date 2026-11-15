using System.Collections.Immutable;
using System.Globalization;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Validation;

public static class AoeValidator
{
    private static readonly HashSet<ShapeKind> CountedKinds =
    [
        ShapeKind.Circle,
        ShapeKind.Donut,
        ShapeKind.Cone,
        ShapeKind.Rect,
        ShapeKind.Cross,
    ];

    public static ImmutableArray<AoeReport> Evaluate(
        EncounterModel em,
        IReadOnlyList<RecordingContext> contexts,
        ValidationOptions options)
    {
        var units = ComponentPlanner.Plan(em);
        var reports = ImmutableArray.CreateBuilder<AoeReport>();
        var abilities = em.Abilities.IsDefaultOrEmpty ? [] : em.Abilities;
        foreach (var ability in abilities.OrderBy(a => a.ActionId))
        {
            if (ability.Shape is null || !CountedKinds.Contains(ability.Shape.Kind))
                continue;
            reports.Add(EvaluateAbility(ability, units, contexts, options));
        }

        return reports.ToImmutable();
    }

    private static AoeReport EvaluateAbility(
        AbilityModel ability,
        ImmutableArray<ComponentUnit> units,
        IReadOnlyList<RecordingContext> contexts,
        ValidationOptions options)
    {
        var shape = ability.Shape!;
        var lag = ability.ShapeFit?.LagSeconds ?? 0f;
        var tp = 0;
        var fp = 0;
        var fn = 0;
        var tn = 0;
        var instances = 0;
        foreach (var ctx in contexts)
        {
            Ability? cataloged = null;
            foreach (var a in ctx.Catalog.Abilities.IsDefaultOrEmpty ? [] : ctx.Catalog.Abilities)
            {
                if (a.Action.Id == ability.ActionId)
                {
                    cataloged = a;
                    break;
                }
            }

            if (cataloged is null)
                continue;
            var observations = ShapeObservations.Observe(cataloged, ctx.Recording, ctx.Pulls, lag);
            instances += observations.Length;
            foreach (var obs in observations)
            {
                var origin = AoeFitters.OriginOf(obs, shape.Origin);
                var direction = AoeFitters.DirectionOf(obs, origin, shape.Direction);
                foreach (var (pos, actual) in obs.Players.IsDefaultOrEmpty ? [] : obs.Players)
                {
                    var predicted = shape.Contains(pos, origin, direction);
                    if (predicted && actual)
                        tp++;
                    else if (predicted)
                        fp++;
                    else if (actual)
                        fn++;
                    else
                        tn++;
                }
            }
        }

        var precision = tp + fp == 0 ? 1.0 : (double)tp / (tp + fp);
        var recall = tp + fn == 0 ? 1.0 : (double)tp / (tp + fn);
        var passed = precision >= options.AoeMinPrecision && recall >= options.AoeMinRecall;
        var component = units.FirstOrDefault(u => !u.Aids.IsDefaultOrEmpty && u.Aids.Contains(ability.ActionId))?.ClassName
            ?? ability.EnumName;
        return new AoeReport(component, ability.ActionId, instances, tp, fp, fn, tn, precision, recall, passed);
    }

    public static ReviewItem ShapeReview(AoeReport report)
        => new(
            "shape-" + report.Aid.ToString("X8", CultureInfo.InvariantCulture),
            ReviewSeverity.High,
            "shape",
            $"AID {report.Aid.ToString(CultureInfo.InvariantCulture)} precision {report.Precision.ToString("0.000", CultureInfo.InvariantCulture)} recall {report.Recall.ToString("0.000", CultureInfo.InvariantCulture)} below threshold",
            ["Refit the telegraph or lower confidence"]);
}
