using System.Diagnostics;
using System.Globalization;
using BossModForge.Core.Replay;
using Xunit;

namespace BossModForge.Tests.Replay;

public sealed class StreamingPerfTests
{
    private const int MoveActors = 8;

    [Fact]
    public void LargeFile_ParsesWithinBudget()
    {
        if (Environment.GetEnvironmentVariable("FORGE_PERF") != "1")
            return; // skip silently: env-gated so CI stays fast

        var path = Path.Combine(Path.GetTempPath(), $"forge-perf-{Guid.NewGuid():N}.log");
        try
        {
            BuildLargeLog(path, targetMb: 200);
            var before = Process.GetCurrentProcess().PeakWorkingSet64;
            var sw = Stopwatch.StartNew();
            var rec = ReplayParser.Parse(path);
            sw.Stop();
            var peakDelta = Process.GetCurrentProcess().PeakWorkingSet64 - before;

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"parse took {sw.Elapsed}");
            Assert.True(peakDelta < 1L << 30, $"peak working-set delta {peakDelta / (1L << 20)} MB exceeds 1 GB");
            Assert.False(rec.Degraded);
            Assert.NotEmpty(rec.Ops);
            Assert.True(rec.Ops.Count > 1_000_000, $"expected >1M ops, got {rec.Ops.Count}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Emits a text log directly with a StreamWriter (RecordingBuilder would be too slow at
    // this scale). Grammar per docs/UPSTREAM.md §3 and TextRecordingWriter:
    //   timestamp|VER |24|10000000|2026.08.20.0000.0000
    //   timestamp|FRAM|<prevMs:f3>|<gaugeLow:X16>|<gaugeHigh:X16>|<qpc>|<index>|<dur:g9>|<dur:g9>|<tick:g9>|<azimuth:f3>
    //   timestamp|MOVE|<instanceId:X8>|<x:f3>/<y:f3>/<z:f3>|<rot:f3>
    private static void BuildLargeLog(string path, int targetMb)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file, System.Text.Encoding.UTF8);

        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var now = start;
        ulong qpc = 0;
        uint index = 0;

        void AdvanceFrame()
        {
            now = now.AddMilliseconds(17);
            qpc += 170_000; // 17 ms at QPF 10_000_000
            index++;
        }

        var version = string.Create(CultureInfo.InvariantCulture, $"{start:O}|VER |24|10000000|2026.08.20.0000.0000");
        writer.WriteLine(version);

        var targetBytes = (long)targetMb << 20;
        while (true)
        {
            AdvanceFrame();
            var ts = now.ToString("O", CultureInfo.InvariantCulture);
            writer.Write(ts);
            writer.Write("|FRAM|17.000|0000000000000000|0000000000000000|");
            writer.Write(qpc.ToString(CultureInfo.InvariantCulture));
            writer.Write('|');
            writer.Write(index.ToString(CultureInfo.InvariantCulture));
            writer.Write("|0.016666668|0.016666668|1|0.000");

            var posX = 100 + (index % 100) * 0.013f;
            var pos = string.Create(CultureInfo.InvariantCulture, $"{posX:f3}/0.000/100.000");
            for (var a = 0; a < MoveActors; a++)
            {
                writer.WriteLine();
                writer.Write(ts);
                writer.Write("|MOVE|");
                writer.Write((0x100u + a).ToString("X8", CultureInfo.InvariantCulture));
                writer.Write('|');
                writer.Write(pos);
                writer.Write("|0.000");
            }
            writer.WriteLine();

            if ((index & 0x3FF) == 0)
            {
                writer.Flush();
                if (file.Length >= targetBytes)
                    break;
            }
        }
    }
}
