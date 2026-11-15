using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Emit;

public sealed class ModuleEmitterVbmTests
{
    [Fact]
    public void Emit_HandBuilt_ContainsTrivialPhaseAndModuleInfo()
    {
        var em = HandBuiltEncounter.Create();
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var result = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm));
        var cs = Assert.Single(result.Files).Content;
        Assert.Contains("namespace BossMod.Forge.TestBoss;", cs, StringComparison.Ordinal);
        Assert.Contains("TrivialPhase();", cs, StringComparison.Ordinal);
        Assert.Contains("[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 826, NameID = 12716)]", cs, StringComparison.Ordinal);
        Assert.Contains("public class TestBoss(WorldState ws, Actor primary) : BossModule", cs, StringComparison.Ordinal);
        Assert.Contains("new ArenaBoundsCircle(20)", cs, StringComparison.Ordinal);
        Assert.Contains("Boss = 0x4321,", cs, StringComparison.Ordinal);
        Assert.Contains("Helper = 0x233C,", cs, StringComparison.Ordinal);
        Assert.Contains("// TODO(forge:E1) Set Expansion/Category", cs, StringComparison.Ordinal);
        Assert.Contains("public enum AID : uint", cs, StringComparison.Ordinal);
        Assert.Contains("public enum SID : uint", cs, StringComparison.Ordinal);
        Assert.Contains("public enum IconID : uint", cs, StringComparison.Ordinal);
        Assert.Contains("public enum TetherID : uint", cs, StringComparison.Ordinal);
        Assert.Contains(result.Review, r => r.Id == "E1" && r.Question.Contains("Expansion/Category", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Review, r => r.Severity == ReviewSeverity.High);
    }

    [Fact]
    public void Emit_Bmr_Verified_OmitsHighFlavorReview()
    {
        var em = HandBuiltEncounter.Create();
        var profile = ModuleEmitter.ProfileFor(Flavor.Bmr, ModuleEmitter.ResolveFlavorsDirectory());
        Assert.True(profile.Profile.Verified);
        var result = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Bmr));
        Assert.DoesNotContain(
            result.Review,
            r => r.Severity == ReviewSeverity.High && r.Question.Contains("unverified", StringComparison.Ordinal));
        Assert.Contains("TrivialPhase();", result.Files[0].Content, StringComparison.Ordinal);
    }
}
