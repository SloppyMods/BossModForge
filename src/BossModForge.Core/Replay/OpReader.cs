using System.Collections.Immutable;
using System.Numerics;

namespace BossModForge.Core.Replay;

public sealed class OpReader
{
    public const int MaxSupportedVersion = 31;

    private const int DeepDungeonRooms = 25;
    private const int DeepDungeonParty = 4;
    private const int DeepDungeonPomanders = 16;
    private const int DeepDungeonChests = 16;

    private readonly IReplayInput _input;
    private readonly Dictionary<uint, Func<Op?>> _dispatch;
    private Tag _currentTag;
    private DateTime _currentTs;
    private DateTime _legacyPrevTs;
    private ulong _qpcStart;
    private bool _seenFrame;

    public OpReader(IReplayInput input, ReplayLineage lineage = ReplayLineage.Auto)
    {
        _input = input;
        Lineage = lineage;
        Qpf = (ulong)TimeSpan.TicksPerSecond;
        GameVersion = "";
        _dispatch = BuildDispatch();
    }

    public int Version { get; private set; }
    public int HeaderVersion { get; private set; }
    public ulong Qpf { get; private set; }
    public string GameVersion { get; private set; }
    public DateTime StartTime { get; private set; }
    public bool Unsupported { get; private set; }
    public bool NewerVersion { get; private set; }
    public bool SawVersion { get; private set; }

    /// <summary>Writer lineage; <see cref="ReplayLineage.Auto"/> until detected from the first v27+ ACT+.</summary>
    public ReplayLineage Lineage { get; private set; }
    public bool LineageDetected { get; private set; }

    public bool IsKnown(Tag tag) => _dispatch.ContainsKey(tag.Value);

    public Op? ReadKnown(Tag tag)
    {
        _currentTag = tag;
        return _dispatch[tag.Value]();
    }

    public DateTime Timestamp() => _input.IsBinary ? _currentTs : _input.LineTimestamp;

    public ImmutableArray<string> ReadRemainingFields() => _input.ReadRemainingFields();

