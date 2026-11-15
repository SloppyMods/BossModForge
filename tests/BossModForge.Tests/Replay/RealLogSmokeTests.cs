using System.Linq;
using BossModForge.Core.Replay;
using Xunit;

namespace BossModForge.Tests.Replay;

public sealed class RealLogSmokeTests
{
    [Fact]
    public void ParseRealLogs_WhenConfigured()
    {
        var spec = Environment.GetEnvironmentVariable("FORGE_REAL_LOGS");
        if (string.IsNullOrWhiteSpace(spec))
            return;

        var files = Resolve(spec);
        Assert.NotEmpty(files);

        foreach (var path in files)
        {
            Recording recording;
            var ex = Record.Exception(() => recording = ReplayParser.Parse(path));
            Assert.True(ex is null, $"{path}: {ex}");
            recording = ReplayParser.Parse(path);
            if (recording.Meta.Version >= 23 && recording.Ops.Count > 0)
            {
                var raw = recording.Ops.Count(op => op is RawOp);
                Assert.True(
                    raw < 0.01 * recording.Ops.Count,
                    $"{path}: RawOp fraction {raw}/{recording.Ops.Count} >= 0.01");
            }
        }
    }

    private static List<string> Resolve(string spec)
    {
        var files = new List<string>();
        foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Directory.Exists(part))
                files.AddRange(Directory.GetFiles(part, "*.log", SearchOption.AllDirectories));
            else if (File.Exists(part))
                files.Add(part);
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
