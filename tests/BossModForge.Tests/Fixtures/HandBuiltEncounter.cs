using System.Collections.Immutable;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Tests.Fixtures;

public static class HandBuiltEncounter
{
    public static readonly DateTime T0 = new(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);

    public static EncounterModel Create()
    {
        var low = Confidence.LowBecause("placeholder");
        var high = Confidence.Of(0.9);
        var oid = ImmutableSortedDictionary<uint, string>.Empty
            .Add(0x233C, "Helper")
            .Add(0x4321, "Boss");
        var aid = ImmutableSortedDictionary<uint, string>.Empty.Add(40001, "HeavySlash");
        var sid = ImmutableSortedDictionary<uint, string>.Empty.Add(1234, "Status1234");
        var tether = ImmutableSortedDictionary<uint, string>.Empty.Add(7, "Tether7");
        var icon = ImmutableSortedDictionary<uint, string>.Empty.Add(50, "Icon50");
        return new EncounterModel(
            SchemaVersion: 1,
            Encounter: new EncounterInfo(1234, 826, null, 0x4321, "Test Boss", 12716, "TestBoss", "BossMod.Forge.TestBoss"),
            Sources:
            [
                new SourceInfo("SyntheticDungeon.log", "", 24, "2026.08.20.0000.0000", T0, T0.AddSeconds(30)),
            ],
            Pulls:
            [
                new PullSummary(0, "SyntheticDungeon.log", T0.AddSeconds(1), T0.AddSeconds(8), "Wipe", 7, high),
                new PullSummary(1, "SyntheticDungeon.log", T0.AddSeconds(10), T0.AddSeconds(17), "Wipe", 7, high),
                new PullSummary(2, "SyntheticDungeon.log", T0.AddSeconds(19), T0.AddSeconds(26), "Kill", 7, high),
            ],
            Participants:
            [
                new ParticipantModel(0x4321, "Boss", "Test Boss", 12716, "Enemy", "Boss", 5f, 1, false, high, []),
                new ParticipantModel(0x233C, "Helper", "Helper", 0, "Helper", "Helper", 0.5f, 1, false, high, []),
            ],
            Abilities:
            [
                new AbilityModel(0x4321, 40001, "Spell", "HeavySlash", "Heavy Slash", true, 5f, false, 3, MechanicClass.Unclassified, null, null, high, []),
            ],
            Statuses: [new StatusModel(1234, "Status1234", "", 3)],
            Tethers: [new TetherModel(7, "Tether7", 3)],
            Icons: [new IconModel(50, "Icon50", 3)],
            Enums: new EnumNames(oid, aid, sid, tether, icon),
            Arena: new ArenaModel("Circle", 100f, 100f, 20f, 0, 0, [], low, []),
            Review:
            [
                new ReviewItem("R1", ReviewSeverity.Medium, "module", "Set Expansion/Category", ["Set Expansion and Category on ModuleInfo"]),
            ],
            Overrides: Overrides.Empty);
    }
}
