using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using BossModForge.Core.Model;

namespace BossModForge.Cli.Validation;

public sealed record BossmodBuildResult(
    bool BuildSucceeded,
    ImmutableArray<CompileErrorItem> Errors,
    string? ValidatorHostOutput);

public static class BossmodBuildValidator
{
    public const string DefaultScratchNamespace = "BossMod.ForgeScratch";

    private const int BuildTimeoutMs = 600_000;
    private const int HostTimeoutMs = 300_000;

    // Throws InvalidOperationException (by design, so the CLI can attribute the
    // error to --bossmod) when the directory is not a BossMod/BMR checkout.
    public static void ValidateCheckoutShape(string bossmodCheckout)
    {
        if (!Directory.Exists(bossmodCheckout)
            || !Directory.Exists(Path.Combine(bossmodCheckout, "BossMod"))
            || FindSolutionFile(bossmodCheckout) is null)
        {
            throw new InvalidOperationException(
                $"{bossmodCheckout} does not look like a BossMod checkout "
                + "(expected a BossMod/ directory and a .sln/.slnx solution file)");
        }
    }

    public static BossmodBuildResult Run(
        string moduleDir,
        string bossmodCheckout,
        EncounterModel em,
        IReadOnlyList<string> recordings,
        string scratchNamespace = DefaultScratchNamespace)
    {
        ValidateCheckoutShape(bossmodCheckout);

        try
        {
            var solution = FindSolutionFile(bossmodCheckout)!;
            var moduleName = em.Encounter.ModuleName;
            var scratchDir = Path.Combine(
                bossmodCheckout, "BossMod", "Modules",
                scratchNamespace.Split('.')[^1], moduleName);
            Directory.CreateDirectory(scratchDir);
            foreach (var file in Directory.GetFiles(moduleDir).OrderBy(f => f, StringComparer.Ordinal))
                File.Copy(file, Path.Combine(scratchDir, Path.GetFileName(file)), overwrite: true);

            var build = RunProcess("dotnet", $"build \"{solution}\" -c Release", bossmodCheckout, BuildTimeoutMs);
            if (build.ExitCode != 0)
            {
                var mapped = CompileErrorMapper.Map(build.Output, em);
                if (mapped.IsEmpty)
                {
                    mapped =
                    [
                        new CompileErrorItem("", 0, "BUILD", "dotnet build failed with no mappable compile errors", null),
                    ];
                }

                return new BossmodBuildResult(false, mapped, null);
            }

            var hostDir = ExtractValidatorHost(bossmodCheckout);
            var moduleTypeName = $"{scratchNamespace}.{moduleName}.{moduleName}";
            var hostArgs = $"run --project \"{hostDir}\" -c Release -- \"{moduleTypeName}\""
                + string.Concat(recordings.Select(r => $" \"{r}\""));
            var host = RunProcess("dotnet", hostArgs, bossmodCheckout, HostTimeoutMs);
            if (host.ExitCode != 0)
            {
                var detail = host.Output.Trim().Split('\n').LastOrDefault()?.Trim() ?? $"exit code {host.ExitCode}";
                return new BossmodBuildResult(
                    false,
                    [new CompileErrorItem("", 0, "FORGE-HOST", $"ReplayValidator host failed: {detail}", null)],
                    host.Output);
            }

            return new BossmodBuildResult(true, [], host.Output);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            return new BossmodBuildResult(
                false,
                [new CompileErrorItem("", 0, "FORGE", ex.Message, null)],
                null);
        }
    }

    private static string? FindSolutionFile(string checkout)
        => Directory.GetFiles(checkout, "*.sln")
            .Concat(Directory.GetFiles(checkout, "*.slnx"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();

    private static string ExtractValidatorHost(string checkout)
    {
        var hostDir = Path.Combine(checkout, "ForgeReplayValidator");
        Directory.CreateDirectory(hostDir);
        ExtractResource("BossModForge.Cli.ReplayValidator.ForgeReplayValidator.csproj", Path.Combine(hostDir, "ForgeReplayValidator.csproj"));
        ExtractResource("BossModForge.Cli.ReplayValidator.Program.cs", Path.Combine(hostDir, "Program.cs"));
        return hostDir;
    }

    private static void ExtractResource(string name, string dest)
    {
        var assembly = typeof(BossmodBuildValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing embedded resource '{name}'");
        using var reader = new StreamReader(stream);
        File.WriteAllText(dest, reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private static ProcessResult RunProcess(string file, string args, string workingDirectory, int timeoutMs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start '{file}'");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"'{file}' timed out after {timeoutMs / 1000}s");
        }

        return new ProcessResult(process.ExitCode, stdout.Result + "\n" + stderr.Result);
    }
}
