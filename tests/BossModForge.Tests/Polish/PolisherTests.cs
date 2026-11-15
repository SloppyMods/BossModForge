using System.Text.Json;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Polish;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Polish;

public sealed class PolisherTests
{
    [Fact]
    public void Polish_RenamesEnumName_UpdatesEnums()
    {
        var em = HandBuiltEncounter.Create();
        var result = Polisher.Apply(em, [Replace("/abilities/40001/enumName", "big slash")]);
        Assert.Equal("BigSlash", result.Model.Enums.Aid[40001]);
        Assert.Equal("BigSlash", Assert.Single(result.Model.Abilities).EnumName);
        Assert.Contains(result.Accepted, a => a.StartsWith("/abilities/40001/enumName:", StringComparison.Ordinal));
        Assert.Empty(result.Rejected);
        Assert.Equal(EncounterModelJson.Serialize(em), EncounterModelJson.Serialize(HandBuiltEncounter.Create()));
    }

    [Fact]
    public void Polish_RejectsOutOfAllowlist()
    {
        var em = HandBuiltEncounter.Create();
        var before = EncounterModelJson.Serialize(em);
        var result = Polisher.Apply(em,
        [
            Replace("/abilities/40001/shape", "circle"),
            Replace("/abilities/40001/castTime", "9"),
            Replace("/encounter/moduleName", "Nope"),
            Replace("/arena/radius", "99"),
        ]);
        Assert.Equal(before, EncounterModelJson.Serialize(result.Model));
        Assert.Equal(4, result.Rejected.Length);
        Assert.Empty(result.Accepted);
        Assert.Contains(result.Rejected, r => r.StartsWith("/abilities/40001/shape:", StringComparison.Ordinal));
        Assert.Contains(result.Rejected, r => r.StartsWith("/abilities/40001/castTime:", StringComparison.Ordinal));
    }

