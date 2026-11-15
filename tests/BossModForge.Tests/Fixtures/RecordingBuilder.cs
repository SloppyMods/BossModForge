using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using BossModForge.Core.Replay;

namespace BossModForge.Tests.Fixtures;

public sealed class RecordingBuilder
{
    internal abstract record Entry(DateTime Timestamp);
    internal sealed record OpEntry(Op Op) : Entry(Op.Timestamp);
    internal sealed record RawEntry(DateTime Timestamp, Tag Tag, string[] Fields) : Entry(Timestamp);

    private readonly DateTime _start;
    private readonly List<Entry> _entries = [];
    private uint _frameIndex;
    private uint _globalSeq = 1;
    private uint _sourceSeq = 1;
    private bool _emittedFrame;

    public RecordingBuilder(DateTime start, ulong qpf = 10_000_000, string gameVersion = "2026.08.20.0000.0000", int version = 24)
    {
        _start = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        Now = _start;
        Qpf = qpf;
        GameVersion = gameVersion;
        Version = version;
    }

    public DateTime Now { get; private set; }
    internal DateTime Start => _start;
    internal ulong Qpf { get; }
    internal string GameVersion { get; }
    internal int Version { get; }
    internal IReadOnlyList<Entry> Entries => _entries;

    public RecordingBuilder Frame(double dtSeconds = 1.0 / 60)
    {
        if (_emittedFrame)
        {
            var ms = Math.Max(1, (int)Math.Round(dtSeconds * 1000.0));
            Now = Now.AddMilliseconds(ms);
        }

        var ticks = (ulong)Math.Max(0, (Now - _start).Ticks);
        var qpc = (ulong)((UInt128)ticks * Qpf / (UInt128)TimeSpan.TicksPerSecond);
        var duration = (float)dtSeconds;
        var prevMs = _emittedFrame ? dtSeconds * 1000.0 : 0.0;
        Add(new FrameStartOp(
            Now,
            new FrameInfo(qpc, _frameIndex++, duration, duration, 1f),
            TimeSpan.FromMilliseconds(prevMs),
            0,
            0,
            default));
        _emittedFrame = true;
        return this;
    }

    public RecordingBuilder Advance(TimeSpan by, double dtSeconds = 1.0 / 60)
    {
        var target = Now + by;
        if (!_emittedFrame)
            Frame(dtSeconds);
        while (Now < target)
            Frame(dtSeconds);
        return this;
    }

    public RecordingBuilder Zone(ushort zone, ushort cfcId)
        => Add(new ZoneChangeOp(Now, zone, cfcId));

    public RecordingBuilder DirectorUpdate(uint directorId, uint updateId, uint p1 = 0, uint p2 = 0, uint p3 = 0, uint p4 = 0)
        => Add(new DirectorUpdateOp(Now, directorId, updateId, p1, p2, p3, p4));

    public RecordingBuilder EnvControl(byte index, uint state)
        => Add(new EnvControlOp(Now, index, state));

    public RecordingBuilder Actor(
        ulong id,
        uint oid,
        string name,
        ActorType type,
        Vector3 pos,
        Angle rot,
        float radius = 0.5f,
        Class cls = Class.None,
        int level = 0,
        uint nameId = 0,
        bool targetable = true,
        bool ally = false,
        ulong owner = 0,
        int spawnIndex = -1,
        HpMp? hpmp = null,
        uint fateId = 0)
        => Add(new ActorCreateOp(
            Now, id, oid, spawnIndex, name, nameId, type, cls, level,
            new PosRot(pos, rot), radius,
            hpmp ?? new HpMp(1, 1, 0, 10000, 10000),
            targetable, ally, owner, fateId));

    public RecordingBuilder Player(ulong id, string name, Class cls, Vector3 pos, Angle rot = default, int level = 100)
        => Actor(
            id, 0, name, ActorType.Player, pos, rot, 0.5f, cls, level,
            targetable: true, ally: true,
            hpmp: new HpMp(100000, 100000, 0, 10000, 10000));

    public RecordingBuilder Helper(ulong id, Vector3 pos, Angle rot = default)
        => Actor(id, 0x233C, "Helper", ActorType.Helper, pos, rot, 0.5f, targetable: false, ally: false);

    public RecordingBuilder Destroy(ulong id) => Add(new ActorDestroyOp(Now, id));
    public RecordingBuilder Rename(ulong id, string name, uint nameId) => Add(new ActorRenameOp(Now, id, name, nameId));
    public RecordingBuilder Move(ulong id, Vector3 pos, Angle rot) => Add(new ActorMoveOp(Now, id, new PosRot(pos, rot)));
    public RecordingBuilder Targetable(ulong id, bool value) => Add(new ActorTargetableOp(Now, id, value) { Tag = value ? Tag.AtgPlus : Tag.AtgMinus });
    public RecordingBuilder Dead(ulong id, bool value) => Add(new ActorDeadOp(Now, id, value) { Tag = value ? Tag.DiePlus : Tag.DieMinus });
    public RecordingBuilder Combat(ulong id, bool value) => Add(new ActorCombatOp(Now, id, value) { Tag = value ? Tag.ComPlus : Tag.ComMinus });
    public RecordingBuilder HpMp(ulong id, HpMp value) => Add(new ActorHpMpOp(Now, id, value));
    public RecordingBuilder Target(ulong id, ulong target) => Add(new ActorTargetOp(Now, id, target));