    private Dictionary<uint, Func<Op?>> BuildDispatch()
    {
        var d = new Dictionary<uint, Func<Op?>>
        {
            [Tag.Ver.Value] = ParseVersion,
            [Tag.Fram.Value] = ParseFrameStart,
            [Tag.Umrk.Value] = () => new UserMarkerOp(Timestamp(), _input.ReadString()),
            [Tag.Rsv.Value] = () => new RsvDataOp(Timestamp(), _input.ReadString(), _input.ReadString()),
            [Tag.Zone.Value] = () => new ZoneChangeOp(Timestamp(), _input.ReadUShort(false), Version >= 13 ? _input.ReadUShort(false) : (ushort)0),
            [Tag.Diru.Value] = () => new DirectorUpdateOp(Timestamp(), _input.ReadUInt(true), _input.ReadUInt(true), _input.ReadUInt(true), _input.ReadUInt(true), _input.ReadUInt(true), _input.ReadUInt(true)),
            [Tag.Envc.Value] = ParseEnvControl,
            [Tag.Slog.Value] = ParseSystemLog,
            [Tag.WayPlus.Value] = () => ParseWaymark(true),
            [Tag.WayMinus.Value] = () => ParseWaymark(false),
            [Tag.ActPlus.Value] = ParseActorCreate,
            [Tag.ActMinus.Value] = () => new ActorDestroyOp(Timestamp(), _input.ReadActorId()),
            [Tag.Name.Value] = ParseActorRename,
            [Tag.Clsr.Value] = ParseActorClassChange,
            [Tag.Move.Value] = () => new ActorMoveOp(Timestamp(), _input.ReadULong(true), new PosRot(_input.ReadVec3(), _input.ReadAngle())),
            [Tag.Acsz.Value] = () => new ActorSizeChangeOp(Timestamp(), _input.ReadActorId(), _input.ReadFloat()),
            [Tag.Hp.Value] = () => new ActorHpMpOp(Timestamp(), _input.ReadActorId(), ReadHpMp()),
            [Tag.AtgPlus.Value] = () => new ActorTargetableOp(Timestamp(), _input.ReadActorId(), true) { Tag = Tag.AtgPlus },
            [Tag.AtgMinus.Value] = () => new ActorTargetableOp(Timestamp(), _input.ReadActorId(), false) { Tag = Tag.AtgMinus },
            [Tag.Ally.Value] = () => new ActorAllyOp(Timestamp(), _input.ReadActorId(), _input.ReadBool()),
            [Tag.DiePlus.Value] = () => new ActorDeadOp(Timestamp(), _input.ReadActorId(), true) { Tag = Tag.DiePlus },
            [Tag.DieMinus.Value] = () => new ActorDeadOp(Timestamp(), _input.ReadActorId(), false) { Tag = Tag.DieMinus },
            [Tag.ComPlus.Value] = () => new ActorCombatOp(Timestamp(), _input.ReadActorId(), true) { Tag = Tag.ComPlus },
            [Tag.ComMinus.Value] = () => new ActorCombatOp(Timestamp(), _input.ReadActorId(), false) { Tag = Tag.ComMinus },
            [Tag.Nenp.Value] = () => new ActorAggroPlayerOp(Timestamp(), _input.ReadActorId(), _input.ReadBool()),
            [Tag.Mdls.Value] = ParseModelState,
            [Tag.Evts.Value] = () => new ActorEventStateOp(Timestamp(), _input.ReadActorId(), _input.ReadByte(false)),
            [Tag.Targ.Value] = () => new ActorTargetOp(Timestamp(), _input.ReadActorId(), _input.ReadActorId()),
            [Tag.Mntd.Value] = () => new ActorMountOp(Timestamp(), _input.ReadActorId(), _input.ReadUInt(false)),
            [Tag.Fora.Value] = () => new ActorForayOp(Timestamp(), _input.ReadActorId(), _input.ReadByte(false), _input.ReadByte(false)),
            [Tag.Teth.Value] = () => ParseTether(true, Tag.Teth),
            [Tag.TetPlus.Value] = () => ParseTether(true, Tag.TetPlus),
            [Tag.TetMinus.Value] = () => ParseTether(false, Tag.TetMinus),
            [Tag.CstPlus.Value] = () => ParseCastInfo(true),
            [Tag.CstMinus.Value] = () => ParseCastInfo(false),
            [Tag.CstBang.Value] = ParseCastEvent,
            [Tag.Er.Value] = () => new ActorEffectResultOp(Timestamp(), _input.ReadActorId(), _input.ReadUInt(false), _input.ReadInt()),
            [Tag.StaPlus.Value] = () => ParseStatus(true, false),
            [Tag.StaMinus.Value] = () => ParseStatus(false, false),
            [Tag.StaBang.Value] = () => ParseStatus(true, true),
            [Tag.AiePlus.Value] = () => ParseIncomingEffect(true),
            [Tag.AieMinus.Value] = () => ParseIncomingEffect(false),
            [Tag.Icon.Value] = () => new ActorIconOp(Timestamp(), _input.ReadActorId(), _input.ReadUInt(false), Version >= 22 ? _input.ReadActorId() : 0),
            [Tag.Esta.Value] = () => new EventObjectStateOp(Timestamp(), _input.ReadActorId(), _input.ReadUShort(true)),
            [Tag.Eanm.Value] = () => new EventObjectAnimationOp(Timestamp(), _input.ReadActorId(), _input.ReadUShort(true), _input.ReadUShort(true)),
            [Tag.Pate.Value] = () => new PlayActionTimelineOp(Timestamp(), _input.ReadActorId(), _input.ReadUShort(true)),
            [Tag.Nyel.Value] = () => new NpcYellOp(Timestamp(), _input.ReadActorId(), _input.ReadUShort(false)),
            [Tag.Opnt.Value] = () => new OpenTreasureOp(Timestamp(), _input.ReadActorId()),
            [Tag.Par.Value] = () => ParsePartyModify(Tag.Par),
            [Tag.ParPlus.Value] = () => ParsePartyModify(Tag.ParPlus),
            [Tag.ParBang.Value] = () => ParsePartyModify(Tag.ParBang),
            [Tag.ParMinus.Value] = () => new PartyModifyOp(Timestamp(), _input.ReadInt(), 0, 0, false, "") { Tag = Tag.ParMinus },
            [Tag.Lb.Value] = () => new PartyLimitBreakOp(Timestamp(), _input.ReadInt(), _input.ReadInt()),
            [Tag.Clar.Value] = ParseClientActionRequest,
            [Tag.Clrj.Value] = ParseClientActionReject,
            [Tag.CdnPlus.Value] = () => { _input.ReadFloat(); return Opaque(); },
            [Tag.CdnMinus.Value] = Opaque,
            [Tag.Clal.Value] = () => { _input.ReadFloat(); return Opaque(); },
            [Tag.Clmv.Value] = () => { _input.ReadFloat(); return Opaque(); },
            [Tag.Clcb.Value] = () => { _input.ReadUInt(false); _input.ReadFloat(); return Opaque(); },
            [Tag.Clst.Value] = () => { _input.ReadInt(); _input.ReadInt(); _input.ReadInt(); return Opaque(); },
            [Tag.Clcd.Value] = ParseClientCooldown,
            [Tag.Clda.Value] = ParseClientDutyActions,
            [Tag.Clbh.Value] = ParseClientBozjaHolster,
            [Tag.Cblu.Value] = ParseCountedUInt,
            [Tag.Clvl.Value] = ParseCountedShort,
            [Tag.Claf.Value] = ParseClientActiveFate,
            [Tag.Cpet.Value] = () => { _input.ReadULong(true); _input.ReadByte(false); _input.ReadByte(false); return Opaque(); },
            [Tag.Clft.Value] = () => { _input.ReadULong(true); return Opaque(); },
            [Tag.Clfd.Value] = () => { _input.ReadAngle(); return Opaque(); },
            [Tag.Clkv.Value] = () => { for (var i = 0; i < 6; i++) _input.ReadUInt(false); return Opaque(); },
            [Tag.Fate.Value] = () => { _input.ReadUInt(false); _input.ReadLong(); return Opaque(); },
            [Tag.Ddpg.Value] = () => { _input.ReadByte(false); for (var i = 0; i < 8; i++) _input.ReadByte(false); if (Version >= 29) _input.ReadBool(); return Opaque(); },
            [Tag.Ddmp.Value] = ParseDeepDungeonMap,
            [Tag.Ddpt.Value] = () => { for (var i = 0; i < DeepDungeonParty; i++) { _input.ReadActorId(); _input.ReadByte(false); } return Opaque(); },
            [Tag.Ddit.Value] = () => { for (var i = 0; i < DeepDungeonPomanders; i++) { _input.ReadByte(false); _input.ReadByte(true); } return Opaque(); },
            [Tag.Ddct.Value] = () => { for (var i = 0; i < DeepDungeonChests; i++) { _input.ReadByte(false); _input.ReadByte(false); } return Opaque(); },
            [Tag.Ddmg.Value] = () => { _input.ReadBytes(); return Opaque(); },
            [Tag.Ipci.Value] = () => { _input.ReadUInt(false); return Opaque(); },
            [Tag.Ipcs.Value] = () => { _input.ReadInt(); _input.ReadUShort(false); _input.ReadUInt(false); _input.ReadUInt(true); _input.ReadLong(); _input.ReadBytes(); return Opaque(); },

            // Tags added after v24 (layouts from ReplayParserLog.cs at vbm 6e410ef8 / bmr 577675d8; docs/UPSTREAM.md §12).
            // Tags are not version-gated upstream; payloads are consumed exactly and kept opaque.
            [Tag.PvpPlus.Value] = Opaque,
            [Tag.PvpMinus.Value] = Opaque,
            [Tag.FlyPlus.Value] = Opaque,
            [Tag.FlyMinus.Value] = Opaque,
            [Tag.Leme.Value] = () => { _input.ReadByte(true); _input.ReadByte(true); _input.ReadBytes(); return Opaque(); },
            [Tag.SgnPlus.Value] = () => { _input.ReadByte(false); _input.ReadActorId(); return Opaque(); },
            [Tag.SgnMinus.Value] = () => { _input.ReadByte(false); return Opaque(); },
            [Tag.AroPlus.Value] = () => { _input.ReadActorId(); _input.ReadFloat(); return Opaque(); },
            [Tag.AroMinus.Value] = () => { _input.ReadActorId(); return Opaque(); },
            [Tag.Vfx.Value] = () => { _input.ReadActorId(); _input.ReadUInt(false); _input.ReadActorId(); return Opaque(); },
            [Tag.Pats.Value] = ParseActionTimelineSync,
            [Tag.Avis.Value] = () => { _input.ReadActorId(); _input.ReadUShort(false); return Opaque(); },
            [Tag.Rflg.Value] = () => { _input.ReadActorId(); _input.ReadInt(); return Opaque(); },
            [Tag.Algi.Value] = () => { _input.ReadInt(); return Opaque(); },
            [Tag.Cbst.Value] = ParseCountedByte,
            [Tag.Ccru.Value] = ParseCountedUShort,
            [Tag.Choc.Value] = () => { _input.ReadULong(true); _input.ReadByte(false); _input.ReadFloat(); if (Version >= 30) _input.ReadBool(); return Opaque(); },
            [Tag.Hate.Value] = ParseClientHate,
            [Tag.Clpr.Value] = () => { for (var i = 0; i < 4; i++) _input.ReadFloat(); return Opaque(); },
            [Tag.Invt.Value] = () => { _input.ReadUInt(false); _input.ReadUInt(false); return Opaque(); },
            [Tag.Flos.Value] = () => { _input.ReadUInt(false); _input.ReadActorId(); return Opaque(); },
            [Tag.Ipcx.Value] = () => { for (var i = 0; i < 5; i++) _input.ReadUInt(false); return Opaque(); },
            [Tag.Rtt.Value] = () => { _input.ReadFloat(); return Opaque(); },
            [Tag.Rpre.Value] = () => { _input.ReadBytes(); return Opaque(); },
            [Tag.Rmod.Value] = () => { _input.ReadByte(false); return Opaque(); },
            [Tag.Rrol.Value] = () => { for (var i = 0; i < 8; i++) _input.ReadByte(false); return Opaque(); },
            [Tag.Rmov.Value] = () => { _input.ReadFloat(); _input.ReadFloat(); return Opaque(); },
            [Tag.Ract.Value] = ParseReplayActions,
            [Tag.Rpri.Value] = ParseReplayPriorities,
        };
        return d;
    }

