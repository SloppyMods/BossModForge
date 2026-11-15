using System.CommandLine;
using BossModForge.Cli.GameData;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;

namespace BossModForge.Cli.Commands;

public static class InferCommand
{
    public static Command Create()
    {
        var log = new Argument<string>("log")
        {
            Description = "BossMod replay .log path",
        };
        var output = new Option<string?>("-o")
        {
            Description = "Write Encounter Model JSON to this path (default: stdout)",
        };
        var moduleName = new Option<string?>("--module-name")
        {
            Description = "Override emitted module class name",
        };
        var ns = new Option<string?>("--namespace")
        {
            Description = "Override emitted C# namespace",
        };
        var gamePath = new Option<string?>("--game-path")
        {
            Description = "Path to a FFXIV game install for Lumina-backed names (optional; degrades gracefully)",
        };
        var command = new Command("infer", "Segment a recording and write Encounter Model JSON")
        {
            log,
            output,
            moduleName,
            ns,
            gamePath,
        };
        command.SetAction(parseResult =>
        {
            var logPath = parseResult.GetValue(log) ?? "";
            var outputPath = parseResult.GetValue(output);
            var module = parseResult.GetValue(moduleName);
            var namespaceName = parseResult.GetValue(ns);
            var gameDir = parseResult.GetValue(gamePath);
            return Run(logPath, outputPath, module, namespaceName, gameDir, Console.Out, Console.Error);
        });
        return command;
    }

    public static int Run(
        string logPath,
        string? outputPath,
        string? moduleName,
        string? namespaceName,
        string? gamePath,
        TextWriter stdout,
        TextWriter stderr,
        IGameData? gameData = null)
    {
        string json;
        EncounterModel em;
        LuminaGameData? lumina = null;
        try
        {
            if (gameData == null)
            {
                if (!string.IsNullOrWhiteSpace(gamePath))
                {
                    lumina = new LuminaGameData(gamePath);
                    if (lumina.Available)
                    {
                        gameData = lumina;
                    }
                    else
                    {
                        stderr.WriteLine($"warning: --game-path '{gamePath}' is not a readable FFXIV install; continuing without game data");
                    }
                }

                gameData ??= NullGameData.Instance;
            }

            var rec = ForgePipeline.Parse(logPath);
            var r = ForgePipeline.Analyze(
                rec,
                gameData,
                new PipelineOptions(Build: new BuildOptions(moduleName, namespaceName)));
            em = r.Effective;
            json = EncounterModelJson.Serialize(em);
            lumina?.Dispose();
        }
        catch (NotImplementedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {logPath}: {ex.Message}");
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
            if (string.IsNullOrWhiteSpace(outputPath))
                stdout.Write(json);
            else
                File.WriteAllText(outputPath, json);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        return em.Review.Any(r => r.Severity == ReviewSeverity.High) ? 2 : 0;
    }
}
