using System.Collections.Immutable;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Core.Validation;
using BossModForge.Tests.Fixtures;

namespace BossModForge.Tests.Validation;

internal static class ValidationFixtures
{
    public static readonly ValidationOptions RotationOptions = new(
        Segmenter: SyntheticRotation.PullOptions,
        GameData: SyntheticRotation.GameData);

    public static (EncounterModel Em, Recording Recording) Rotation(bool jitter = false)
    {
        using var stream = SyntheticRotation.Build(jitter).Open(ReplayEncoding.Text);
        var rec = ReplayParser.Parse(stream, jitter ? "SyntheticRotation.jitter.log" : "SyntheticRotation.log");
        var pulls = PullSegmenter.Segment(rec, SyntheticRotation.PullOptions);
        var participants = ParticipantClassifier.Classify(rec, pulls, SyntheticRotation.GameData);
        var catalog = AbilityCataloger.Catalog(rec, pulls, participants, SyntheticRotation.GameData);
        var em = EncounterModelBuilder.Build(rec, pulls, participants, catalog, SyntheticRotation.GameData);
        return (em, rec);
    }

    public static EncounterModel WithCircleRadius(EncounterModel em, float radius)
    {
        var abilities = em.Abilities.Select(a =>
            a.ActionId == SyntheticRotation.Circle && a.Shape is not null
                ? a with { Shape = a.Shape with { Radius = radius } }
                : a).ToImmutableArray();
        return em with { Abilities = abilities };
    }

    public static EncounterModel WithPlantedStep(EncounterModel em)
    {
        var tl = em.Timeline ?? throw new InvalidOperationException("timeline missing");
        var planted = tl.Steps[0] with
        {
            Id = "planted",
            Kind = "Cast",
            Aid = 40999,
            Of = [40999],
            Delay = 1,
            Name = "Planted",
            Review = null,
        };
        var phase = tl.Phases[0] with { StepCount = tl.Phases[0].StepCount + 1 };
        return em with
        {
            Timeline = tl with
            {
                Steps = tl.Steps.Add(planted),
                Phases = tl.Phases.SetItem(0, phase),
            },
        };
    }
}
