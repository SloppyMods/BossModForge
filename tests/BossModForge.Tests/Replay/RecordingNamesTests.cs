using BossModForge.Core.Replay;
using Xunit;

namespace BossModForge.Tests.Replay;

public sealed class RecordingNamesTests
{
    [Theory]
    [InlineData("TheClyteum_MCH100_2026_10_17_20_01_02.log", "TheClyteum")]
    [InlineData("Aegeiros-Garlemald_BRD100_2026_10_17_13_51_11.log", "Aegeiros-Garlemald")]
    [InlineData("WorqorZormor_2026_10_01_21_27_53.log", "WorqorZormor")]
    [InlineData("HellsKier_BLM70_Ma_Ga_2026_03_27_23_33_12.log", "HellsKier")]
    [InlineData("notarecording.log", null)]
    public void ZoneOf_reads_zone(string file, string? zone) => Assert.Equal(zone, RecordingNames.ZoneOf(file));

    [Fact]
    public void CountFor_matches_display_name_ignoring_spaces_and_case()
    {
        string[] files =
        [
            "/r/TheClyteum_MCH100_2026_10_17_20_01_02.log",
            "/r/TheClyteum_WAR100_2026_10_18_20_01_02.log",
            "/r/Mistwake_BRD100_2026_10_18_20_01_02.log",
            "/r/TheFieldsOfGloryShatter_DNC100_2026_10_17_19_30_43.log",
        ];
        Assert.Equal(2, RecordingNames.CountFor(files, "The Clyteum"));
        Assert.Equal(1, RecordingNames.CountFor(files, "The Fields of Glory (Shatter)"));
        Assert.Equal(0, RecordingNames.CountFor(files, "Kugane Castle"));
    }
}
