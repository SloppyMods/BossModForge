using BossModForge.Cli.Commands;
using BossModForge.Cli.Validation;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Validation;

public sealed class ValidateCommandBossmodTests
{
    [Fact]
    public void Bossmod_NonexistentDir_Exit4_MentionsOption()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, _) = ValidationFixtures.Rotation();
        var log = SyntheticRotation.Build().WriteTemp(ReplayEncoding.Text);
        var emPath = Path.Combine(Path.GetTempPath(), "forge-val-em-" + Guid.NewGuid().ToString("N") + ".json");
        var outDir = Directory.CreateTempSubdirectory("forge-val-out-").FullName;
        var missing = Path.Combine(Path.GetTempPath(), "forge-no-such-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(emPath, EncounterModelJson.Serialize(em));
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ValidateCommand.Run(emPath, [log], outDir, writeEm: false, bossmodPath: missing, stdout, stderr, ValidationFixtures.RotationOptions);
            Assert.Equal(4, exit);
            Assert.Contains("--bossmod", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(log);
            File.Delete(emPath);
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Bossmod_EmptyDir_Exit4_CheckoutMessage()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, _) = ValidationFixtures.Rotation();
        var log = SyntheticRotation.Build().WriteTemp(ReplayEncoding.Text);
        var emPath = Path.Combine(Path.GetTempPath(), "forge-val-em-" + Guid.NewGuid().ToString("N") + ".json");
        var outDir = Directory.CreateTempSubdirectory("forge-val-out-").FullName;
        var empty = Directory.CreateTempSubdirectory("forge-val-empty-checkout-").FullName;
        File.WriteAllText(emPath, EncounterModelJson.Serialize(em));
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = ValidateCommand.Run(emPath, [log], outDir, writeEm: false, bossmodPath: empty, stdout, stderr, ValidationFixtures.RotationOptions);
            Assert.Equal(4, exit);
            Assert.Contains("does not look like a BossMod checkout", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(log);
            File.Delete(emPath);
            Directory.Delete(outDir, recursive: true);
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void Run_InvalidCheckout_Throws()
    {
        var empty = Directory.CreateTempSubdirectory("forge-val-empty-checkout-").FullName;
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => BossmodBuildValidator.Run(
                    moduleDir: empty,
                    bossmodCheckout: empty,
                    em: HandBuiltEncounter.Create(),
                    recordings: []));
            Assert.Contains("does not look like a BossMod checkout", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void Run_BrokenSolution_BuildFailsGracefully()
    {
        // A checkout-shaped directory whose solution cannot build: dotnet build fails,
        // the failure is captured and returned — never thrown. (No BossMod checkout
        // exists on this machine; see docs/UPSTREAM.md §11.)
        var checkout = Directory.CreateTempSubdirectory("forge-val-checkout-").FullName;
        var moduleDir = Directory.CreateTempSubdirectory("forge-val-module-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(checkout, "BossMod"));
            File.WriteAllText(Path.Combine(checkout, "BossMod.sln"), string.Empty);
            File.WriteAllText(Path.Combine(moduleDir, "TestBoss.cs"), "// module\n");

            var result = BossmodBuildValidator.Run(moduleDir, checkout, HandBuiltEncounter.Create(), []);

            Assert.False(result.BuildSucceeded);
            Assert.True(result.Errors.Length > 0, "expected a synthesized BUILD failure item");
            Assert.Null(result.ValidatorHostOutput);
            // module files were staged into the scratch namespace before the build
            Assert.True(File.Exists(Path.Combine(checkout, "BossMod", "Modules", "ForgeScratch", "TestBoss", "TestBoss.cs")));
        }
        finally
        {
            Directory.Delete(checkout, recursive: true);
            Directory.Delete(moduleDir, recursive: true);
        }
    }
}
