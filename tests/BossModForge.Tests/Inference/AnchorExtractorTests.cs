using System.Collections.Immutable;
using BossModForge.Core.Inference;
using BossModForge.Core.Inference.Timeline;
using BossModForge.Core.Replay;
using Xunit;

namespace BossModForge.Tests.Inference;

public sealed class AnchorExtractorTests
{
    [Fact]
    public void CastTimeFilter_Fallback_Helpers_Collapse_Targetable_Order()
    {
        var t0 = new DateTime(2026, 9, 20, 5, 0, 0, DateTimeKind.Unspecified);
        var catalog = new AbilityCatalog(
            [
                Ability(1, 0.4f, Inst(0, 0x1000, t0.AddSeconds(2), 0.4f)),
                Ability(2, 0.5f, Inst(0, 0x1000, t0.AddSeconds(4), 0.5f)),
                Ability(3, 2f, Inst(0, 0x233C, t0.AddSeconds(6), 2f)),
                Ability(4, 3f, Inst(0, 0x1000, t0.AddSeconds(8), 3f), Inst(0, 0x1000, t0.AddSeconds(8.3), 3f)),
            ],
            [],
            [],
            []);
        var pulls = ImmutableArray.Create(new Pull(0, t0, t0.AddSeconds(20), PullOutcome.Kill, 1, 1, 0x1000, 0x4321, [0x10], Confidence.Of(1), []));
        var rec = RecordingWithBoss(t0, targetableBlip: true);
        var extracted = AnchorExtractor.Extract(rec, pulls, catalog);
        var seq = Assert.Single(extracted);
        Assert.DoesNotContain(seq, a => a.Aid == 1);
        Assert.Contains(seq, a => a.Aid == 2);
        Assert.DoesNotContain(seq, a => a.SourceInstanceId == 0x233C);
        Assert.Equal(1, seq.Count(a => a.Aid == 4));
        Assert.DoesNotContain(seq, a => a.Kind == AnchorKind.TargetableOff);

        var fallbackCatalog = new AbilityCatalog(
            [Ability(1, 0.4f, Inst(0, 0x1000, t0.AddSeconds(2), 0.4f))],
            [], [], []);
        var fallback = Assert.Single(AnchorExtractor.Extract(rec, pulls, fallbackCatalog));
        Assert.Contains(fallback, a => a.Aid == 1);
    }

    private static Ability Ability(uint aid, float ct, params AbilityInstance[] inst)
        => new(0x4321, ActionId.Spell(aid), "A" + aid, true, ct, false, inst.Length, [.. inst], null, Confidence.Of(1), []);

    private static AbilityInstance Inst(int pull, ulong source, DateTime start, float ct)
        => new(pull, source, start, start.AddSeconds(ct), start.AddSeconds(ct), default, default, 0, default, []);

    private static Recording RecordingWithBoss(DateTime t0, bool targetableBlip)
    {
        var b = new BossModForge.Tests.Fixtures.RecordingBuilder(t0);
        b.Frame().Zone(1, 1)
            .Actor(0x1000, 0x4321, "Boss", ActorType.Enemy, new System.Numerics.Vector3(100, 0, 100), default, 5f, targetable: true);
        b.Advance(TimeSpan.FromSeconds(1)).Combat(0x1000, true);
        if (targetableBlip)
        {
            b.Advance(TimeSpan.FromSeconds(1)).Targetable(0x1000, false)
                .Advance(TimeSpan.FromSeconds(1)).Targetable(0x1000, true);
        }

        b.Advance(TimeSpan.FromSeconds(15)).Dead(0x1000, true).Destroy(0x1000);
        using var stream = b.Open(ReplayEncoding.Text);
        return ReplayParser.Parse(stream, "anchors.log");
    }
}
