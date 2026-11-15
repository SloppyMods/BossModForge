using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

public sealed record BoundsFitterOptions(
    float RadiusSnapTolerance = 1.0f,
    float RectPadding = 1.0f,
    double CircleFillMin = 0.7,
    double BoxFillMin = 0.8,
    float PolygonEpsilon = 0.5f,
    TimeSpan? KnockbackExclusion = null,
    TimeSpan? DeathExclusion = null,
    TimeSpan? SampleStep = null)
{
    public static readonly BoundsFitterOptions Default = new();
}

public enum ArenaKind { Circle, Square, Rect, Custom }

public sealed record ArenaFit(
    ArenaKind Kind,
    Vector2 Center,
    float Radius,
    float HalfWidth,
    float HalfHeight,
    ImmutableArray<Vector2> Vertices,
    int SampleCount,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence);

public static class BoundsFitter
{
    private static readonly float[] RadiusSnaps = [10, 15, 19, 20, 25, 30];

    public static ArenaFit Fit(Recording recording, ImmutableArray<Pull> pulls, uint primaryOid, BoundsFitterOptions? options = null)
    {
        options ??= BoundsFitterOptions.Default;
        var step = options.SampleStep ?? TimeSpan.FromSeconds(0.5);
        var kbWindow = options.KnockbackExclusion ?? TimeSpan.FromSeconds(2);
        var deathWindow = options.DeathExclusion ?? TimeSpan.FromSeconds(2);

        var knockbacks = CollectKnockbacks(recording);
        var deaths = CollectDeaths(recording);
        var samples = ImmutableArray.CreateBuilder<Vector2>();
        var timed = new List<(DateTime T, Vector2 P)>();

        if (!pulls.IsDefaultOrEmpty)
        {
            foreach (var pull in pulls)
            {
                for (var t = pull.Start; t <= pull.End; t += step)
                {
                    foreach (var pid in pull.PlayerInstanceIds)
                    {
                        if (IsKnockedBack(knockbacks, pid, t, kbWindow) || IsNearDeath(deaths, pid, t, deathWindow))
                            continue;
                        var actor = recording.ActorAt(pid, t);
                        if (actor is null || actor.IsDeadAt(t) || !actor.IsAliveAt(t))
                            continue;
                        var p = Geometry2D.Xz(actor.PosRotAt(t).Pos);
                        samples.Add(p);
                        timed.Add((t, p));
                    }
                }
            }
        }

        Vector2? spawn = null;
        if (!pulls.IsDefaultOrEmpty)
        {
            var pull0 = pulls[0];
            foreach (var actor in recording.Actors)
            {
                if (actor.Oid != primaryOid)
                    continue;
                if (actor.Destroyed is not null && actor.Destroyed <= pull0.Start)
                    continue;
                if (actor.Created > pull0.End)
                    continue;
                if (actor.PosRotSamples.Count == 0)
                    continue;
                spawn = Geometry2D.Xz(actor.PosRotSamples[0].Value.Pos);
                break;
            }
        }

        var fit = Fit(samples.ToImmutable(), spawn, options);
        var change = DetectArenaChange(timed);
        if (change is null)
            return fit;
        return fit with { Evidence = fit.Evidence.Add(change) };
    }

