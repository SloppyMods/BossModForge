using System.Numerics;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public static class SyntheticRotation
{
    public const uint BossOid = 0x4444;
    public const uint Opener = 40101;
    public const uint Circle = 40102;
    public const uint Cone = 40103;
    public const uint Raidwide2 = 40104;
    public const uint Circle2 = 40105;
    public const uint Buster = 40106;
    public const uint Raidwide3 = 40107;
    public const uint ConeAlt = 40108;
    public const ulong BossId = 0x1000;
    public static readonly ulong[] Players = [0x10, 0x11, 0x12, 0x13];
    public static readonly SegmenterOptions PullOptions = new(TimeSpan.FromSeconds(5), TimeSpan.Zero);
    public static readonly Vector3 Center = new(100, 0, 100);
    public static readonly IGameData GameData = new RotationGameData();

    public static RecordingBuilder Build(bool jitter = false, DateTime? start = null)
    {
        var t0 = start ?? new DateTime(2026, 9, 20, 3, 0, 0, DateTimeKind.Unspecified);
        var b = new RecordingBuilder(t0);
        var rot = Angle.FromDeg(0);
        var names = new[] { "A", "B", "C", "D" };
        var jobs = new[] { Class.PLD, Class.WHM, Class.NIN, Class.BLM };
        b.Frame().Zone(1234, 826);
        for (var i = 0; i < 4; i++)
        {
            b.Player(Players[i], names[i], jobs[i], PlayerPos(i));
            b.Party(i, 0xA0 + (ulong)i, Players[i], names[i]);
        }

        b.Actor(BossId, BossOid, "Test Boss", ActorType.Enemy, Center, rot, 5f, Class.None, 90, nameId: 12716, targetable: true, ally: false, hpmp: new HpMp(500000, 500000, 0, 10000, 10000))
            .Target(BossId, Players[0]);

        var circle2Index = 0;
        uint[][] bodies =
        [
            [Circle, Raidwide2, Cone, Circle2, Buster, Raidwide3, Circle, Raidwide2, Cone, Circle2, Buster, Raidwide3, Circle, Raidwide2, Cone],
            [Circle, Raidwide2, Cone, Circle2, Buster, Raidwide3, Circle, Raidwide2, Cone, Circle2, Buster, Raidwide3, Circle],
            [Circle, Raidwide2, Cone, Circle2, Buster, Raidwide3, Circle, Raidwide2, Cone, Circle2, Buster, Raidwide3],
        ];
        float[][] delays =
        [
            [6, 4.5f, 5, 7, 4, 5.5f, 6, 4.5f, 5, 7, 4, 5.5f, 6, 4.5f, 5],
            [6, 4.5f, 5, 7, 4, 5.5f, 6, 4.5f, 5, 7, 4, 5.5f, 6],
            [6, 4.5f, 5, 7, 4, 5.5f, 6, 4.5f, 5, 7, 4, 5.5f],
        ];

        for (var pull = 0; pull < 3; pull++)
        {
            b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, true).Combat(Players[0], true).Targetable(BossId, true);
            Place(b);
            Cast(b, Opener, 2f, 5f, RaidwideHits());
            var openerAid = pull == 2 ? ConeAlt : Cone;
            Cast(b, openerAid, 3.2f, 4f, ConeHits());
            for (var i = 0; i < bodies[pull].Length; i++)
            {
                var aid = bodies[pull][i];
                var delay = delays[pull][i];
                if (jitter && aid == Circle2)
                {
                    var perturb = (circle2Index % 3) switch { 0 => 3f, 1 => -3f, _ => 0f };
                    delay += perturb;
                    circle2Index++;
                }

                Cast(b, aid, delay, CastTime(aid), HitsFor(aid));
            }

            if (pull == 1)
                b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, false);
            else if (pull == 0)
                b.Advance(TimeSpan.FromSeconds(1)).Dead(BossId, true).Combat(BossId, false).Advance(TimeSpan.FromSeconds(1)).Dead(BossId, false);
            else
                b.Advance(TimeSpan.FromSeconds(1)).Dead(BossId, true).Combat(BossId, false);
        }

        b.Advance(TimeSpan.FromSeconds(1)).Dead(BossId, true).Combat(BossId, false).Destroy(BossId);
        return b;
    }

    private static void Cast(RecordingBuilder b, uint aid, float delay, float castTime, CastTarget[] hits)
    {
        var action = ActionId.Spell(aid);
        Place(b);
        b.Advance(TimeSpan.FromSeconds(delay));
        b.CastStart(BossId, action, Players[0], Center, castTime, default);
        b.Advance(TimeSpan.FromSeconds(castTime));
        Place(b);
        b.CastEvent(BossId, action, Players[0], Center, default, hits).CastStop(BossId);
    }

    private static float CastTime(uint aid) => aid switch
    {
        Opener => 5,
        Circle => 3,
        Cone => 4,
        ConeAlt => 4,
        Raidwide2 => 4,
        Circle2 => 3,
        Buster => 4,
        Raidwide3 => 5,
        _ => 3,
    };

    private static CastTarget[] HitsFor(uint aid) => aid switch
    {
        Buster => Hits(i => i == 0 ? Effects.Damage(40000) : Effects.Miss()),
        Cone or ConeAlt => ConeHits(),
        Circle or Circle2 => CircleHits(),
        _ => RaidwideHits(),
    };

    private static CastTarget[] RaidwideHits() => Hits(_ => Effects.Damage(8000));
    private static CastTarget[] ConeHits() => Hits(i => i < 2 ? Effects.Damage(8000) : Effects.Miss());
    private static CastTarget[] CircleHits() => Hits(i => i < 2 ? Effects.Damage(8000) : Effects.Miss());

    private static CastTarget[] Hits(Func<int, ulong> effect)
        => Enumerable.Range(0, 4).Select(i => new CastTarget(Players[i], ActionEffects.From([effect(i)]))).ToArray();

    private static void Place(RecordingBuilder b)
    {
        for (var i = 0; i < 4; i++)
            b.Move(Players[i], PlayerPos(i), default);
    }

    private static Vector3 PlayerPos(int i) => i switch
    {
        0 => Center + new Vector3(0, 0, 4),
        1 => Center + new Vector3(0, 0, 5),
        2 => Center + new Vector3(8, 0, 0),
        _ => Center + new Vector3(10, 0, 0),
    };

    private sealed class RotationGameData : IGameData
    {
        private static readonly Dictionary<uint, ActionRow> Rows = new()
        {
            [Opener] = new ActionRow(Opener, "Opener", 2, 40, 0, 50, 0),
            [Circle] = new ActionRow(Circle, "Circle", 2, 6, 0, 30, 0),
            [Cone] = new ActionRow(Cone, "Cone", 3, 20, 0, 40, 12, "gl_fan090_1bf"),
            [Raidwide2] = new ActionRow(Raidwide2, "Raidwide2", 2, 40, 0, 40, 0),
            [Circle2] = new ActionRow(Circle2, "Circle2", 2, 6, 0, 30, 0),
            [Buster] = new ActionRow(Buster, "Buster", 1, 0, 0, 40, 0),
            [Raidwide3] = new ActionRow(Raidwide3, "Raidwide3", 2, 40, 0, 50, 0),
            [ConeAlt] = new ActionRow(ConeAlt, "ConeAlt", 3, 20, 0, 40, 12, "gl_fan090_1bf"),
        };

        public ActionRow? Action(uint id) => Rows.GetValueOrDefault(id);
        public string? BNpcName(uint nameId) => "Test Boss";
        public string? StatusName(uint id) => null;
        public (uint CfcId, string Name)? ContentForTerritory(uint territoryType) => null;
        public string? PlaceName(uint territoryType) => null;
    }
}
