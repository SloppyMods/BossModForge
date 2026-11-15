using System.Numerics;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

/// <summary>
/// One recording containing two sequential boss encounters in the same CFC (a "door boss"):
/// boss A (OID 0x1234) then boss B (OID 0x5678), each with its own pull. Used to pin the
/// encounter-identity rule (§5.1-4: two OIDs in one CFC → two encounters) and <c>--select-boss</c>.
/// </summary>
public sealed class SyntheticDoorBoss
{
    public const ushort Zone = 1180;
    public const ushort Cfc = 826;
    public const uint BossAOid = 0x1234;
    public const uint BossBOid = 0x5678;
    public const uint BossANameId = 9101;
    public const uint BossBNameId = 9102;
    public const uint BossAAttack = 40020;
    public const uint BossBAttack = 40021;

    public static readonly SegmenterOptions PullOptions = new(TimeSpan.FromSeconds(5), TimeSpan.Zero);

    public RecordingBuilder Builder { get; } = Build();

    public string WriteTemp(ReplayEncoding encoding) => Builder.WriteTemp(encoding);

    private static RecordingBuilder Build()
    {
        var b = new RecordingBuilder(new DateTime(2026, 9, 20, 22, 0, 0, DateTimeKind.Unspecified));
        var hp = new HpMp(100000, 100000, 0, 10000, 10000);
        b.Frame()
            .Zone(Zone, Cfc)
            .Actor(0x10, 0, "Alice", ActorType.Player, new Vector3(95, 0, 100), default, 0.5f, Class.PLD, 100, targetable: true, ally: true, hpmp: hp)
            .Actor(0x11, 0, "Bob", ActorType.Player, new Vector3(105, 0, 100), default, 0.5f, Class.WHM, 100, targetable: true, ally: true, hpmp: hp)
            .Actor(0x12, 0, "Cara", ActorType.Player, new Vector3(100, 0, 95), default, 0.5f, Class.NIN, 100, targetable: true, ally: true, hpmp: hp)
            .Actor(0x13, 0, "Dee", ActorType.Player, new Vector3(100, 0, 105), default, 0.5f, Class.BLM, 100, targetable: true, ally: true, hpmp: hp);

        Pull(b, bossId: 0x1000, BossAOid, "Door Boss", BossANameId, ActionId.Spell(BossAAttack));
        Pull(b, bossId: 0x2000, BossBOid, "Gate Lord", BossBNameId, ActionId.Spell(BossBAttack));
        return b;
    }

    private static void Pull(RecordingBuilder b, ulong bossId, uint oid, string name, uint nameId, ActionId attack)
    {
        var origin = new Vector3(100, 0, 100);
        var rot = Angle.FromDeg(90);
        b.Advance(TimeSpan.FromSeconds(1))
            .Actor(bossId, oid, name, ActorType.Enemy, origin, rot, 5f, Class.None, 90, nameId: nameId, targetable: true, ally: false, hpmp: new HpMp(500000, 500000, 0, 10000, 10000))
            .Combat(bossId, true)
            .Target(bossId, 0x10UL)
            .CastStart(bossId, attack, 0x10UL, origin, 5f, rot)
            .Advance(TimeSpan.FromSeconds(5))
            .CastEvent(
                bossId, attack, 0x10UL, origin, rot,
                new CastTarget(0x10UL, ActionEffects.From([Effects.Damage(12000)])),
                new CastTarget(0x11UL, ActionEffects.From([Effects.Damage(11000)])),
                new CastTarget(0x12UL, ActionEffects.From([Effects.Damage(10000)])),
                new CastTarget(0x13UL, ActionEffects.From([Effects.Damage(9000)])))
            .CastStop(bossId)
            .Advance(TimeSpan.FromSeconds(5))
            .Combat(bossId, false)
            .Dead(bossId, true)
            .Destroy(bossId)
            .Advance(TimeSpan.FromSeconds(30));
    }
}
