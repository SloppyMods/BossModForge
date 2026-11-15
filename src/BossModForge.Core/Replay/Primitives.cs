using System.Collections.Immutable;
using System.Numerics;

namespace BossModForge.Core.Replay;

public readonly record struct Angle(float Rad)
{
    public float Deg => Rad * (180f / MathF.PI);

    public static Angle FromDeg(float deg) => new(deg * (MathF.PI / 180f));
}

public enum ActionType : byte
{
    None = 0,
    Spell = 1,
    Item = 2,
    KeyItem = 3,
    Ability = 4,
    General = 5,
    Buddy = 6,
    MainCommand = 7,
    Companion = 8,
    CraftAction = 9,
    PetAction = 11,
    Mount = 13,
    PvPAction = 14,
    Waymark = 15,
    ChocoboRaceAbility = 16,
    ChocoboRaceItem = 17,
    SquadronAction = 19,
    Ornament = 20,
    BozjaHolsterSlot0 = 0xE0,
    BozjaHolsterSlot1 = 0xE1,
    Pomander = 0xE2,
    Magicite = 0xE3,
}

public readonly record struct ActionId(uint Raw)
{
    public ActionId(ActionType type, uint id) : this(((uint)type << 24) | id)
    {
    }

    public ActionType Type => (ActionType)(Raw >> 24);
    public uint Id => Raw & 0x00FFFFFFu;
    public static ActionId Spell(uint id) => new(ActionType.Spell, id);

    public override string ToString() => $"{Type} {Id}";
}

public enum ActorType : ushort
{
    None = 0,
    Player = 0x104,
    Part = 0x201,
    Pet = 0x202,
    Chocobo = 0x203,
    Enemy = 0x205,
    DutySupport = 0x209,
    Helper = 0x20B,
    EventNpc = 0x300,
    Treasure = 0x400,
    Aetheryte = 0x500,
    GatheringPoint = 0x600,
    EventObj = 0x700,
    MountType = 0x800,
    Companion = 0x900,
    Retainer = 0xA00,
    Area = 0xB00,
    Housing = 0xC00,
    Cutscene = 0xD00,
    CardStand = 0xE00,
}

public enum Class : byte
{
    None = 0,
    GLA = 1,
    PGL = 2,
    MRD = 3,
    LNC = 4,
    ARC = 5,
    CNJ = 6,
    THM = 7,
    CRP = 8,
    BSM = 9,
    ARM = 10,
    GSM = 11,
    LTW = 12,
    WVR = 13,
    ALC = 14,
    CUL = 15,
    MIN = 16,
    BTN = 17,
    FSH = 18,
    PLD = 19,
    MNK = 20,
    WAR = 21,
    DRG = 22,
    BRD = 23,
    WHM = 24,
    BLM = 25,
    ACN = 26,
    SMN = 27,
    SCH = 28,
    ROG = 29,
    NIN = 30,
    MCH = 31,
    DRK = 32,
    AST = 33,
    SAM = 34,
    RDM = 35,
    BLU = 36,
    GNB = 37,
    DNC = 38,
    RPR = 39,
    SGE = 40,
    VPR = 41,
    PCT = 42,
}

public enum ActionEffectType : byte
{
    Nothing = 0,
    Miss = 1,
    FullResist = 2,
    Damage = 3,
    Heal = 4,
    BlockedDamage = 5,
    ParriedDamage = 6,
    Invulnerable = 7,
    NoEffectText = 8,
    FailMissingStatus = 9,
    MpLoss = 10,
    MpGain = 11,
    TpLoss = 12,
    TpGain = 13,
    ApplyStatusEffectTarget = 14,
    ApplyStatusEffectSource = 15,
    RecoveredFromStatusEffect = 16,
    LoseStatusEffectTarget = 17,
    LoseStatusEffectSource = 18,
    StatusNoEffect = 20,
    ThreatPosition = 24,
    EnmityAmountUp = 25,
    EnmityAmountDown = 26,
    StartActionCombo = 27,
    Retaliation = 28,
    Knockback = 31,
    Mount = 39,
    ReviveLB = 50,
    FullResistStatus = 55,
    VFX = 59,
    Resource = 62,
    SetModelState = 73,
    SetHP = 74,
    PartialInvulnerable = 75,
    Interrupt = 76,
}

public readonly record struct ActionEffect(ulong Raw)
{
    public ActionEffectType Type => (ActionEffectType)(byte)Raw;
    public byte Param0 => (byte)(Raw >> 8);
    public byte Param1 => (byte)(Raw >> 16);
    public byte Param2 => (byte)(Raw >> 24);
    public byte Param3 => (byte)(Raw >> 32);
    public byte Param4 => (byte)(Raw >> 40);
    public ushort Value => (ushort)(Raw >> 48);
    public bool FromTarget => (Param4 & 0x20) != 0;
    public bool AtSource => (Param4 & 0x80) != 0;
    public int DamageHealValue => Value + ((Param4 & 0x40) != 0 ? Param3 * 0x10000 : 0);

    public static ulong Pack(ActionEffectType type, byte p0 = 0, byte p1 = 0, byte p2 = 0, byte p3 = 0, byte p4 = 0, ushort value = 0)
        => (byte)type
           | ((ulong)p0 << 8)
           | ((ulong)p1 << 16)
           | ((ulong)p2 << 24)
           | ((ulong)p3 << 32)
           | ((ulong)p4 << 40)
           | ((ulong)value << 48);
}

public readonly record struct ActionEffects(ImmutableArray<ulong> Raw)
{
    public const int MaxCount = 8;
    public static readonly ActionEffects Empty = From([]);

    public static ActionEffects From(ReadOnlySpan<ulong> upToEight)
    {
        var arr = new ulong[MaxCount];
        var n = Math.Min(upToEight.Length, MaxCount);
        upToEight[..n].CopyTo(arr);
        return new ActionEffects(ImmutableArray.Create(arr));
    }

    public IEnumerable<ActionEffect> NonEmpty()
    {
        var raw = Raw.IsDefault ? ImmutableArray<ulong>.Empty : Raw;
        foreach (var value in raw)
        {
            if (value != 0)
                yield return new ActionEffect(value);
        }
    }

    public bool Equals(ActionEffects other)
    {
        var a = Raw.IsDefaultOrEmpty ? Empty.Raw : Raw;
        var b = other.Raw.IsDefaultOrEmpty ? Empty.Raw : other.Raw;
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var raw = Raw.IsDefaultOrEmpty ? Empty.Raw : Raw;
        var hash = new HashCode();
        foreach (var value in raw)
            hash.Add(value);
        return hash.ToHashCode();
    }
}

public readonly record struct CastTarget(ulong InstanceId, ActionEffects Effects);

public readonly record struct ActorStatus(uint Id, ushort Extra, DateTime ExpireAt, ulong SourceId);

public readonly record struct HpMp(uint CurHp, uint MaxHp, uint Shield, uint CurMp, uint MaxMp);

public readonly record struct PosRot(Vector3 Pos, Angle Rot);

public readonly record struct FrameInfo(ulong Qpc, uint Index, float DurationRaw, float Duration, float TickSpeedMultiplier);
