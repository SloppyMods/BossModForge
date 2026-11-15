using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace BossModForge.Core.Replay;

public sealed class TextInput : IReplayInput
{
    private readonly StreamReader _reader;
    private string[] _line = [];
    private int _nextPayload;
    private ParseIssue? _malformed;

    public TextInput(Stream stream, bool leaveOpen = true)
    {
        _reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen);
    }

    public bool IsBinary => false;
    public DateTime LineTimestamp { get; private set; }
    public int LineNumber { get; private set; }
    public long ByteOffset => _reader.BaseStream.CanSeek ? _reader.BaseStream.Position : 0;

    public ParseIssue? TakeMalformed()
    {
        var issue = _malformed;
        _malformed = null;
        return issue;
    }

    public Tag NextEntry()
    {
        _malformed = null;
        while (_reader.ReadLine() is { } line)
        {
            LineNumber++;
            if (line.Length == 0 || line[0] == '#')
                continue;

            _line = line.Split('|');
            if (_line.Length < 2)
            {
                _malformed = new ParseIssue(IssueSeverity.Warning, "MalformedLine", line, LineNumber, ByteOffset);
                return default;
            }

            var tagText = _line[1];
            if (tagText.Length != 4)
            {
                _malformed = new ParseIssue(IssueSeverity.Warning, "MalformedLine", line, LineNumber, ByteOffset);
                return default;
            }

            try
            {
                LineTimestamp = DateTime.Parse(_line[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }
            catch (FormatException)
            {
                _malformed = new ParseIssue(IssueSeverity.Warning, "MalformedLine", line, LineNumber, ByteOffset);
                return default;
            }

            _nextPayload = 2;
            Span<byte> four = [(byte)tagText[0], (byte)tagText[1], (byte)tagText[2], (byte)tagText[3]];
            return new Tag(four);
        }

        return default;
    }

    public bool CanRead() => _nextPayload < _line.Length;
    public void ReadVoid() => _nextPayload++;
    public string ReadString() => _line[_nextPayload++];
    public float ReadFloat() => float.Parse(ReadString(), CultureInfo.InvariantCulture);
    public double ReadDouble() => double.Parse(ReadString(), CultureInfo.InvariantCulture);

    public Vector3 ReadVec3()
    {
        var parts = ReadString().Split('/');
        return new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    public Angle ReadAngle() => Angle.FromDeg(ReadFloat());
    public bool ReadBool() => bool.Parse(ReadString());
    public sbyte ReadSByte() => sbyte.Parse(ReadString(), CultureInfo.InvariantCulture);
    public short ReadShort() => short.Parse(ReadString(), CultureInfo.InvariantCulture);
    public int ReadInt() => int.Parse(ReadString(), CultureInfo.InvariantCulture);
    public long ReadLong() => long.Parse(ReadString(), CultureInfo.InvariantCulture);
    public byte ReadByte(bool hex) => byte.Parse(ReadString(), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
    public ushort ReadUShort(bool hex) => ushort.Parse(ReadString(), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
    public uint ReadUInt(bool hex) => uint.Parse(ReadString(), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
    public ulong ReadULong(bool hex) => ulong.Parse(ReadString(), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);

    public byte[] ReadBytes()
    {
        var str = ReadString();
        var res = new byte[str.Length >> 1];
        for (var i = 0; i < res.Length; i++)
            res[i] = byte.Parse(str.AsSpan(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return res;
    }

    public ActionId ReadAction()
    {
        var parts = ReadString().Split(' ');
        var type = parts.Length > 0 ? Enum.Parse<ActionType>(parts[0]) : ActionType.None;
        var id = parts.Length > 1 ? uint.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        return new ActionId(type, id);
    }

    public Class ReadClass() => Enum.Parse<Class>(ReadString());

    public ActorStatus ReadStatus()
    {
        var sid = ReadString();
        var sep = sid.IndexOf(' ', StringComparison.Ordinal);
        var id = uint.Parse(sep >= 0 ? sid.AsSpan(0, sep) : sid.AsSpan(), CultureInfo.InvariantCulture);
        var extra = ushort.Parse(ReadString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var expire = LineTimestamp.AddSeconds(ReadFloat());
        return new ActorStatus(id, extra, expire, ReadActorId());
    }

    public ActionEffects ReadActionEffects()
    {
        Span<ulong> effects = stackalloc ulong[ActionEffects.MaxCount];
        for (var i = 0; i < ActionEffects.MaxCount; i++)
            effects[i] = ReadULong(true);
        return ActionEffects.From(effects);
    }

    public ImmutableArray<CastTarget> ReadTargets()
    {
        var builder = ImmutableArray.CreateBuilder<CastTarget>();
        Span<ulong> effects = stackalloc ulong[ActionEffects.MaxCount];
        while (CanRead())
        {
            effects.Clear();
            var parts = ReadString().Split('!');
            var n = Math.Min(parts.Length - 1, ActionEffects.MaxCount);
            for (var j = 0; j < n; j++)
                effects[j] = ulong.Parse(parts[j + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            builder.Add(new CastTarget(ParseActorId(parts[0]), ActionEffects.From(effects)));
        }
        return builder.ToImmutable();
    }

    public (float, float) ReadFloatPair()
    {
        var parts = ReadString().Split('/');
        return (
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    public (DateTime, float) ReadTimePair()
    {
        var parts = ReadString().Split('/');
        return (
            LineTimestamp.AddSeconds(float.Parse(parts[0], CultureInfo.InvariantCulture)),
            float.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    public ulong ReadActorId() => ParseActorId(ReadString());

    public ImmutableArray<string> ReadRemainingFields()
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        while (CanRead())
            builder.Add(ReadString());
        return builder.ToImmutable();
    }

    public void Dispose() => _reader.Dispose();

    private static ulong ParseActorId(string actor)
    {
        var sep = actor.IndexOf('/', StringComparison.Ordinal);
        return ulong.Parse(sep >= 0 ? actor.AsSpan(0, sep) : actor.AsSpan(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
