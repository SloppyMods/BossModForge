using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using BossModForge.Core.GameData;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

public sealed record ShapeInferenceOptions(
    float LagMinSeconds = -0.2f,
    float LagMaxSeconds = 1.0f,
    float LagStepSeconds = 0.1f,
    double PriorWeight = 0.35,
    double FitWeight = 0.55,
    double AmbiguityWeight = 0.10,
    float AmbiguityMarginY = 1.0f,
    float AmbiguityMarginDeg = 3f,
    double RaidwideFraction = 0.9,
    float RaidwideArenaRatio = 0.95f,
    float RadiusSnapY = 0.5f,
    float AngleSnapDeg = 3f)
{
    public static readonly ShapeInferenceOptions Default = new();
}

public static class ShapeInference
{
    private static readonly ShapeKind[] KindOrder = [ShapeKind.Circle, ShapeKind.Cone, ShapeKind.Rect, ShapeKind.Donut, ShapeKind.Cross];

    public static ShapePrior? PriorFor(ActionRow? sheet)
    {
        if (sheet is null)
            return null;
        var r = (float)sheet.EffectRange;
        var hw = sheet.XAxisModifier / 2f;
        switch (sheet.CastType)
        {
            case 1:
                return new ShapePrior(ShapeKind.SingleTarget, null, null, null, null, ShapeOrigin.TargetActor, ShapeDirection.ToTarget, false);
            case 2:
                return new ShapePrior(ShapeKind.Circle, r, null, null, null, ShapeOrigin.Source, ShapeDirection.SourceRotation, false);
            case 3:
            {
                var fromOmen = OmenParser.TryParseFan(sheet.OmenPath, out var half);
                return new ShapePrior(ShapeKind.Cone, r, null, fromOmen ? half : null, null, ShapeOrigin.Source, ShapeDirection.SourceRotation, fromOmen);
            }
            case 4:
                return new ShapePrior(ShapeKind.Rect, r, null, null, hw, ShapeOrigin.Source, ShapeDirection.SourceRotation, false);
            case 5:
                return new ShapePrior(ShapeKind.Circle, r, null, null, null, ShapeOrigin.TargetActor, ShapeDirection.SourceRotation, false);
            case 7:
                return new ShapePrior(ShapeKind.Circle, r, null, null, null, ShapeOrigin.CastLocation, ShapeDirection.SourceRotation, false);
            case 8:
                return new ShapePrior(ShapeKind.Rect, null, null, null, hw, ShapeOrigin.Source, ShapeDirection.ToTarget, false);
            case 10:
                return new ShapePrior(ShapeKind.Donut, r, null, null, null, ShapeOrigin.Source, ShapeDirection.SourceRotation, false);
            case 11:
                return new ShapePrior(ShapeKind.Cross, r, null, null, hw, ShapeOrigin.Source, ShapeDirection.SourceRotation, false);
            case 12:
                return new ShapePrior(ShapeKind.Rect, r, null, null, hw, ShapeOrigin.Source, ShapeDirection.SourceRotation, false);
            case 13:
                return new ShapePrior(ShapeKind.Cone, r, null, null, null, ShapeOrigin.Source, ShapeDirection.ToTarget, false);
            default:
                return null;
        }
    }

