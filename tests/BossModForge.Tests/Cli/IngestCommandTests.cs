using BossModForge.Cli.Commands;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Cli;

public sealed class IngestCommandTests
{
    [Fact]
    public void Ingest_TextRawAndBrotli_PrintIdenticalOpAndTagTables()
    {
        var builder = SyntheticDungeon.Build();
        var textPath = builder.WriteTemp(ReplayEncoding.Text);
        var rawPath = builder.WriteTemp(ReplayEncoding.Binary);
        var brotliPath = builder.WriteTemp(ReplayEncoding.BinaryBrotli);
        try
        {
            var text = Capture(textPath, json: false);
            var raw = Capture(rawPath, json: false);
            var brotli = Capture(brotliPath, json: false);

            Assert.Equal(0, text.Exit);
            Assert.Equal(0, raw.Exit);
            Assert.Equal(0, brotli.Exit);
            Assert.Equal(TagTable(raw.Stdout), TagTable(brotli.Stdout));
            Assert.Equal(OpCount(raw.Stdout) + 1, OpCount(text.Stdout)); // text-only Raw("ZZZZ")
            Assert.Equal(TagTable(raw.Stdout), TagTable(text.Stdout, dropTag: "ZZZZ"));
            Assert.Contains("encoding: text", text.Stdout, StringComparison.Ordinal);
            Assert.Contains("encoding: binary   ", raw.Stdout, StringComparison.Ordinal);
            Assert.Contains("encoding: binary+brotli", brotli.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(textPath);
            File.Delete(rawPath);
            File.Delete(brotliPath);
        }
    }

    [Fact]
    public void Ingest_Json_EmitsSortedKeysAndEncodingName()
    {
        var path = SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text);
        try
        {
            var result = Capture(path, json: true);
            Assert.Equal(0, result.Exit);
            Assert.StartsWith("{\"path\":", result.Stdout.Trim(), StringComparison.Ordinal);
            Assert.Contains("\"encoding\":\"text\"", result.Stdout, StringComparison.Ordinal);
            Assert.Contains("\"tagCounts\":", result.Stdout, StringComparison.Ordinal);
            Assert.Contains("\"issues\":", result.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Ingest_MissingFile_Exit2AndContinues()
    {
        var missing = Path.Combine(Path.GetTempPath(), "forge-missing-" + Guid.NewGuid().ToString("N") + ".log");
        var good = SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text);
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = IngestCommand.Run([missing, good], json: false, stdout, stderr);
            Assert.Equal(2, exit);
            Assert.Contains("error:", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("ops:", stdout.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(good);
        }
    }

    private static (int Exit, string Stdout) Capture(string path, bool json)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = IngestCommand.Run([path], json, stdout, stderr);
        return (exit, stdout.ToString());
    }

    private static int OpCount(string output)
    {
        var line = output.Split('\n').First(l => l.Contains("ops:", StringComparison.Ordinal));
        var marker = "ops: ";
        var start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = line.IndexOf(' ', start);
        return int.Parse(line[start..(end < 0 ? line.Length : end)]);
    }

    private static string TagTable(string output, string? dropTag = null)
    {
        var lines = output.Split('\n')
            .SkipWhile(l => !l.Contains("tag    count", StringComparison.Ordinal))
            .Skip(1)
            .Where(l => l.StartsWith("  ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(l));
        if (dropTag is not null)
            lines = lines.Where(l => !l.TrimStart().StartsWith(dropTag, StringComparison.Ordinal));
        return string.Join('\n', lines);
    }
}
