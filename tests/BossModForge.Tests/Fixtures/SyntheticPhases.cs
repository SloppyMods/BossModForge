using System.Numerics;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public static class SyntheticPhases
{
    public const uint BossOid = 0x4444;
    public const uint A = 40201;
    public const uint B = 40202;
    public const uint C = 40203;
    public const uint D = 40204;
    public const ulong BossId = 0x1000;
    public static readonly ulong[] Players = [0x10, 0x11, 0x12, 0x13];
    public static readonly SegmenterOptions PullOptions = new(TimeSpan.FromSeconds(5), TimeSpan.Zero);
    public static readonly Vector3 Center = new(100, 0, 100);
    public static readonly IGameData GameData = new PhaseGameData();

    public static RecordingBuilder Build(DateTime? start = null)
    {
        var t0 = start ?? new DateTime(2026, 9, 20, 4, 0, 0, DateTimeKind.Unspecified);
        var b = new RecordingBuilder(t0);
        var names = new[] { "A", "B", "C", "D" };
        var jobs = new[] { Class.PLD, Class.WHM, Class.NIN, Class.BLM };
        b.Frame().Zone(1234, 826);
        for (var i = 0; i < 4; i++)
        {
            b.Player(Players[i], names[i], jobs[i], Center + new Vector3(4 + i, 0, 0));
            b.Party(i, 0xA0 + (ulong)i, Players[i], names[i]);
        }

        b.Actor(BossId, BossOid, "Test Boss", ActorType.Enemy, Center, default, 5f, Class.None, 90, nameId: 12716, targetable: true, ally: false, hpmp: new HpMp(100000, 100000, 0, 10000, 10000))
            .Target(BossId, Players[0]);

        for (var pull = 0; pull < 3; pull++)
        {
            b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, true).Combat(Players[0], true).Targetable(BossId, true)
                .HpMp(BossId, new HpMp(100000, 100000, 0, 10000, 10000));
            Cast(b, A, 2f, 4f);
            Cast(b, B, 2f, 4f);
            Cast(b, A, 2f, 4f);
            Cast(b, B, 2f, 4f);
            b.Advance(TimeSpan.FromSeconds(1))
                .HpMp(BossId, new HpMp(50000, 100000, 0, 10000, 10000))
                .Targetable(BossId, false)
                .Advance(TimeSpan.FromSeconds(12))
                .Targetable(BossId, true);
            Cast(b, C, 1f, 5f);
            Cast(b, D, 2f, 4f);
            Cast(b, C, 2f, 5f);
            Cast(b, D, 2f, 4f);
            if (pull < 2)
                b.Advance(TimeSpan.FromSeconds(1)).Combat(BossId, false);
        }

        b.Advance(TimeSpan.FromSeconds(1)).Dead(BossId, true).Combat(BossId, false).Destroy(BossId);
        return b;
    }

    private static void Cast(RecordingBuilder b, uint aid, float delay, float castTime)
    {
        var action = ActionId.Spell(aid);
        var hits = Enumerable.Range(0, 4).Select(i => new CastTarget(Players[i], ActionEffects.From([Effects.Damage(8000)]))).ToArray();
        b.Advance(TimeSpan.FromSeconds(delay));
        b.CastStart(BossId, action, Players[0], Center, castTime, default);
        b.Advance(TimeSpan.FromSeconds(castTime));
        b.CastEvent(BossId, action, Players[0], Center, default, hits).CastStop(BossId);
    }

    private sealed class PhaseGameData : IGameData
    {
        private static readonly Dictionary<uint, ActionRow> Rows = new()
        {
            [A] = new ActionRow(A, "A", 2, 40, 0, 40, 0),
            [B] = new ActionRow(B, "B", 2, 40, 0, 40, 0),
            [C] = new ActionRow(C, "C", 2, 40, 0, 50, 0),
            [D] = new ActionRow(D, "D", 2, 40, 0, 40, 0),
        };

        public ActionRow? Action(uint id) => Rows.GetValueOrDefault(id);
        public string? BNpcName(uint nameId) => "Test Boss";
        public string? StatusName(uint id) => null;
        public (uint CfcId, string Name)? ContentForTerritory(uint territoryType) => null;
        public string? PlaceName(uint territoryType) => null;
    }
}
