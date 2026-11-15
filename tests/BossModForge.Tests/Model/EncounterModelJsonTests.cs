using System.Text.RegularExpressions;
using BossModForge.Core.Model;
using BossModForge.Tests.Fixtures;
using Xunit;
using Xunit.Sdk;

namespace BossModForge.Tests.Model;

public sealed class EncounterModelJsonTests
{
    private static readonly Regex TopLevelKey = new(@"^ {2}""([^""]+)"":", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void Serialize_Deserialize_IsByteExact()
    {
        var em = HandBuiltEncounter.Create();
        var json = EncounterModelJson.Serialize(em);
        var again = EncounterModelJson.Serialize(EncounterModelJson.Deserialize(json));
        Assert.Equal(json, again);
    }

    [Fact]
    public void Serialize_SchemaVersionFirst_UnixNewlines_TrailingNewline()
    {
        var json = EncounterModelJson.Serialize(HandBuiltEncounter.Create());
        Assert.StartsWith("{\n  \"schemaVersion\": 1,\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", json, StringComparison.Ordinal);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
        Assert.False(json.EndsWith("\n\n", StringComparison.Ordinal));
        var keys = TopLevelKey.Matches(json).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal("schemaVersion", keys[0]);
        Assert.Equal(keys.Skip(1).OrderBy(k => k, StringComparer.Ordinal), keys.Skip(1));
    }

    [Fact]
    public void Validate_Golden_EmptyWhenPresent()
    {
        var path = GoldenPaths.Find("SyntheticDungeon.em.json");
        if (!File.Exists(path))
            throw SkipException.ForSkip("golden EM not generated yet");
        var json = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Empty(EncounterModelJson.Validate(json));
    }
}
