using System.Numerics;
using Dalamud.Bindings.ImGui;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Replay;

namespace BossModForge.Plugin.UI;

internal static class EvidencePlot
{
    public static void Draw(AnalysisResult result, AbilityModel ability, int instanceIndex, Action<int> setInstance)
    {
        var catalog = result.Catalog.Abilities.FirstOrDefault(a => a.Action.Id == ability.ActionId);
        if (catalog is null)
        {
            ImGui.TextDisabled("No catalog instances for this AID.");
            return;
        }

        var arenaR = MathF.Max(result.Effective.Arena.Radius, 20f);
        DrawScatter(result, catalog, ability.Shape, arenaR);
        ImGui.Separator();
        DrawTopDown(result, catalog, ability.Shape, instanceIndex, setInstance, arenaR);
    }

    private static void DrawScatter(AnalysisResult result, Ability catalog, Shape? shape, float arenaR)
    {
        ImGui.Text("Angle vs distance");
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var size = new Vector2(MathF.Max(180, avail.X), 180);
        ImGui.Dummy(size);
        var draw = ImGui.GetWindowDrawList();
        var maxDist = MathF.Max(arenaR * 2f, 40f);
        var points = CollectPolar(result, catalog);
        Subsample(points, 2000);

        var x0 = origin.X + 28;
        var y0 = origin.Y + 8;
        var w = size.X - 36;
        var h = size.Y - 24;
        var axis = Ui.ColorU32(new Vector4(0.55f, 0.55f, 0.55f, 1f));
        draw.AddLine(new Vector2(x0, y0 + h), new Vector2(x0 + w, y0 + h), axis, 1f);
        draw.AddLine(new Vector2(x0 + w * 0.5f, y0), new Vector2(x0 + w * 0.5f, y0 + h), axis, 1f);
        for (var a = -180; a <= 180; a += 45)
        {
            var x = x0 + ((a + 180) / 360f) * w;
            draw.AddLine(new Vector2(x, y0 + h), new Vector2(x, y0 + h + 4), axis, 1f);
            draw.AddText(new Vector2(x - 8, y0 + h + 4), axis, a.ToString());
        }

        OverlayShape(draw, shape, x0, y0, w, h, maxDist);

        foreach (var p in points)
        {
            var x = x0 + ((p.AngleDeg + 180f) / 360f) * w;
            var y = y0 + h - Math.Clamp(p.Distance / maxDist, 0, 1) * h;
            var color = p.Hit
                ? Ui.ColorU32(new Vector4(0.2f, 0.8f, 0.3f, 0.9f))
                : Ui.ColorU32(new Vector4(0.55f, 0.55f, 0.55f, 0.7f));
            draw.AddCircleFilled(new Vector2(x, y), 2.2f, color, 8);
        }
    }

    private static void DrawTopDown(
        AnalysisResult result,
        Ability catalog,
        Shape? shape,
        int instanceIndex,
        Action<int> setInstance,
        float arenaR)
    {
        var n = catalog.Instances.Length;
        if (n == 0)
        {
            ImGui.TextDisabled("No instances.");
            return;
        }

        instanceIndex = Math.Clamp(instanceIndex, 0, n - 1);
        if (ImGui.Button("◄") && instanceIndex > 0)
            setInstance(instanceIndex - 1);
        ImGui.SameLine();
        ImGui.Text($"{instanceIndex + 1}/{n}");
        ImGui.SameLine();
        if (ImGui.Button("►") && instanceIndex + 1 < n)
            setInstance(instanceIndex + 1);

        var inst = catalog.Instances[instanceIndex];
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var canvas = MathF.Min(MathF.Max(160, avail.X), 280);
        ImGui.Dummy(new Vector2(canvas, canvas));
        var center = origin + new Vector2(canvas * 0.5f, canvas * 0.5f);
        var scale = (canvas * 0.5f) / MathF.Max(1f, arenaR);
        var draw = ImGui.GetWindowDrawList();
        var arena = result.Effective.Arena;
        var arenaCenter = new Vector2(arena.CenterX, arena.CenterZ);

        draw.AddCircle(center, canvas * 0.5f, 0x40FFFFFF, 64, 1.5f);
        DrawArena(draw, arena, arenaCenter, center, scale);

        var t = inst.EffectTime;
        var srcActor = result.Recording.ActorAt(inst.SourceInstanceId, t);
        var srcPos = srcActor?.PosRotAt(t).Pos ?? inst.Origin;
        var srcRot = srcActor?.PosRotAt(t).Rot ?? inst.Rotation;
        var srcCanvas = WorldToCanvas(Xz(srcPos), arenaCenter, center, scale);
        draw.AddCircle(srcCanvas, 5f, 0xFF00FFFF, 16, 2f);
        var facing = Facing(srcRot);
        draw.AddLine(srcCanvas, srcCanvas + new Vector2(facing.X, -facing.Y) * 12f, 0xFF00FFFF, 2f);

        if (shape is not null)
            DrawShapeWorld(draw, shape, Xz(srcPos), srcRot, arenaCenter, center, scale);

        if (inst.PullIndex >= 0 && inst.PullIndex < result.Pulls.Length)
        {
            var pull = result.Pulls[inst.PullIndex];
            var hitIds = new HashSet<ulong>(inst.Hits.Select(h => h.TargetInstanceId));
            foreach (var pid in pull.PlayerInstanceIds)
            {
                var actor = result.Recording.ActorAt(pid, t);
                if (actor is null)
                    continue;
                var p = WorldToCanvas(Xz(actor.PosRotAt(t).Pos), arenaCenter, center, scale);
                var hit = hitIds.Contains(pid);
                draw.AddCircleFilled(p, 3.5f, hit ? 0xFF4444FF : 0xFF888888, 8);
            }
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"AID {catalog.Action.Id} @ {inst.EffectTime:HH:mm:ss.fff}\nsource {srcPos}");
    }