    public static ShapeFit? Fit(ImmutableArray<ShapeObservation> observations, ShapePrior? prior, float arenaRadius, ShapeInferenceOptions? options = null)
    {
        _ = arenaRadius;
        options ??= ShapeInferenceOptions.Default;
        if (observations.IsDefaultOrEmpty)
            return null;

        var allHit = 0;
        foreach (var o in observations)
        {
            if (o.Players.Length > 0 && o.Players.All(p => p.Hit))
                allHit++;
        }

        var allHitFrac = allHit / (double)observations.Length;
        ShapeFit? raidwide = null;
        if (allHitFrac > 0)
        {
            var score = allHitFrac;
            if (observations.Length < 2)
                score = Math.Min(score, 0.84);
            raidwide = MakeFit(Shape.Raidwide, Shape.Raidwide, score, allHitFrac, prior, 0, 999f, 0, observations.Length, options);
        }

        if (allHitFrac >= options.RaidwideFraction)
            return raidwide;

        var candidates = new List<ShapeFit>();
        if (raidwide is not null && allHitFrac >= 0.5)
            candidates.Add(raidwide);

        if (prior?.Kind == ShapeKind.SingleTarget)
            return MakeFit(Shape.SingleTarget, Shape.SingleTarget, 0.3, 1, prior, 1, 999f, 0, observations.Length, options);

        foreach (var kind in KindOrder)
        {
            foreach (var (origin, dir) in ModesFor(kind, prior))
            {
                var proposal = kind switch
                {
                    ShapeKind.Circle => AoeFitters.Circle(observations, origin, prior, options),
                    ShapeKind.Donut => AoeFitters.Donut(observations, origin, prior, options),
                    ShapeKind.Cone => AoeFitters.Cone(observations, origin, dir, prior, options),
                    ShapeKind.Rect => AoeFitters.Rect(observations, origin, dir, prior, options),
                    ShapeKind.Cross => AoeFitters.Cross(observations, origin, dir, prior, options),
                    _ => null,
                };
                if (proposal is null)
                    continue;
                var scored = ScoreProposal(proposal.Value, observations, prior, options);
                if (scored is not null)
                    candidates.Add(scored);
            }
        }

        if (candidates.Count == 0)
        {
            if (prior is null)
                return null;
            return PriorFallback(prior, observations.Length);
        }

        if (candidates.All(c => c.FitConsistency <= 0) && prior is not null)
            return PriorFallback(prior, observations.Length);
        if (candidates.All(c => c.FitConsistency <= 0) && prior is null)
            return null;

        var best = candidates.OrderByDescending(c => c.Score)
            .ThenByDescending(c => prior is not null && c.Shape.Kind == prior.Kind)
            .ThenBy(c => Array.IndexOf(KindOrder, c.Shape.Kind) is var i and >= 0 ? i : 99)
            .ThenByDescending(c => c.FitConsistency)
            .ThenByDescending(c => c.MinMargin)
            .First();

        if (raidwide is not null && allHitFrac >= 0.5 && raidwide.Score + 1e-6 >= best.Score && best.Shape.Kind != ShapeKind.RaidwideNoShape)
            best = raidwide;

        return best with
        {
            Evidence = best.Evidence.Add(new Evidence("ShapeFit", "candidates", JsonSerializer.SerializeToElement(
                candidates.Select(c => new { kind = c.Shape.Kind.ToString(), score = c.Score, consistency = c.FitConsistency }).ToArray()))),
        };
    }

    public static ShapeFit? Infer(Ability ability, Recording recording, ImmutableArray<Pull> pulls, ArenaFit arena, ShapeInferenceOptions? options = null)
    {
        options ??= ShapeInferenceOptions.Default;
        if (ability.Instances.IsDefaultOrEmpty)
            return null;

        var usable = new List<AbilityInstance>();
        foreach (var inst in ability.Instances)
        {
            if (pulls.IsDefaultOrEmpty || inst.PullIndex < 0 || inst.PullIndex >= pulls.Length)
                continue;
            if (!HasPlayerDamage(inst, pulls[inst.PullIndex]))
                continue;
            usable.Add(inst);
        }

        if (usable.Count == 0)
            return null;

        var prior = PriorFor(ability.Sheet);
        ShapeFit? best = null;
        var bestDelta = float.PositiveInfinity;
        foreach (var delta in LagGrid(options))
        {
            var obs = ImmutableArray.CreateBuilder<ShapeObservation>();
            foreach (var inst in usable)
                obs.Add(Observe(inst, recording, pulls[inst.PullIndex], delta));
            var fit = Fit(obs.ToImmutable(), prior, arena.Radius, options);
            if (fit is null)
                continue;
            if (best is null
                || fit.FitConsistency > best.FitConsistency
                || (Math.Abs(fit.FitConsistency - best.FitConsistency) < 1e-9 && fit.MinMargin > best.MinMargin)
                || (Math.Abs(fit.FitConsistency - best.FitConsistency) < 1e-9 && Math.Abs(fit.MinMargin - best.MinMargin) < 1e-6 && Math.Abs(delta) < Math.Abs(bestDelta)))
            {
                best = fit;
                bestDelta = delta;
            }
        }

        return best is null ? null : best with { LagSeconds = bestDelta };
    }