    public static ArenaFit Fit(ImmutableArray<Vector2> samples, Vector2? bossSpawn, BoundsFitterOptions? options = null)
    {
        options ??= BoundsFitterOptions.Default;
        var pts = samples.IsDefault ? [] : samples.ToList().Distinct().ToList();
        var evidence = ImmutableArray.CreateBuilder<Evidence>();
        var n = pts.Count;

        if (n < 20)
        {
            var fallbackCenter = bossSpawn ?? (n == 0 ? Vector2.Zero : Mean(pts));
            evidence.Add(new Evidence("Bounds", "insufficient samples"));
            return new ArenaFit(
                ArenaKind.Circle, fallbackCenter, 20f, 0, 0, [], n,
                Confidence.LowBecause("insufficient samples"),
                evidence.ToImmutable());
        }

        var (mecC, mecR) = Geometry2D.MinEnclosingCircle(pts);
        var hull = Geometry2D.ConvexHull(pts);
        var hullArea = Geometry2D.PolygonArea(hull);
        var minX = pts.Min(p => p.X);
        var maxX = pts.Max(p => p.X);
        var minZ = pts.Min(p => p.Y);
        var maxZ = pts.Max(p => p.Y);
        var halfW = (maxX - minX) * 0.5f + options.RectPadding;
        var halfH = (maxZ - minZ) * 0.5f + options.RectPadding;
        var boxC = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        var circleArea = MathF.PI * mecR * mecR;
        var boxArea = (halfW * 2) * (halfH * 2);
        var hullOverCircle = circleArea <= 1e-6f ? 1 : hullArea / circleArea;
        var hullOverBox = boxArea <= 1e-6f ? 1 : hullArea / boxArea;

        ArenaKind kind;
        Vector2 center;
        float radius;
        float hw;
        float hh;
        ImmutableArray<Vector2> vertices = [];
        var snapped = false;

        if (hullOverCircle < options.CircleFillMin && hullOverBox > options.BoxFillMin)
        {
            if (MathF.Abs(halfW - halfH) <= 1f)
            {
                kind = ArenaKind.Square;
                var mean = (halfW + halfH) * 0.5f;
                hw = mean;
                hh = mean;
            }
            else
            {
                kind = ArenaKind.Rect;
                hw = halfW;
                hh = halfH;
            }

            center = boxC;
            radius = mecR;
            snapped = kind == ArenaKind.Square;
        }
        else if (hullOverCircle < options.CircleFillMin && hullOverBox <= options.BoxFillMin)
        {
            kind = ArenaKind.Custom;
            center = mecC;
            radius = mecR;
            hw = halfW;
            hh = halfH;
            var simplified = Geometry2D.DouglasPeucker(hull, options.PolygonEpsilon);
            vertices = [.. simplified];
        }
        else
        {
            kind = ArenaKind.Circle;
            center = mecC;
            hw = 0;
            hh = 0;
            var (snappedR, isSnap) = SnapRadius(mecR, options.RadiusSnapTolerance);
            radius = snappedR;
            snapped = isSnap;
        }

        if (kind == ArenaKind.Circle && !snapped)
            radius = MathF.Round(radius * 2f, MidpointRounding.AwayFromZero) / 2f;

        if (bossSpawn is { } spawn)
        {
            var d = Vector2.Distance(center, spawn);
            evidence.Add(new Evidence("CenterOffset", d.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        evidence.Add(new Evidence("Bounds", $"{kind} n={n} hull/circle={hullOverCircle:0.###} hull/box={hullOverBox:0.###}"));

        Confidence conf;
        if (kind == ArenaKind.Custom)
            conf = Confidence.LowBecause("custom polygon");
        else if (snapped && n >= 200)
            conf = Confidence.Of(0.9);
        else
            conf = Confidence.Of(0.7);

        if (bossSpawn is { } spawn2 && Vector2.Distance(center, spawn2) > 3f && kind is ArenaKind.Circle or ArenaKind.Square)
            conf = LowerBand(conf);

        return new ArenaFit(kind, center, radius, hw, hh, vertices, n, conf, evidence.ToImmutable());
    }

    private static (float Radius, bool Snapped) SnapRadius(float raw, float tol)
    {
        float? best = null;
        var bestDist = float.PositiveInfinity;
        foreach (var snap in RadiusSnaps)
        {
            var d = MathF.Abs(raw - snap);
            if (d <= tol && d < bestDist)
            {
                best = snap;
                bestDist = d;
            }
        }

        return best is { } r ? (r, true) : (raw, false);
    }

    private static Confidence LowerBand(Confidence conf)
    {
        var score = conf.Score >= 0.85 ? 0.84 : conf.Score >= 0.6 ? 0.59 : conf.Score;
        var reasons = conf.Reasons.IsDefault ? new[] { "center offset" } : conf.Reasons.Add("center offset").ToArray();
        return Confidence.Of(score, reasons);
    }

    private static Vector2 Mean(List<Vector2> pts)
    {
        double x = 0, y = 0;
        foreach (var p in pts)
        {
            x += p.X;
            y += p.Y;
        }

        return new Vector2((float)(x / pts.Count), (float)(y / pts.Count));
    }

    private static List<(ulong Id, DateTime T)> CollectKnockbacks(Recording recording)
    {
        var list = new List<(ulong, DateTime)>();
        foreach (var op in recording.Ops)
        {
            if (op is not ActorCastEventOp ev || ev.Targets.IsDefaultOrEmpty)
                continue;
            foreach (var target in ev.Targets)
            {
                if (target.Effects.NonEmpty().Any(e => e.Type == ActionEffectType.Knockback))
                    list.Add((target.InstanceId, ev.Timestamp));
            }
        }

        return list;
    }

    private static List<(ulong Id, DateTime T)> CollectDeaths(Recording recording)
    {
        var list = new List<(ulong, DateTime)>();
        foreach (var op in recording.Ops)
        {
            if (op is ActorDeadOp { Value: true } dead)
                list.Add((dead.InstanceId, dead.Timestamp));
        }

        return list;
    }

    private static bool IsKnockedBack(List<(ulong Id, DateTime T)> events, ulong id, DateTime t, TimeSpan window)
        => events.Any(e => e.Id == id && t >= e.T && t <= e.T + window);

    private static bool IsNearDeath(List<(ulong Id, DateTime T)> events, ulong id, DateTime t, TimeSpan window)
        => events.Any(e => e.Id == id && t >= e.T - window && t <= e.T);

    private static Evidence? DetectArenaChange(List<(DateTime T, Vector2 P)> timed)
    {
        if (timed.Count < 40)
            return null;
        var start = timed.Min(s => s.T);
        var end = timed.Max(s => s.T);
        var window = TimeSpan.FromSeconds(30);
        float? prevR = null;
        for (var t = start; t < end; t += window)
        {
            var slice = timed.Where(s => s.T >= t && s.T < t + window).Select(s => s.P).ToList();
            if (slice.Count < 8)
                continue;
            var r = Geometry2D.MinEnclosingCircle(slice).Radius;
            if (prevR is { } p && MathF.Abs(r - p) > 3f)
                return new Evidence("ArenaChange", t.ToString("O", CultureInfo.InvariantCulture));
            prevR = r;
        }

        return null;
    }
}
