using System.Numerics;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public static class SyntheticShapes
{
    public const uint BossOid = 0x4321;
    public const uint ConeAid = 41001;
    public const uint CircleAid = 41002;
    public const uint RaidwideAid = 41003;
    public const ulong BossId = 0x1000;
    public static readonly ulong[] Players = [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17];
    public static readonly SegmenterOptions PullOptions = new(TimeSpan.FromSeconds(5), TimeSpan.Zero);

    public static RecordingBuilder Build(DateTime? start = null)
    {
        var t0 = start ?? new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var b = new RecordingBuilder(t0);
        var center = new Vector3(100, 0, 100);
        var rot = Angle.FromDeg(0);
        var names = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
        var jobs = new[] { Class.PLD, Class.WHM, Class.NIN, Class.BLM, Class.WAR, Class.SCH, Class.DRG, Class.PCT };

        b.Frame().Zone(1234, 826);
        for (var i = 0; i < 8; i++)
        {
            b.Player(Players[i], names[i], jobs[i], ArenaPos(center, i, 0));
            b.Party(i, 0xA0 + (ulong)i, Players[i], names[i]);
        }

        b.Actor(BossId, BossOid, "Test Boss", ActorType.Enemy, center, rot, 5f, Class.None, 90, nameId: 12716, targetable: true, ally: false, hpmp: new HpMp(500000, 500000, 0, 10000, 10000))
            .Target(BossId, Players[0]);

        var tick = 0;
        for (var pull = 0; pull < 3; pull++)
        {
            b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, true).Combat(Players[0], true).Targetable(BossId, true);
            WalkArena(b, center, ref tick, 12);
            CastAt(b, center, rot, ActionId.Spell(ConeAid), Shape.Cone(40, 45), ConePos, ref tick);
            WalkArena(b, center, ref tick, 8);
            CastAt(b, center, rot, ActionId.Spell(CircleAid), Shape.Circle(6), CirclePos, ref tick);
            WalkArena(b, center, ref tick, 8);
            CastAt(b, center, rot, ActionId.Spell(RaidwideAid), Shape.Raidwide, ArenaPos, ref tick);
            WalkArena(b, center, ref tick, 8);
            if (pull < 2)
                b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, false);
        }

        b.Advance(TimeSpan.FromSeconds(1)).Dead(BossId, true).Combat(BossId, false).Destroy(BossId);
        return b;
    }

    private static void CastAt(
        RecordingBuilder b,
        Vector3 center,
        Angle rot,
        ActionId action,
        Shape shape,
        Func<Vector3, int, int, Vector3> place,
        ref int tick)
    {
        for (var i = 0; i < 8; i++)
            b.Move(Players[i], place(center, i, tick), default);
        b.CastStart(BossId, action, Players[0], center, 2f, rot);
        for (var s = 0; s < 20; s++)
        {
            b.Advance(TimeSpan.FromSeconds(0.1));
            tick++;
            for (var i = 0; i < 8; i++)
                b.Move(Players[i], place(center, i, tick), default);
        }

        var origin = new Vector2(center.X, center.Z);
        var targets = new List<CastTarget>();
        for (var i = 0; i < 8; i++)
        {
            var pos = place(center, i, tick);
            var xz = new Vector2(pos.X, pos.Z);
            var hit = shape.Kind == ShapeKind.RaidwideNoShape || shape.Contains(xz, origin, rot);
            targets.Add(new CastTarget(Players[i], ActionEffects.From([hit ? Effects.Damage(8000) : Effects.Miss()])));
        }

        b.CastEvent(BossId, action, Players[0], center, rot, [.. targets]).CastStop(BossId);
    }

    private static void WalkArena(RecordingBuilder b, Vector3 center, ref int tick, int steps)
    {
        for (var s = 0; s < steps; s++)
        {
            b.Advance(TimeSpan.FromSeconds(0.5));
            tick++;
            for (var i = 0; i < 8; i++)
                b.Move(Players[i], ArenaPos(center, i, tick), default);
        }
    }

    private static Vector3 ArenaPos(Vector3 center, int i, int tick)
    {
        var ang = (i * 45 + tick * 11) * (MathF.PI / 180f);
        var r = 6f + (i % 4) * 4.5f;
        return center + new Vector3(r * MathF.Sin(ang), 0, r * MathF.Cos(ang));
    }

    private static Vector3 ConePos(Vector3 center, int i, int tick)
    {
        _ = tick;
        float deg;
        float r;
        switch (i)
        {
            case 0: deg = -25; r = 18; break;
            case 1: deg = 0; r = 12; break;
            case 2: deg = 25; r = 18; break;
            case 3: deg = 0; r = 6; break;
            case 4: deg = 90; r = 18; break;
            case 5: deg = 180; r = 18; break;
            case 6: deg = -55; r = 18; break;
            default: deg = 70; r = 16; break;
        }

        var a = deg * (MathF.PI / 180f);
        return center + new Vector3(r * MathF.Sin(a), 0, r * MathF.Cos(a));
    }

    private static Vector3 CirclePos(Vector3 center, int i, int tick)
    {
        _ = tick;
        var r = i < 3 ? 3f : 12f;
        var a = i * 45 * (MathF.PI / 180f);
        return center + new Vector3(r * MathF.Sin(a), 0, r * MathF.Cos(a));
    }
}