    public static MechanicClass Classify(Ability ability, ShapeFit? fit, ArenaFit arena, ShapeInferenceOptions? options = null)
    {
        options ??= ShapeInferenceOptions.Default;
        if (fit is null || fit.Shape.Kind == ShapeKind.SingleTarget)
            return MechanicClass.Unclassified;
        if (fit.Shape.Kind == ShapeKind.RaidwideNoShape)
            return MechanicClass.Raidwide;
        if (fit.Shape.Kind == ShapeKind.Circle
            && fit.Shape.Origin == ShapeOrigin.Source
            && fit.Shape.Radius >= options.RaidwideArenaRatio * arena.Radius)
            return MechanicClass.Raidwide;
        if (fit.Shape.Kind is not (ShapeKind.Circle or ShapeKind.Donut or ShapeKind.Cone or ShapeKind.Rect or ShapeKind.Cross))
            return MechanicClass.Unclassified;
        if (!ability.HasCastBar)
            return MechanicClass.Unclassified;
        if (ability.Sheet is null)
            return MechanicClass.TelegraphedAOE;
        if (ability.Sheet.OmenId == 0 && string.IsNullOrEmpty(ability.Sheet.OmenPath))
            return MechanicClass.HiddenAOE;
        return MechanicClass.TelegraphedAOE;
    }

    private static IEnumerable<(ShapeOrigin Origin, ShapeDirection Dir)> ModesFor(ShapeKind kind, ShapePrior? prior)
    {
        if (prior is not null)
        {
            if (prior.Kind != kind)
                yield break;
            yield return (prior.Origin, prior.Direction);
            yield break;
        }

        switch (kind)
        {
            case ShapeKind.Circle:
                yield return (ShapeOrigin.Source, ShapeDirection.SourceRotation);
                yield return (ShapeOrigin.CastLocation, ShapeDirection.SourceRotation);
                yield return (ShapeOrigin.TargetActor, ShapeDirection.SourceRotation);
                break;
            case ShapeKind.Donut:
                yield return (ShapeOrigin.Source, ShapeDirection.SourceRotation);
                yield return (ShapeOrigin.CastLocation, ShapeDirection.SourceRotation);
                break;
            case ShapeKind.Cone:
            case ShapeKind.Rect:
            case ShapeKind.Cross:
                yield return (ShapeOrigin.Source, ShapeDirection.SourceRotation);
                yield return (ShapeOrigin.Source, ShapeDirection.ToTarget);
                break;
        }
    }

    private static ShapeFit ScoreProposal(FitProposal proposal, ImmutableArray<ShapeObservation> observations, ShapePrior? prior, ShapeInferenceOptions options)
    {
        var (consistency, minMargin, ambiguity) = Evaluate(proposal.Snapped, observations, options);
        var priorMatch = PriorMatch(proposal.Snapped, prior, options);
        double score;
        if (prior is null)
            score = 0.90 * consistency - options.AmbiguityWeight * ambiguity;
        else
            score = options.PriorWeight * priorMatch + options.FitWeight * consistency - options.AmbiguityWeight * ambiguity;

        if (prior is not null && priorMatch < 1 - 1e-6)
            score = Math.Min(score, 0.59);
        var angleLimited = proposal.Snapped.Kind == ShapeKind.Cone;
        var cap = angleLimited ? options.AmbiguityMarginDeg : options.AmbiguityMarginY;
        if (minMargin < cap)
            score = Math.Min(score, 0.84);

        return MakeFit(proposal.Snapped, proposal.Raw, score, consistency, prior, priorMatch, minMargin, ambiguity, observations.Length, options);
    }

