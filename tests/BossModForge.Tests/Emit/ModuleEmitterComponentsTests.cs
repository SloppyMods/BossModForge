using System.Collections.Immutable;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Emit;

public sealed class ModuleEmitterComponentsTests
{
    [Fact]
    public void Emit_GroupsAndActivateOnEnterChain()
    {
        var em = ComponentsEncounter();
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var cs = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
        Assert.Contains("class Raidwide(BossModule module) : Components.RaidwideCast(module, AID.Raidwide);", cs, StringComparison.Ordinal);
        Assert.Contains("class AAndB(BossModule module) : Components.GroupedAOEs(module, [AID.A, AID.B], new AOEShapeCircle(6));", cs, StringComparison.Ordinal);
        Assert.Contains("class C(BossModule module) : Components.StandardAOEs(module, AID.C, new AOEShapeCone(40, 45.Degrees()));", cs, StringComparison.Ordinal);
        Assert.Contains(
            "        TrivialPhase()\n            .ActivateOnEnter<Raidwide>()\n            .ActivateOnEnter<AAndB>()\n            .ActivateOnEnter<C>();",
            cs,
            StringComparison.Ordinal);
        Assert.DoesNotContain("class Unclassified", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_NewMechanicComponents()
    {
        var em = HandBuiltEncounter.Create();
        var high = Confidence.Of(0.9);
        var abilities = ImmutableArray.Create(
            new AbilityModel(0x4321, 10, "Spell", "Buster", "Buster", true, 3, false, 1, MechanicClass.Tankbuster, Shape.SingleTarget, null, high, []),
            new AbilityModel(0x4321, 11, "Spell", "Spread", "Spread", true, 2, false, 4, MechanicClass.Spread, Shape.Circle(6), null, high, []),
            new AbilityModel(0x4321, 12, "Spell", "Stack", "Stack", true, 2, false, 3, MechanicClass.Stack, Shape.Circle(8), null, high, []),
            new AbilityModel(0x4321, 13, "Spell", "Knock", "Knock", true, 2, false, 3, MechanicClass.Knockback, null, null, high, [new Evidence(MechanicClassifier.ParamsKind, "distance=15")]),
            new AbilityModel(0x4321, 14, "Spell", "Gaze", "Gaze", true, 3, false, 3, MechanicClass.Gaze, null, null, high, []),
            new AbilityModel(0x4321, 15, "Spell", "Chase", "Chase", true, 1, false, 5, MechanicClass.Chasing, Shape.Circle(6), null, high, []));
        var aid = ImmutableSortedDictionary<uint, string>.Empty
            .Add(10, "Buster").Add(11, "Spread").Add(12, "Stack").Add(13, "Knock").Add(14, "Gaze").Add(15, "Chase");
        em = em with { Abilities = abilities, Enums = em.Enums with { Aid = aid } };
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var cs = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
        Assert.Contains("class Buster(BossModule module) : Components.SingleTargetCast(module, AID.Buster);", cs, StringComparison.Ordinal);
        Assert.Contains("class Spread(BossModule module) : Components.SpreadFromCastTargets(module, AID.Spread, 6);", cs, StringComparison.Ordinal);
        Assert.Contains("class Stack(BossModule module) : Components.StackWithCastTargets(module, AID.Stack, 8);", cs, StringComparison.Ordinal);
        Assert.Contains("class Knock(BossModule module) : Components.KnockbackFromCastTarget(module, AID.Knock, 15);", cs, StringComparison.Ordinal);
        Assert.Contains("class Gaze(BossModule module) : Components.CastGaze(module, AID.Gaze);", cs, StringComparison.Ordinal);
        Assert.Contains("// TODO(forge) Chasing: Chase — no standard component", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateOnEnter<Chase>", cs, StringComparison.Ordinal);
        Assert.Contains(
            "        TrivialPhase()\n            .ActivateOnEnter<Buster>()\n            .ActivateOnEnter<Spread>()\n            .ActivateOnEnter<Stack>()\n            .ActivateOnEnter<Knock>()\n            .ActivateOnEnter<Gaze>();",
            cs,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_SquareRectCustomBounds()
    {
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var square = ModuleEmitter.Emit(WithArena(new ArenaModel("Square", 0, 0, 20, 20, 20, [], Confidence.Of(0.9), [])), profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
        Assert.Contains("new ArenaBoundsSquare(20)", square, StringComparison.Ordinal);
        var rect = ModuleEmitter.Emit(WithArena(new ArenaModel("Rect", 0, 0, 25, 20, 15, [], Confidence.Of(0.7), [])), profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
        Assert.Contains("new ArenaBoundsRect(20, 15)", rect, StringComparison.Ordinal);
        var custom = ModuleEmitter.Emit(
            WithArena(new ArenaModel("Custom", 0, 0, 20, 0, 0, [new PointModel(1, 2), new PointModel(3, 4), new PointModel(0, 5)], Confidence.LowBecause("custom polygon"), [])),
            profile,
            new EmitOptions(Flavor.Vbm));
        Assert.Contains("new ArenaBoundsCustom([new(1, 2), new(3, 4), new(0, 5)])", custom.Files[0].Content, StringComparison.Ordinal);
        Assert.Contains(custom.Review, r => r.Severity == ReviewSeverity.High && r.Question.Contains("ArenaBoundsCustom", StringComparison.Ordinal));
    }

    [Fact]
    public void Emit_CastLocationCircle_UsesLocationTargetedAOEs()
    {
        var baseEm = HandBuiltEncounter.Create();
        var high = Confidence.Of(0.9);
        var ground = Shape.Circle(8, ShapeOrigin.CastLocation);
        var abilities = ImmutableArray.Create(
            new AbilityModel(0x4321, 20, "Spell", "Ground", "Ground", true, 2, false, 3, MechanicClass.TelegraphedAOE, ground, new ShapeFitSummary(0.9, 1, 2, 0, 3), high, []));
        var aid = ImmutableSortedDictionary<uint, string>.Empty.Add(20, "Ground");
        var em = baseEm with { Abilities = abilities, Enums = baseEm.Enums with { Aid = aid } };
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var cs = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
        Assert.Contains("class Ground(BossModule module) : Components.LocationTargetedAOEs(module, AID.Ground, 8);", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("StandardAOEs", cs, StringComparison.Ordinal);
    }

    private static EncounterModel ComponentsEncounter()
    {
        var baseEm = HandBuiltEncounter.Create();
        var high = Confidence.Of(0.9);
        var circle = Shape.Circle(6);
        var cone = Shape.Cone(40, 45);
        var raid = Shape.Raidwide;
        var abilities = ImmutableArray.Create(
            new AbilityModel(0x4321, 1, "Spell", "Raidwide", "Raidwide", true, 3, false, 3, MechanicClass.Raidwide, raid, new ShapeFitSummary(0.9, 1, 9, 0, 3), high, []),
            new AbilityModel(0x4321, 2, "Spell", "A", "A", true, 2, false, 3, MechanicClass.TelegraphedAOE, circle, new ShapeFitSummary(0.9, 1, 2, 0, 3), high, []),
            new AbilityModel(0x4321, 3, "Spell", "B", "B", true, 2, false, 3, MechanicClass.TelegraphedAOE, circle, new ShapeFitSummary(0.9, 1, 2, 0, 3), high, []),
            new AbilityModel(0x4321, 4, "Spell", "C", "C", true, 4, false, 3, MechanicClass.TelegraphedAOE, cone, new ShapeFitSummary(0.9, 1, 4, 0, 3), high, []),
            new AbilityModel(0x4321, 5, "Spell", "SkipMe", "Skip", true, 1, false, 1, MechanicClass.Unclassified, null, null, high, []));
        var aid = ImmutableSortedDictionary<uint, string>.Empty
            .Add(1, "Raidwide").Add(2, "A").Add(3, "B").Add(4, "C").Add(5, "SkipMe");
        return baseEm with
        {
            Abilities = abilities,
            Enums = baseEm.Enums with { Aid = aid },
        };
    }

    private static EncounterModel WithArena(ArenaModel arena) => HandBuiltEncounter.Create() with { Arena = arena };
}
