using System.Globalization;
using System.Linq;
using BossModForge.Core.Replay;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Replay;

public sealed class TextParserTests
{
    [Fact]
    public void UnknownTag_BecomesRawOp_OncePerDistinctTag()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var text = string.Join('\n',
            $"{O(start)}|VER |24|10000000|2026.08.20.0000.0000",
            $"{O(start)}|ZZZZ|one|two",
            $"{O(start)}|ZZZZ|three",
            $"{O(start)}|YYYY|only");
        var recording = ParseText(text);

        Assert.Equal(3, recording.Ops.OfType<RawOp>().Count());
        Assert.Contains(recording.Ops, op => op is RawOp raw && raw.RawTag.ToString() == "ZZZZ" && raw.Fields[0] == "one");
        Assert.Equal(2, recording.Issues.Count(i => i.Code == "UnknownTag" && i.Severity == IssueSeverity.Info));
        Assert.Contains(recording.Issues, i => i.Message.Contains("ZZZZ", StringComparison.Ordinal));
        Assert.Contains(recording.Issues, i => i.Message.Contains("YYYY", StringComparison.Ordinal));
    }

    [Fact]
    public void Version9_UnsupportedVersion_EmptyOps()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var text = $"{O(start)}|VER |9\n{O(start)}|ZONE|1|2\n";
        var recording = ParseText(text);

        Assert.Empty(recording.Ops);
        Assert.Contains(recording.Issues, i => i.Code == "UnsupportedVersion" && i.Severity == IssueSeverity.Error);
        Assert.Equal(9, recording.Meta.Version);
    }

    [Fact]
    public void VerWithoutGameVersion_OlderVersion()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var recording = ParseText($"{O(start)}|VER |10|10000000\n");
        Assert.Contains(recording.Issues, i => i.Code == "OlderVersion");
        Assert.Equal(10, recording.Meta.Version);
        Assert.Equal("old", recording.Meta.GameVersion);
    }

    [Fact]
    public void VerWithGameVersion_OlderVersion()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var recording = ParseText($"{O(start)}|VER |22|10000000|2024.01.01.0000.0000\n");
        Assert.Contains(recording.Issues, i => i.Code == "OlderVersion");
        Assert.Equal(22, recording.Meta.Version);
        Assert.Equal("2024.01.01.0000.0000", recording.Meta.GameVersion);
    }

    [Fact]
    public void CastEvent_ZeroEffectsInMiddle_RoundTripKeepsNonZeroSet()
    {
        var start = new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Unspecified);
        var mixed = ActionEffects.From([Effects.Damage(5), 0, Effects.Miss()]);
        var builder = new RecordingBuilder(start)
            .Frame()
            .CastEvent(0x1000, ActionId.Spell(40001), 0x10, new System.Numerics.Vector3(1, 2, 3), default,
                new CastTarget(0x10, mixed),
                new CastTarget(0x11, ActionEffects.From([Effects.Heal(3)])),
                new CastTarget(0x12, ActionEffects.Empty));
        using var stream = builder.Open(ReplayEncoding.Text);
        var recording = ReplayParser.Parse(stream, "cst.log");
        var ev = Assert.Single(recording.Ops.OfType<ActorCastEventOp>());
        Assert.Equal(3, ev.Targets.Length);
        Assert.Equal(
            new[] { ActionEffectType.Damage, ActionEffectType.Miss },
            ev.Targets[0].Effects.NonEmpty().Select(e => e.Type).ToArray());
        Assert.Equal(ActionEffectType.Heal, Assert.Single(ev.Targets[1].Effects.NonEmpty()).Type);
        Assert.Empty(ev.Targets[2].Effects.NonEmpty());
    }

    private static string O(DateTime t) => t.ToString("O", CultureInfo.InvariantCulture);

    private static Recording ParseText(string text)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")));
        return ReplayParser.Parse(stream, "mem.log");
    }
}
