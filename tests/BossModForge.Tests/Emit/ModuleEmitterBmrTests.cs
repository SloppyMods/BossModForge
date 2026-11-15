using System.Collections.Immutable;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Emit;

public sealed class ModuleEmitterBmrTests
{
    [Fact]
    public void BmrEmittedCode_UsesBmrApi()
    {
        var profile = ModuleEmitter.ProfileFor(Flavor.Bmr, ModuleEmitter.ResolveFlavorsDirectory());
        Assert.True(profile.Profile.Verified);
        Assert.Equal("b919e0970d6559de280c40defb4117dd7275c961", profile.Profile.Commit);
        var result = ModuleEmitter.Emit(BmrEncounter(), profile, new EmitOptions(Flavor.Bmr));
        var all = string.Join("\n", result.Files.Select(f => f.Content));
        Assert.Contains("Components.SimpleAOEs(module, (uint)AID.", all, StringComparison.Ordinal);
        Assert.Contains("Components.RaidwideCast(module, (uint)AID.", all, StringComparison.Ordinal);
        Assert.Contains("Components.SingleTargetCast(module, (uint)AID.", all, StringComparison.Ordinal);
        Assert.Contains("Components.CastGaze(module, (uint)AID.", all, StringComparison.Ordinal);
        Assert.Contains("Components.SimpleKnockbacks(module, (uint)AID.", all, StringComparison.Ordinal);
        Assert.Contains("Components.SimpleAOEs(module, (uint)AID.Ground, 8)", all, StringComparison.Ordinal);
        Assert.DoesNotContain("StandardAOEs", all, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupedAOEs", all, StringComparison.Ordinal);
        Assert.DoesNotContain("LocationTargetedAOEs", all, StringComparison.Ordinal);
        Assert.DoesNotContain("KnockbackFromCastTarget", all, StringComparison.Ordinal);
        Assert.Contains("Cast(id, AID.HeavySlash, 5.1f, 5, \"Opener\")", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Cast(id, (uint)", all, StringComparison.Ordinal);
    }

    [Fact]
    public void BmrAndVbm_DifferOnlyInComponentCtorLines()
    {
        var em = BmrEncounter();
        var dir = ModuleEmitter.ResolveFlavorsDirectory();
        var vbm = ModuleEmitter.Emit(em, ModuleEmitter.ProfileFor(Flavor.Vbm, dir), new EmitOptions(Flavor.Vbm)).Files[0].Content;
        var bmr = ModuleEmitter.Emit(em, ModuleEmitter.ProfileFor(Flavor.Bmr, dir), new EmitOptions(Flavor.Bmr)).Files[0].Content;

        var vbmLines = vbm.Split('\n');
        var bmrLines = bmr.Split('\n');
        Assert.Equal(vbmLines.Length, bmrLines.Length);
        var diffCount = 0;
        for (var i = 0; i < vbmLines.Length; i++)
        {
            if (vbmLines[i] == bmrLines[i])
                continue;
            diffCount++;
            Assert.StartsWith("class ", vbmLines[i], StringComparison.Ordinal);
            Assert.StartsWith("class ", bmrLines[i], StringComparison.Ordinal);
            Assert.Contains("(BossModule module) : Components.", vbmLines[i], StringComparison.Ordinal);
            Assert.Contains("(BossModule module) : Components.", bmrLines[i], StringComparison.Ordinal);
        }

        Assert.Equal(6, diffCount);
        Assert.Contains("class Circle(BossModule module) : Components.StandardAOEs(module, AID.Circle, new AOEShapeCircle(6));", vbm, StringComparison.Ordinal);
        Assert.Contains("class Circle(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Circle, new AOEShapeCircle(6));", bmr, StringComparison.Ordinal);
        Assert.Contains("class Ground(BossModule module) : Components.LocationTargetedAOEs(module, AID.Ground, 8);", vbm, StringComparison.Ordinal);
        Assert.Contains("class Ground(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Ground, 8);", bmr, StringComparison.Ordinal);
        Assert.Contains("class Knock(BossModule module) : Components.KnockbackFromCastTarget(module, AID.Knock, 15);", vbm, StringComparison.Ordinal);
        Assert.Contains("class Knock(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.Knock, 15);", bmr, StringComparison.Ordinal);
        Assert.Contains("class Raidwide(BossModule module) : Components.RaidwideCast(module, AID.Raidwide);", vbm, StringComparison.Ordinal);
        Assert.Contains("class Raidwide(BossModule module) : Components.RaidwideCast(module, (uint)AID.Raidwide);", bmr, StringComparison.Ordinal);
    }

    private static EncounterModel BmrEncounter()
    {
        var baseEm = HandBuiltEncounter.Create();
        var high = Confidence.Of(0.9);
        var fit = new ShapeFitSummary(0.9, 1, 2, 0, 3);
        var abilities = ImmutableArray.Create(
            new AbilityModel(0x4321, 1, "Spell", "Raidwide", "Raidwide", true, 3, false, 3, MechanicClass.Raidwide, Shape.Raidwide, fit, high, []),
            new AbilityModel(0x4321, 2, "Spell", "Circle", "Circle", true, 2, false, 3, MechanicClass.TelegraphedAOE, Shape.Circle(6), fit, high, []),
            new AbilityModel(0x4321, 3, "Spell", "Ground", "Ground", true, 2, false, 3, MechanicClass.TelegraphedAOE, Shape.Circle(8, ShapeOrigin.CastLocation), fit, high, []),
            new AbilityModel(0x4321, 4, "Spell", "Buster", "Buster", true, 3, false, 1, MechanicClass.Tankbuster, Shape.SingleTarget, null, high, []),
            new AbilityModel(0x4321, 13, "Spell", "Knock", "Knock", true, 2, false, 3, MechanicClass.Knockback, null, null, high, [new Evidence(MechanicClassifier.ParamsKind, "distance=15")]),
            new AbilityModel(0x4321, 14, "Spell", "Gaze", "Gaze", true, 3, false, 3, MechanicClass.Gaze, null, null, high, []),
            new AbilityModel(0x4321, 40001, "Spell", "HeavySlash", "Heavy Slash", true, 5f, false, 3, MechanicClass.Unclassified, null, null, high, []));
        var aid = ImmutableSortedDictionary<uint, string>.Empty
            .Add(1, "Raidwide").Add(2, "Circle").Add(3, "Ground").Add(4, "Buster")
            .Add(13, "Knock").Add(14, "Gaze").Add(40001, "HeavySlash");
        var phases = ImmutableArray.Create(new PhaseModel(0, "Phase0", "Death", null, 0, 1, []));
        var steps = ImmutableArray.Create(
            new TimelineStep("0x00000000", "Cast", 40001, 5.1f, 5, 0, 3, [40001u], "Opener", [], [], null, null));
        return baseEm with
        {
            Abilities = abilities,
            Enums = baseEm.Enums with { Aid = aid },
            Timeline = new TimelineModel(phases, steps, null, 3, Confidence.Of(1), []),
        };
    }
}