    private Op Opaque() => new OpaqueOp(Timestamp(), _currentTag);

    private Op? ParseVersion()
    {
        HeaderVersion = _input.ReadInt();
        SawVersion = true;
        if (HeaderVersion < 10)
        {
            Version = HeaderVersion;
            Unsupported = true;
            return null;
        }

        NewerVersion = HeaderVersion > MaxSupportedVersion;
        Version = NewerVersion ? MaxSupportedVersion : HeaderVersion;

        Qpf = Version >= 10 ? _input.ReadULong(false) : (ulong)TimeSpan.TicksPerSecond;
        GameVersion = Version >= 11 ? _input.ReadString() : "old";
        StartTime = _input.IsBinary ? new DateTime(_input.ReadLong()) : _input.LineTimestamp;
        _currentTs = StartTime;
        _legacyPrevTs = StartTime;
        return null;
    }

    private Op ParseFrameStart()
    {
        var prevUpdate = TimeSpan.FromMilliseconds(_input.ReadDouble());
        _input.ReadVoid();
        var gaugeLow = _input.CanRead() ? _input.ReadULong(true) : 0UL;
        var gaugeHigh = Version >= 18 ? _input.ReadULong(true) : 0UL;

        ulong qpc = 0;
        uint index = 0;
        float durationRaw = 0, duration = 0, tick = 1;
        if (Version >= 10)
        {
            qpc = _input.ReadULong(false);
            index = _input.ReadUInt(false);
            durationRaw = _input.ReadFloat();
            duration = _input.ReadFloat();
            tick = _input.ReadFloat();
        }

        if (_input.IsBinary)
        {
            if (!_seenFrame)
            {
                _qpcStart = qpc;
                _currentTs = StartTime;
                _seenFrame = true;
            }
            else if (Qpf != 0)
            {
                _currentTs = StartTime.AddTicks((long)((UInt128)(qpc - _qpcStart) * (UInt128)TimeSpan.TicksPerSecond / Qpf));
            }
        }

        var azimuth = Version >= 16 ? _input.ReadAngle() : default;
        _legacyPrevTs = Timestamp();
        return new FrameStartOp(Timestamp(), new FrameInfo(qpc, index, durationRaw, duration, tick), prevUpdate, gaugeLow, gaugeHigh, azimuth);
    }

