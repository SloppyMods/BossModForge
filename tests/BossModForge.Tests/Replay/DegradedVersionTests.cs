using System.Numerics;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Replay;

public sealed class DegradedVersionTests
{
    [Fact]
    public void NewerVersion_ParsesLenientDegraded()
    {
        // A version above OpReader.MaxSupportedVersion (31) is parsed with the newest known gates and flagged Degraded.
        // (Was version 25 while MaxSupportedVersion was 24.)
        var b = new RecordingBuilder(new DateTime(2026, 9, 19), version: 32)
            .Zone(1180, 826)
            .Player(1, "P", Class.PLD, new Vector3(0, 0, 0))
            .Advance(TimeSpan.FromSeconds(2));
        var rec = ReplayParser.Parse(b.WriteTemp(ReplayEncoding.Text));
        Assert.True(rec.Degraded);
        Assert.Contains(rec.Issues, i => i.Severity == IssueSeverity.Degraded && i.Code == "UnsupportedVersion");
        Assert.NotEmpty(rec.Ops); // v24 layouts still recovered
    }

    [Fact]
    public void Version24_Unchanged() // golden guard: existing fixtures parse with zero Degraded issues
    {
        var rec = ReplayParser.Parse(SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text));
        Assert.False(rec.Degraded);
    }
}
