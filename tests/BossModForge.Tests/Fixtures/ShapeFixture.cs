using System.Collections.Immutable;
using System.Numerics;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public static class ShapeFixture
{
    public static ImmutableArray<ShapeObservation> Planted(
        Shape shape,
        int seed,
        int observations = 12,
        int players = 8,
        float fieldRadius = 15f,
        bool clearBoundary = true,
        float? ambiguityOutside = null)
    {
        _ = fieldRadius;
        _ = clearBoundary;
        var rng = new Random(seed);
        var origin = Vector2.Zero;
        var dir = Angle.FromDeg(0);
        var list = ImmutableArray.CreateBuilder<ShapeObservation>(observations);
        for (var i = 0; i < observations; i++)
        {
            var pts = CanonicalPlayers(shape, rng, ambiguityOutside);
            var labeled = pts.Take(players).Select(p => (p, shape.Contains(p, origin, dir))).ToArray();
            list.Add(new ShapeObservation(origin, dir, origin, null, [.. labeled]));
        }

        return list.ToImmutable();
    }

    public static ShapePrior PriorFor(Shape shape)
        => new(
            shape.Kind,
            shape.Kind is ShapeKind.Circle or ShapeKind.Cone or ShapeKind.Rect or ShapeKind.Cross or ShapeKind.Donut ? shape.Radius : null,
            shape.Kind == ShapeKind.Donut ? shape.InnerRadius : null,
            shape.Kind == ShapeKind.Cone ? shape.HalfAngleDeg : null,
            shape.Kind is ShapeKind.Rect or ShapeKind.Cross ? shape.HalfWidth : null,
            shape.Origin,
            shape.Direction,
            false);

    public static ImmutableArray<ShapeObservation> Raidwide(int seed, int observations, int players, int allHitCount)
    {
        var rng = new Random(seed);
        var list = ImmutableArray.CreateBuilder<ShapeObservation>(observations);
        for (var i = 0; i < observations; i++)
        {
            var allHit = i < allHitCount;
            var pts = ImmutableArray.CreateBuilder<(Vector2, bool)>(players);
            for (var p = 0; p < players; p++)
            {
                var pos = Disc(rng, 15f);
                pts.Add((pos, allHit));
            }

            if (!allHit)
                pts[0] = (pts[0].Item1, false);

            list.Add(new ShapeObservation(Vector2.Zero, default, Vector2.Zero, null, pts.ToImmutable()));
        }

        return list.ToImmutable();
    }

    private static List<Vector2> CanonicalPlayers(Shape shape, Random rng, float? ambiguityOutside)
    {
        var facing = new Vector2(0, 1);
        var right = new Vector2(1, 0);
        var n = Noise(rng, 0.04f);
        Vector2 P(Vector2 v) => v + n + Noise(rng, 0.04f);
        var pts = new List<Vector2>();
        var gap = 1.3f;
        var inGap = ambiguityOutside is not null ? 0.15f : gap;
        var outGap = ambiguityOutside ?? gap;

        switch (shape.Kind)
        {
            case ShapeKind.Circle:
                pts.Add(P(facing * (shape.Radius - inGap)));
                pts.Add(P(-facing * (shape.Radius - inGap)));
                pts.Add(P(right * (shape.Radius * 0.5f)));
                pts.Add(P(-right * (shape.Radius * 0.3f)));
                pts.Add(P(facing * (shape.Radius + outGap)));
                pts.Add(P(-facing * (shape.Radius + outGap + 0.4f)));
                pts.Add(P(right * (shape.Radius + outGap + 1f)));
                pts.Add(P((facing + right) * ((shape.Radius + 4f) * 0.7f)));
                break;
            case ShapeKind.Donut:
                pts.Add(P(facing * (shape.InnerRadius + inGap)));
                pts.Add(P(right * (shape.Radius - inGap)));
                pts.Add(P(-facing * (shape.InnerRadius + inGap)));
                pts.Add(P(-right * (shape.Radius - inGap)));
                pts.Add(P(facing * (shape.InnerRadius - outGap)));
                pts.Add(P(facing * (shape.Radius + outGap)));
                pts.Add(P(right * (shape.InnerRadius - outGap)));
                pts.Add(P(-facing * (shape.Radius + outGap)));
                break;
            case ShapeKind.Cone:
                pts.Add(P(Rot(facing, -shape.HalfAngleDeg + 8f) * (shape.Radius - inGap)));
                pts.Add(P(Rot(facing, shape.HalfAngleDeg - 8f) * (shape.Radius - inGap)));
                pts.Add(P(facing * (shape.Radius * 0.45f)));
                pts.Add(P(facing * (shape.Radius + outGap)));
                pts.Add(P(Rot(facing, shape.HalfAngleDeg + 12f) * (shape.Radius * 0.7f)));
                pts.Add(P(Rot(facing, -(shape.HalfAngleDeg + 18f)) * (shape.Radius * 0.6f)));
                pts.Add(P(Rot(facing, 130f) * 12f));
                pts.Add(P(facing * 4f));
                break;
            case ShapeKind.Rect:
                pts.Add(P(facing * (shape.Radius - inGap)));
                pts.Add(P(facing * (shape.Radius * 0.4f) + right * (shape.HalfWidth * 0.4f)));
                pts.Add(P(facing * 2f + right * (shape.HalfWidth * 0.85f)));
                pts.Add(P(facing * (shape.Radius + outGap)));
                pts.Add(P(facing * (shape.Radius * 0.5f) + right * (shape.HalfWidth + outGap)));
                pts.Add(P(facing * 2f + right * (shape.HalfWidth + outGap)));
                pts.Add(P(-facing * 6f));
                pts.Add(P(right * 10f));
                break;
            case ShapeKind.Cross:
                pts.Add(P(facing * (shape.Radius - inGap)));
                pts.Add(P(-facing * (shape.Radius - inGap)));
                pts.Add(P(right * (shape.Radius - inGap)));
                pts.Add(P(-right * (shape.Radius * 0.4f)));
                pts.Add(P(facing * (shape.Radius + outGap)));
                pts.Add(P(facing * (shape.Radius * 0.5f) + right * (shape.HalfWidth + outGap)));
                pts.Add(P(Vector2.Normalize(facing + right) * 14f));
                pts.Add(P(facing * 2f + right * (shape.HalfWidth * 0.4f)));
                break;
            default:
                for (var i = 0; i < 8; i++)
                    pts.Add(Disc(rng, 15f));
                break;
        }

        return pts;
    }

    private static Vector2 Noise(Random rng, float mag)
        => new((float)((rng.NextDouble() * 2 - 1) * mag), (float)((rng.NextDouble() * 2 - 1) * mag));

    private static Vector2 Rot(Vector2 v, float deg)
    {
        var r = deg * (MathF.PI / 180f);
        var c = MathF.Cos(r);
        var s = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static Vector2 Disc(Random rng, float radius)
    {
        var a = rng.NextDouble() * Math.PI * 2;
        var r = radius * Math.Sqrt(rng.NextDouble());
        return new Vector2((float)(r * Math.Sin(a)), (float)(r * Math.Cos(a)));
    }
}
