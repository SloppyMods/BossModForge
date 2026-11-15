using System.Linq;
using System.Numerics;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class ParticipantClassifierTests
{
    [Fact]
    public void SyntheticDungeon_BossHelperPlayer()
    {
        var recording = Parse(SyntheticDungeon.Build(), "SyntheticDungeon.log");
        var pulls = PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions);
        var participants = ParticipantClassifier.Classify(recording, pulls, NullGameData.Instance);

        Assert.Equal(3, participants.Length);
        Assert.Equal([0u, ParticipantClassifier.HelperOid, SyntheticDungeon.BossOid], participants.Select(p => p.Oid).ToArray());

        var player = participants[0];
        Assert.Equal(ParticipantRole.Player, player.Role);
        Assert.Equal(0u, player.Oid);
        Assert.Equal(4, player.InstanceCount);
        Assert.Equal(3, player.PullCount);
        Assert.False(player.SpawnsDuringFight);

        var helper = participants[1];
        Assert.Equal(ParticipantRole.Helper, helper.Role);
        Assert.Equal(ParticipantClassifier.HelperOid, helper.Oid);

        var boss = participants[2];
        Assert.Equal(ParticipantRole.Boss, boss.Role);
        Assert.Equal(SyntheticDungeon.BossOid, boss.Oid);
        Assert.Equal("Test Boss", boss.Name);
        Assert.Equal(12716u, boss.NameId);
        Assert.Equal(5f, boss.HitboxRadius);
    }

    [Fact]
    public void UntargetableEnemyThatCasts_IsCaster()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var action = ActionId.Spell(9);
        var builder = new RecordingBuilder(start)
            .Frame()
            .Zone(1, 1)
            .Player(0x10, "Alice", Class.PLD, Vector3.Zero)
            .Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, Vector3.Zero, default, 5f, targetable: true, ally: false)
            .Actor(0x3000, 0x9999, "Orb", ActorType.Enemy, Vector3.One, default, 1f, targetable: false, ally: false)
            .Advance(TimeSpan.FromSeconds(1))
            .Combat(0x1000, true)
            .CastStart(0x3000, action, 0x10, Vector3.Zero, 2f, default)
            .Advance(TimeSpan.FromSeconds(2))
            .CastEvent(0x3000, action, 0x10, Vector3.Zero, default, new CastTarget(0x10, ActionEffects.From([Effects.Damage(1)])))
            .Advance(TimeSpan.FromSeconds(4))
            .Dead(0x1000, true)
            .Combat(0x1000, false);

        var recording = Parse(builder, "caster.log");
        var pulls = PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions);
        var participants = ParticipantClassifier.Classify(recording, pulls, NullGameData.Instance);
        var caster = Assert.Single(participants, p => p.Oid == 0x9999);
        Assert.Equal(ParticipantRole.Caster, caster.Role);
        Assert.Equal(ConfidenceBand.Medium, caster.Confidence.Band);
    }

    [Fact]
    public void TargetableNonPrimaryEnemy_IsAdd()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var builder = new RecordingBuilder(start)
            .Frame()
            .Zone(1, 1)
            .Player(0x10, "Alice", Class.PLD, Vector3.Zero)
            .Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, Vector3.Zero, default, 5f, targetable: true, ally: false)
            .Actor(0x3000, 0x1111, "Add", ActorType.Enemy, Vector3.One, default, 1f, targetable: true, ally: false)
            .Advance(TimeSpan.FromSeconds(1))
            .Combat(0x1000, true)
            .Combat(0x3000, true)
            .Advance(TimeSpan.FromSeconds(6))
            .Dead(0x1000, true)
            .Combat(0x1000, false)
            .Combat(0x3000, false);

        var recording = Parse(builder, "add.log");
        var pulls = PullSegmenter.Segment(recording, SyntheticDungeon.PullOptions);
        var participants = ParticipantClassifier.Classify(recording, pulls, NullGameData.Instance);
        var add = Assert.Single(participants, p => p.Oid == 0x1111);
        Assert.Equal(ParticipantRole.Add, add.Role);
        var boss = Assert.Single(participants, p => p.Oid == 0x4321);
        Assert.Equal(ParticipantRole.Boss, boss.Role);
    }

    private static Recording Parse(RecordingBuilder builder, string name)
    {
        using var stream = builder.Open(ReplayEncoding.Text);
        return ReplayParser.Parse(stream, name);
    }
}
