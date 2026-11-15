using System.Collections.Immutable;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Emit;

public sealed class StatesPlannerTests
{
    [Fact]
    public void Offsets_LoopAndTail()
    {
        var em = HandBuiltWithLoop();
        var plan = StatesPlanner.Plan(em, em.Enums.Aid);
        var phase = Assert.Single(plan.Phases);
        Assert.Equal(0u, phase.Prefix[0].Offset);
        Assert.Equal(StatesPlanner.StepStride, phase.Prefix[1].Offset);
        Assert.NotNull(phase.Loop);
        var loop = phase.Loop!;
        Assert.Equal(StatesPlanner.StepStride * 2, loop.FirstCallOffset);
        Assert.Equal(StatesPlanner.StepStride * (uint)loop.Body.Length, loop.Stride);
        Assert.Equal(loop.FirstCallOffset + loop.Stride * (uint)loop.Repeats, phase.Tail[0].Offset);
    }

    [Fact]
    public void TruncationReview_WhenTooLong()
    {
        var steps = ImmutableArray.CreateBuilder<TimelineStep>();
        for (var i = 0; i < 260; i++)
        {
            steps.Add(new TimelineStep(
                $"0x{(i * (int)StatesPlanner.StepStride):X8}", "Cast", 1, 1, 1, 0, 1, [1], "A", [], [], null, null));
        }

        var em = HandBuiltEncounter.Create() with
        {
            Timeline = new TimelineModel(
                [new PhaseModel(0, "Phase0", "Death", null, 0, 260, [])],
                steps.ToImmutable(),
                null,
                1,
                Confidence.Of(1),
                []),
        };
        var plan = StatesPlanner.Plan(em, em.Enums.Aid);
        Assert.True(plan.Phases[0].Prefix.Length <= 260);
    }

    private static EncounterModel HandBuiltWithLoop()
    {
        var steps = ImmutableArray.Create(
            Step("0x00000000", "Cast", 40001, 0),
            Step("0x00010000", "Cast", 40001, 1),
            Step("0x00020000", "Cast", 40001, 2),
            Step("0x00030000", "Cast", 40001, 3));
        return HandBuiltEncounter.Create() with
        {
            Timeline = new TimelineModel(
                [new PhaseModel(0, "Phase0", "Death", null, 0, 4, [])],
                steps,
                new LoopModel(2, 2, 2, 1, 6),
                3,
                Confidence.Of(1),
                []),
        };
    }

    private static TimelineStep Step(string id, string kind, uint aid, int index)
        => new(id, kind, aid, 1, 1, 0, 3, [aid], "A", [], [], null, null);
}
