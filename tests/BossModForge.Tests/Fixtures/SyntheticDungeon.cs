using System.Numerics;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public static class SyntheticDungeon
{
    public const uint BossOid = 0x4321;
    public const uint HeavySlash = 40001;
    public const ulong BossId = 0x1000;
    public const ulong HelperId = 0x2000;
    public const ulong Player1 = 0x10;
    public const ulong Player2 = 0x11;
    public const ulong Player3 = 0x12;
    public const ulong Player4 = 0x13;

    public static readonly SegmenterOptions PullOptions = new(TimeSpan.FromSeconds(5), TimeSpan.Zero);

    public static RecordingBuilder Build(DateTime? start = null)
    {
        var t0 = start ?? new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var b = new RecordingBuilder(t0);
        var cone = ActionId.Spell(HeavySlash);
        var origin = new Vector3(100, 0, 100);
        var rot = Angle.FromDeg(90);

        b.Frame()
            .Zone(1234, 826)
            .DirectorUpdate(0x11111111, 0x1)
            .EnvControl(0x0A, 0x00000001)
            .Player(Player1, "Alice", Class.PLD, new Vector3(95, 0, 100))
            .Player(Player2, "Bob", Class.WHM, new Vector3(105, 0, 100))
            .Player(Player3, "Cara", Class.NIN, new Vector3(100, 0, 95))
            .Player(Player4, "Dee", Class.BLM, new Vector3(100, 0, 105))
            .Actor(BossId, BossOid, "Test Boss", ActorType.Enemy, origin, rot, 5f, Class.None, 90, nameId: 12716, targetable: true, ally: false, hpmp: new HpMp(500000, 500000, 0, 10000, 10000))
            .Helper(HelperId, new Vector3(100, 0, 90))
            .Party(0, 0xA1, Player1, "Alice")
            .Party(1, 0xA2, Player2, "Bob")
            .Party(2, 0xA3, Player3, "Cara")
            .Party(3, 0xA4, Player4, "Dee")
            .Rename(BossId, "Test Boss", 12716)
            .Target(BossId, Player1)
            .Raw("ZZZZ", "x");

        for (var pull = 0; pull < 3; pull++)
        {
            b.Advance(TimeSpan.FromSeconds(1))
                .Combat(BossId, true)
                .Combat(Player1, true)
                .Targetable(BossId, true)
                .Move(BossId, origin + new Vector3(pull, 0, 0), rot)
                .HpMp(BossId, new HpMp((uint)(500000 - pull * 10000), 500000, 0, 10000, 10000))
                .CastStart(BossId, cone, Player1, origin, 5f, rot)
                .Advance(TimeSpan.FromSeconds(5))
                .CastEvent(
                    BossId, cone, Player1, origin, rot,
                    new CastTarget(Player1, ActionEffects.From([Effects.Damage(12000)])),
                    new CastTarget(Player2, ActionEffects.From([Effects.Damage(11000)])),
                    new CastTarget(Player3, ActionEffects.From([Effects.Damage(10000)])),
                    new CastTarget(Player4, ActionEffects.From([Effects.Damage(9000)])))
                .CastStop(BossId)
                .StatusGain(Player1, 0, 1234, 0x0001, 8f, BossId)
                .Icon(Player1, 50, BossId)
                .Tether(HelperId, 7, Player2)
                .Advance(TimeSpan.FromSeconds(1))
                .StatusLose(Player1, 0)
                .Untether(HelperId);

            if (pull < 2)
            {
                b.Dead(Player3, true)
                    .Advance(TimeSpan.FromSeconds(1))
                    .Dead(Player3, false)
                    .Combat(BossId, false);
            }
        }

        b.Advance(TimeSpan.FromSeconds(1))
            .Dead(BossId, true)
            .Combat(BossId, false)
            .Destroy(HelperId)
            .Destroy(BossId);

        return b;
    }
}
