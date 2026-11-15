using System.Collections.Immutable;
using BossModForge.Core.Emit;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Model;

public sealed class OverridesApplierTests
{
    [Fact]
    public void ShapeOverride_ChangesEmAndCSharp()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Cone(40, 45));
        var applied = Apply(em, OverrideFixtures.Shape(40001, Shape.Circle(12)));
        var ability = Assert.Single(applied.Abilities);
        Assert.Equal(Shape.Circle(12), ability.Shape);
        Assert.Null(ability.ShapeFit);
        Assert.Equal(ConfidenceBand.High, ability.Confidence.Band);
        Assert.Contains("user override: shape", ability.Confidence.Reasons);
        Assert.Contains("AOEShapeCircle(12)", EmitCs(applied));
    }

    [Fact]
    public void Reanalyze_PreservesOverrides()
    {
        var inferred = WithAoe(HandBuiltEncounter.Create(), Shape.Cone(40, 45));
        var overrides = OverrideFixtures.Shape(40001, Shape.Circle(12));
        var first = OverridesApplier.Apply(inferred with { Overrides = overrides });
        var inferredV2 = inferred with
        {
            Abilities = [inferred.Abilities[0] with { Shape = Shape.Rect(20, 4), ShapeFit = new ShapeFitSummary(0.4, 1, 0, 0, 1) }],
        };
        var second = OverridesApplier.Apply(inferredV2 with { Overrides = overrides });
        Assert.Equal(Shape.Circle(12), Assert.Single(first.Abilities).Shape);
        Assert.Equal(Shape.Circle(12), Assert.Single(second.Abilities).Shape);
        Assert.Null(second.Abilities[0].ShapeFit);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Cone(40, 45));
        var once = Apply(em, OverrideFixtures.Shape(40001, Shape.Circle(12)));
        var twice = OverridesApplier.Apply(once);
        Assert.True(ReferenceEquals(once, twice));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void ClassOverride()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Circle(8));
        var applied = Apply(em, OverrideFixtures.Class(40001, MechanicClass.Raidwide));
        Assert.Equal(MechanicClass.Raidwide, Assert.Single(applied.Abilities).Class);
        Assert.Contains("user override: class", applied.Abilities[0].Confidence.Reasons);
        Assert.Contains("RaidwideCast", EmitCs(applied));
    }

    [Fact]
    public void EnumNameOverride_UpdatesEnumsAndComponents()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Circle(8));
        var applied = Apply(em, OverrideFixtures.EnumName(40001, "big circle"));
        Assert.Equal("BigCircle", applied.Enums.Aid[40001]);
        Assert.Equal("BigCircle", Assert.Single(applied.Abilities).EnumName);
        var cs = EmitCs(applied);
        Assert.Contains("AID.BigCircle", cs, StringComparison.Ordinal);
        Assert.Contains("class BigCircle", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumNameCollision_ReviewNotApplied()
    {
        var em = TwoAbilities();
        var applied = Apply(em, OverrideFixtures.EnumName(40001, "Other"));
        Assert.Equal("HeavySlash", applied.Enums.Aid[40001]);
        Assert.Equal("Other", applied.Enums.Aid[40002]);
        Assert.Contains(applied.Review, r => r.Severity == ReviewSeverity.High && r.Subject == "override");
    }

    [Fact]
    public void Ignore_RemovesComponentKeepsEnumAndTimeline()
    {
        using var stream = SyntheticShapes.Build().Open(ReplayEncoding.Text);
        var rec = ForgePipeline.Parse(stream, "SyntheticShapes.log");
        var result = ForgePipeline.Analyze(
            rec,
            NullGameData.Instance,
            new PipelineOptions(Segmenter: SyntheticShapes.PullOptions, Build: new BuildOptions(ModuleName: "SyntheticShapes")));
        var aid = SyntheticShapes.ConeAid;
        var enumName = result.Inferred.Enums.Aid[aid];
        var applied = ForgePipeline.WithOverrides(result, OverrideFixtures.Ignore(aid)).Effective;
        Assert.Equal(enumName, applied.Enums.Aid[aid]);
        Assert.Equal(MechanicClass.Unclassified, applied.Abilities.First(a => a.ActionId == aid).Class);
        Assert.Null(applied.Abilities.First(a => a.ActionId == aid).Shape);
        Assert.NotNull(applied.Timeline);
        Assert.Contains(applied.Timeline!.Steps, s => s.Aid == aid);
        Assert.DoesNotContain("class " + enumName + "(", EmitCs(applied), StringComparison.Ordinal);
    }

    [Fact]
    public void ParticipantEnumName()
    {
        var em = HandBuiltEncounter.Create();
        var applied = Apply(em, OverrideFixtures.ParticipantEnumName(0x233C, "arena helper"));
        Assert.Equal("ArenaHelper", applied.Enums.Oid[0x233C]);
        Assert.Equal("ArenaHelper", applied.Participants.Single(p => p.Oid == 0x233C).EnumName);
        Assert.Equal("Boss", applied.Enums.Oid[0x4321]);
    }

    [Fact]
    public void BossRename_Rejected()
    {
        var em = HandBuiltEncounter.Create();
        var applied = Apply(em, OverrideFixtures.ParticipantEnumName(0x4321, "NotBoss"));
        Assert.Equal("Boss", applied.Enums.Oid[0x4321]);
        Assert.Equal("Boss", applied.Participants.Single(p => p.Oid == 0x4321).EnumName);
        Assert.Contains(applied.Review, r => r.Severity == ReviewSeverity.High && r.Subject == "override");
    }

    [Fact]
    public void ModuleNameAndNamespace()
    {
        var em = HandBuiltEncounter.Create();
        var overrides = Overrides.Empty
            .With(OverridesApplier.ModuleNameKey, OverridesApplier.ToElement("custom module"))
            .With(OverridesApplier.NamespaceKey, OverridesApplier.ToElement(" BossMod.Custom "));
        var applied = Apply(em, overrides);
        Assert.Equal("CustomModule", applied.Encounter.ModuleName);
        Assert.Equal("BossMod.Custom", applied.Encounter.Namespace);
    }

    [Fact]
    public void ArenaOverride_EmitsNewBounds()
    {
        var em = HandBuiltEncounter.Create();
        var arena = new ArenaModel("Square", 100, 100, 20, 20, 20, [], Confidence.Of(0.4), []);
        var applied = Apply(em, Overrides.Empty.With(OverridesApplier.ArenaKey, OverridesApplier.ToElement(arena)));
        Assert.Equal("Square", applied.Arena.Kind);
        Assert.Contains("user override: arena", applied.Arena.Confidence.Reasons);
        Assert.Equal("Override", Assert.Single(applied.Arena.Evidence).Kind);
        Assert.Contains("new ArenaBoundsSquare(20)", EmitCs(applied), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownKey_LowReview()
    {
        var em = HandBuiltEncounter.Create();
        var applied = Apply(em, Overrides.Empty.With("/nope", OverridesApplier.ToElement(true)));
        var item = Assert.Single(applied.Review, r => r.Subject == "override");
        Assert.Equal(ReviewSeverity.Low, item.Severity);
        Assert.Contains("/nope", item.Question, StringComparison.Ordinal);
        Assert.Equal(em.Abilities, applied.Abilities);
    }

    [Fact]
    public void MalformedValue_HighReview_NoThrow()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Circle(8));
        var applied = Apply(em, Overrides.Empty.With(OverridesApplier.AbilityKey(40001, "shape"), OverridesApplier.ToElement("nope")));
        Assert.Equal(Shape.Circle(8), Assert.Single(applied.Abilities).Shape);
        Assert.Contains(applied.Review, r => r.Severity == ReviewSeverity.High && r.Subject == "override");
    }

    [Fact]
    public void NullShape_ClearsShape()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Circle(8));
        var applied = Apply(em, OverrideFixtures.Shape(40001, null));
        var ability = Assert.Single(applied.Abilities);
        Assert.Null(ability.Shape);
        Assert.Null(ability.ShapeFit);
        Assert.Contains("user override: shape", ability.Confidence.Reasons);
    }

    [Fact]
    public void JsonRoundTrip_KeepsOverrides()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Cone(40, 45));
        var applied = Apply(em, OverrideFixtures.Shape(40001, Shape.Circle(12)));
        var json = EncounterModelJson.Serialize(applied);
        Assert.Empty(EncounterModelJson.Validate(json));
        var back = EncounterModelJson.Deserialize(json);
        Assert.Equal(applied.Overrides.Values.Keys, back.Overrides.Values.Keys);
        Assert.Equal(Shape.Circle(12), Assert.Single(back.Abilities).Shape);
        var reapplied = OverridesApplier.Apply(back);
        Assert.Equal(EncounterModelJson.Serialize(applied), EncounterModelJson.Serialize(reapplied));
    }

    [Fact]
    public void Determinism_TwoRunsByteIdentical()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Cone(40, 45));
        var overrides = OverrideFixtures.Of(
            (OverridesApplier.AbilityKey(40001, "shape"), OverridesApplier.ToElement(Shape.Circle(12))),
            (OverridesApplier.ModuleNameKey, OverridesApplier.ToElement("Alt")));
        var a = EncounterModelJson.Serialize(Apply(em, overrides));
        var b = EncounterModelJson.Serialize(Apply(em, overrides));
        Assert.Equal(a, b);
    }

    private static EncounterModel Apply(EncounterModel em, Overrides overrides)
        => OverridesApplier.Apply(em with { Overrides = overrides });

    private static EncounterModel WithAoe(EncounterModel em, Shape shape)
    {
        var ability = em.Abilities[0] with
        {
            Class = MechanicClass.TelegraphedAOE,
            Shape = shape,
            ShapeFit = new ShapeFitSummary(0.8, 1, 1, 0, 3),
        };
        return em with { Abilities = [ability] };
    }

    private static EncounterModel TwoAbilities()
    {
        var em = WithAoe(HandBuiltEncounter.Create(), Shape.Circle(8));
        var other = em.Abilities[0] with { ActionId = 40002, EnumName = "Other", Name = "Other" };
        var aid = em.Enums.Aid.Add(40002, "Other");
        return em with { Abilities = [em.Abilities[0], other], Enums = em.Enums with { Aid = aid } };
    }

    private static string EmitCs(EncounterModel em)
    {
        var result = ForgePipeline.Emit(em, Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        return result.Files[0].Content;
    }
}
