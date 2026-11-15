using System.Collections.Immutable;
using BossModForge.Core.Emit;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class MechanicClassifierTests
{
    [Fact]
    public void Raidwide_IsNotTankbuster()
    {
        var (em, cs) = Run(MechanicFixtures.Raidwide());
        var model = em.Abilities.Single(a => a.ActionId == MechanicFixtures.RaidwideAid);
        Assert.Equal(MechanicClass.Raidwide, model.Class);
        Assert.Contains(model.Evidence, e => e.Kind == MechanicClassifier.RuleKind && e.Summary == "RW-1");
        Assert.NotEqual(MechanicClass.Tankbuster, model.Class);
        Assert.Contains("Components.RaidwideCast(module, AID.Ability42001)", cs, StringComparison.Ordinal);
        Assert.Contains(".ActivateOnEnter<Ability42001>()", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Tankbuster_EmitsSingleTargetCast()
    {
        var (em, cs) = Run(MechanicFixtures.Tankbuster(), catalog => Plant(catalog, MechanicFixtures.TankbusterAid, 1));
        var model = em.Abilities.Single(a => a.ActionId == MechanicFixtures.TankbusterAid);
        Assert.Equal(MechanicClass.Tankbuster, model.Class);
        Assert.Contains(model.Evidence, e => e.Kind == MechanicClassifier.RuleKind && e.Summary == "TB-1");
        Assert.Contains("Components.SingleTargetCast(module, AID.Ability42002)", cs, StringComparison.Ordinal);
        Assert.Contains(".ActivateOnEnter<Ability42002>()", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void TelegraphedAOE_EmitsStandardAOEs()
    {
        var (em, cs) = Run(MechanicFixtures.TelegraphedCone(), catalog => Plant(catalog, MechanicFixtures.ConeAid, 3, "gl_fan090_1bf"));
        var model = em.Abilities.Single(a => a.ActionId == MechanicFixtures.ConeAid);
        Assert.Equal(MechanicClass.TelegraphedAOE, model.Class);
        Assert.Contains(model.Evidence, e => e.Kind == MechanicClassifier.RuleKind && e.Summary == "TA-1");
        Assert.Contains("Components.StandardAOEs(module, AID.Ability42003", cs, StringComparison.Ordinal);
        Assert.Contains(".ActivateOnEnter<Ability42003>()", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Knockback_EmitsKnockbackFromCastTarget()
    {
        var (em, cs) = Run(MechanicFixtures.Knockback());
        var model = em.Abilities.Single(a => a.ActionId == MechanicFixtures.KnockbackAid);
        Assert.Equal(MechanicClass.Knockback, model.Class);
        Assert.Contains(model.Evidence, e => e.Kind == MechanicClassifier.RuleKind && e.Summary == "KB-1");
        Assert.Contains("Components.KnockbackFromCastTarget(module, AID.Ability42004, 15)", cs, StringComparison.Ordinal);
        Assert.Contains(".ActivateOnEnter<Ability42004>()", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Gaze_EmitsCastGaze()
    {
        var (em, cs) = Run(MechanicFixtures.Gaze());
        var model = em.Abilities.Single(a => a.ActionId == MechanicFixtures.GazeAid);
        Assert.Equal(MechanicClass.Gaze, model.Class);
        Assert.Contains(model.Evidence, e => e.Kind == MechanicClassifier.RuleKind && e.Summary == "GZ-1");
        Assert.Contains("Components.CastGaze(module, AID.Ability42005)", cs, StringComparison.Ordinal);
        Assert.Contains(".ActivateOnEnter<Ability42005>()", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Spread_EmitsSpreadFromCastTargets()
    {
        var (em, cs) = Run(MechanicFixtures.Spread());
        var model = em.Abilities.Single(a => a.ActionId == MechanicFixtures.SpreadAid);
        Assert.Equal(MechanicClass.Spread, model.Class);
        Assert.Contains("Components.SpreadFromCastTargets(module, AID.Ability42006", cs, StringComparison.Ordinal);
        Assert.Contains(".ActivateOnEnter<Ability42006>()", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Stack_EmitsStackWithCastTargets()
    {
        var (em, cs) = Run(MechanicFixtures.Stack(singleHit: false));
        var model = em.Abilities.Single(a => a.ActionId == MechanicFixtures.StackAid);
        Assert.Equal(MechanicClass.Stack, model.Class);
        Assert.Contains("Components.StackWithCastTargets(module, AID.Ability42007", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Stack_SingleHit_IsLow()
    {
        var (em, _) = Run(MechanicFixtures.Stack(singleHit: true));
        var model = em.Abilities.Single(a => a.ActionId == MechanicFixtures.StackAid);
        Assert.Equal(MechanicClass.Stack, model.Class);
        Assert.Equal(ConfidenceBand.Low, model.Confidence.Band);
    }

    private static (EncounterModel Em, string Cs) Run(RecordingBuilder builder, Func<AbilityCatalog, AbilityCatalog>? tweak = null)
    {
        using var stream = builder.Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, "mech.log");
        var pulls = PullSegmenter.Segment(rec, MechanicFixtures.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, NullGameData.Instance);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, NullGameData.Instance);
        if (tweak is not null)
            catalog = tweak(catalog);
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, NullGameData.Instance);
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        var cs = ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
        return (em, cs);
    }

    private static AbilityCatalog Plant(AbilityCatalog catalog, uint aid, byte castType, string? omen = null)
        => catalog with
        {
            Abilities = catalog.Abilities.Select(a =>
                a.Action.Id == aid
                    ? a with { Sheet = new ActionRow(a.Action.Id, a.Name, castType, 40, 0, 20, omen is null ? 0u : 12u, omen) }
                    : a).ToImmutableArray(),
        };
}