    private static (double Consistency, float MinMargin, double Ambiguity) Evaluate(Shape shape, ImmutableArray<ShapeObservation> observations, ShapeInferenceOptions options)
    {
        var ok = 0;
        var thin = 0;
        var minMargin = float.PositiveInfinity;
        var angleLimited = shape.Kind == ShapeKind.Cone;
        var cap = angleLimited ? options.AmbiguityMarginDeg : options.AmbiguityMarginY;
        foreach (var o in observations)
        {
            var origin = AoeFitters.OriginOf(o, shape.Origin);
            var dir = AoeFitters.DirectionOf(o, origin, shape.Direction);
            var errors = 0;
            foreach (var (pos, hit) in o.Players)
            {
                if (shape.Contains(pos, origin, dir) != hit)
                    errors++;
            }

            var margin = ObservationMargin(shape, o, origin, dir, angleLimited);
            if (errors == 0)
                ok++;
            if (margin < cap)
                thin++;
            minMargin = Math.Min(minMargin, margin);
        }

        if (float.IsInfinity(minMargin))
            minMargin = 0;
        return (ok / (double)observations.Length, minMargin, thin / (double)observations.Length);
    }

    private static float ObservationMargin(Shape shape, ShapeObservation o, System.Numerics.Vector2 origin, Angle dir, bool angleLimited)
    {
        var facing = Geometry2D.Facing(dir);
        float maxHit = float.NegativeInfinity, minMiss = float.PositiveInfinity;
        foreach (var (pos, hit) in o.Players)
        {
            var metric = angleLimited
                ? MathF.Abs(Geometry2D.SignedAngleDeg(pos - origin, facing))
                : DistanceFromOriginMetric(shape, pos, origin, dir);
            if (hit)
                maxHit = Math.Max(maxHit, metric);
            else
                minMiss = Math.Min(minMiss, metric);
        }

        if (float.IsNegativeInfinity(maxHit) || float.IsPositiveInfinity(minMiss))
            return 999f;
        var gap = minMiss - maxHit;
        return gap > 0 ? gap : 0;
    }

    private static float DistanceFromOriginMetric(Shape shape, System.Numerics.Vector2 pos, System.Numerics.Vector2 origin, Angle dir)
    {
        var rel = pos - origin;
        var facing = Geometry2D.Facing(dir);
        return shape.Kind switch
        {
            ShapeKind.Rect => MathF.Max(
                MathF.Abs(System.Numerics.Vector2.Dot(rel, Geometry2D.Right(facing))),
                MathF.Max(0, System.Numerics.Vector2.Dot(rel, facing))),
            ShapeKind.Cross => Math.Min(
                MathF.Abs(System.Numerics.Vector2.Dot(rel, facing)),
                MathF.Abs(System.Numerics.Vector2.Dot(rel, Geometry2D.Right(facing)))),
            _ => rel.Length(),
        };
    }

    private static double PriorMatch(Shape shape, ShapePrior? prior, ShapeInferenceOptions options)
    {
        if (prior is null)
            return 0;
        var kind = shape.Kind == prior.Kind ? 1.0 : 0.0;
        var checks = new List<(float? Prior, float Actual, float Tol)>();
        if (prior.Radius is { } r)
            checks.Add((r, shape.Radius, options.RadiusSnapY));
        if (prior.InnerRadius is { } inner)
            checks.Add((inner, shape.InnerRadius, options.RadiusSnapY));
        if (prior.HalfAngleDeg is { } ang)
            checks.Add((ang, shape.HalfAngleDeg, options.AngleSnapDeg));
        if (prior.HalfWidth is { } hw)
            checks.Add((hw, shape.HalfWidth, options.RadiusSnapY));
        var param = checks.Count == 0 ? 1.0 : (checks.All(c => MathF.Abs(c.Actual - c.Prior!.Value) <= c.Tol) ? 1.0 : 0.0);
        return 0.7 * kind + 0.3 * param;
    }

    private static ShapeFit MakeFit(
        Shape shape,
        Shape raw,
        double score,
        double consistency,
        ShapePrior? prior,
        double priorMatch,
        float minMargin,
        double ambiguity,
        int instances,
        ShapeInferenceOptions options)
    {
        _ = options;
        score = Math.Clamp(score, 0, 1);
        var reasons = new List<string>();
        if (consistency < 1)
            reasons.Add("inconsistent observations");
        if (minMargin < 1)
            reasons.Add("thin margin");
        var evidence = ImmutableArray.Create(new Evidence(
            "ShapeFit",
            $"{shape.Kind} score={score.ToString("0.000", CultureInfo.InvariantCulture)} r={shape.Radius.ToString("0.#", CultureInfo.InvariantCulture)}",
            JsonSerializer.SerializeToElement(new
            {
                kind = shape.Kind.ToString(),
                rawKind = raw.Kind.ToString(),
                radius = shape.Radius,
                rawRadius = raw.Radius,
                halfAngle = shape.HalfAngleDeg,
                minMargin,
                consistency,
                priorMatch,
            })));
        return new ShapeFit(
            shape, raw, score, consistency, prior is null ? 0 : priorMatch, ambiguity,
            minMargin, 0, instances, Confidence.Of(score, [.. reasons]), evidence);
    }