    private static void DrawArena(ImDrawListPtr draw, ArenaModel arena, Vector2 arenaCenter, Vector2 center, float scale)
    {
        var color = 0x80FFFFFF;
        switch (arena.Kind.ToLowerInvariant())
        {
            case "square":
            case "rect":
                var hw = (arena.HalfWidth > 0 ? arena.HalfWidth : arena.Radius) * scale;
                var hh = (arena.HalfHeight > 0 ? arena.HalfHeight : arena.Radius) * scale;
                draw.AddRect(
                    center - new Vector2(hw, hh),
                    center + new Vector2(hw, hh),
                    color);
                break;
            case "custom":
                if (arena.Vertices.Length >= 2)
                {
                    for (var i = 0; i < arena.Vertices.Length; i++)
                    {
                        var a = arena.Vertices[i];
                        var b = arena.Vertices[(i + 1) % arena.Vertices.Length];
                        draw.AddLine(
                            WorldToCanvas(new Vector2(a.X, a.Z), arenaCenter, center, scale),
                            WorldToCanvas(new Vector2(b.X, b.Z), arenaCenter, center, scale),
                            color,
                            1.5f);
                    }
                }
                break;
            default:
                draw.AddCircle(center, MathF.Max(arena.Radius, 1) * scale, color, 64, 1.5f);
                break;
        }
    }

    private static void DrawShapeWorld(
        ImDrawListPtr draw,
        Shape shape,
        Vector2 origin,
        Angle rot,
        Vector2 arenaCenter,
        Vector2 center,
        float scale)
    {
        var fill = Ui.ColorU32(new Vector4(1f, 0.35f, 0.2f, 0.25f));
        var src = WorldToCanvas(origin, arenaCenter, center, scale);
        switch (shape.Kind)
        {
            case ShapeKind.Circle:
                draw.AddCircle(src, shape.Radius * scale, fill, 48, 2f);
                break;
            case ShapeKind.Donut:
                draw.AddCircle(src, shape.Radius * scale, fill, 48, 2f);
                draw.AddCircle(src, shape.InnerRadius * scale, fill, 48, 1.5f);
                break;
            case ShapeKind.Cone:
                DrawCone(draw, src, rot, shape.Radius * scale, shape.HalfAngleDeg, fill);
                break;
            case ShapeKind.Rect:
            case ShapeKind.Cross:
                DrawRect(draw, src, rot, shape.Radius * scale, shape.HalfWidth * scale, shape.LengthBack * scale, fill);
                if (shape.Kind == ShapeKind.Cross)
                    DrawRect(draw, src, new Angle(rot.Rad + MathF.PI / 2), shape.Radius * scale, shape.HalfWidth * scale, 0, fill);
                break;
        }
    }

    private static void DrawCone(ImDrawListPtr draw, Vector2 src, Angle rot, float radiusPx, float halfDeg, uint fill)
    {
        // VERIFY-5: PathArcTo / PathLineTo / PathFillConvex
        var facing = Facing(rot);
        var start = rot.Rad - halfDeg * (MathF.PI / 180f);
        var end = rot.Rad + halfDeg * (MathF.PI / 180f);
        draw.PathLineTo(src);
        draw.PathArcTo(src, radiusPx, StartAtan(start, facing), StartAtan(end, facing), 24);
        draw.PathFillConvex(fill);
    }

    private static float StartAtan(float rad, Vector2 _)
        => rad - MathF.PI / 2;

