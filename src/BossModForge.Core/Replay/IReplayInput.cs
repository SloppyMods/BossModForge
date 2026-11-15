using System.Collections.Immutable;
using System.Numerics;

namespace BossModForge.Core.Replay;

public interface IReplayInput : IDisposable
{
    bool IsBinary { get; }
    DateTime LineTimestamp { get; }
    int LineNumber { get; }
    long ByteOffset { get; }
    ParseIssue? TakeMalformed();

    Tag NextEntry();
    bool CanRead();
    void ReadVoid();
    string ReadString();
    float ReadFloat();
    double ReadDouble();
    Vector3 ReadVec3();
    Angle ReadAngle();
    bool ReadBool();
    sbyte ReadSByte();
    short ReadShort();
    int ReadInt();
    long ReadLong();
    byte ReadByte(bool hex);
    ushort ReadUShort(bool hex);
    uint ReadUInt(bool hex);
    ulong ReadULong(bool hex);
    byte[] ReadBytes();
    ActionId ReadAction();
    Class ReadClass();
    ActorStatus ReadStatus();
    ActionEffects ReadActionEffects();
    ImmutableArray<CastTarget> ReadTargets();
    (float, float) ReadFloatPair();
    (DateTime, float) ReadTimePair();
    ulong ReadActorId();
    ImmutableArray<string> ReadRemainingFields();
}
