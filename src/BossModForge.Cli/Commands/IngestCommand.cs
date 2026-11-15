using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BossModForge.Core.Replay;

namespace BossModForge.Cli.Commands;

public static class IngestCommand
{
    public static Command Create()
    {
        var files = new Argument<string[]>("files")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "BossMod replay .log paths",
        };
        var json = new Option<bool>("--json")
        {
            Description = "Emit one JSON object per file instead of the human table",
        };
        var command = new Command("ingest", "Parse replay recordings and print per-tag op counts")
        {
            files,
            json,
        };
        command.SetAction(parseResult =>
        {
            var paths = parseResult.GetValue(files) ?? [];
            var asJson = parseResult.GetValue(json);
            return Run(paths, asJson, Console.Out, Console.Error);
        });
        return command;
    }

    public static int Run(IReadOnlyList<string> paths, bool json, TextWriter stdout, TextWriter stderr)
    {
        var exit = 0;
        foreach (var path in paths)
        {
            try
            {
                var recording = ReplayParser.Parse(path);
                if (recording.Degraded && exit == 0)
                    exit = 1;
                if (json)
                    WriteJson(stdout, recording);
                else
                    WriteHuman(stdout, recording);
            }
            catch (Exception ex)
            {
                exit = 2;
                stderr.WriteLine($"error: {path}: {ex.Message}");
            }
        }

        return exit;
    }

    internal static string EncodingName(ReplayEncoding encoding) => encoding switch
    {
        ReplayEncoding.Text => "text",
        ReplayEncoding.Binary => "binary",
        ReplayEncoding.BinaryBrotli => "binary+brotli",
        _ => encoding.ToString().ToLowerInvariant(),
    };

    private static void WriteHuman(TextWriter stdout, Recording recording)
    {
        var meta = recording.Meta;
        stdout.WriteLine(meta.Path);
        stdout.WriteLine(
            $"  encoding: {EncodingName(meta.Encoding)}   version: {meta.Version}   qpf: {meta.Qpf}   game: {meta.GameVersion}");
        stdout.WriteLine(
            $"  start: {meta.StartTime.ToString("o", CultureInfo.InvariantCulture)}   ops: {recording.Ops.Count}   actors: {recording.Actors.Count}   issues: {recording.Issues.Count}");
        stdout.WriteLine("  tag    count");
        foreach (var (tag, count) in OrderedTagCounts(recording))
            stdout.WriteLine($"  {tag,-6} {count}");
    }

    private static void WriteJson(TextWriter stdout, Recording recording)
    {
        var meta = recording.Meta;
        var obj = new JsonObject
        {
            ["path"] = meta.Path,
            ["encoding"] = EncodingName(meta.Encoding),
            ["version"] = meta.Version,
            ["qpf"] = meta.Qpf,
            ["gameVersion"] = meta.GameVersion,
            ["startTime"] = meta.StartTime.ToString("o", CultureInfo.InvariantCulture),
            ["opCount"] = recording.Ops.Count,
            ["actorCount"] = recording.Actors.Count,
        };

        var issues = new JsonArray();
        foreach (var issue in recording.Issues)
        {
            var item = new JsonObject
            {
                ["severity"] = issue.Severity.ToString(),
                ["code"] = issue.Code,
                ["message"] = issue.Message,
            };
            if (issue.Line is { } line)
                item["line"] = line;
            if (issue.ByteOffset is { } offset)
                item["byteOffset"] = offset;
            issues.Add(item);
        }

        obj["issues"] = issues;

        var tags = new JsonObject();
        foreach (var (tag, count) in OrderedTagCounts(recording))
            tags[tag.ToString()] = count;
        obj["tagCounts"] = tags;

        stdout.WriteLine(obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static IEnumerable<(Tag Tag, int Count)> OrderedTagCounts(Recording recording) =>
        recording.TagCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value));
}