    private Op ParseEnvControl()
    {
        if (Version < 11)
            _input.ReadUInt(true);
        return new EnvControlOp(Timestamp(), _input.ReadByte(true), _input.ReadUInt(true));
    }

    private Op ParseSystemLog()
    {
        var id = _input.ReadUInt(false);
        var n = _input.ReadInt();
        var args = ImmutableArray.CreateBuilder<int>(n);
        for (var i = 0; i < n; i++)
            args.Add(_input.ReadInt());
        return new SystemLogOp(Timestamp(), id, args.ToImmutable());
    }

    private Op ParseWaymark(bool set)
    {
        var waymark = _input.ReadByte(false);
        Vector3? pos = set ? _input.ReadVec3() : null;
        return new WaymarkOp(Timestamp(), waymark, pos) { Tag = set ? Tag.WayPlus : Tag.WayMinus };
    }

    private Op ParseActorCreate()
    {
        var instanceId = _input.ReadULong(true);
        var oid = _input.ReadUInt(true);
        var spawnIndex = _input.ReadInt();
        var layoutId = Version >= 26 ? _input.ReadUInt(true) : 0u;
        var name = _input.ReadString();
        var nameId = Version >= 13 ? _input.ReadUInt(false) : 0u;
        var type = (ActorType)_input.ReadUShort(true);
        var cls = _input.ReadClass();
        var level = Version >= 12 ? _input.ReadInt() : 0;
        var pos = _input.ReadVec3();
        var rot = _input.ReadAngle();
        var radius = _input.ReadFloat();
        var hpmp = ReadHpMp();
        var targetable = _input.ReadBool();
        var ally = _input.ReadBool();
        var owner = _input.ReadActorId();
        var fateId = Version >= 14 ? _input.ReadUInt(false) : 0u;
        var renderflags = Version >= 27 && ResolveActorCreateLineage() == ReplayLineage.Bmr ? _input.ReadInt() : 0;
        return new ActorCreateOp(Timestamp(), instanceId, oid, spawnIndex, name, nameId, type, cls, level, new PosRot(pos, rot), radius, hpmp, targetable, ally, owner, fateId)
        {
            LayoutId = layoutId,
            Renderflags = renderflags,
        };
    }

