using System.Diagnostics;
using System.Text;
using ECommons.DalamudServices;
using BossModForge.Core.Emit;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Validation;

namespace BossModForge.Plugin.Services;

public sealed class Exporter
{
    private readonly Configuration _config;
    private readonly ForgeSession _session;
    private int _running;

    public Exporter(Configuration config, ForgeSession session)
    {
        _config = config;
        _session = session;
    }

    public bool IsBusy => Volatile.Read(ref _running) != 0;

    public string FlavorsDir
    {
        get
        {
            // VERIFY-11: IDalamudPluginInterface.AssemblyLocation.DirectoryName
            var dir = Svc.PluginInterface.AssemblyLocation.DirectoryName;
            return Path.Combine(dir ?? ".", "flavors");
        }
    }

    public string OutputRoot
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_config.OutputDirectory))
                return _config.OutputDirectory;
            // VERIFY-11: ConfigDirectory as output fallback
            return Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "output");
        }
    }

    public string ModuleOutputDir(EncounterModel em)
        => Path.Combine(OutputRoot, em.Encounter.ModuleName);

    public bool FlavorsPresent() => File.Exists(Path.Combine(FlavorsDir, "vbm.json"));

    public void WriteEm(EncounterModel em)
    {
        Run("Write EM", () =>
        {
            var dir = EnsureModuleDir(em);
            var json = EncounterModelJson.Serialize(em);
            var issues = EncounterModelJson.Validate(json);
            var path = Path.Combine(dir, em.Encounter.ModuleName + ".em.json");
            File.WriteAllText(path, json);
            _session.SetLastOutputDir(dir);
            if (issues.Length > 0)
                _session.Message($"Wrote {path} with {issues.Length} schema issue(s)");
            else
                _session.Message($"Wrote {path}");
        });
    }

    public void EmitCsharp(EncounterModel em, Flavor flavor, EmitOptions options)
    {
        Run("Emit C#", () =>
        {
            if (!FlavorsPresent())
            {
                _session.Message("flavors/vbm.json missing next to the plugin DLL");
                return;
            }

            var dir = EnsureModuleDir(em);
            var result = ForgePipeline.Emit(em, flavor, FlavorsDir, options);
            Directory.CreateDirectory(dir);
            foreach (var file in result.Files)
                File.WriteAllText(Path.Combine(dir, file.RelativePath), file.Content);
            File.WriteAllText(Path.Combine(dir, "FORGE.md"), result.ForgeMd);
            _session.SetLastEmit(result);
            _session.SetLastOutputDir(dir);
            _session.Message($"Emitted {result.Files.Length} file(s) + FORGE.md ({result.Review.Length} review items)");
            if (_config.OpenFolderAfterExport)
                OpenFolder(dir);
        });
    }

    public void Validate(EncounterModel em, AnalysisResult result)
    {
        Run("Validate", () =>
        {
            var report = DryValidator.Validate(em, [(result.Recording.Meta.Path, result.Recording)]);
            _session.SetLastValidation(report);
            _session.Message(report.Passed ? "Validate passed" : $"Validate failed ({report.Failures.Length} failure(s))");
        });
    }

    public void WriteReport(ValidationReport report, string outputDir)
    {
        Run("Write report", () =>
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "validation-report.json"), ValidationReportWriter.ToJson(report));
            File.WriteAllText(Path.Combine(outputDir, "validation-report.md"), ValidationReportWriter.ToMarkdown(report));
            _session.SetLastOutputDir(outputDir);
            _session.Message("Wrote validation-report.json/md");
        });
    }

    public static string ConcatenateCsharp(EmitResult emit)
    {
        var sb = new StringBuilder();
        foreach (var file in emit.Files)
        {
            if (!file.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine($"// {file.RelativePath}");
            sb.AppendLine(file.Content);
        }

        return sb.ToString();
    }

    public static void OpenFolder(string dir)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private string EnsureModuleDir(EncounterModel em)
    {
        var dir = ModuleOutputDir(em);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void Run(string label, Action action)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            _session.Message($"{label} already running");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, $"BossModForge export {label} failed");
                _session.Message($"{label} failed: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        });
    }
}
