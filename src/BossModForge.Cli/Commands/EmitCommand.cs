using System.CommandLine;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Cli.Commands;

public static class EmitCommand
{
    public static Command Create()
    {
        var em = new Argument<string>("em.json")
        {
            Description = "Encounter Model JSON path",
        };
        var flavor = new Option<string>("--flavor")
        {
            Description = "Emitter flavor: vbm or bmr",
            Required = true,
        };
        var output = new Option<string?>("-o")
        {
            Description = "Output directory (default: current directory)",
        };
        var flavors = new Option<string?>("--flavors")
        {
            Description = "Directory containing vbm.json / bmr.json",
        };
        var command = new Command("emit", "Emit a BossMod module from Encounter Model JSON")
        {
            em,
            flavor,
            output,
            flavors,
        };
        command.SetAction(parseResult =>
        {
            var emPath = parseResult.GetValue(em) ?? "";
            var flavorValue = parseResult.GetValue(flavor) ?? "";
            var outputDir = parseResult.GetValue(output);
            var flavorsDir = parseResult.GetValue(flavors);
            return Run(emPath, flavorValue, outputDir, flavorsDir, Console.Out, Console.Error);
        });
        return command;
    }

    public static int Run(
        string emPath,
        string flavorName,
        string? outputDir,
        string? flavorsDir,
        TextWriter stdout,
        TextWriter stderr)
    {
        _ = stdout;
        if (!TryParseFlavor(flavorName, out var flavor))
        {
            stderr.WriteLine("error: --flavor must be vbm or bmr");
            return 4;
        }

        string json;
        EncounterModel em;
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
            return 3;
        }

        try
        {
            em = EncounterModelJson.Deserialize(json);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {emPath}: {ex.Message}");
            return 4;
        }

        EmitResult result;
        try
        {
            var dir = ModuleEmitter.ResolveFlavorsDirectory(flavorsDir);
            var profile = ModuleEmitter.ProfileFor(flavor, dir);
            result = ModuleEmitter.Emit(em, profile, new EmitOptions(flavor));
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        var dest = string.IsNullOrWhiteSpace(outputDir) ? Directory.GetCurrentDirectory() : outputDir;
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var file in result.Files)
                File.WriteAllText(Path.Combine(dest, file.RelativePath), file.Content);
            File.WriteAllText(Path.Combine(dest, "FORGE.md"), result.ForgeMd);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 4;
        }

        return result.Review.Any(r => r.Severity == ReviewSeverity.High) ? 2 : 0;
    }

    private static bool TryParseFlavor(string name, out Flavor flavor)
    {
        if (name.Equals("vbm", StringComparison.OrdinalIgnoreCase))
        {
            flavor = Flavor.Vbm;
            return true;
        }

        if (name.Equals("bmr", StringComparison.OrdinalIgnoreCase))
        {
            flavor = Flavor.Bmr;
            return true;
        }

        flavor = default;
        return false;
    }
}
