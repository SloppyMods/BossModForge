using BossModForge.Cli.Commands;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Core.Validation;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Validation;

public sealed class ValidateCommandTests
{
    [Fact]
    public void Pass_WritesReportFiles_Exit0()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, _) = ValidationFixtures.Rotation();
        var log = SyntheticRotation.Build().WriteTemp(ReplayEncoding.Text);
        var emPath = Path.Combine(Path.GetTempPath(), "forge-val-em-" + Guid.NewGuid().ToString("N") + ".json");
        var outDir = Directory.CreateTempSubdirectory("forge-val-out-").FullName;
        File.WriteAllText(emPath, EncounterModelJson.Serialize(em));
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ValidateCommand.Run(emPath, [log], outDir, writeEm: false, bossmodPath: null, stdout, stderr, ValidationFixtures.RotationOptions);
            Assert.True(exit == 0, stderr.ToString() + stdout.ToString());
            Assert.True(File.Exists(Path.Combine(outDir, "validation-report.json")));
            Assert.True(File.Exists(Path.Combine(outDir, "validation-report.md")));
            Assert.Contains("# Validation report", stdout.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(log);
            File.Delete(emPath);
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void WrongRadius_Exit3()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, _) = ValidationFixtures.Rotation();
        var broken = ValidationFixtures.WithCircleRadius(em, 3);
        var log = SyntheticRotation.Build().WriteTemp(ReplayEncoding.Text);
        var emPath = Path.Combine(Path.GetTempPath(), "forge-val-em-" + Guid.NewGuid().ToString("N") + ".json");
        var outDir = Directory.CreateTempSubdirectory("forge-val-out-").FullName;
        File.WriteAllText(emPath, EncounterModelJson.Serialize(broken));
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ValidateCommand.Run(emPath, [log], outDir, writeEm: false, bossmodPath: null, stdout, stderr, ValidationFixtures.RotationOptions);
            Assert.Equal(3, exit);
            Assert.True(File.Exists(Path.Combine(outDir, "validation-report.json")));
        }
        finally
        {
            File.Delete(log);
            File.Delete(emPath);
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Bossmod_Exit4_WritesNothing()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var outDir = Directory.CreateTempSubdirectory("forge-val-bossmod-").FullName;
        try
        {
            var exit = ValidateCommand.Run("missing.em.json", ["missing.log"], outDir, writeEm: false, bossmodPath: "x", stdout, stderr);
            Assert.Equal(4, exit);
            Assert.Contains("--bossmod", stderr.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(outDir, "validation-report.json")));
            Assert.False(File.Exists(Path.Combine(outDir, "validation-report.md")));
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void MissingEm_Exit4()
    {
        var missing = Path.Combine(Path.GetTempPath(), "forge-missing-" + Guid.NewGuid().ToString("N") + ".json");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = ValidateCommand.Run(missing, ["no.log"], null, false, null, stdout, stderr);
        Assert.Equal(4, exit);
        Assert.Contains("error:", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnparsableLog_Exit4()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, _) = ValidationFixtures.Rotation();
        var emPath = Path.Combine(Path.GetTempPath(), "forge-val-em-" + Guid.NewGuid().ToString("N") + ".json");
        var log = Path.Combine(Path.GetTempPath(), "forge-val-missing-" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(emPath, EncounterModelJson.Serialize(em));
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ValidateCommand.Run(emPath, [log], null, false, null, stdout, stderr, ValidationFixtures.RotationOptions);
            Assert.Equal(4, exit);
            Assert.Contains("error:", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(emPath);
        }
    }

    [Fact]
    public void WriteEm_RewritesValidEncounterModel()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, _) = ValidationFixtures.Rotation();
        var broken = ValidationFixtures.WithCircleRadius(em, 3);
        var log = SyntheticRotation.Build().WriteTemp(ReplayEncoding.Text);
        var emPath = Path.Combine(Path.GetTempPath(), "forge-val-em-" + Guid.NewGuid().ToString("N") + ".json");
        var outDir = Directory.CreateTempSubdirectory("forge-val-out-").FullName;
        File.WriteAllText(emPath, EncounterModelJson.Serialize(broken));
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ValidateCommand.Run(emPath, [log], outDir, writeEm: true, bossmodPath: null, stdout, stderr, ValidationFixtures.RotationOptions);
            Assert.Equal(3, exit);
            var json = File.ReadAllText(emPath);
            Assert.Empty(EncounterModelJson.Validate(json));
            var round = EncounterModelJson.Deserialize(json);
            var ability = Assert.Single(round.Abilities, a => a.ActionId == SyntheticRotation.Circle);
            Assert.Equal(ConfidenceBand.Low, ability.Confidence.Band);
            Assert.Contains(round.Review, r => r.Subject == "shape");
        }
        finally
        {
            File.Delete(log);
            File.Delete(emPath);
            Directory.Delete(outDir, recursive: true);
        }
    }
}