    // Called after the vbm-layout ACT+ fields are consumed (v>=27). bmr appends one int (Renderflags).
    // Text: an extra field remains on the line. Binary: no length prefix, so look ahead — under the vbm
    // layout the next 4 bytes are a known tag (or EOF); under bmr they are the int and bytes 4..8 are a tag.
    private ReplayLineage ResolveActorCreateLineage()
    {
        if (Lineage != ReplayLineage.Auto)
            return Lineage;

        ReplayLineage detected;
        if (!_input.IsBinary)
        {
            detected = _input.CanRead() ? ReplayLineage.Bmr : ReplayLineage.Vbm;
        }
        else if (_input is BinaryInput bin)
        {
            Span<byte> next = stackalloc byte[8];
            var n = bin.Peek(next);
            if (n < 4 || IsKnown(new Tag(next[..4])))
                detected = ReplayLineage.Vbm;
            else if (n == 8 && IsKnown(new Tag(next[4..8])))
                detected = ReplayLineage.Bmr;
            else
                return ReplayLineage.Vbm; // undecidable here; do not lock, try again on the next ACT+
        }
        else
        {
            return ReplayLineage.Vbm;
        }

        Lineage = detected;
        LineageDetected = true;
        return detected;
    }

    private Op ParseActorRename()
        => new ActorRenameOp(Timestamp(), _input.ReadULong(true), _input.ReadString(), Version >= 13 ? _input.ReadUInt(false) : 0u);

    private Op ParseActorClassChange()
    {
        var id = _input.ReadActorId();
        _input.ReadVoid();
        return new ActorClassChangeOp(Timestamp(), id, _input.ReadClass(), Version >= 12 ? _input.ReadInt() : 0);
    }

    private Op ParseModelState()
    {
        var id = _input.ReadActorId();
        var model = _input.ReadByte(false);
        var a1 = _input.CanRead() ? _input.ReadByte(false) : (byte)0;
        var a2 = _input.CanRead() ? _input.ReadByte(false) : (byte)0;
        return new ActorModelStateOp(Timestamp(), id, model, a1, a2);
    }

