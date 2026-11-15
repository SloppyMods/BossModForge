using System.Numerics;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

/// <summary>
/// Three recordings cut from one logical fight (zone 1180 / CFC 826, boss OID 0x1234):
/// <see cref="SyntheticSinglePull"/> = pull 0, <see cref="SyntheticTwoPulls"/> = pulls 1–2,
/// <see cref="SyntheticThreePulls"/> = pulls 0–2. Every pull runs the identical per-pull schedule
/// at identical absolute timestamps and uses a fresh boss instance id; the party is created once
/// per file header. Concatenating SinglePull + TwoPulls therefore replays the same op stream as
/// ThreePulls, modulo the repeated per-file header block (FRAM + an identical ZONE + party ACT+).
/// </summary>
public static class SyntheticMerge
{
    public const ushort Zone = 1180;
    public const ushort Cfc = 826;
    public const uint BossOid = 0x1234;
    public const uint BossNameId = 9001;
    public const string BossName = "Merge Boss";
    public const uint VoidWave = 40010;

    public static readonly SegmenterOptions PullOptions = new(TimeSpan.FromSeconds(5), TimeSpan.Zero);

    private static readonly DateTime T0 = new DateTime(2026, 9, 20, 21, 0, 0, DateTimeKind.Unspecified);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PullPeriod = MeasurePullPeriod();

    public static DateTime SingleStart => T0;
    public static DateTime TwoStart => T0 + PullPeriod;

    public static RecordingBuilder SinglePull(DateTime? start = null) => Build(start ?? T0, 0, 1);

    public static RecordingBuilder TwoPulls(DateTime? start = null) => Build(start ?? TwoStart, 1, 2);

    public static RecordingBuilder ThreePulls(DateTime? start = null) => Build(start ?? T0, 0, 3);

    private static RecordingBuilder Build(DateTime start, int first, int count)
    {
        var b = new RecordingBuilder(start);
        Header(b);
        for (var k = first; k < first + count; k++)
            Pull(b, k);
        return b;
    }

    private static void Header(RecordingBuilder b)
    {
        var hp = new HpMp(100000, 100000, 0, 10000, 10000);
        b.Frame()
            .Zone(Zone, Cfc)
            .Actor(0x10, 0, "Alice", ActorType.Player, new Vector3(95, 0, 100), default, 0.5f, Class.PLD, 100, targetable: true, ally: true, hpmp: hp)
            .Actor(0x11, 0, "Bob", ActorType.Player, new Vector3(105, 0, 100), default, 0.5f, Class.WHM, 100, targetable: true, ally: true, hpmp: hp)
            .Actor(0x12, 0, "Cara", ActorType.Player, new Vector3(100, 0, 95), default, 0.5f, Class.NIN, 100, targetable: true, ally: true, hpmp: hp)
            .Actor(0x13, 0, "Dee", ActorType.Player, new Vector3(100, 0, 105), default, 0.5f, Class.BLM, 100, targetable: true, ally: true, hpmp: hp);
    }

    // The probes build the same header + one pull so the measured pull period (pull duration +
    // inter-pull gap, including RecordingBuilder's millisecond frame quantization) is exactly what
    // the canonical fixtures produce; TwoPulls then starts where SinglePull's pull 0 ends.
    private static TimeSpan MeasurePullPeriod()
    {
        var probe = new RecordingBuilder(T0);
        Header(probe);
        Pull(probe, 0);
        return probe.Now - T0;
    }

    private static ulong BossId(int pull) => 0x1000UL + (ulong)pull * 0x100;

    private static void Pull(RecordingBuilder b, int k)
    {
        var boss = BossId(k);
        var p1 = 0x10UL;
        var p2 = 0x11UL;
        var p3 = 0x12UL;
        var p4 = 0x13UL;
        var wave = ActionId.Spell(VoidWave);
        var origin = new Vector3(100, 0, 100);
        var rot = Angle.FromDeg(90);

        b.Advance(TimeSpan.FromSeconds(1))
            .Actor(boss, BossOid, BossName, ActorType.Enemy, origin, rot, 5f, Class.None, 90, nameId: BossNameId, targetable: true, ally: false, hpmp: new HpMp(500000, 500000, 0, 10000, 10000))
            .Combat(boss, true)
            .Target(boss, p1)
            .CastStart(boss, wave, p1, origin, 5f, rot)
            .Advance(TimeSpan.FromSeconds(5))
            .CastEvent(
                boss, wave, p1, origin, rot,
                new CastTarget(p1, ActionEffects.From([Effects.Damage(12000)])),
                new CastTarget(p2, ActionEffects.From([Effects.Damage(11000)])),
                new CastTarget(p3, ActionEffects.From([Effects.Damage(10000)])),
                new CastTarget(p4, ActionEffects.From([Effects.Damage(9000)])))
            .CastStop(boss)
            .Advance(TimeSpan.FromSeconds(5))
            .Combat(boss, false)
            .Dead(boss, true)
            .Destroy(boss)
            .Advance(Gap);
    }
}

public sealed class SyntheticSinglePull
{
    public SyntheticSinglePull() => Builder = SyntheticMerge.SinglePull();

    public RecordingBuilder Builder { get; }

    public string WriteTemp(ReplayEncoding encoding) => Builder.WriteTemp(encoding);
}

public sealed class SyntheticTwoPulls
{
    public SyntheticTwoPulls() => Builder = SyntheticMerge.TwoPulls();

    public RecordingBuilder Builder { get; }

    public string WriteTemp(ReplayEncoding encoding) => Builder.WriteTemp(encoding);
}

public sealed class SyntheticThreePulls
{
    public SyntheticThreePulls() => Builder = SyntheticMerge.ThreePulls();

    public RecordingBuilder Builder { get; }

    public string WriteTemp(ReplayEncoding encoding) => Builder.WriteTemp(encoding);
}
