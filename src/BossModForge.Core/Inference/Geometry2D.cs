using System.Numerics;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

internal static class Geometry2D
{
    public static Vector2 Xz(Vector3 p) => new(p.X, p.Z);

    public static Vector2 Facing(Angle a) => new(MathF.Sin(a.Rad), MathF.Cos(a.Rad));

    public static Vector2 Right(Vector2 facing) => new(facing.Y, -facing.X);

    public static Angle Atan2(Vector2 v) => new(MathF.Atan2(v.X, v.Y));

    public static float SignedAngleDeg(Vector2 rel, Vector2 facing)
    {
        var n = rel.Length();
        if (n < 1e-6f)
            return 0;
        var fwd = Vector2.Dot(rel, facing) / n;
        var lat = Vector2.Dot(rel, Right(facing)) / n;
        return MathF.Atan2(lat, fwd) * (180f / MathF.PI);
    }

    public static List<Vector2> ConvexHull(IReadOnlyList<Vector2> points)
    {
        if (points.Count == 0)
            return [];
        var pts = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (pts.Count <= 2)
            return pts;

        static float Cross(Vector2 o, Vector2 a, Vector2 b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var lower = new List<Vector2>();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        var upper = new List<Vector2>();
        for (var i = pts.Count - 1; i >= 0; i--)
        {
            var p = pts[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    public static float PolygonArea(IReadOnlyList<Vector2> hull)
    {
        if (hull.Count < 3)
            return 0;
        double acc = 0;
        for (var i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            acc += (double)a.X * b.Y - (double)b.X * a.Y;
        }

        return (float)(Math.Abs(acc) * 0.5);
    }

    public static (Vector2 Center, float Radius) MinEnclosingCircle(IReadOnlyList<Vector2> points)
    {
        if (points.Count == 0)
            return (Vector2.Zero, 0);
        if (points.Count == 1)
            return (points[0], 0);

        var hull = ConvexHull(points);
        if (hull.Count == 1)
            return (hull[0], 0);
        if (hull.Count == 2)
            return FromTwo(hull[0], hull[1]);

        var bestC = Vector2.Zero;
        var bestR = float.PositiveInfinity;
        for (var i = 0; i < hull.Count; i++)
        {
            for (var j = i + 1; j < hull.Count; j++)
            {
                var (c2, r2) = FromTwo(hull[i], hull[j]);
                if (r2 < bestR && Covers(c2, r2, hull))
                {
                    bestC = c2;
                    bestR = r2;
                }

                for (var k = j + 1; k < hull.Count; k++)
                {
                    if (!TryCircum(hull[i], hull[j], hull[k], out var c3, out var r3))
                        continue;
                    if (r3 < bestR && Covers(c3, r3, hull))
                    {
                        bestC = c3;
                        bestR = r3;
                    }
                }
            }
        }

        if (float.IsInfinity(bestR))
        {
            var (c2, r2) = FromTwo(hull[0], hull[1]);
            return (c2, r2);
        }

        return (bestC, bestR);
    }

    public static List<Vector2> DouglasPeucker(IReadOnlyList<Vector2> ring, float epsilon)
    {
        if (ring.Count <= 3)
            return [.. ring];
        var pts = ring.ToList();
        if (pts[0] != pts[^1])
            pts.Add(pts[0]);
        var keep = Simplify(pts, 0, pts.Count - 1, epsilon);
        if (keep.Count > 1 && keep[0] == keep[^1])
            keep.RemoveAt(keep.Count - 1);
        return keep;
    }

    private static List<Vector2> Simplify(List<Vector2> pts, int start, int end, float epsilon)
    {
        var maxDist = 0f;
        var index = start;
        for (var i = start + 1; i < end; i++)
        {
            var d = PerpDist(pts[i], pts[start], pts[end]);
            if (d > maxDist)
            {
                maxDist = d;
                index = i;
            }
        }

        if (maxDist > epsilon && index != start)
        {
            var left = Simplify(pts, start, index, epsilon);
            var right = Simplify(pts, index, end, epsilon);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        return [pts[start], pts[end]];
    }

    private static float PerpDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len = ab.Length();
        if (len < 1e-8f)
            return (p - a).Length();
        return MathF.Abs(ab.X * (a.Y - p.Y) - ab.Y * (a.X - p.X)) / len;
    }

    private static (Vector2 C, float R) FromTwo(Vector2 a, Vector2 b)
        => ((a + b) * 0.5f, Vector2.Distance(a, b) * 0.5f);

    private static bool TryCircum(Vector2 a, Vector2 b, Vector2 c, out Vector2 center, out float radius)
    {
        var d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (MathF.Abs(d) < 1e-8f)
        {
            center = default;
            radius = 0;
            return false;
        }

        var a2 = a.LengthSquared();
        var b2 = b.LengthSquared();
        var c2 = c.LengthSquared();
        center = new Vector2(
            (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d,
            (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
        radius = Vector2.Distance(center, a);
        return true;
    }

    private static bool Covers(Vector2 c, float r, IReadOnlyList<Vector2> pts)
    {
        var lim = r + 1e-3f;
        foreach (var p in pts)
        {
            if (Vector2.Distance(c, p) > lim)
                return false;
        }

        return true;
    }
}
