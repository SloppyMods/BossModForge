using BossModForge.Cli.Commands;
using BossModForge.Cli.Conformance;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Cli;

public sealed class ConformTests
{
    [Fact]
    public void Compare_FindsDiffs()
    {
        var forge = new Dictionary<string, int>(StringComparer.Ordinal) { ["CST+"] = 3, ["CST!"] = 2 };
        var bossMod = new Dictionary<string, int>(StringComparer.Ordinal) { ["CST+"] = 4, ["CST!"] = 2, ["MOVE"] = 1 };

        var diffs = ConformComparer.Compare(forge, bossMod);

        Assert.Equal(
            [new TagCountDiff("CST+", 3, 4), new TagCountDiff("MOVE", 0, 1)],
            diffs.ToArray());
    }

    [Fact]
    public void Compare_Identical_Empty()
    {
        var forge = new Dictionary<string, int>(StringComparer.Ordinal) { ["CST+"] = 3, ["FRAM"] = 10 };
        var bossMod = new Dictionary<string, int>(StringComparer.Ordinal) { ["FRAM"] = 10, ["CST+"] = 3 };

        Assert.Empty(ConformComparer.Compare(forge, bossMod));
    }

    [Fact]
    public void Run_WithoutDll_Exit4()
    {
        var log = SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text);
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ConformCommand.Run([log], bossModDllPath: null, json: false, selftest: false, stdout, stderr);
            Assert.Equal(4, exit);
            Assert.Contains("--bossmod-dll", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(log);
        }
    }

    [Fact]
    public void Run_BadDll_Exit4()
    {
        var log = SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text);
        var badDll = Path.Combine(Path.GetTempPath(), "forge-no-such-" + Guid.NewGuid().ToString("N"), "BossMod.dll");
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ConformCommand.Run([log], badDll, json: false, selftest: false, stdout, stderr);
            Assert.Equal(4, exit);
            Assert.Contains("error:", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(log);
        }
    }

    [Fact]
    public void Run_MissingLog_Exit4()
    {
        var missing = Path.Combine(Path.GetTempPath(), "forge-missing-" + Guid.NewGuid().ToString("N") + ".log");
        var thisAssembly = typeof(ConformTests).Assembly.Location; // loads fine, but the log fails first
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = ConformCommand.Run([missing], thisAssembly, json: false, selftest: false, stdout, stderr);
        Assert.Equal(4, exit);
        Assert.Contains("error:", stderr.ToString(), StringComparison.Ordinal);
    }
}
