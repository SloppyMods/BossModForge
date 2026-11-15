using BossModForge.Cli.Commands;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Cli;

public sealed class InferEmitCommandTests
{
    [Fact]
    public void Infer_Synthetic_WritesSchemaVersion1_EmitContainsTrivialPhase_BmrExit0()
    {
        InferenceStages.SkipIfUnimplemented();
        var log = SyntheticDungeon.Build().WriteTemp(ReplayEncoding.Text);
        var emPath = Path.Combine(Path.GetTempPath(), "forge-em-" + Guid.NewGuid().ToString("N") + ".json");
        var vbmDir = Directory.CreateTempSubdirectory("forge-vbm-").FullName;
        var bmrDir = Directory.CreateTempSubdirectory("forge-bmr-").FullName;
        try
        {
            var inferOut = new StringWriter();
            var inferErr = new StringWriter();
            var inferExit = InferCommand.Run(log, emPath, null, null, null, inferOut, inferErr);
            Assert.True(inferExit is 0 or 2, inferErr.ToString());
            var json = File.ReadAllText(emPath);
            Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);

            var emitOut = new StringWriter();
            var emitErr = new StringWriter();
            var emitExit = EmitCommand.Run(emPath, "vbm", vbmDir, null, emitOut, emitErr);
            Assert.True(emitExit is 0 or 2, emitErr.ToString());
            var csPath = Directory.GetFiles(vbmDir, "*.cs").Single();
            Assert.Contains("DeathPhase(", File.ReadAllText(csPath), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(vbmDir, "FORGE.md")));

            var bmrExit = EmitCommand.Run(emPath, "bmr", bmrDir, null, emitOut, emitErr);
            Assert.Equal(0, bmrExit);
            var bmrCsPath = Directory.GetFiles(bmrDir, "*.cs").Single();
            var bmrCs = File.ReadAllText(bmrCsPath);
            Assert.Contains("DeathPhase(", bmrCs, StringComparison.Ordinal);
            Assert.DoesNotContain("StandardAOEs", bmrCs, StringComparison.Ordinal);
            Assert.DoesNotContain("GroupedAOEs", bmrCs, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(bmrDir, "FORGE.md")));
        }
        finally
        {
            File.Delete(log);
            File.Delete(emPath);
            Directory.Delete(vbmDir, recursive: true);
            Directory.Delete(bmrDir, recursive: true);
        }
    }
}
