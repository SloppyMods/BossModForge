using BossModForge.Core.Inference;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class OmenParserTests
{
    [Fact]
    public void TryParseFan_Corpus()
    {
        foreach (var (path, expected) in OmenCorpus.FanRows)
        {
            var ok = OmenParser.TryParseFan(path, out var half);
            if (expected is { } h)
            {
                Assert.True(ok);
                Assert.Equal(h, half);
            }
            else
            {
                Assert.False(ok);
            }
        }
    }

    [Fact]
    public void TryParseDonut_AlwaysFalseUntilVerified()
    {
        foreach (var (path, _) in OmenCorpus.FanRows)
            Assert.False(OmenParser.TryParseDonut(path, out _));
        Assert.False(OmenParser.TryParseDonut("sircle_0500", out _));
    }
}