    private Op ParseTether(bool tether, Tag disk)
    {
        var id = _input.ReadActorId();
        if (!tether)
            return new ActorTetherOp(Timestamp(), id, 0, 0) { Tag = disk };
        return new ActorTetherOp(Timestamp(), id, _input.ReadUInt(false), _input.ReadActorId()) { Tag = disk };
    }

    private Op ParseCastInfo(bool start)
    {
        var id = _input.ReadActorId();
        if (!start)
            return new ActorCastStopOp(Timestamp(), id);

        var action = _input.ReadAction();
        var target = _input.ReadActorId();
        var loc = _input.ReadVec3();
        float elapsed, total;
        if (Version >= 17)
        {
            (elapsed, total) = _input.ReadFloatPair();
        }
        else
        {
            var (finishAt, tot) = _input.ReadTimePair();
            total = tot;
            elapsed = total - (float)(finishAt - _legacyPrevTs).TotalSeconds;
        }
        var interruptible = _input.CanRead() && _input.ReadBool();
        var rot = _input.CanRead() ? _input.ReadAngle() : default;
        return new ActorCastStartOp(Timestamp(), id, action, target, loc, elapsed, total, interruptible, rot);
    }

    private Op ParseCastEvent()
    {
        var actor = _input.ReadActorId();
        var action = _input.ReadAction();
        var mainTarget = _input.ReadActorId();
        var animLock = _input.ReadFloat();
        var maxTargets = _input.ReadUInt(false);
        var targetPos = Version >= 6 ? _input.ReadVec3() : default;
        var globalSeq = Version >= 7 ? _input.ReadUInt(false) : 0u;
        var sourceSeq = Version >= 9 ? _input.ReadUInt(false) : 0u;
        var rot = Version >= 21 ? _input.ReadAngle() : default;
        var targets = _input.ReadTargets();
        return new ActorCastEventOp(Timestamp(), actor, action, mainTarget, animLock, maxTargets, targetPos, globalSeq, sourceSeq, rot, targets);
    }

    private Op ParseStatus(bool gainOrUpdate, bool isUpdate)
    {
        var id = _input.ReadActorId();
        var index = _input.ReadInt();
        ActorStatus? status = gainOrUpdate ? _input.ReadStatus() : null;
        return new ActorStatusOp(Timestamp(), id, index, status, isUpdate) { Tag = _currentTag };
    }

    private Op ParseIncomingEffect(bool add)
    {
        var id = _input.ReadActorId();
        var index = _input.ReadInt();
        if (!add)
            return new ActorIncomingEffectOp(Timestamp(), id, index, 0, 0, 0, default, ActionEffects.Empty, false) { Tag = Tag.AieMinus };
        return new ActorIncomingEffectOp(
            Timestamp(), id, index,
            _input.ReadUInt(false),
            _input.ReadInt(),
            _input.ReadActorId(),
            _input.ReadAction(),
            _input.ReadActionEffects(),
            true) { Tag = Tag.AiePlus };
    }

    private Op ParsePartyModify(Tag disk)
    {
        var slot = _input.ReadInt();
        var contentId = _input.ReadULong(true);
        var instanceId = _input.ReadULong(true);
        var inCutscene = Version >= 15 && _input.ReadBool();
        // v31 dropped the member name (vbm 736de2a9d "replay anonymizer"; bmr merged at 1f708d9da)
        var name = Version is >= 15 and < 31 ? _input.ReadString() : "";
        return new PartyModifyOp(Timestamp(), slot, contentId, instanceId, inCutscene, name) { Tag = disk };
    }

    private HpMp ReadHpMp()
    {
        var curHp = _input.ReadUInt(false);
        var maxHp = _input.ReadUInt(false);
        var shield = _input.ReadUInt(false);
        var curMp = _input.ReadUInt(false);
        var maxMp = Version >= 24 ? _input.ReadUInt(false) : 10000u;
        return new HpMp(curHp, maxHp, shield, curMp, maxMp);
    }

    private Op ParseClientActionRequest()
    {
        _input.ReadAction();
        _input.ReadActorId();
        _input.ReadVec3();
        _input.ReadUInt(false);
        _input.ReadFloat();
        _input.ReadFloatPair();
        _input.ReadFloatPair();
        return Opaque();
    }

