using System.CommandLine;
using BossModForge.Cli.Validation;
using BossModForge.Core.Emit;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Core.Validation;

namespace BossModForge.Cli.Commands;

public static class ValidateCommand
{
    public static Command Create()
    {
        var em = new Option<string>("--em")
        {
            Description = "Encounter Model JSON path",
            Required = true,
        };
        var recordings = new Option<string[]>("--recordings")
        {
            Description = "BossMod replay .log paths",
            Required = true,
            Arity = ArgumentArity.OneOrMore,
        };
        var output = new Option<string?>("--out")
        {
            Description = "Directory for validation-report.json/md (default: current directory)",
        };
        var writeEm = new Option<bool>("--write-em")
        {
            Description = "Rewrite the EM with lowered confidence and review items for failing abilities",
        };
        var bossmod = new Option<string?>("--bossmod")
        {
            Description = "BossMod checkout for in-checkout build + ReplayValidator host run (M4b)",
        };
        var flavor = new Option<string?>("--flavor")
        {
            Description = "Emitter flavor for the --bossmod module build: vbm or bmr (default vbm)",
        };
        var command = new Command("validate", "Replay recordings against an Encounter Model and write a dry-validation report")
        {
            em,
            recordings,
            output,
            writeEm,
            bossmod,
            flavor,
        };
        command.SetAction(parseResult =>
        {
            var emPath = parseResult.GetValue(em) ?? "";
            var recs = parseResult.GetValue(recordings) ?? [];
            var outDir = parseResult.GetValue(output);
            var doWrite = parseResult.GetValue(writeEm);
            var bossmodPath = parseResult.GetValue(bossmod);
            var flavorName = parseResult.GetValue(flavor);
            return Run(emPath, recs, outDir, doWrite, bossmodPath, Console.Out, Console.Error, flavorName: flavorName);
        });
        return command;
    }

    public static int Run(
        string emPath,
        IReadOnlyList<string> recordings,
        string? outDir,
        bool writeEm,
        string? bossmodPath,
        TextWriter stdout,
        TextWriter stderr,
        ValidationOptions? options = null,
        string? flavorName = null)
    {
        if (!string.IsNullOrWhiteSpace(bossmodPath))
        {
            try
            {
                BossmodBuildValidator.ValidateCheckoutShape(bossmodPath);
            }
            catch (InvalidOperationException ex)
            {
                stderr.WriteLine($"error: --bossmod: {ex.Message}");
                return 4;
            }
        }

        string json;
        try
        {
            json = File.ReadAllText(emPath);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {emPath}: {ex.Message}");
            return 4;
        }

        var errors = EncounterModelJson.Validate(json);
        if (errors.Length > 0)
        {
            foreach (var error in errors)
                stderr.WriteLine("error: " + error);
            return 4;
        }

        EncounterModel em;
        try
        {
            em = EncounterModelJson.Deserialize(json);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {emPath}: {ex.Message}");
            return 4;
        }

        var parsed = new List<Recording>(recordings.Count);
        foreach (var path in recordings)
        {
            try
            {
                parsed.Add(ReplayParser.Parse(path, new ParseOptions(KeepRawOps: false)));
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"error: {path}: {ex.Message}");
                return 4;
            }
        }

        ValidationReport report;
        try
        {
            report = Validator.Validate(em, parsed, options);
            report = report with { EmPath = emPath, Recordings = [.. recordings] };
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        string? validatorHostOutput = null;
        if (!string.IsNullOrWhiteSpace(bossmodPath))
        {
            int bossExit;
            (bossExit, validatorHostOutput) = RunBossmodBuild(em, recordings, bossmodPath, flavorName, stderr);
            if (bossExit == 4)
                return 4;
            if (bossExit == 3)
            {
                report = report with
                {
                    Passed = false,
                    Failures = report.Failures.Add("bossmod build failed; see stderr for mapped compile errors"),
                };
            }
        }

        var dest = string.IsNullOrWhiteSpace(outDir) ? Directory.GetCurrentDirectory() : outDir;
        try
        {
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "validation-report.json"), ValidationReportWriter.ToJson(report));
            var markdown = ValidationReportWriter.ToMarkdown(report);
            if (!string.IsNullOrWhiteSpace(validatorHostOutput))
            {
                markdown += "\n## BossMod in-checkout validation\n\n```json\n"
                    + validatorHostOutput.Trim().Replace("\r\n", "\n", StringComparison.Ordinal)
                    + "\n```\n";
            }

            File.WriteAllText(Path.Combine(dest, "validation-report.md"), markdown);
            stdout.Write(markdown);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        if (writeEm)
        {
            try
            {
                var downgraded = Validator.ApplyDowngrades(em, report);
                File.WriteAllText(emPath, EncounterModelJson.Serialize(downgraded));
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"error: {ex.Message}");
                return 4;
            }
        }

        return report.Passed ? 0 : 3;
    }

    // Returns (exit, hostOutput): 0 = build+host succeeded, 3 = build failed
    // (errors already printed to stderr), 4 = setup error (message printed).
    private static (int Exit, string? HostOutput) RunBossmodBuild(
        EncounterModel em,
        IReadOnlyList<string> recordings,
        string bossmodPath,
        string? flavorName,
        TextWriter stderr)
    {
        var flavor = Flavor.Vbm;
        if (!string.IsNullOrWhiteSpace(flavorName)
            && !flavorName.Equals("vbm", StringComparison.OrdinalIgnoreCase))
        {
            if (!flavorName.Equals("bmr", StringComparison.OrdinalIgnoreCase))
            {
                stderr.WriteLine("error: --flavor must be vbm or bmr");
                return (4, null);
            }

            flavor = Flavor.Bmr;
        }

        string? moduleDir = null;
        try
        {
            var profile = ModuleEmitter.ProfileFor(flavor, ModuleEmitter.ResolveFlavorsDirectory(null));
            var scratchNs = BossmodBuildValidator.DefaultScratchNamespace;
            var emit = ModuleEmitter.Emit(em, profile, new EmitOptions(flavor, scratchNs + "." + em.Encounter.ModuleName));
            moduleDir = Directory.CreateTempSubdirectory("forge-bossmod-module-").FullName;
            foreach (var file in emit.Files)
            {
                var path = Path.Combine(moduleDir, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Content);
            }

            var result = BossmodBuildValidator.Run(moduleDir, bossmodPath, em, recordings);
            if (result.BuildSucceeded)
                return (0, result.ValidatorHostOutput);

            foreach (var error in result.Errors)
            {
                var where = error.File.Length > 0 ? $"{error.File}({error.Line}): " : "";
                var target = error.EmTarget is { } t ? $" [em: {t}]" : "";
                stderr.WriteLine($"error: {where}{error.Code}: {error.Message}{target}");
            }

            return (3, null);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: --bossmod: {ex.Message}");
            return (4, null);
        }
        finally
        {
            if (moduleDir is not null)
                Directory.Delete(moduleDir, recursive: true);
        }
    }
}
