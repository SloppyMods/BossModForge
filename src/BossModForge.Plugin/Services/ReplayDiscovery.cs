using ECommons.DalamudServices;

namespace BossModForge.Plugin.Services;

public sealed record ReplayFile(string Path, long SizeBytes, DateTime LastWriteUtc);

public sealed class ReplayDiscovery
{
    private readonly Configuration _config;

    public ReplayDiscovery(Configuration config) => _config = config;

    public IReadOnlyList<string> Directories
    {
        get
        {
            if (_config.ReplayDirectories.Count > 0)
                return _config.ReplayDirectories;
            return DefaultDirectories();
        }
    }

    public static IReadOnlyList<string> DefaultDirectories()
    {
        var parent = ConfigParent();
        if (parent is null)
            return [];

        // VERIFY-7: BossMod / BossModReborn replay folder names next to the plugin config directory.
        return
        [
            Path.Combine(parent, "BossMod", "replays"),
            Path.Combine(parent, "BossModReborn", "replays"),
        ];
    }

    public IReadOnlyList<ReplayFile> Scan()
    {
        var files = new List<ReplayFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directories)
        {
            if (!Directory.Exists(dir))
                continue;
            AddLogs(files, seen, dir);
            foreach (var sub in Directory.EnumerateDirectories(dir))
                AddLogs(files, seen, sub);
        }

        files.Sort(static (a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
        return files;
    }

    private static void AddLogs(List<ReplayFile> files, HashSet<string> seen, string dir)
    {
        // VERIFY-8: BossMod replay files are top-level *.log (World_yyyyMMdd_HHmmss.log and encoding variants).
        IEnumerable<string> logs;
        try
        {
            logs = Directory.EnumerateFiles(dir, "*.log", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, $"Replay scan failed in {dir}");
            return;
        }

        foreach (var path in logs)
        {
            if (!seen.Add(path))
                continue;
            try
            {
                var info = new FileInfo(path);
                files.Add(new ReplayFile(path, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, $"Replay file stat failed: {path}");
            }
        }
    }

    private static string? ConfigParent()
    {
        // VERIFY-11: IDalamudPluginInterface.ConfigDirectory is DirectoryInfo with Parent.
        var dir = Svc.PluginInterface.ConfigDirectory;
        return dir.Parent?.FullName;
    }
}
