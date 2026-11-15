using BossModForge.Cli.Commands;
using BossModForge.Cli.GameData;
using BossModForge.Core.GameData;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Cli;

public sealed class InferCommandGameDataTests
{
    [Fact]
    public void LuminaGameData_BadPath_DegradesGracefully()
    {
        var gd = new LuminaGameData(Path.Combine(Path.GetTempPath(), "forge-noinstall-" + Guid.NewGuid().ToString("N")));
        Assert.False(gd.Available);
        Assert.Null(gd.Action(SyntheticDungeon.HeavySlash));
        Assert.Null(gd.BNpcName(12716));
        Assert.Null(gd.StatusName(1234));
        Assert.Null(gd.ContentForTerritory(1234));
        Assert.Null(gd.PlaceName(1234));
    }

    [Fact]
    public void Infer_WithBadGamePath_StillRuns()
    {
        InferenceStages.SkipIfUnimplemented();
        var log = SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text);
        var emNull = Path.Combine(Path.GetTempPath(), "forge-em-null-" + Guid.NewGuid().ToString("N") + ".json");
        var emBad = Path.Combine(Path.GetTempPath(), "forge-em-bad-" + Guid.NewGuid().ToString("N") + ".json");
        var badPath = Path.Combine(Path.GetTempPath(), "forge-noinstall-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nullErr = new StringWriter();
            var nullExit = InferCommand.Run(log, emNull, null, null, null, new StringWriter(), nullErr);
            Assert.True(nullExit is 0 or 2, nullErr.ToString());

            var badErr = new StringWriter();
            var badExit = InferCommand.Run(log, emBad, null, null, badPath, new StringWriter(), badErr);
            Assert.True(badExit is 0 or 2, badErr.ToString());
            Assert.Contains("warning", badErr.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(File.ReadAllText(emNull), File.ReadAllText(emBad));
        }
        finally
        {
            File.Delete(log);
            File.Delete(emNull);
            File.Delete(emBad);
        }
    }

    [Fact]
    public void FakeGameData_ChangesNames()
    {
        InferenceStages.SkipIfUnimplemented();
        var log = SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text);
        var emNull = Path.Combine(Path.GetTempPath(), "forge-em-null-" + Guid.NewGuid().ToString("N") + ".json");
        var emFake = Path.Combine(Path.GetTempPath(), "forge-em-fake-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var nullErr = new StringWriter();
            var nullExit = InferCommand.Run(log, emNull, null, null, null, new StringWriter(), nullErr);
            Assert.True(nullExit is 0 or 2, nullErr.ToString());

            var fakeErr = new StringWriter();
            var fakeExit = InferCommand.Run(log, emFake, null, null, null, new StringWriter(), fakeErr, new FakeGameData());
            Assert.True(fakeExit is 0 or 2, fakeErr.ToString());
            var fake = File.ReadAllText(emFake);
            Assert.Contains("Heavy Slash", fake, StringComparison.Ordinal);
            Assert.Contains("HeavySlash", fake, StringComparison.Ordinal);
            Assert.Contains("Drowsie", fake, StringComparison.Ordinal);
            Assert.NotEqual(File.ReadAllText(emNull), fake);
        }
        finally
        {
            File.Delete(log);
            File.Delete(emNull);
            File.Delete(emFake);
        }
    }

    private sealed class FakeGameData : IGameData
    {
        public ActionRow? Action(uint id) =>
            id == SyntheticDungeon.HeavySlash
                ? new ActionRow(id, "Heavy Slash", 3, 40, 0, 50, 0, "gl_fan090_1bf")
                : null;

        public string? BNpcName(uint nameId) => nameId == 12716 ? "Drowsie" : null;

        public string? StatusName(uint id) => null;

        public (uint CfcId, string Name)? ContentForTerritory(uint territoryType) => null;

        public string? PlaceName(uint territoryType) => null;
    }
}
