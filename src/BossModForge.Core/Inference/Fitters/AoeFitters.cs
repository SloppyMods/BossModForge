using System.Collections.Immutable;
using System.Numerics;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

internal readonly record struct FitProposal(Shape Raw, Shape Snapped, float CombinedMargin);

internal static class AoeFitters
{
    public static Vector2 OriginOf(ShapeObservation o, ShapeOrigin origin)
        => origin switch
        {
            ShapeOrigin.CastLocation => o.CastLocation == Vector2.Zero ? o.Source : o.CastLocation,
            ShapeOrigin.TargetActor => o.TargetActor ?? o.Source,
            _ => o.Source,
        };

    public static Angle DirectionOf(ShapeObservation o, Vector2 origin, ShapeDirection dir)
    {
        if (dir == ShapeDirection.ToTarget)
        {
            var target = o.TargetActor ?? (o.CastLocation == origin ? (Vector2?)null : o.CastLocation);
            if (target is { } t && t != origin)
                return Geometry2D.Atan2(t - origin);
        }

        return o.SourceRotation;
    }

    public static FitProposal? Circle(
        ImmutableArray<ShapeObservation> observations,
        ShapeOrigin originMode,
        ShapePrior? prior,
        ShapeInferenceOptions options)
    {
        float maxHit = float.NegativeInfinity, minMiss = float.PositiveInfinity;
        var anyHit = false;
        var anyMiss = false;
        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            foreach (var (pos, hit) in o.Players)
            {
                var d = Vector2.Distance(pos, origin);
                if (hit)
                {
                    maxHit = Math.Max(maxHit, d);
                    anyHit = true;
                }
                else
                {
                    minMiss = Math.Min(minMiss, d);
                    anyMiss = true;
                }
            }
        }

