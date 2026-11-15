using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using BossModForge.Cli.Conformance;
using BossModForge.Core.Replay;

namespace BossModForge.Cli.Commands;

public static class ConformCommand
{
    public static Command Create()
    {
        var logs = new Argument<string[]>("logs")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "BossMod replay .log paths",
        };
        var bossModDll = new Option<string?>("--bossmod-dll")
        {
            Description = "Path to BossMod.dll built from the pinned checkout (docs/UPSTREAM.md §1)",
        };
        var json = new Option<bool>("--json")
        {
            Description = "Emit one JSON object per log instead of the human table",
        };
        var selftest = new Option<bool>("--selftest")
        {
            Description = "Load the DLL and print the resolved op-type -> tag map, then exit",
        };
        var command = new Command("conform", "Compare Forge's per-tag op counts against BossMod's own parser")
        {
            logs,
            bossModDll,
            json,
            selftest,
        };
        command.SetAction(parseResult =>
        {
            var logPaths = parseResult.GetValue(logs) ?? [];
            var dllPath = parseResult.GetValue(bossModDll);
            var asJson = parseResult.GetValue(json);
            var selfTest = parseResult.GetValue(selftest);
            return Run(logPaths, dllPath, asJson, selfTest, Console.Out, Console.Error);
        });
        return command;
    }

    public static int Run(
        IReadOnlyList<string> logPaths,
        string? bossModDllPath,
        bool json,
        bool selftest,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (selftest)
        {
            if (string.IsNullOrWhiteSpace(bossModDllPath))
            {
                stderr.WriteLine(
                    "error: conformance requires --bossmod-dll (a build of BossMod.dll from the pinned checkout); see docs/UPSTREAM.md");
                return 4;
            }
            try
            {
                stdout.Write(BossModReflection.SelfTest(bossModDllPath));
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                stderr.WriteLine("error: " + ex.Message);
                return 4;
            }
        }

        if (string.IsNullOrWhiteSpace(bossModDllPath))
        {
            stderr.WriteLine(
                "error: conformance requires --bossmod-dll (a build of BossMod.dll from the pinned checkout); see docs/UPSTREAM.md");
            return 4;
        }

        if (logPaths.Count == 0)
        {
            stderr.WriteLine("error: conform requires at least one replay log");
            return 4;
        }

        List<Recording> recordings;
        try
        {
            recordings = [.. logPaths.Select(path => ReplayParser.Parse(path, new ParseOptions(KeepRawOps: false)))];
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        var exit = 0;
        foreach (var (path, recording) in logPaths.Zip(recordings))
        {
            IReadOnlyDictionary<string, int> bossModCounts;
            try
            {
                bossModCounts = BossModReflection.CountOps(bossModDllPath, path);
            }
            catch (InvalidOperationException ex)
            {
                stderr.WriteLine("error: " + ex.Message);
                return 4;
            }

            var forgeCounts = recording.TagCounts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value, StringComparer.Ordinal);
            var diffs = ConformComparer.Compare(forgeCounts, bossModCounts);
            if (diffs.Length > 0)
                exit = 3;
            if (json)
                WriteJson(stdout, path, diffs);
            else
                WriteHuman(stdout, path, diffs);
        }

        return exit;
    }

    private static void WriteHuman(TextWriter stdout, string path, IReadOnlyList<TagCountDiff> diffs)
    {
        stdout.WriteLine(path);
        if (diffs.Count == 0)
        {
            stdout.WriteLine("  all tags match");
            return;
        }
        stdout.WriteLine("  tag     forge  bossmod");
        foreach (var diff in diffs)
            stdout.WriteLine($"  {diff.Tag,-6}  {diff.ForgeCount,5}  {diff.BossModCount,7}");
    }

    private static void WriteJson(TextWriter stdout, string path, IReadOnlyList<TagCountDiff> diffs)
    {
        var obj = new JsonObject
        {
            ["path"] = path,
            ["diffs"] = new JsonArray([.. diffs.Select(d => (JsonNode)new JsonObject
            {
                ["tag"] = d.Tag,
                ["forge"] = d.ForgeCount,
                ["bossMod"] = d.BossModCount,
            })]),
        };
        stdout.WriteLine(obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }
}
