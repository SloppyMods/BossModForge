using BossModForge.Cli.Commands;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Cli;

public sealed class MergeCommandTests
{
    [Fact]
    public void Merge_TwoLogs_WritesSchemaValidEmWithTwoSourcesAndThreePulls()
    {
        var a = new SyntheticSinglePull().WriteTemp(ReplayEncoding.Text);
        var b = new SyntheticTwoPulls().WriteTemp(ReplayEncoding.Text);
        var outPath = TempPath("em");
        try
        {
            var err = new StringWriter();
            var exit = MergeCommand.Run([a, b], outPath, null, null, new StringWriter(), err);
            Assert.True(exit is 0 or 2, err.ToString());

            var json = File.ReadAllText(outPath);
            Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
            Assert.Empty(EncounterModelJson.Validate(json));
            var em = EncounterModelJson.Deserialize(json);
            Assert.Equal(2, em.Sources.Length);
            Assert.Equal(3, em.Pulls.Length);
            Assert.Equal(SyntheticMerge.BossOid, em.Encounter.PrimaryOid);
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Merge_CarriesOverridesFromExistingEm()
    {
        var a = new SyntheticSinglePull().WriteTemp(ReplayEncoding.Text);
        var b = new SyntheticTwoPulls().WriteTemp(ReplayEncoding.Text);
        var firstPath = TempPath("em");
        var emPath = TempPath("em");
        var outPath = TempPath("em");
        try
        {
            var err = new StringWriter();
            var firstExit = MergeCommand.Run([a, b], firstPath, null, null, new StringWriter(), err);
            Assert.True(firstExit is 0 or 2, err.ToString());

            var first = EncounterModelJson.Deserialize(File.ReadAllText(firstPath));
            Assert.NotEqual("MergedModule", first.Encounter.ModuleName);
            var withOverride = first with
            {
                Overrides = first.Overrides.With(OverridesApplier.ModuleNameKey, OverridesApplier.ToElement("MergedModule")),
            };
            File.WriteAllText(emPath, EncounterModelJson.Serialize(withOverride));

            var exit = MergeCommand.Run([a, b], outPath, emPath, null, new StringWriter(), err);
            Assert.True(exit is 0 or 2, err.ToString());
            var em = EncounterModelJson.Deserialize(File.ReadAllText(outPath));
            Assert.Equal("MergedModule", em.Encounter.ModuleName);
            Assert.False(em.Overrides.IsEmpty);
            Assert.True(em.Overrides.Values.ContainsKey(OverridesApplier.ModuleNameKey));
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
            File.Delete(firstPath);
            File.Delete(emPath);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Merge_IsDeterministic_TwoRunsByteIdentical()
    {
        var a = new SyntheticSinglePull().WriteTemp(ReplayEncoding.Text);
        var b = new SyntheticTwoPulls().WriteTemp(ReplayEncoding.Text);
        var out1 = TempPath("em");
        var out2 = TempPath("em");
        try
        {
            var err = new StringWriter();
            var exit1 = MergeCommand.Run([a, b], out1, null, null, new StringWriter(), err);
            var exit2 = MergeCommand.Run([a, b], out2, null, null, new StringWriter(), err);
            Assert.True(exit1 is 0 or 2, err.ToString());
            Assert.True(exit2 is 0 or 2, err.ToString());
            Assert.Equal(File.ReadAllText(out1), File.ReadAllText(out2));
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
            File.Delete(out1);
            File.Delete(out2);
        }
    }

    [Fact]
    public void Merge_SelectBoss_PicksRequestedPrimaryOid()
    {
        var log = new SyntheticDoorBoss().WriteTemp(ReplayEncoding.Text);
        var defaultOut = TempPath("em");
        var selectedOut = TempPath("em");
        try
        {
            var err = new StringWriter();
            var defaultExit = MergeCommand.Run([log], defaultOut, null, null, new StringWriter(), err);
            Assert.True(defaultExit is 0 or 2, err.ToString());
            var em = EncounterModelJson.Deserialize(File.ReadAllText(defaultOut));
            Assert.Equal(SyntheticDoorBoss.BossAOid, em.Encounter.PrimaryOid);
            Assert.Equal(2, em.Pulls.Length);

            var exit = MergeCommand.Run([log], selectedOut, null, SyntheticDoorBoss.BossBOid, new StringWriter(), err);
            Assert.True(exit is 0 or 2, err.ToString());
            var selected = EncounterModelJson.Deserialize(File.ReadAllText(selectedOut));
            Assert.Equal(SyntheticDoorBoss.BossBOid, selected.Encounter.PrimaryOid);
            Assert.Single(selected.Pulls);
        }
        finally
        {
            File.Delete(log);
            File.Delete(defaultOut);
            File.Delete(selectedOut);
        }
    }

    [Fact]
    public void Merge_MissingLog_Exit4()
    {
        var outPath = TempPath("em");
        try
        {
            var err = new StringWriter();
            var exit = MergeCommand.Run(["/nonexistent/forge-missing.log"], outPath, null, null, new StringWriter(), err);
            Assert.Equal(4, exit);
            Assert.Contains("error:", err.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    private static string TempPath(string stem)
        => Path.Combine(Path.GetTempPath(), $"forge-merge-{stem}-{Guid.NewGuid():N}.json");
}
