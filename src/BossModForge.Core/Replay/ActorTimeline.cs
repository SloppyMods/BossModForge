namespace BossModForge.Core.Replay;

public sealed class ActorTimeline
{
    private readonly List<(DateTime T, PosRot Value)> _posRot = [];
    private readonly List<(DateTime T, bool Value)> _targetable = [];
    private readonly List<(DateTime T, bool Value)> _dead = [];
    private readonly List<(DateTime T, bool Value)> _inCombat = [];
    private readonly List<(DateTime T, HpMp Value)> _hpMp = [];

    internal ActorTimeline(ActorCreateOp create, int incarnation)
    {
        InstanceId = create.InstanceId;
        Incarnation = incarnation;
        Oid = create.Oid;
        Name = create.Name;
        NameId = create.NameId;
        Type = create.Type;
        Class = create.Class;
        Level = create.Level;
        HitboxRadius = create.HitboxRadius;
        SpawnIndex = create.SpawnIndex;
        OwnerId = create.OwnerId;
        FateId = create.FateId;
        Created = create.Timestamp;
        _posRot.Add((create.Timestamp, create.PosRot));
        _targetable.Add((create.Timestamp, create.IsTargetable));
        _hpMp.Add((create.Timestamp, create.HpMp));
    }

    public ulong InstanceId { get; }
    public int Incarnation { get; }
    public uint Oid { get; }
    public string Name { get; private set; }
    public uint NameId { get; private set; }
    public ActorType Type { get; }
    public Class Class { get; private set; }
    public int Level { get; private set; }
    public float HitboxRadius { get; private set; }
    public int SpawnIndex { get; }
    public ulong OwnerId { get; }
    public uint FateId { get; }
    public DateTime Created { get; }
    public DateTime? Destroyed { get; private set; }

    public IReadOnlyList<(DateTime T, PosRot Value)> PosRotSamples => _posRot;
    public IReadOnlyList<(DateTime T, bool Value)> Targetable => _targetable;
    public IReadOnlyList<(DateTime T, bool Value)> Dead => _dead;
    public IReadOnlyList<(DateTime T, bool Value)> InCombat => _inCombat;
    public IReadOnlyList<(DateTime T, HpMp Value)> HpMp => _hpMp;

    public PosRot PosRotAt(DateTime t) => At(_posRot, t, fallback: default, beforeFirstUsesFirst: true);

    public bool IsTargetableAt(DateTime t) => At(_targetable, t, fallback: false, beforeFirstUsesFirst: true);

    public bool IsDeadAt(DateTime t) => At(_dead, t, fallback: false, beforeFirstUsesFirst: false);

    public bool IsAliveAt(DateTime t) => Created <= t && (Destroyed is null || t < Destroyed);

    internal void Destroy(DateTime t) => Destroyed ??= t;

    internal void Rename(string name, uint nameId)
    {
        Name = name;
        NameId = nameId;
    }

    internal void ClassChange(Class cls, int level)
    {
        Class = cls;
        Level = level;
    }

    internal void SizeChange(float hitboxRadius) => HitboxRadius = hitboxRadius;

    internal void AddMove(DateTime t, PosRot value) => _posRot.Add((t, value));
    internal void AddTargetable(DateTime t, bool value) => _targetable.Add((t, value));
    internal void AddDead(DateTime t, bool value) => _dead.Add((t, value));
    internal void AddCombat(DateTime t, bool value) => _inCombat.Add((t, value));
    internal void AddHpMp(DateTime t, HpMp value) => _hpMp.Add((t, value));

    private static T At<T>(List<(DateTime T, T Value)> samples, DateTime t, T fallback, bool beforeFirstUsesFirst)
    {
        if (samples.Count == 0)
            return fallback;
        var best = -1;
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i].T <= t)
                best = i;
            else
                break;
        }
        if (best < 0)
            return beforeFirstUsesFirst ? samples[0].Value : fallback;
        return samples[best].Value;
    }
}