    private static void DrawRect(ImDrawListPtr draw, Vector2 src, Angle rot, float lengthPx, float halfWPx, float backPx, uint fill)
    {
        var facing = Facing(rot);
        var fwd = new Vector2(facing.X, -facing.Y);
        var right = new Vector2(fwd.Y, -fwd.X);
        var a = src - fwd * backPx - right * halfWPx;
        var b = src - fwd * backPx + right * halfWPx;
        var c = src + fwd * lengthPx + right * halfWPx;
        var d = src + fwd * lengthPx - right * halfWPx;
        // VERIFY-5: AddQuadFilled
        draw.AddQuadFilled(a, b, c, d, fill);
    }

    private static Vector2 WorldToCanvas(Vector2 world, Vector2 arenaCenter, Vector2 canvasCenter, float scale)
    {
        var rel = world - arenaCenter;
        return canvasCenter + new Vector2(rel.X, -rel.Y) * scale;
    }

    private readonly record struct Polar(float AngleDeg, float Distance, bool Hit);

    private static List<Polar> CollectPolar(AnalysisResult result, Ability catalog)
    {
        var list = new List<Polar>();
        foreach (var inst in catalog.Instances)
        {
            if (inst.PullIndex < 0 || inst.PullIndex >= result.Pulls.Length)
                continue;
            var pull = result.Pulls[inst.PullIndex];
            var t = inst.EffectTime;
            var src = result.Recording.ActorAt(inst.SourceInstanceId, t);
            var origin = Xz(src?.PosRotAt(t).Pos ?? inst.Origin);
            var facing = Facing(src?.PosRotAt(t).Rot ?? inst.Rotation);
            var hitIds = new HashSet<ulong>(inst.Hits.Select(h => h.TargetInstanceId));
            foreach (var pid in pull.PlayerInstanceIds)
            {
                var actor = result.Recording.ActorAt(pid, t);
                if (actor is null)
                    continue;
                var rel = Xz(actor.PosRotAt(t).Pos) - origin;
                var dist = rel.Length();
                var ang = dist < 1e-5f ? 0 : SignedAngleDeg(rel, facing);
                list.Add(new Polar(ang, dist, hitIds.Contains(pid) && inst.Hits.Any(h => h.TargetInstanceId == pid && ShapeObservations.IsDamaging(h))));
            }
        }

        return list;
    }

    private static void Subsample(List<Polar> points, int max)
    {
        if (points.Count <= max)
            return;
        var stride = (int)Math.Ceiling(points.Count / (double)max);
        var n = 0;
        for (var i = 0; i < points.Count; i += stride)
            points[n++] = points[i];
        points.RemoveRange(n, points.Count - n);
    }

    private static void OverlayShape(ImDrawListPtr draw, Shape? shape, float x0, float y0, float w, float h, float maxDist)
    {
        if (shape is null)
            return;
        var fill = Ui.ColorU32(new Vector4(1f, 0.4f, 0.15f, 0.18f));
        switch (shape.Kind)
        {
            case ShapeKind.Circle:
                FillBand(draw, x0, y0, w, h, maxDist, 0, shape.Radius, fill);
                break;
            case ShapeKind.Donut:
                FillBand(draw, x0, y0, w, h, maxDist, shape.InnerRadius, shape.Radius, fill);
                break;
            case ShapeKind.Cone:
                var xL = x0 + ((-shape.HalfAngleDeg + 180f) / 360f) * w;
                var xR = x0 + ((shape.HalfAngleDeg + 180f) / 360f) * w;
                var yTop = y0 + h - Math.Clamp(shape.Radius / maxDist, 0, 1) * h;
                draw.AddRectFilled(new Vector2(xL, yTop), new Vector2(xR, y0 + h), fill);
                break;
        }
    }

    private static void FillBand(ImDrawListPtr draw, float x0, float y0, float w, float h, float maxDist, float inner, float outer, uint fill)
    {
        var yOuter = y0 + h - Math.Clamp(outer / maxDist, 0, 1) * h;
        var yInner = y0 + h - Math.Clamp(inner / maxDist, 0, 1) * h;
        draw.AddRectFilled(new Vector2(x0, yOuter), new Vector2(x0 + w, yInner), fill);
    }

    // Geometry2D is internal to Core; keep the same XZ / facing math here.
    private static Vector2 Xz(Vector3 p) => new(p.X, p.Z);

    private static Vector2 Facing(Angle a) => new(MathF.Sin(a.Rad), MathF.Cos(a.Rad));

    private static float SignedAngleDeg(Vector2 rel, Vector2 facing)
    {
        var n = rel.Length();
        if (n < 1e-6f)
            return 0;
        var fwd = Vector2.Dot(rel, facing) / n;
        var right = new Vector2(facing.Y, -facing.X);
        var lat = Vector2.Dot(rel, right) / n;
        return MathF.Atan2(lat, fwd) * (180f / MathF.PI);
    }
}