    private Op ParseClientActionReject()
    {
        _input.ReadAction();
        _input.ReadUInt(false);
        _input.ReadFloatPair();
        _input.ReadUInt(false);
        return Opaque();
    }

    private Op ParseClientCooldown()
    {
        _input.ReadBool();
        var count = _input.ReadByte(false);
        for (var i = 0; i < count; i++)
        {
            _input.ReadByte(false);
            _input.ReadFloat();
            _input.ReadFloat();
        }
        return Opaque();
    }

    private Op ParseClientDutyActions()
    {
        if (Version >= 25)
        {
            // v25: variable slot count (vbm 9c50bd5f1)
            var count = _input.ReadByte(false);
            for (var i = 0; i < count; i++)
            {
                _input.ReadAction();
                _input.ReadByte(false);
                _input.ReadByte(false);
            }
            return Opaque();
        }

        for (var slot = 0; slot < 2; slot++)
        {
            _input.ReadAction();
            if (Version >= 20)
            {
                _input.ReadByte(false);
                _input.ReadByte(false);
            }
        }
        return Opaque();
    }

    private Op ParseClientBozjaHolster()
    {
        var count = _input.ReadByte(false);
        for (var i = 0; i < count; i++)
        {
            _input.ReadByte(false);
            _input.ReadByte(false);
        }
        return Opaque();
    }

    private Op ParseCountedUInt()
    {
        var count = _input.ReadByte(false);
        for (var i = 0; i < count; i++)
            _input.ReadUInt(false);
        return Opaque();
    }

    private Op ParseCountedShort()
    {
        var count = _input.ReadByte(false);
        for (var i = 0; i < count; i++)
            _input.ReadShort();
        return Opaque();
    }

    private Op ParseDeepDungeonMap()
    {
        if (Version < 23)
            _input.ReadBytes();
        else
        {
            for (var i = 0; i < DeepDungeonRooms; i++)
            {
                // v29 widened room flags to ushort (vbm 0a2f606b0)
                if (Version >= 29)
                    _input.ReadUShort(true);
                else
                    _input.ReadByte(true);
            }
        }
        return Opaque();
    }

    private Op ParseClientActiveFate()
    {
        _input.ReadUInt(false);
        _input.ReadVec3();
        _input.ReadFloat();
        if (Version >= 27)
        {
            _input.ReadByte(false); // progress
            _input.ReadByte(false); // hand-in count
        }
        // objective npc: vbm v28 (2b7441329), bmr v29 (38e9638a2)
        var objectiveNpcGate = Lineage == ReplayLineage.Bmr ? 29 : 28;
        if (Version >= objectiveNpcGate)
            _input.ReadUInt(false);
        return Opaque();
    }

    private Op ParseActionTimelineSync()
    {
        _input.ReadActorId();
        var count = _input.ReadInt();
        for (var i = 0; i < count; i++)
        {
            _input.ReadActorId();
            _input.ReadUShort(true);
        }
        return Opaque();
    }

    private Op ParseCountedByte()
    {
        var count = _input.ReadByte(false);
        for (var i = 0; i < count; i++)
            _input.ReadByte(false);
        return Opaque();
    }

    private Op ParseCountedUShort()
    {
        var count = _input.ReadByte(false);
        for (var i = 0; i < count; i++)
            _input.ReadUShort(false);
        return Opaque();
    }

    private Op ParseClientHate()
    {
        _input.ReadActorId();
        var count = _input.ReadInt();
        for (var i = 0; i < count; i++)
        {
            _input.ReadActorId();
            _input.ReadInt();
        }
        return Opaque();
    }

    private Op ParseReplayActions()
    {
        var count = _input.ReadInt();
        for (var i = 0; i < count; i++)
        {
            _input.ReadAction();
            _input.ReadActorId();
            _input.ReadFloat();
            _input.ReadFloat();
            _input.ReadVec3();
        }
        return Opaque();
    }

    private Op ParseReplayPriorities()
    {
        var count = _input.ReadInt();
        for (var i = 0; i < count; i++)
        {
            _input.ReadActorId();
            _input.ReadInt();
            var trace = _input.ReadInt();
            for (var j = 0; j < trace; j++)
            {
                _input.ReadInt();
                _input.ReadString();
            }
        }
        return Opaque();
    }
}