        if (!anyHit)
            return null;
        if (!anyMiss)
            minMiss = float.PositiveInfinity;
        var rawR = maxHit < minMiss ? 0.5f * (maxHit + (float.IsInfinity(minMiss) ? maxHit : minMiss)) : maxHit;
        var priorR = prior?.Kind == ShapeKind.Circle ? prior.Radius : null;
        var snapR = SnapRadial(maxHit, minMiss, priorR, options.RadiusSnapY);
        return new FitProposal(Shape.Circle(rawR, originMode), Shape.Circle(snapR, originMode), Margin(maxHit, minMiss));
    }

    public static FitProposal? Donut(
        ImmutableArray<ShapeObservation> observations,
        ShapeOrigin originMode,
        ShapePrior? prior,
        ShapeInferenceOptions options)
    {
        float minHit = float.PositiveInfinity, maxHit = float.NegativeInfinity;
        float maxInnerMiss = 0, minOuterMiss = float.PositiveInfinity;
        var anyHit = false;
        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            foreach (var (pos, hit) in o.Players)
            {
                var d = Vector2.Distance(pos, origin);
                if (hit)
                {
                    minHit = Math.Min(minHit, d);
                    maxHit = Math.Max(maxHit, d);
                    anyHit = true;
                }
            }
        }

        if (!anyHit)
            return null;

        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            foreach (var (pos, hit) in o.Players)
            {
                if (hit)
                    continue;
                var d = Vector2.Distance(pos, origin);
                if (d < minHit)
                    maxInnerMiss = Math.Max(maxInnerMiss, d);
                else if (d > maxHit)
                    minOuterMiss = Math.Min(minOuterMiss, d);
            }
        }

        var rawIn = 0.5f * (maxInnerMiss + minHit);
        var rawOut = maxHit < minOuterMiss ? 0.5f * (maxHit + (float.IsInfinity(minOuterMiss) ? maxHit : minOuterMiss)) : maxHit;
        var priorIn = prior?.Kind == ShapeKind.Donut ? prior.InnerRadius : null;
        var priorOut = prior?.Kind == ShapeKind.Donut ? prior.Radius : null;
        var snapIn = SnapRadial(maxInnerMiss, minHit, priorIn, options.RadiusSnapY);
        var snapOut = SnapRadial(maxHit, minOuterMiss, priorOut, options.RadiusSnapY);
        if (snapIn <= maxInnerMiss)
            snapIn = MathF.BitIncrement(maxInnerMiss);
        if (snapIn >= snapOut)
            snapIn = MathF.Max(0, snapOut - options.RadiusSnapY);
        var margin = Math.Min(Margin(maxInnerMiss, minHit), Margin(maxHit, minOuterMiss));
        return new FitProposal(
            Shape.Donut(rawIn, rawOut, originMode),
            Shape.Donut(snapIn, snapOut, originMode),
            margin);
    }

    public static FitProposal? Cone(
        ImmutableArray<ShapeObservation> observations,
        ShapeOrigin originMode,
        ShapeDirection dirMode,
        ShapePrior? prior,
        ShapeInferenceOptions options)
    {
        float maxHitR = float.NegativeInfinity, minMissR = float.PositiveInfinity;
        float maxHitAng = 0, minMissAng = float.PositiveInfinity;
        var anyHit = false;
        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            var dir = DirectionOf(o, origin, dirMode);
            var facing = Geometry2D.Facing(dir);
            foreach (var (pos, hit) in o.Players)
            {
                var rel = pos - origin;
                var d = rel.Length();
                var ang = MathF.Abs(Geometry2D.SignedAngleDeg(rel, facing));
                if (hit)
                {
                    anyHit = true;
                    maxHitR = Math.Max(maxHitR, d);
                    maxHitAng = Math.Max(maxHitAng, ang);
                }
            }
        }

        if (!anyHit)
            return null;

        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            var dir = DirectionOf(o, origin, dirMode);
            var facing = Geometry2D.Facing(dir);
            foreach (var (pos, hit) in o.Players)
            {
                if (hit)
                    continue;
                var rel = pos - origin;
                var d = rel.Length();
                var ang = MathF.Abs(Geometry2D.SignedAngleDeg(rel, facing));
                if (d <= maxHitR + 0.25f)
                    minMissAng = Math.Min(minMissAng, ang);
                if (ang <= maxHitAng + 1f)
                    minMissR = Math.Min(minMissR, d);
            }
        }

        var rawR = maxHitR < minMissR ? 0.5f * (maxHitR + (float.IsInfinity(minMissR) ? maxHitR : minMissR)) : maxHitR;
        var rawA = maxHitAng < minMissAng ? 0.5f * (maxHitAng + (float.IsInfinity(minMissAng) ? maxHitAng : minMissAng)) : maxHitAng;
        var priorR = prior?.Kind == ShapeKind.Cone ? prior.Radius : null;
        var priorA = prior?.Kind == ShapeKind.Cone ? prior.HalfAngleDeg : null;
        var snapR = SnapRadial(maxHitR, minMissR, priorR, options.RadiusSnapY);
        var snapA = SnapAngle(maxHitAng, minMissAng, priorA, options.AngleSnapDeg);
        var margin = Math.Min(Margin(maxHitR, minMissR), Margin(maxHitAng, minMissAng));
        return new FitProposal(
            Shape.Cone(rawR, rawA, originMode, dirMode),
            Shape.Cone(snapR, snapA, originMode, dirMode),
            margin);
    }

    public static FitProposal? Rect(
        ImmutableArray<ShapeObservation> observations,
        ShapeOrigin originMode,
        ShapeDirection dirMode,
        ShapePrior? prior,
        ShapeInferenceOptions options)
    {
        float maxFwd = float.NegativeInfinity, minFwd = 0, maxLat = 0;
        var anyHit = false;
        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            var dir = DirectionOf(o, origin, dirMode);
            var facing = Geometry2D.Facing(dir);
            var right = Geometry2D.Right(facing);
            foreach (var (pos, hit) in o.Players)
            {
                if (!hit)
                    continue;
                anyHit = true;
                var rel = pos - origin;
                var fwd = Vector2.Dot(rel, facing);
                var lat = MathF.Abs(Vector2.Dot(rel, right));
                maxFwd = Math.Max(maxFwd, fwd);
                minFwd = Math.Min(minFwd, fwd);
                maxLat = Math.Max(maxLat, lat);
            }
        }

        if (!anyHit)
            return null;

        var lengthBackRaw = minFwd < -0.05f ? -minFwd : 0f;
        float minFwdMiss = float.PositiveInfinity, minLatMiss = float.PositiveInfinity;
        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            var dir = DirectionOf(o, origin, dirMode);
            var facing = Geometry2D.Facing(dir);
            var right = Geometry2D.Right(facing);
            foreach (var (pos, hit) in o.Players)
            {
                if (hit)
                    continue;
                var rel = pos - origin;
                var fwd = Vector2.Dot(rel, facing);
                var lat = MathF.Abs(Vector2.Dot(rel, right));
                if (lat <= maxLat + 0.25f && fwd > 0)
                    minFwdMiss = Math.Min(minFwdMiss, fwd);
                if (fwd >= -lengthBackRaw - 0.25f && fwd <= maxFwd + 0.25f)
                    minLatMiss = Math.Min(minLatMiss, lat);
            }
        }

        var rawLen = maxFwd < minFwdMiss ? 0.5f * (maxFwd + (float.IsInfinity(minFwdMiss) ? maxFwd : minFwdMiss)) : maxFwd;
        var rawHw = maxLat < minLatMiss ? 0.5f * (maxLat + (float.IsInfinity(minLatMiss) ? maxLat : minLatMiss)) : maxLat;
        var priorLen = prior?.Kind == ShapeKind.Rect ? prior.Radius : null;
        var priorHw = prior?.Kind == ShapeKind.Rect ? prior.HalfWidth : null;
        var snapLen = SnapRadial(maxFwd, minFwdMiss, priorLen, options.RadiusSnapY);
        var snapHw = SnapRadial(maxLat, minLatMiss, priorHw, options.RadiusSnapY);
        var snapBack = lengthBackRaw > 0 ? MathF.Round(lengthBackRaw * 10f, MidpointRounding.AwayFromZero) / 10f : 0f;
        var margin = Math.Min(Margin(maxFwd, minFwdMiss), Margin(maxLat, minLatMiss));
        return new FitProposal(
            Shape.Rect(rawLen, rawHw, lengthBackRaw, originMode, dirMode),
            Shape.Rect(snapLen, snapHw, snapBack, originMode, dirMode),
            margin);
    }

    public static FitProposal? Cross(
        ImmutableArray<ShapeObservation> observations,
        ShapeOrigin originMode,
        ShapeDirection dirMode,
        ShapePrior? prior,
        ShapeInferenceOptions options)
    {
        float maxNear = 0, maxFar = 0;
        var anyHit = false;

        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            var dir = DirectionOf(o, origin, dirMode);
            var facing = Geometry2D.Facing(dir);
            var right = Geometry2D.Right(facing);
            foreach (var (pos, hit) in o.Players)
            {
                if (!hit)
                    continue;
                anyHit = true;
                var rel = pos - origin;
                var a = MathF.Abs(Vector2.Dot(rel, facing));
                var b = MathF.Abs(Vector2.Dot(rel, right));
                var near = Math.Min(a, b);
                var far = Math.Max(a, b);
                maxNear = Math.Max(maxNear, near);
                maxFar = Math.Max(maxFar, far);
            }
        }

        if (!anyHit)
            return null;

        float minNearMiss = float.PositiveInfinity, minFarMiss = float.PositiveInfinity;

        foreach (var o in observations)
        {
            var origin = OriginOf(o, originMode);
            var dir = DirectionOf(o, origin, dirMode);
            var facing = Geometry2D.Facing(dir);
            var right = Geometry2D.Right(facing);
            foreach (var (pos, hit) in o.Players)
            {
                if (hit)
                    continue;
                var rel = pos - origin;
                var a = MathF.Abs(Vector2.Dot(rel, facing));
                var b = MathF.Abs(Vector2.Dot(rel, right));
                var near = Math.Min(a, b);
                var far = Math.Max(a, b);
                if (far <= maxFar + 0.5f)
                    minNearMiss = Math.Min(minNearMiss, near);
                if (near <= maxNear + 0.5f)
                    minFarMiss = Math.Min(minFarMiss, far);
            }
        }
        var rawHw = maxNear < minNearMiss ? 0.5f * (maxNear + (float.IsInfinity(minNearMiss) ? maxNear : minNearMiss)) : maxNear;
        var rawLen = maxFar < minFarMiss ? 0.5f * (maxFar + (float.IsInfinity(minFarMiss) ? maxFar : minFarMiss)) : maxFar;
        var priorLen = prior?.Kind == ShapeKind.Cross ? prior.Radius : null;
        var priorHw = prior?.Kind == ShapeKind.Cross ? prior.HalfWidth : null;
        var snapLen = SnapRadial(maxFar, minFarMiss, priorLen, options.RadiusSnapY);
        var snapHw = SnapRadial(maxNear, minNearMiss, priorHw, options.RadiusSnapY);
        var margin = Math.Min(Margin(maxNear, minNearMiss), Margin(maxFar, minFarMiss));
        return new FitProposal(
            Shape.Cross(rawLen, rawHw, originMode, dirMode),
            Shape.Cross(snapLen, snapHw, originMode, dirMode),
            margin);
    }

    internal static float SnapRadial(float maxHit, float minMiss, float? prior, float snapY)
    {
        if (prior is { } p && p >= maxHit && p < minMiss)
            return p;
        if (float.IsPositiveInfinity(minMiss))
        {
            if (prior is { } p2 && p2 >= maxHit)
                return p2;
            return MathF.Max(maxHit, RoundTenths(maxHit));
        }

        if (maxHit >= minMiss)
            return prior ?? RoundTenths(0.5f * (maxHit + minMiss));

        var mid = 0.5f * (maxHit + minMiss);
        var lo = MathF.Ceiling((maxHit + 1e-4f) / snapY) * snapY;
        var hi = MathF.Floor((minMiss - 1e-4f) / snapY) * snapY;
        if (lo <= hi + 1e-4f)
        {
            var best = lo;
            for (var v = lo; v <= hi + snapY * 0.5f; v += snapY)
            {
                if (v > hi + 1e-4f)
                    break;
                if (MathF.Abs(v - mid) < MathF.Abs(best - mid))
                    best = v;
            }

            return best;
        }

        return RoundTenths(mid);
    }

    internal static float SnapAngle(float maxHit, float minMiss, float? prior, float snapDeg)
    {
        float[] snaps = [15, 22.5f, 30, 45, 60, 67.5f, 90, 105, 135];
        if (prior is { } p && p >= maxHit && p < minMiss)
            return p;
        var raw = maxHit < minMiss ? 0.5f * (maxHit + (float.IsInfinity(minMiss) ? maxHit : minMiss)) : maxHit;
        if (prior is { } p2 && float.IsPositiveInfinity(minMiss) && p2 >= raw - snapDeg)
            return p2;
        foreach (var s in snaps)
        {
            if (MathF.Abs(raw - s) <= snapDeg && s >= maxHit && (float.IsInfinity(minMiss) || s < minMiss))
                return s;
        }

        foreach (var s in snaps)
        {
            if (MathF.Abs(raw - s) <= snapDeg)
                return s;
        }

        return MathF.Max(maxHit, RoundTenths(raw));
    }

    private static float Margin(float maxHit, float minMiss)
        => minMiss > maxHit ? minMiss - maxHit : 0;

    private static float RoundTenths(float v)
        => MathF.Round(v * 10f, MidpointRounding.AwayFromZero) / 10f;
}
