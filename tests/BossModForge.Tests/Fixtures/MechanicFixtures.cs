using System.Numerics;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public static class MechanicFixtures
{
    public const uint BossOid = 0x4321;
    public const ulong BossId = 0x1000;
    public const uint RaidwideAid = 42001;
    public const uint TankbusterAid = 42002;
    public const uint ConeAid = 42003;
    public const uint KnockbackAid = 42004;
    public const uint GazeAid = 42005;
    public const uint SpreadAid = 42006;
    public const uint StackAid = 42007;
    public static readonly ulong[] Players = [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17];
    public static readonly SegmenterOptions PullOptions = new(TimeSpan.FromSeconds(5), TimeSpan.Zero);
    public static readonly Vector3 Center = new(100, 0, 100);

    public static RecordingBuilder Raidwide() => OneCast(RaidwideAid, (b, action) =>
    {
        b.CastStart(BossId, action, Players[0], Center, 2f, default);
        b.Advance(TimeSpan.FromSeconds(2));
        b.CastEvent(BossId, action, Players[0], Center, default, Hits(i => Effects.Damage(8000)));
        b.CastStop(BossId);
    });

    public static RecordingBuilder Tankbuster() => OneCast(TankbusterAid, (b, action) =>
    {
        b.CastStart(BossId, action, Players[0], Center, 2f, default);
        b.Advance(TimeSpan.FromSeconds(2));
        b.CastEvent(BossId, action, Players[0], Center, default, Hits(i => i == 0 ? Effects.Damage(40000) : Effects.Miss()));
        b.CastStop(BossId);
    });

    public static RecordingBuilder TelegraphedCone() => OneCast(ConeAid, (b, action) =>
    {
        Place(b, ConePos);
        b.CastStart(BossId, action, Players[0], Center, 2f, default);
        b.Advance(TimeSpan.FromSeconds(2));
        Place(b, ConePos);
        var origin = new Vector2(Center.X, Center.Z);
        var rot = Angle.FromDeg(0);
        var targets = new List<CastTarget>();
        for (var i = 0; i < 8; i++)
        {
            var pos = ConePos(i);
            var hit = InferenceShapeContains(new Vector2(pos.X, pos.Z), origin, rot);
            targets.Add(new CastTarget(Players[i], ActionEffects.From([hit ? Effects.Damage(8000) : Effects.Miss()])));
        }

        b.CastEvent(BossId, action, Players[0], Center, rot, [.. targets]).CastStop(BossId);
    });

    public static RecordingBuilder Knockback() => OneCast(KnockbackAid, (b, action) =>
    {
        b.CastStart(BossId, action, Players[0], Center, 2f, default);
        b.Advance(TimeSpan.FromSeconds(2));
        b.CastEvent(BossId, action, Players[0], Center, default, Hits(_ => Effects.Knockback(15, 0)));
        b.CastStop(BossId);
        for (var i = 0; i < 8; i++)
        {
            var pos = ArenaPos(i);
            var away = pos + Vector3.Normalize(pos - Center) * 15f;
            b.Move(Players[i], away, default);
        }

        b.Advance(TimeSpan.FromSeconds(1.5));
    });

    public static RecordingBuilder Gaze() => OneCast(GazeAid, (b, action) =>
    {
        for (var i = 0; i < 8; i++)
            b.Move(Players[i], GazePos(i), GazeRot(i));
        b.CastStart(BossId, action, Players[0], Center, 2f, default);
        b.Advance(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 8; i++)
            b.Move(Players[i], GazePos(i), GazeRot(i));
        b.CastEvent(BossId, action, Players[0], Center, default, Hits(i => i % 2 == 0 ? Effects.Damage(5000) : Effects.Miss()));
        b.CastStop(BossId);
    });

    public static RecordingBuilder Spread()
    {
        var b = Start();
        var action = ActionId.Spell(SpreadAid);
        for (var i = 0; i < 4; i++)
            b.Helper(0x2000 + (ulong)i, Center + new Vector3(i * 2, 0, -8));
        b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, true).Combat(Players[0], true).Targetable(BossId, true);
        for (var i = 0; i < 4; i++)
            b.CastStart(0x2000 + (ulong)i, action, Players[i], Center, 2f, default);
        b.Advance(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 4; i++)
        {
            var targets = Hits(p => p == i ? Effects.Damage(6000) : Effects.Miss());
            b.CastEvent(0x2000 + (ulong)i, action, Players[i], Center, default, targets).CastStop(0x2000 + (ulong)i);
        }

        return Finish(b);
    }

    public static RecordingBuilder Stack(bool singleHit)
    {
        var b = Start();
        var action = ActionId.Spell(StackAid);
        b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, true).Combat(Players[0], true).Targetable(BossId, true);
        for (var i = 0; i < 8; i++)
            b.Move(Players[i], StackPos(i), default);
        b.Icon(Players[1], 50, BossId);
        b.CastStart(BossId, action, Players[1], Center, 2f, default);
        b.Advance(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 8; i++)
            b.Move(Players[i], StackPos(i), default);
        b.CastEvent(BossId, action, Players[1], Center, default, Hits(i =>
        {
            if (singleHit)
                return i == 1 ? Effects.Damage(9000) : Effects.Miss();
            return i < 4 ? Effects.Damage(9000) : Effects.Miss();
        })).CastStop(BossId);
        return Finish(b);
    }

    private static RecordingBuilder OneCast(uint aid, Action<RecordingBuilder, ActionId> cast)
    {
        var b = Start();
        b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, true).Combat(Players[0], true).Targetable(BossId, true);
        b.Advance(TimeSpan.FromSeconds(1));
        Place(b, ArenaPos);
        cast(b, ActionId.Spell(aid));
        return Finish(b);
    }

    private static RecordingBuilder Start()
    {
        var t0 = new DateTime(2026, 9, 20, 2, 0, 0, DateTimeKind.Unspecified);
        var b = new RecordingBuilder(t0);
        b.Frame().Zone(1234, 826);
        var names = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
        var jobs = new[] { Class.PLD, Class.WHM, Class.NIN, Class.BLM, Class.WAR, Class.SCH, Class.DRG, Class.PCT };
        for (var i = 0; i < 8; i++)
        {
            b.Player(Players[i], names[i], jobs[i], ArenaPos(i));
            b.Party(i, 0xA0 + (ulong)i, Players[i], names[i]);
        }

        b.Actor(BossId, BossOid, "Test Boss", ActorType.Enemy, Center, default, 5f, Class.None, 90, nameId: 12716, targetable: true, ally: false, hpmp: new HpMp(500000, 500000, 0, 10000, 10000))
            .Target(BossId, Players[0]);
        return b;
    }

    private static RecordingBuilder Finish(RecordingBuilder b)
    {
        b.Advance(TimeSpan.FromSeconds(3)).Dead(BossId, true).Combat(BossId, false).Destroy(BossId);
        return b;
    }

    private static void Place(RecordingBuilder b, Func<int, Vector3> pos)
    {
        for (var i = 0; i < 8; i++)
            b.Move(Players[i], pos(i), default);
    }

    private static CastTarget[] Hits(Func<int, ulong> effect)
        => Enumerable.Range(0, 8).Select(i => new CastTarget(Players[i], ActionEffects.From([effect(i)]))).ToArray();

    private static Vector3 ArenaPos(int i)
    {
        var ang = i * 45 * (MathF.PI / 180f);
        return Center + new Vector3(8f * MathF.Sin(ang), 0, 8f * MathF.Cos(ang));
    }

    private static Vector3 ConePos(int i) => i switch
    {
        0 => Center + new Vector3(0, 0, 12),
        1 => Center + new Vector3(4, 0, 10),
        2 => Center + new Vector3(-4, 0, 10),
        3 => Center + new Vector3(0, 0, 6),
        4 => Center + new Vector3(18, 0, 0),
        5 => Center + new Vector3(0, 0, -18),
        6 => Center + new Vector3(-18, 0, 0),
        _ => Center + new Vector3(12, 0, -12),
    };

    private static bool InferenceShapeContains(Vector2 xz, Vector2 origin, Angle rot)
    {
        var rel = xz - origin;
        var dist = rel.Length();
        if (dist > 18)
            return false;
        var facing = new Vector2(MathF.Sin(rot.Rad), MathF.Cos(rot.Rad));
        var n = rel.Length();
        if (n < 1e-5f)
            return true;
        var fwd = Vector2.Dot(rel, facing) / n;
        var lat = Vector2.Dot(rel, new Vector2(facing.Y, -facing.X)) / n;
        var deg = MathF.Abs(MathF.Atan2(lat, fwd) * (180f / MathF.PI));
        return deg <= 40;
    }

    private static Vector3 GazePos(int i)
    {
        var ang = i * 45 * (MathF.PI / 180f);
        return Center + new Vector3(12f * MathF.Sin(ang), 0, 12f * MathF.Cos(ang));
    }

    private static Angle GazeRot(int i)
    {
        var pos = GazePos(i);
        var dx = Center.X - pos.X;
        var dz = Center.Z - pos.Z;
        var toward = new Angle(MathF.Atan2(dx, dz));
        return i % 2 == 0 ? toward : new Angle(toward.Rad + MathF.PI);
    }

    private static Vector3 StackPos(int i)
    {
        if (i < 4)
            return Center + new Vector3((i - 1.5f) * 1.2f, 0, 4);
        var ang = i * 45 * (MathF.PI / 180f);
        return Center + new Vector3(16f * MathF.Sin(ang), 0, 16f * MathF.Cos(ang));
    }
}
