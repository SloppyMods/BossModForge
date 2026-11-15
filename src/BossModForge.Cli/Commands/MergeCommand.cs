using System.CommandLine;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Replay;

namespace BossModForge.Cli.Commands;

public static class MergeCommand
{
    public static Command Create()
    {
        var logs = new Argument<string[]>("logs")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "BossMod replay .log paths (one logical fight, distinct actor instance ids)",
        };
        var output = new Option<string?>("--out")
        {
            Description = "Write the merged Encounter Model JSON to this path",
            Required = true,
        };
        var em = new Option<string?>("--em")
        {
            Description = "Existing Encounter Model JSON; its overrides are carried into the merged EM",
        };
        var selectBoss = new Option<uint?>("--select-boss")
        {
            Description = "Only segment pulls whose primary actor has this OID (e.g. door bosses)",
        };
        var command = new Command("merge", "Merge multiple recordings of one encounter into a single Encounter Model")
        {
            logs,
            output,
            em,
            selectBoss,
        };
        command.SetAction(parseResult =>
        {
            var logPaths = parseResult.GetValue(logs) ?? [];
            var outputPath = parseResult.GetValue(output);
            var emPath = parseResult.GetValue(em);
            var bossOid = parseResult.GetValue(selectBoss);
            return Run(logPaths, outputPath, emPath, bossOid, Console.Out, Console.Error);
        });
        return command;
    }

    public static int Run(
        IReadOnlyList<string> logPaths,
        string? outputPath,
        string? emPath,
        uint? selectBoss,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (logPaths.Count == 0)
        {
            stderr.WriteLine("error: merge requires at least one replay log");
            return 4;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            stderr.WriteLine("error: --out is required");
            return 4;
        }

        List<Recording> recordings;
        try
        {
            recordings = [.. logPaths.Select(path => ReplayParser.Parse(path, new ParseOptions(KeepRawOps: false)))];
        }
        catch (NotImplementedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        Overrides? overrides = null;
        if (!string.IsNullOrWhiteSpace(emPath))
        {
            try
            {
                var existing = EncounterModelJson.Deserialize(File.ReadAllText(emPath));
                if (!existing.Overrides.IsEmpty)
                    overrides = existing.Overrides;
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"error: {emPath}: {ex.Message}");
                return 4;
            }
        }

        PipelineOptions? options = null;
        if (selectBoss is uint bossOid)
        {
            options = new PipelineOptions(Segmenter: new SegmenterOptions(
                SegmenterOptions.Default.MinPullDuration,
                SegmenterOptions.Default.CombatGapToSplit,
                bossOid));
        }

        string json;
        EncounterModel em;
        try
        {
            var result = ForgePipeline.Analyze(recordings, NullGameData.Instance, options, overrides);
            em = result.Effective;
            json = EncounterModelJson.Serialize(em);
        }
        catch (NotImplementedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        var errors = EncounterModelJson.Validate(json);
        if (errors.Length > 0)
        {
            foreach (var error in errors)
                stderr.WriteLine("error: " + error);
            return 3;
        }

        try
        {
            File.WriteAllText(outputPath, json);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        stdout.WriteLine(
            $"merged {recordings.Count} recordings, {em.Pulls.Length} pulls, {em.Sources.Length} sources -> {outputPath}");
        return em.Review.Any(r => r.Severity == ReviewSeverity.High) ? 2 : 0;
    }
}