    private static ShapeFit PriorFallback(ShapePrior prior, int instances)
    {
        var shape = FromPrior(prior);
        return new ShapeFit(
            shape, shape, 0.3, 0, 1, 1, 0, 0, instances,
            Confidence.LowBecause("no consistent fit"),
            [new Evidence("ShapeFit", "no consistent fit")]);
    }

    private static Shape FromPrior(ShapePrior prior)
        => prior.Kind switch
        {
            ShapeKind.Circle => Shape.Circle(prior.Radius ?? 0, prior.Origin),
            ShapeKind.Donut => Shape.Donut(prior.InnerRadius ?? 0, prior.Radius ?? 0, prior.Origin),
            ShapeKind.Cone => Shape.Cone(prior.Radius ?? 0, prior.HalfAngleDeg ?? 0, prior.Origin, prior.Direction),
            ShapeKind.Rect => Shape.Rect(prior.Radius ?? 0, prior.HalfWidth ?? 0, 0, prior.Origin, prior.Direction),
            ShapeKind.Cross => Shape.Cross(prior.Radius ?? 0, prior.HalfWidth ?? 0, prior.Origin, prior.Direction),
            ShapeKind.RaidwideNoShape => Shape.Raidwide,
            _ => Shape.SingleTarget,
        };

    private static IEnumerable<float> LagGrid(ShapeInferenceOptions options)
    {
        for (var d = options.LagMinSeconds; d <= options.LagMaxSeconds + 1e-4f; d += options.LagStepSeconds)
            yield return MathF.Round(d, 3);
    }

    private static bool HasPlayerDamage(AbilityInstance inst, Pull pull)
    {
        foreach (var hit in inst.Hits)
        {
            if (!pull.PlayerInstanceIds.Contains(hit.TargetInstanceId))
                continue;
            if (hit.Effects.NonEmpty().Any(e => e.Type is ActionEffectType.Damage or ActionEffectType.BlockedDamage or ActionEffectType.ParriedDamage))
                return true;
        }

        return false;
    }

    private static ShapeObservation Observe(AbilityInstance inst, Recording recording, Pull pull, float delta)
    {
        var t0 = inst.CastEnd ?? inst.EffectTime;
        var tSnap = t0 + TimeSpan.FromSeconds(delta);
        var origin = inst.Origin;
        if (origin == System.Numerics.Vector3.Zero)
        {
            var src = recording.ActorAt(inst.SourceInstanceId, tSnap);
            origin = src?.PosRotAt(tSnap).Pos ?? origin;
        }

        System.Numerics.Vector2? targetActor = null;
        if (inst.MainTargetId != 0)
        {
            var actor = recording.ActorAt(inst.MainTargetId, tSnap);
            if (actor is not null)
                targetActor = Geometry2D.Xz(actor.PosRotAt(tSnap).Pos);
        }

        var players = ImmutableArray.CreateBuilder<(System.Numerics.Vector2 Pos, bool Hit)>();
        foreach (var pid in pull.PlayerInstanceIds)
        {
            var actor = recording.ActorAt(pid, tSnap);
            if (actor is null || actor.IsDeadAt(tSnap) || !actor.IsAliveAt(tSnap))
                continue;
            var hit = inst.Hits.Any(h => h.TargetInstanceId == pid && h.Effects.NonEmpty().Any(e =>
                e.Type is ActionEffectType.Damage or ActionEffectType.BlockedDamage or ActionEffectType.ParriedDamage));
            players.Add((Geometry2D.Xz(actor.PosRotAt(tSnap).Pos), hit));
        }

        return new ShapeObservation(
            Geometry2D.Xz(origin),
            inst.Rotation,
            Geometry2D.Xz(inst.TargetPos),
            targetActor,
            players.ToImmutable());
    }
}
