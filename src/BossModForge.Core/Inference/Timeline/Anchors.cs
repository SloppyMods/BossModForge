using System.Collections.Immutable;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference.Timeline;

public enum AnchorKind
{
    Cast,
    TargetableOff,
    TargetableOn,
}

public sealed record AnchorEvent(
    int PullIndex,
    DateTime T,
    AnchorKind Kind,
    uint Aid,
    float CastTime,
    DateTime? CastEnd,
    ulong SourceInstanceId);

public static class AnchorExtractor
{
    public static ImmutableArray<ImmutableArray<AnchorEvent>> Extract(
        Recording recording,
        ImmutableArray<Pull> pulls,
        AbilityCatalog catalog,
        TimelineAlignerOptions? options = null)
    {
        options ??= TimelineAlignerOptions.Default;
        if (pulls.IsDefaultOrEmpty)
            return [];

        var result = ImmutableArray.CreateBuilder<ImmutableArray<AnchorEvent>>(pulls.Length);
        foreach (var pull in pulls)
            result.Add(ExtractPull(recording, pull, catalog, options));
        return result.ToImmutable();
    }

    private static ImmutableArray<AnchorEvent> ExtractPull(
        Recording recording,
        Pull pull,
        AbilityCatalog catalog,
        TimelineAlignerOptions options)
    {
        var casts = ExtractCasts(pull, catalog, options);
        var targetable = ExtractTargetable(recording, pull, options);
        var merged = new List<AnchorEvent>(casts.Length + targetable.Length);
        merged.AddRange(casts);
        merged.AddRange(targetable);
        merged.Sort(Compare);
        return [.. merged];
    }

    private static ImmutableArray<AnchorEvent> ExtractCasts(
        Pull pull,
        AbilityCatalog catalog,
        TimelineAlignerOptions options)
    {
        var qualified = new List<AnchorEvent>();
        var fallback = new List<AnchorEvent>();
        foreach (var ability in catalog.Abilities.IsDefault ? [] : catalog.Abilities)
        {
            foreach (var inst in ability.Instances.IsDefault ? [] : ability.Instances)
            {
                if (inst.PullIndex != pull.Index || inst.SourceInstanceId != pull.PrimaryInstanceId || inst.CastStart is null)
                    continue;
                var ev = new AnchorEvent(
                    pull.Index,
                    inst.CastStart.Value,
                    AnchorKind.Cast,
                    ability.Action.Id,
                    ability.CastTime,
                    inst.CastEnd,
                    inst.SourceInstanceId);
                fallback.Add(ev);
                if (ability.CastTime >= options.AnchorMinCastTime)
                    qualified.Add(ev);
            }
        }

        var source = qualified.Count > 0 ? qualified : fallback;
        source.Sort(Compare);
        return CollapseMultiCast(source, options.MultiCastWindowSeconds);
    }

    private static ImmutableArray<AnchorEvent> CollapseMultiCast(List<AnchorEvent> ordered, float windowSeconds)
    {
        if (ordered.Count == 0)
            return [];
        var window = TimeSpan.FromSeconds(windowSeconds);
        var kept = ImmutableArray.CreateBuilder<AnchorEvent>();
        var i = 0;
        while (i < ordered.Count)
        {
            var first = ordered[i];
            var j = i + 1;
            while (j < ordered.Count
                   && ordered[j].Aid == first.Aid
                   && ordered[j].T - first.T <= window)
            {
                j++;
            }

            kept.Add(first);
            i = j;
        }

        return kept.ToImmutable();
    }

    private static ImmutableArray<AnchorEvent> ExtractTargetable(
        Recording recording,
        Pull pull,
        TimelineAlignerOptions options)
    {
        var actor = recording.ActorAt(pull.PrimaryInstanceId, pull.Start)
            ?? recording.Actors.LastOrDefault(a => a.InstanceId == pull.PrimaryInstanceId && a.Created <= pull.End);
        if (actor is null)
            return [];

        var prev = actor.Targetable.Count > 0 ? actor.Targetable[0].Value : false;
        var transitions = new List<(DateTime T, bool Value)>();
        foreach (var sample in actor.Targetable)
        {
            if (sample.T == actor.Created)
            {
                prev = sample.Value;
                continue;
            }
            if (sample.T < pull.Start || sample.T > pull.End)
                continue;
            if (sample.Value == prev)
                continue;
            transitions.Add(sample);
            prev = sample.Value;
        }

        var minUntargetable = TimeSpan.FromSeconds(options.MinUntargetableSeconds);
        var drop = new bool[transitions.Count];
        for (var i = 0; i < transitions.Count - 1; i++)
        {
            if (!transitions[i].Value && transitions[i + 1].Value
                && transitions[i + 1].T - transitions[i].T < minUntargetable)
            {
                drop[i] = true;
                drop[i + 1] = true;
            }
        }

        var result = ImmutableArray.CreateBuilder<AnchorEvent>();
        for (var i = 0; i < transitions.Count; i++)
        {
            if (drop[i])
                continue;
            var kind = transitions[i].Value ? AnchorKind.TargetableOn : AnchorKind.TargetableOff;
            result.Add(new AnchorEvent(pull.Index, transitions[i].T, kind, 0, 0, null, pull.PrimaryInstanceId));
        }

        return result.ToImmutable();
    }

    internal static int Compare(AnchorEvent a, AnchorEvent b)
    {
        var t = a.T.CompareTo(b.T);
        if (t != 0)
            return t;
        var k = a.Kind.CompareTo(b.Kind);
        if (k != 0)
            return k;
        return a.Aid.CompareTo(b.Aid);
    }
}
