using System.Collections.Immutable;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using Xunit;

namespace BossModForge.Tests.GameData;

public sealed class CachedGameDataTests
{
    [Fact]
    public void Empty_CountIsZero_LookupsMissLikeNullGameData()
    {
        Assert.Equal(0, CachedGameData.Empty.Count);
        Assert.Null(CachedGameData.Empty.Action(1));
        Assert.Null(NullGameData.Instance.Action(1));
        Assert.Null(CachedGameData.Empty.BNpcName(1));
        Assert.Null(NullGameData.Instance.BNpcName(1));
        Assert.Null(CachedGameData.Empty.StatusName(1));
        Assert.Null(NullGameData.Instance.StatusName(1));
        Assert.Null(CachedGameData.Empty.ContentForTerritory(1));
        Assert.Null(NullGameData.Instance.ContentForTerritory(1));
        Assert.Null(CachedGameData.Empty.PlaceName(1));
        Assert.Null(NullGameData.Instance.PlaceName(1));
    }

    [Fact]
    public void Lookups_ReturnPlantedRows()
    {
        var data = new CachedGameData(
            ImmutableSortedDictionary<uint, ActionRow>.Empty.Add(7, new ActionRow(7, "Slash", 2, 8, 0, 10, 0)),
            ImmutableSortedDictionary<uint, string>.Empty.Add(12716, "Test Boss"),
            ImmutableSortedDictionary<uint, string>.Empty.Add(9, "Bleed"),
            ImmutableSortedDictionary<uint, (uint CfcId, string Name)>.Empty.Add(1234, (826, "Duty")),
            ImmutableSortedDictionary<uint, string>.Empty.Add(1234, "Place"));
        Assert.Equal(5, data.Count);
        Assert.Equal("Slash", data.Action(7)!.Name);
        Assert.Null(data.Action(8));
        Assert.Equal("Test Boss", data.BNpcName(12716));
        Assert.Equal("Bleed", data.StatusName(9));
        Assert.Equal((826u, "Duty"), data.ContentForTerritory(1234));
        Assert.Equal("Place", data.PlaceName(1234));
        Assert.Null(data.PlaceName(99));
    }

    [Fact]
    public void CastType3_OmenFan_YieldsConePrior()
    {
        var row = new ActionRow(1, "Fan", 3, 40, 0, 50, 12, "gl_fan090_1bf");
        var data = new CachedGameData(
            ImmutableSortedDictionary<uint, ActionRow>.Empty.Add(1, row),
            ImmutableSortedDictionary<uint, string>.Empty,
            ImmutableSortedDictionary<uint, string>.Empty,
            ImmutableSortedDictionary<uint, (uint CfcId, string Name)>.Empty,
            ImmutableSortedDictionary<uint, string>.Empty);
        var prior = ShapeInference.PriorFor(data.Action(1));
        Assert.NotNull(prior);
        Assert.Equal(ShapeKind.Cone, prior!.Kind);
        Assert.Equal(45f, prior.HalfAngleDeg);
        Assert.True(prior.FromOmen);
    }
}
