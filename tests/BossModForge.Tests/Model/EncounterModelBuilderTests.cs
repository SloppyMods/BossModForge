using System.Collections.Immutable;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Model;

public sealed class EncounterModelBuilderTests
{
    [Fact]
    public void Sanitise_PascalCase_Strips_PrefixDigits_KeywordSafe()
    {
        Assert.Equal("TestBoss", CSharpIdentifier.Sanitise("Test Boss", "Encounter"));
        Assert.Equal("HeavySlash", CSharpIdentifier.Sanitise("heavy slash", "Ability"));
        Assert.Equal("Encounter", CSharpIdentifier.Sanitise("", "Encounter"));
        Assert.Equal("Encounter", CSharpIdentifier.Sanitise("***", "Encounter"));
        Assert.Equal("_123Foo", CSharpIdentifier.Sanitise("123 foo", "Encounter"));
        Assert.Equal("Class", CSharpIdentifier.Sanitise("class", "Encounter"));
        Assert.Equal("@class", CSharpIdentifier.Sanitise("", "class"));
    }

    [Fact]
    public void Dedupe_AppendsInInputOrder()
    {
        Assert.Equal(["Boss", "Boss1", "Boss2"], CSharpIdentifier.Dedupe(["Boss", "Boss", "Boss"]).ToArray());
        Assert.Equal(["Foo", "Foo1", "Foo2"], CSharpIdentifier.Dedupe(["Foo", "Foo1", "Foo"]).ToArray());
    }

    [Fact]
    public void Build_HandConstructed_SetsSchemaUnclassifiedAndHelperOid()
    {
        using var stream = SyntheticDungeon.Build().Open(ReplayEncoding.Text);
        var recording = ReplayParser.Parse(stream, "SyntheticDungeon.log");
        var t0 = HandBuiltEncounter.T0;
        var high = Confidence.Of(0.9);
        var pulls = Immutable(new Pull(
            0, t0.AddSeconds(1), t0.AddSeconds(8), PullOutcome.Wipe, 1234, 826,
            SyntheticDungeon.BossId, SyntheticDungeon.BossOid, [SyntheticDungeon.Player1], high, []));
        var participants = Immutable(
            new Participant(SyntheticDungeon.BossOid, "Test Boss", 12716, ActorType.Enemy, ParticipantRole.Boss, 5f, 1, 1, false, high, []),
            new Participant(0x233C, "Helper", 0, ActorType.Helper, ParticipantRole.Helper, 0.5f, 1, 1, false, high, []),
            new Participant(0, "Alice", 0, ActorType.Player, ParticipantRole.Player, 0.5f, 4, 1, false, high, []));
        var catalog = new AbilityCatalog(
            [
                new Ability(
                    SyntheticDungeon.BossOid, ActionId.Spell(SyntheticDungeon.HeavySlash), "Heavy Slash",
                    true, 5f, false, 3, [], null, high, []),
            ],
            [new StatusEntry(1234, "", [SyntheticDungeon.BossOid], 3, [])],
            [new TetherEntry(7, [(0x233C, 0u)], 3)],
            [new IconEntry(50, [0], 3)]);

        var em = EncounterModelBuilder.Build(recording, pulls, participants, catalog, NullGameData.Instance);

        Assert.Equal(1, em.SchemaVersion);
        Assert.Equal("TestBoss", em.Encounter.ModuleName);
        Assert.Equal("BossMod.Forge.TestBoss", em.Encounter.Namespace);
        Assert.DoesNotContain(em.Participants, p => p.Role is "Player" or "Pet");
        Assert.Equal(MechanicClass.Unclassified, Assert.Single(em.Abilities).Class);
        Assert.Equal("Helper", em.Enums.Oid[0x233C]);
        Assert.Equal("Boss", em.Enums.Oid[SyntheticDungeon.BossOid]);
        Assert.Equal("Circle", em.Arena.Kind);
        Assert.True(em.Arena.Radius > 0);
        Assert.Empty(em.Overrides.Values);
        Assert.Equal("", em.Sources[0].Sha256);
        Assert.All(em.Review, item => Assert.StartsWith("R", item.Id, StringComparison.Ordinal));
    }

    private static ImmutableArray<T> Immutable<T>(params T[] items) => [.. items];
}