    public RecordingBuilder CastStart(ulong id, ActionId action, ulong target, Vector3 location, float totalTime, Angle rot, float elapsed = 0, bool interruptible = false)
        => Add(new ActorCastStartOp(Now, id, action, target, location, elapsed, totalTime, interruptible, rot));

    public RecordingBuilder CastStop(ulong id) => Add(new ActorCastStopOp(Now, id));

    public RecordingBuilder CastEvent(ulong id, ActionId action, ulong mainTarget, Vector3 targetPos, Angle rot, params CastTarget[] targets)
        => Add(new ActorCastEventOp(
            Now, id, action, mainTarget, 0.1f, (uint)targets.Length, targetPos,
            _globalSeq++, _sourceSeq++, rot, targets.ToImmutableArray()));

    public RecordingBuilder StatusGain(ulong id, int index, uint statusId, ushort extra, float durationSeconds, ulong source)
        => Add(new ActorStatusOp(Now, id, index, new ActorStatus(statusId, extra, Now.AddSeconds(durationSeconds), source), false) { Tag = Tag.StaPlus });

    public RecordingBuilder StatusLose(ulong id, int index)
        => Add(new ActorStatusOp(Now, id, index, null, false) { Tag = Tag.StaMinus });

    public RecordingBuilder Tether(ulong id, uint tetherId, ulong target)
        => Add(new ActorTetherOp(Now, id, tetherId, target) { Tag = Tag.Teth });

    public RecordingBuilder Untether(ulong id)
        => Add(new ActorTetherOp(Now, id, 0, 0) { Tag = Tag.TetMinus });

    public RecordingBuilder Party(int slot, ulong contentId, ulong instanceId, string name, bool inCutscene = false)
        => Add(new PartyModifyOp(Now, slot, contentId, instanceId, inCutscene, name) { Tag = Tag.Par });

    public RecordingBuilder Icon(ulong id, uint iconId, ulong target = 0)
        => Add(new ActorIconOp(Now, id, iconId, target));

    public RecordingBuilder Raw(string tag, params string[] fields)
    {
        var four = tag.Length >= 4 ? tag[..4] : tag.PadRight(4);
        _entries.Add(new RawEntry(Now, Tag.Parse(four), fields));
        return this;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb, CultureInfo.InvariantCulture);
        TextRecordingWriter.Write(writer, this);
        return sb.ToString();
    }

    public byte[] ToBinary(bool brotli)
    {
        using var ms = new MemoryStream();
        BinaryRecordingWriter.Write(ms, this, brotli);
        return ms.ToArray();
    }

    public Stream Open(ReplayEncoding encoding)
    {
        var ms = new MemoryStream();
        switch (encoding)
        {
            case ReplayEncoding.Text:
            {
                var bytes = Encoding.UTF8.GetBytes(ToText());
                ms.Write(bytes, 0, bytes.Length);
                break;
            }
            case ReplayEncoding.Binary:
                BinaryRecordingWriter.Write(ms, this, brotli: false);
                break;
            case ReplayEncoding.BinaryBrotli:
                BinaryRecordingWriter.Write(ms, this, brotli: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(encoding));
        }
        ms.Position = 0;
        return ms;
    }

    public string WriteTemp(ReplayEncoding encoding)
    {
        var ext = encoding switch
        {
            ReplayEncoding.Text => ".log",
            ReplayEncoding.Binary => ".blog",
            ReplayEncoding.BinaryBrotli => ".blcb",
            _ => ".dat",
        };
        var path = Path.Combine(Path.GetTempPath(), $"forge-{Guid.NewGuid():N}{ext}");
        using var stream = Open(encoding);
        using var file = File.Create(path);
        stream.CopyTo(file);
        return path;
    }

    private RecordingBuilder Add(Op op)
    {
        _entries.Add(new OpEntry(op));
        return this;
    }
}

public static class Effects
{
    public static ulong Damage(uint amount, byte damageType = 0, byte element = 0)
    {
        var p1 = (byte)((damageType & 0x0F) | (element << 4));
        if (amount > 0xFFFF)
        {
            var p3 = (byte)(amount >> 16);
            return ActionEffect.Pack(ActionEffectType.Damage, 0, p1, 0, p3, 0x40, (ushort)amount);
        }
        return ActionEffect.Pack(ActionEffectType.Damage, 0, p1, 0, 0, 0, (ushort)amount);
    }

    public static ulong Heal(uint amount)
    {
        if (amount > 0xFFFF)
        {
            var p3 = (byte)(amount >> 16);
            return ActionEffect.Pack(ActionEffectType.Heal, 0, 0, 0, p3, 0x40, (ushort)amount);
        }
        return ActionEffect.Pack(ActionEffectType.Heal, 0, 0, 0, 0, 0, (ushort)amount);
    }

    public static ulong Knockback(byte distance, byte direction)
        => ActionEffect.Pack(ActionEffectType.Knockback, distance, direction);

    public static ulong ApplyStatusTarget(ushort statusId, byte param0 = 0)
        => ActionEffect.Pack(ActionEffectType.ApplyStatusEffectTarget, param0, 0, 0, 0, 0, statusId);

    public static ulong Miss() => ActionEffect.Pack(ActionEffectType.Miss);
    public static ulong Nothing() => 0;
}