    [Fact]
    public void Polish_RejectsNonStringValues()
    {
        var em = HandBuiltEncounter.Create();
        var shapeValue = JsonSerializer.SerializeToElement(new { kind = "circle", radius = 8 });
        var result = Polisher.Apply(em, [new PolishPatch("replace", "/abilities/40001/enumName", shapeValue)]);
        Assert.Equal("HeavySlash", result.Model.Enums.Aid[40001]);
        Assert.Single(result.Rejected);
        Assert.Contains("value must be a string", result.Rejected[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Polish_EnumNameCollision_Rejected()
    {
        var em = TwoAbilities();
        var result = Polisher.Apply(em, [Replace("/abilities/40001/enumName", "Other")]);
        Assert.Equal("HeavySlash", result.Model.Enums.Aid[40001]);
        Assert.Equal("Other", result.Model.Enums.Aid[40002]);
        Assert.Single(result.Rejected);
        Assert.Contains("collides", result.Rejected[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Polish_ResolutionMustBeSuggested()
    {
        var em = HandBuiltEncounter.Create();
        var bad = Polisher.Apply(em, [Replace("/review/R1/resolution", "Foo")]);
        Assert.Single(bad.Rejected);
        Assert.Contains("suggested", bad.Rejected[0], StringComparison.Ordinal);
        Assert.All(bad.Model.Review, r => Assert.Null(r.Resolution));

        var good = Polisher.Apply(em, [Replace("/review/R1/resolution", "Set Expansion and Category on ModuleInfo")]);
        Assert.Empty(good.Rejected);
        Assert.Equal("Set Expansion and Category on ModuleInfo", Assert.Single(good.Model.Review).Resolution);
    }

    [Fact]
    public void Polish_BadAid_Rejected()
    {
        var em = HandBuiltEncounter.Create();
        var result = Polisher.Apply(em, [Replace("/abilities/999999/enumName", "Nope")]);
        Assert.Single(result.Rejected);
        Assert.Contains("unknown ability id", result.Rejected[0], StringComparison.Ordinal);
        Assert.Equal(EncounterModelJson.Serialize(em), EncounterModelJson.Serialize(result.Model));
    }

    [Fact]
    public void Polish_NameAndComment_Applied()
    {
        var em = HandBuiltEncounter.Create();
        var result = Polisher.Apply(em,
        [
            Replace("/abilities/40001/name", "Tankbuster"),
            Replace("/abilities/40001/comment", "bait and dodge"),
        ]);
        var ability = Assert.Single(result.Model.Abilities);
        Assert.Equal("Tankbuster", ability.Name);
        Assert.Equal("bait and dodge", ability.Comment);
        Assert.Equal(2, result.Accepted.Length);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Polish_ParticipantEnumName_UpdatesEnumsAndBossRenameRejected()
    {
        var em = HandBuiltEncounter.Create();
        var result = Polisher.Apply(em,
        [
            Replace("/participants/9020/enumName", "arena helper"),
            Replace("/participants/17185/enumName", "NotBoss"),
        ]);
        Assert.Equal("ArenaHelper", result.Model.Enums.Oid[0x233C]);
        Assert.Equal("ArenaHelper", result.Model.Participants.Single(p => p.Oid == 0x233C).EnumName);
        Assert.Equal("Boss", result.Model.Enums.Oid[0x4321]);
        Assert.Single(result.Rejected);
        Assert.Contains("primary OID", result.Rejected[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Polish_RemoveClearsComment()
    {
        var em = HandBuiltEncounter.Create();
        var withComment = Polisher.Apply(em, [Replace("/abilities/40001/comment", "note")]);
        var cleared = Polisher.Apply(withComment.Model, [new PolishPatch("remove", "/abilities/40001/comment", default)]);
        Assert.Null(Assert.Single(cleared.Model.Abilities).Comment);
        Assert.Single(cleared.Accepted);
    }

    [Fact]
    public void Polish_IsDeterministic()
    {
        var em = TwoAbilities();
        var patches = new List<PolishPatch>
        {
            Replace("/abilities/40002/enumName", "Heavy Slash"), // collides with 40001 -> rejected, still recorded
            Replace("/abilities/40001/enumName", "big slash"),
            Replace("/abilities/40001/comment", "note"),
            Replace("/review/R1/resolution", "Set Expansion and Category on ModuleInfo"),
            Replace("/participants/9020/name", "Helper Unit"),
        };
        var a = Polisher.Apply(em, patches);
        var b = Polisher.Apply(em, patches);
        Assert.Equal(EncounterModelJson.Serialize(a.Model), EncounterModelJson.Serialize(b.Model));
        Assert.Equal(string.Join("|", a.Accepted), string.Join("|", b.Accepted));
        Assert.Equal(a.Rejected.Length, b.Rejected.Length);
        Assert.Equal(string.Join("|", a.Rejected), string.Join("|", b.Rejected));
        for (var i = 0; i < a.Rejected.Length; i++)
            Assert.True(a.Rejected[i] == b.Rejected[i], $"[{i}] {JsonSerializer.Serialize(a.Rejected[i])} vs {JsonSerializer.Serialize(b.Rejected[i])}");
        var sorted = a.Rejected.OrderBy(r => r, StringComparer.Ordinal).ToArray();
        Assert.True(a.Rejected.SequenceEqual(sorted),
            "rejected=" + JsonSerializer.Serialize(a.Rejected.ToArray()) + " sorted=" + JsonSerializer.Serialize(sorted));
        var sortedAccepted = a.Accepted.OrderBy(r => r, StringComparer.Ordinal).ToArray();
        Assert.True(a.Accepted.SequenceEqual(sortedAccepted),
            "accepted=" + JsonSerializer.Serialize(a.Accepted.ToArray()) + " sorted=" + JsonSerializer.Serialize(sortedAccepted));
    }

    private static PolishPatch Replace(string path, string value)
        => new("replace", path, JsonSerializer.SerializeToElement(value));

    private static EncounterModel TwoAbilities()
    {
        var em = HandBuiltEncounter.Create();
        var other = em.Abilities[0] with { ActionId = 40002, EnumName = "Other", Name = "Other" };
        return em with
        {
            Abilities = [em.Abilities[0], other],
            Enums = em.Enums with { Aid = em.Enums.Aid.Add(40002, "Other") },
        };
    }
}
