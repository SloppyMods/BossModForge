using System.Linq;
using System.Numerics;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class AbilityCatalogerTests
{
    [Fact]
    public void SyntheticDungeon_AbilityStatusTetherIcon()
    {
        var recording = Parse(SyntheticDungeon.Build(), "SyntheticDungeon.log");
        var pulls = PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions);
        var participants = ParticipantClassifier.Classify(recording, pulls, NullGameData.Instance);
        var catalog = AbilityCataloger.Catalog(recording, pulls, participants, NullGameData.Instance);

        var ability = Assert.Single(catalog.Abilities);
        Assert.Equal(SyntheticDungeon.BossOid, ability.SourceOid);
        Assert.Equal(ActionType.Spell, ability.Action.Type);
        Assert.Equal(SyntheticDungeon.HeavySlash, ability.Action.Id);
        Assert.True(ability.HasCastBar);
        Assert.Equal(5f, ability.CastTime);
        Assert.Equal(3, ability.InstanceCount);
        Assert.Equal(3, ability.Instances.Length);
        Assert.False(ability.Interruptible);
        Assert.Equal("Ability40001", ability.Name);
        Assert.Null(ability.Sheet);
        Assert.All(ability.Instances, inst =>
        {
            Assert.Equal(4, inst.Hits.Length);
            Assert.True(inst.CastStart.HasValue);
            Assert.True(inst.CastEnd.HasValue);
        });

        var status = Assert.Single(catalog.Statuses);
        Assert.Equal(1234u, status.StatusId);
        Assert.Equal(3, status.Applications);
        Assert.Equal([SyntheticDungeon.BossOid], status.SourceOids.ToArray());

        var tether = Assert.Single(catalog.Tethers);
        Assert.Equal(7u, tether.TetherId);
        Assert.Equal(3, tether.Count);
        Assert.Equal([(ParticipantClassifier.HelperOid, 0u)], tether.Pairs.ToArray());

        var icon = Assert.Single(catalog.Icons);
        Assert.Equal(50u, icon.IconId);
        Assert.Equal(3, icon.Count);
        Assert.Equal([0u], icon.TargetOids.ToArray());
    }

    [Fact]
    public void UnmatchedCast_HasCastBarAndEvidence()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var action = ActionId.Spell(9);
        var builder = new RecordingBuilder(start)
            .Frame()
            .Zone(1, 1)
            .Player(0x10, "Alice", Class.PLD, Vector3.Zero)
            .Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, Vector3.Zero, default, 5f, targetable: true, ally: false)
            .Advance(TimeSpan.FromSeconds(1))
            .Combat(0x1000, true)
            .CastStart(0x1000, action, 0x10, Vector3.Zero, 5f, default)
            .Advance(TimeSpan.FromSeconds(6))
            .Dead(0x1000, true)
            .Combat(0x1000, false);

        var recording = Parse(builder, "unmatched.log");
        var pulls = PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions);
        var participants = ParticipantClassifier.Classify(recording, pulls, NullGameData.Instance);
        var catalog = AbilityCataloger.Catalog(recording, pulls, participants, NullGameData.Instance);

        var ability = Assert.Single(catalog.Abilities);
        Assert.True(ability.HasCastBar);
        Assert.Equal(0, ability.InstanceCount);
        Assert.Empty(ability.Instances);
        Assert.Equal(0f, ability.CastTime);
        Assert.Contains(ability.Evidence, e => e.Kind == "UnmatchedCast");
    }

    private static Recording Parse(RecordingBuilder builder, string name)
    {
        using var stream = builder.Open(ReplayEncoding.Text);
        return ReplayParser.Parse(stream, name);
    }
}
