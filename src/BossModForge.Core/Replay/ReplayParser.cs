using System.IO.Compression;

namespace BossModForge.Core.Replay;

public static class ReplayParser
{
    private static readonly uint MagicBlcb = Tag.Parse("BLCB").Value;
    private static readonly uint MagicBlog = Tag.Parse("BLOG").Value;

    public static ReplayEncoding DetectEncoding(ReadOnlySpan<byte> firstFourBytes)
    {
        if (firstFourBytes.Length < 4)
            return ReplayEncoding.Text;
        var magic = BitConverter.ToUInt32(firstFourBytes);
        if (magic == MagicBlcb)
            return ReplayEncoding.BinaryBrotli;
        if (magic == MagicBlog)
            return ReplayEncoding.Binary;
        return ReplayEncoding.Text;
    }

    public static Recording Parse(string path, ParseOptions? options = null, CancellationToken ct = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Parse(stream, path, options, ct);
    }

    public static Recording Parse(Stream stream, string displayPath, ParseOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ParseOptions();
        Span<byte> header = stackalloc byte[4];
        var n = stream.Read(header);
        var encoding = n == 4 ? DetectEncoding(header) : ReplayEncoding.Text;

        Stream? decompressed = null;
        IReplayInput input;
        switch (encoding)
        {
            case ReplayEncoding.BinaryBrotli:
                decompressed = new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true);
                input = new BinaryInput(decompressed, leaveOpen: true);
                break;
            case ReplayEncoding.Binary:
                input = new BinaryInput(stream, leaveOpen: true);
                break;
            default:
                if (!stream.CanSeek)
                    throw new ArgumentException("Text replay streams must be seekable so the magic probe can rewind.", nameof(stream));
                stream.Position = 0;
                input = new TextInput(stream, leaveOpen: true);
                break;
        }

        try
        {
            return ParseInput(input, displayPath, encoding, options, stream, ct);
        }
        finally
        {
            input.Dispose();
            decompressed?.Dispose();
        }
    }

    private static Recording ParseInput(
        IReplayInput input,
        string displayPath,
        ReplayEncoding encoding,
        ParseOptions options,
        Stream rawStream,
        CancellationToken ct)
    {
        var reader = new OpReader(input, options.Lineage);
        var ops = new List<Op>();
        var issues = new List<ParseIssue>();
        var counts = new SortedDictionary<Tag, int>();
        var seenUnknown = new HashSet<uint>();
        var opIndex = 0;
        var reportedNewerVersion = false;
        var reportedLineage = false;
        var length = rawStream.CanSeek && rawStream.Length > 0 ? rawStream.Length : 0L;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var tag = input.NextEntry();
            var malformed = input.TakeMalformed();
            if (malformed is not null)
            {
                issues.Add(malformed);
                continue;
            }
            if (tag.Value == 0)
                break;

            try
            {
                if (reader.IsKnown(tag))
                {
                    var op = reader.ReadKnown(tag);
                    if (reader.Unsupported)
                    {
                        issues.Add(new ParseIssue(
                            IssueSeverity.Error,
                            "UnsupportedVersion",
                            $"Replay version {reader.Version} is not supported.",
                            input.LineNumber == 0 ? null : input.LineNumber,
                            input.IsBinary ? input.ByteOffset : null));
                        return Finish(displayPath, encoding, reader, [], issues, counts);
                    }
                    if (tag == Tag.Ver)
                    {
                        if (reader.Version < 23)
                        {
                            issues.Add(new ParseIssue(
                                IssueSeverity.Warning,
                                "OlderVersion",
                                $"Replay version {reader.Version} is older than 23; parsing with version gates.",
                                input.LineNumber == 0 ? null : input.LineNumber,
                                input.IsBinary ? input.ByteOffset : null));
                        }
                        if (reader.NewerVersion && !reportedNewerVersion)
                        {
                            reportedNewerVersion = true;
                            issues.Add(new ParseIssue(
                                IssueSeverity.Degraded,
                                "UnsupportedVersion",
                                $"Replay version {reader.HeaderVersion} is newer than supported {OpReader.MaxSupportedVersion}; parsed with v{OpReader.MaxSupportedVersion} gates.",
                                input.LineNumber == 0 ? null : input.LineNumber,
                                input.IsBinary ? input.ByteOffset : null));
                        }
                        continue;
                    }
                    if (op is not null)
                        AddOp(op, ops, counts, options);
                    if (reader.LineageDetected && !reportedLineage)
                    {
                        reportedLineage = true;
                        issues.Add(new ParseIssue(
                            IssueSeverity.Info,
                            "LineageDetected",
                            $"Replay v{reader.Version} written by {(reader.Lineage == ReplayLineage.Bmr ? "BossmodReborn" : "BossMod (vbm)")} layout.",
                            input.LineNumber == 0 ? null : input.LineNumber,
                            input.IsBinary ? input.ByteOffset : null));
                    }
                }
                else if (input.IsBinary)
                {
                    issues.Add(new ParseIssue(
                        IssueSeverity.Degraded,
                        "UnknownBinaryTag",
                        $"Unknown binary tag '{tag}'.",
                        ByteOffset: input.ByteOffset));
                    break;
                }
                else
                {
                    var fields = reader.ReadRemainingFields();
                    var raw = new RawOp(reader.Timestamp(), tag, fields);
                    if (seenUnknown.Add(tag.Value))
                    {
                        issues.Add(new ParseIssue(
                            IssueSeverity.Info,
                            "UnknownTag",
                            $"Unknown tag '{tag}'.",
                            input.LineNumber,
                            input.ByteOffset));
                    }
                    AddOp(raw, ops, counts, options);
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or EndOfStreamException or InvalidOperationException or IndexOutOfRangeException)
            {
                if (input.IsBinary)
                {
                    issues.Add(new ParseIssue(IssueSeverity.Degraded, "UnknownBinaryTag", ex.Message, ByteOffset: input.ByteOffset));
                    break;
                }
                issues.Add(new ParseIssue(IssueSeverity.Warning, "MalformedLine", ex.Message, input.LineNumber, input.ByteOffset));
            }

            if ((++opIndex & 0x3FF) == 0 && length > 0 && rawStream.CanSeek)
                options.Progress?.Report((float)rawStream.Position / length);
        }

        options.Progress?.Report(1f);
        return Finish(displayPath, encoding, reader, ops, issues, counts);
    }

    private static void AddOp(Op op, List<Op> ops, SortedDictionary<Tag, int> counts, ParseOptions options)
    {
        counts[op.Tag] = counts.TryGetValue(op.Tag, out var n) ? n + 1 : 1;
        if (op is RawOp && !options.KeepRawOps)
            return;
        ops.Add(op);
    }

    private static Recording Finish(
        string displayPath,
        ReplayEncoding encoding,
        OpReader reader,
        List<Op> ops,
        List<ParseIssue> issues,
        SortedDictionary<Tag, int> counts)
    {
        var meta = new RecordingMeta(
            displayPath,
            encoding,
            reader.SawVersion ? reader.Version : 0,
            reader.Qpf,
            reader.GameVersion,
            reader.StartTime);
        var frozen = ops.ToArray();
        return new Recording(meta, frozen, issues.ToArray(), counts, Recording.BuildActors(frozen));
    }
}
