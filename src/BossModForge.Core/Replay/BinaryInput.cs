using System.Collections.Immutable;
using System.Numerics;

namespace BossModForge.Core.Replay;

public sealed class BinaryInput : IReplayInput
{
    private readonly LookaheadStream _stream;
    private readonly BinaryReader _reader;

    public BinaryInput(Stream stream, bool leaveOpen = true)
    {
        _stream = new LookaheadStream(stream, leaveOpen);
        _reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, leaveOpen: false);
    }

    /// <summary>Copies up to <c>dest.Length</c> upcoming bytes without consuming them; returns the count available.</summary>
    public int Peek(Span<byte> dest) => _stream.Peek(dest);

    public bool IsBinary => true;
    public DateTime LineTimestamp => default;
    public int LineNumber => 0;
    public long ByteOffset => _reader.BaseStream.CanSeek ? _reader.BaseStream.Position : 0;
    public ParseIssue? TakeMalformed() => null;

    public Tag NextEntry()
    {
        try
        {
            return new Tag(_reader.ReadUInt32());
        }
        catch (EndOfStreamException)
        {
            return default;
        }
    }

    public bool CanRead() => true;
    public void ReadVoid() { }
    public string ReadString() => _reader.ReadString();
    public float ReadFloat() => _reader.ReadSingle();
    public double ReadDouble() => _reader.ReadDouble();
    public Vector3 ReadVec3() => new(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
    public Angle ReadAngle() => new(_reader.ReadSingle());
    public bool ReadBool() => _reader.ReadBoolean();
    public sbyte ReadSByte() => _reader.ReadSByte();
    public short ReadShort() => _reader.ReadInt16();
    public int ReadInt() => _reader.ReadInt32();
    public long ReadLong() => _reader.ReadInt64();
    public byte ReadByte(bool hex) => _reader.ReadByte();
    public ushort ReadUShort(bool hex) => _reader.ReadUInt16();
    public uint ReadUInt(bool hex) => _reader.ReadUInt32();
    public ulong ReadULong(bool hex) => _reader.ReadUInt64();
    public byte[] ReadBytes() => _reader.ReadBytes(_reader.ReadInt32());
    public ActionId ReadAction() => new(_reader.ReadUInt32());
    public Class ReadClass() => (Class)_reader.ReadByte();

    public ActorStatus ReadStatus()
        => new(_reader.ReadUInt32(), _reader.ReadUInt16(), new DateTime(_reader.ReadInt64()), _reader.ReadUInt64());

    public ActionEffects ReadActionEffects()
    {
        Span<ulong> effects = stackalloc ulong[ActionEffects.MaxCount];
        for (var i = 0; i < ActionEffects.MaxCount; i++)
            effects[i] = _reader.ReadUInt64();
        return ActionEffects.From(effects);
    }

    public ImmutableArray<CastTarget> ReadTargets()
    {
        var count = _reader.ReadInt32();
        var builder = ImmutableArray.CreateBuilder<CastTarget>(count);
        for (var i = 0; i < count; i++)
            builder.Add(new CastTarget(_reader.ReadUInt64(), ReadActionEffects()));
        return builder.ToImmutable();
    }

    public (float, float) ReadFloatPair() => (_reader.ReadSingle(), _reader.ReadSingle());
    public (DateTime, float) ReadTimePair() => (new DateTime(_reader.ReadInt64()), _reader.ReadSingle());
    public ulong ReadActorId() => _reader.ReadUInt64();
    public ImmutableArray<string> ReadRemainingFields() => [];

    public void Dispose() => _reader.Dispose();

    // Forward-only stream with a small lookahead buffer; works over non-seekable BrotliStream.
    // Position/CanSeek mirror the inner stream (minus buffered bytes) so ByteOffset is unchanged.
    private sealed class LookaheadStream(Stream inner, bool leaveOpen) : Stream
    {
        private readonly byte[] _buf = new byte[16];
        private int _start;
        private int _count;

        public int Peek(Span<byte> dest)
        {
            if (dest.Length > _buf.Length)
                throw new ArgumentOutOfRangeException(nameof(dest));
            if (_start > 0)
            {
                Buffer.BlockCopy(_buf, _start, _buf, 0, _count);
                _start = 0;
            }
            while (_count < dest.Length)
            {
                var n = inner.Read(_buf, _count, dest.Length - _count);
                if (n == 0)
                    break;
                _count += n;
            }
            var avail = Math.Min(_count, dest.Length);
            _buf.AsSpan(0, avail).CopyTo(dest);
            return avail;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_count > 0)
            {
                var n = Math.Min(_count, buffer.Length);
                _buf.AsSpan(_start, n).CopyTo(buffer);
                _start += n;
                _count -= n;
                if (_count == 0)
                    _start = 0;
                return n;
            }
            return inner.Read(buffer);
        }

        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position - _count;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
