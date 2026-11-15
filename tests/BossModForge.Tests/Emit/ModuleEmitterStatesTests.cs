using System.Collections.Immutable;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Emit;

public sealed class ModuleEmitterStatesTests
{
    [Fact]
    public void ZeroSteps_TrivialPhaseUnchanged()
    {
        var cs = Emit(HandBuiltEncounter.Create());
        Assert.Contains("TrivialPhase();", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("DeathPhase", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("(uint)AID.", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void CastActivateDeactivate()
    {
        var em = WithTimeline(
            [new PhaseModel(0, "Phase0", "Death", null, 0, 1, [])],
            [Step("0x00000000", "Cast", 40001, 5.1f, 5, "Opener", ["Raidwide"], ["Raidwide"], null, null)]);
        var cs = Emit(em);
        Assert.Contains("DeathPhase(0, Phase0)", cs, StringComparison.Ordinal);
        Assert.Contains("Cast(id, AID.HeavySlash, 5.1f, 5, \"Opener\")", cs, StringComparison.Ordinal);
        Assert.Contains(".ActivateOnEnter<Raidwide>()", cs, StringComparison.Ordinal);
        Assert.Contains(".DeactivateOnExit<Raidwide>()", cs, StringComparison.Ordinal);
        Assert.Contains("SimpleState(id + 0xFF0000, 10, \"???\")", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("(uint)AID.", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void CastMultiAndTimeoutReview()
    {
        var em = WithTimeline(
            [new PhaseModel(0, "Phase0", "Death", null, 0, 2, [])],
            [
                Step("0x00000000", "CastMulti", 40103, 3.2f, 4, "Cone / ConeAlt", [], [], "branch: pulls diverge (Cone 2/3, ConeAlt 1/3)", null, [40103, 40108]),
                Step("0x00010000", "Timeout", 40105, 7, 3, "Circle2", [], [], "timing jitter MAD 3.0s > 1.5s — emitted Timeout", null),
            ]);
        em = em with
        {
            Enums = em.Enums with
            {
                Aid = em.Enums.Aid.Add(40103, "Cone").Add(40108, "ConeAlt").Add(40105, "Circle2"),
            },
            Review = em.Review.Add(new ReviewItem("R4", ReviewSeverity.Low, "timeline", "step 0x00000000: branch: pulls diverge (Cone 2/3, ConeAlt 1/3)", []))
                .Add(new ReviewItem("R5", ReviewSeverity.Medium, "timeline", "step 0x00010000: timing jitter MAD 3.0s > 1.5s — emitted Timeout", [])),
        };
        var cs = Emit(em);
        Assert.Contains("CastMulti(id, [AID.Cone, AID.ConeAlt], 3.2f, 4, \"Cone / ConeAlt\")", cs, StringComparison.Ordinal);
        Assert.Contains("// TODO(forge:R4, Low): branch: pulls diverge (Cone 2/3, ConeAlt 1/3)", cs, StringComparison.Ordinal);
        Assert.Contains("Timeout(id + 0x10000, 10, \"Circle2\")", cs, StringComparison.Ordinal);
        Assert.Contains("// TODO(forge:R5, Medium): timing jitter MAD 3.0s > 1.5s — emitted Timeout", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void LoopRepeatsAndHpPhases()
    {
        var steps = ImmutableArray.Create(
            Step("0x00000000", "Cast", 40001, 5.1f, 5, "Opener", [], [], null, null),
            Step("0x01000000", "Targetable", 0, 12.3f, 0, "Boss reappears", [], [], null, true),
            Step("0x01010000", "Cast", 40001, 6.2f, 3, "Circle", ["Circle"], ["Circle"], null, null));
        var em = WithTimeline(
            [
                new PhaseModel(0, "Phase0", "HpThreshold", 0.5, 0, 1, ["Raidwide"]),
                new PhaseModel(1, "Phase1", "Death", null, 1, 2, ["Raidwide"]),
            ],
            steps,
            new LoopModel(2, 1, 2, 0, 6.2f));
        var cs = Emit(em);
        Assert.Contains("SimplePhase(0, Phase0, \"P0\")", cs, StringComparison.Ordinal);
        Assert.Contains(".Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed || Module.PrimaryActor.HPRatio <= 0.5f;", cs, StringComparison.Ordinal);
        Assert.Contains("DeathPhase(1, Phase1)", cs, StringComparison.Ordinal);
        Assert.Contains("Targetable(id, true, 12.3f, \"Boss reappears\")", cs, StringComparison.Ordinal);
        Assert.Contains("Rotation1(", cs, StringComparison.Ordinal);
        Assert.Contains("5.1f", cs, StringComparison.Ordinal);
    }

    private static string Emit(EncounterModel em)
    {
        var profile = ModuleEmitter.ProfileFor(Flavor.Vbm, ModuleEmitter.ResolveFlavorsDirectory());
        return ModuleEmitter.Emit(em, profile, new EmitOptions(Flavor.Vbm)).Files[0].Content;
    }

    private static EncounterModel WithTimeline(
        ImmutableArray<PhaseModel> phases,
        ImmutableArray<TimelineStep> steps,
        LoopModel? loop = null)
        => HandBuiltEncounter.Create() with
        {
            Timeline = new TimelineModel(phases, steps, loop, 3, Confidence.Of(1), []),
        };

    private static TimelineStep Step(
        string id,
        string kind,
        uint aid,
        float delay,
        float cast,
        string name,
        string[] activate,
        string[] deactivate,
        string? review,
        bool? targetable,
        uint[]? of = null)
        => new(
            id, kind, kind == "Targetable" ? null : aid, delay, cast, 0, 3,
            of is null ? [aid] : [.. of], name, [.. activate], [.. deactivate], review, targetable);
}
