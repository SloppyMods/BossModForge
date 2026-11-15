using System.Collections.Immutable;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

public enum PullOutcome
{
    Kill,
    Wipe,
    Abandoned,
    RecordingEnded,
}

public sealed record Pull(
    int Index,
    DateTime Start,
    DateTime End,
    PullOutcome Outcome,
    ushort Zone,
    ushort CfcId,
    ulong PrimaryInstanceId,
    uint PrimaryOid,
    ImmutableArray<ulong> PlayerInstanceIds,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence);

public sealed record SegmenterOptions(TimeSpan MinPullDuration, TimeSpan CombatGapToSplit, uint? BossOid = null)
{
    public static readonly SegmenterOptions Default = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(8));
}

public static class PullSegmenter
{
    public static ImmutableArray<Pull> Segment(Recording recording, SegmenterOptions? options = null)
    {
        options ??= SegmenterOptions.Default;
        var ops = recording.Ops;
        if (ops.Count == 0)
            return ImmutableArray<Pull>.Empty;

        var index = InferenceActors.Index(recording.Actors);
        var pulls = new List<Pull>();
        var pendingDropped = new List<Evidence>();
        var cursor = DateTime.MinValue;

        while (true)
        {
            var start = FindStart(ops, index, cursor);
            if (start is null)
                break;

            var playerIds = PlayersAliveAt(recording.Actors, start.Value);
            var end = ScanEnd(ops, index, recording, start.Value, playerIds, options);
            var duration = end.At - start.Value;
            cursor = end.At;

            if (duration < options.MinPullDuration)
            {
                pendingDropped.Add(new Evidence(
                    "DroppedPull",
                    $"Dropped a {duration.TotalSeconds:0.###}s pull starting {start.Value:O}"));
                continue;
            }

            var (zone, cfc, hasZone) = ZoneAt(recording, start.Value);
            var (primaryId, primaryOid) = PickPrimary(ops, index, end.Candidates, start.Value, end.At);
            var evidence = new List<Evidence>(pendingDropped.Count + 1);
            evidence.AddRange(pendingDropped);
            pendingDropped.Clear();
            if (!hasZone)
                evidence.Add(new Evidence("NoZone", "No ZONE op at or before pull start"));

            pulls.Add(new Pull(
                pulls.Count,
                start.Value,
                end.At,
                end.Outcome,
                zone,
                cfc,
                primaryId,
                primaryOid,
                playerIds,
                ConfidenceFor(end),
                evidence.Count == 0 ? ImmutableArray<Evidence>.Empty : [.. evidence]));
        }

        if (options.BossOid is uint bossOid)
        {
            pulls.RemoveAll(p => p.PrimaryOid != bossOid);
            for (var i = 0; i < pulls.Count; i++)
                pulls[i] = pulls[i] with { Index = i };
        }

        return pulls.Count == 0 ? ImmutableArray<Pull>.Empty : [.. pulls];
    }

    private enum EndKind
    {
        Zone,
        PlayersDead,
        PrimaryDead,
        CombatGap,
        RecordingEnded,
    }

    private readonly record struct EndScan(
        DateTime At,
        PullOutcome Outcome,
        EndKind Kind,
        HashSet<ulong> Candidates);

    private static DateTime? FindStart(IReadOnlyList<Op> ops, Dictionary<ulong, List<ActorTimeline>> index, DateTime after)
    {
        foreach (var op in ops)
        {
            if (op.Timestamp <= after)
                continue;
            if (IsStartOp(op, index))
                return op.Timestamp;
        }

        return null;
    }

    private static bool IsStartOp(Op op, Dictionary<ulong, List<ActorTimeline>> index)
    {
        switch (op)
        {
            case ActorCombatOp { Value: true } com:
            {
                var actor = InferenceActors.At(index, com.InstanceId, com.Timestamp);
                return actor is not null && InferenceActors.IsNonPlayerNonPet(actor.Type);
            }
            case ActorCastStartOp cst:
            {
                var actor = InferenceActors.At(index, cst.InstanceId, cst.Timestamp);
                return actor is not null
                    && InferenceActors.IsNonPlayerNonPet(actor.Type)
                    && InferenceActors.IsPlayer(index, cst.TargetId);
            }
            case ActorCastEventOp ev:
            {
                var actor = InferenceActors.At(index, ev.InstanceId, ev.Timestamp);
                if (actor is null || !InferenceActors.IsNonPlayerNonPet(actor.Type))
                    return false;
                if (InferenceActors.IsPlayer(index, ev.MainTargetId))
                    return true;
                foreach (var hit in ev.Targets)
                {
                    if (InferenceActors.IsPlayer(index, hit.InstanceId))
                        return true;
                }

                return false;
            }
            default:
                return false;
        }
    }

    private static EndScan ScanEnd(
        IReadOnlyList<Op> ops,
        Dictionary<ulong, List<ActorTimeline>> index,
        Recording recording,
        DateTime start,
        ImmutableArray<ulong> playerIds,
        SegmenterOptions options)
    {
        var candidates = new HashSet<ulong>();
        foreach (var actor in recording.Actors)
        {
            if (actor.Type == ActorType.Enemy && actor.IsAliveAt(start) && InferenceActors.InCombatAt(actor, start))
                candidates.Add(actor.InstanceId);
        }

        var i = 0;
        while (i < ops.Count && ops[i].Timestamp < start)
            i++;

        while (i < ops.Count)
        {
            var t = ops[i].Timestamp;
            var j = i + 1;
            while (j < ops.Count && ops[j].Timestamp == t)
                j++;

            var sawZone = false;
            for (var k = i; k < j; k++)
            {
                switch (ops[k])
                {
                    case ZoneChangeOp when t > start:
                        sawZone = true;
                        break;
                    case ActorCombatOp { Value: true } com:
                    {
                        var who = InferenceActors.At(index, com.InstanceId, t);
                        if (who?.Type == ActorType.Enemy)
                            candidates.Add(com.InstanceId);
                        break;
                    }
                }
            }

            if (t > start)
            {
                if (sawZone)
                    return new EndScan(t, PullOutcome.Abandoned, EndKind.Zone, candidates);
                if (AllPlayersDead(index, playerIds, t))
                    return new EndScan(t, PullOutcome.Wipe, EndKind.PlayersDead, candidates);
                if (AllCandidatesResolved(candidates, index, ops, j, t, options.CombatGapToSplit))
                {
                    var (primaryId, _) = PickPrimary(ops, index, candidates, start, t);
                    var dead = InferenceActors.IsDeadOrGone(index, primaryId, t);
                    return dead
                        ? new EndScan(t, PullOutcome.Kill, EndKind.PrimaryDead, candidates)
                        : new EndScan(t, PullOutcome.Wipe, EndKind.CombatGap, candidates);
                }
            }

            i = j;
        }

        return new EndScan(recording.End, PullOutcome.RecordingEnded, EndKind.RecordingEnded, candidates);
    }

    private static bool AllPlayersDead(
        Dictionary<ulong, List<ActorTimeline>> index,
        ImmutableArray<ulong> playerIds,
        DateTime t)
    {
        if (playerIds.IsDefaultOrEmpty)
            return false;
        foreach (var id in playerIds)
        {
            if (!InferenceActors.IsDeadOrGone(index, id, t))
                return false;
        }

        return true;
    }

    private static bool AllCandidatesResolved(
        HashSet<ulong> candidates,
        Dictionary<ulong, List<ActorTimeline>> index,
        IReadOnlyList<Op> ops,
        int nextOp,
        DateTime t,
        TimeSpan gap)
    {
        if (candidates.Count == 0)
            return false;
        foreach (var id in candidates)
        {
            var actor = InferenceActors.At(index, id, t) ?? InferenceActors.Last(index, id);
            var dead = actor is null || actor.IsDeadAt(t) || !actor.IsAliveAt(t);
            var ooc = actor is null || !InferenceActors.InCombatAt(actor, t);
            if (!dead && !ooc)
                return false;
        }

        if (gap <= TimeSpan.Zero)
            return true;

        var limit = t + gap;
        for (var k = nextOp; k < ops.Count; k++)
        {
            if (ops[k].Timestamp > limit)
                break;
            if (ops[k] is ActorCombatOp { Value: true } com && candidates.Contains(com.InstanceId))
                return false;
        }

        return true;
    }

    private static (ulong InstanceId, uint Oid) PickPrimary(
        IReadOnlyList<Op> ops,
        Dictionary<ulong, List<ActorTimeline>> index,
        HashSet<ulong> candidates,
        DateTime start,
        DateTime end)
    {
        if (candidates.Count == 0)
            return (0, 0);

        var counts = new Dictionary<ulong, int>(candidates.Count);
        foreach (var op in ops)
        {
            if (op.Timestamp < start || op.Timestamp > end)
                continue;
            if (op is ActorCastEventOp ev && candidates.Contains(ev.InstanceId))
                counts[ev.InstanceId] = counts.GetValueOrDefault(ev.InstanceId) + 1;
        }

        var ids = new ulong[candidates.Count];
        candidates.CopyTo(ids);
        Array.Sort(ids);

        var bestId = 0UL;
        var bestCount = -1;
        var bestRadius = float.NegativeInfinity;
        uint bestOid = 0;
        foreach (var id in ids)
        {
            var count = counts.GetValueOrDefault(id);
            var actor = InferenceActors.At(index, id, end) ?? InferenceActors.Last(index, id);
            var radius = actor?.HitboxRadius ?? 0f;
            if (count > bestCount || (count == bestCount && radius > bestRadius))
            {
                bestCount = count;
                bestRadius = radius;
                bestId = id;
                bestOid = actor?.Oid ?? 0;
            }
        }

        return (bestId, bestOid);
    }

    private static (ushort Zone, ushort CfcId, bool Found) ZoneAt(Recording recording, DateTime start)
    {
        ushort zone = 0;
        ushort cfc = 0;
        var found = false;
        foreach (var change in recording.Zones)
        {
            if (change.Timestamp > start)
                break;
            zone = change.Zone;
            cfc = change.CfcId;
            found = true;
        }

        return (zone, cfc, found);
    }

    private static ImmutableArray<ulong> PlayersAliveAt(IReadOnlyList<ActorTimeline> actors, DateTime t)
    {
        var ids = new List<ulong>();
        foreach (var actor in actors)
        {
            if (actor.Type != ActorType.Player || !actor.IsAliveAt(t))
                continue;
            if (!ids.Contains(actor.InstanceId))
                ids.Add(actor.InstanceId);
        }

        ids.Sort();
        return ids.Count == 0 ? ImmutableArray<ulong>.Empty : [.. ids];
    }

    private static Confidence ConfidenceFor(EndScan end) => end.Kind switch
    {
        EndKind.Zone or EndKind.PlayersDead or EndKind.PrimaryDead => Confidence.Of(0.9),
        EndKind.CombatGap => Confidence.Of(0.7, "combat-gap end"),
        _ => Confidence.LowBecause("recording ended"),
    };
}

internal static class InferenceActors
{
    public static Dictionary<ulong, List<ActorTimeline>> Index(IReadOnlyList<ActorTimeline> actors)
    {
        var map = new Dictionary<ulong, List<ActorTimeline>>();
        foreach (var actor in actors)
        {
            if (!map.TryGetValue(actor.InstanceId, out var list))
                map[actor.InstanceId] = list = [];
            list.Add(actor);
        }

        foreach (var list in map.Values)
            list.Sort(static (a, b) => a.Incarnation.CompareTo(b.Incarnation));
        return map;
    }

    public static ActorTimeline? At(Dictionary<ulong, List<ActorTimeline>> index, ulong instanceId, DateTime t)
    {
        if (!index.TryGetValue(instanceId, out var list))
            return null;
        ActorTimeline? match = null;
        foreach (var actor in list)
        {
            if (actor.IsAliveAt(t))
                match = actor;
        }

        return match;
    }

    public static ActorTimeline? Last(Dictionary<ulong, List<ActorTimeline>> index, ulong instanceId)
        => index.TryGetValue(instanceId, out var list) && list.Count > 0 ? list[^1] : null;

    public static bool InCombatAt(ActorTimeline actor, DateTime t) => FlagAt(actor.InCombat, t);

    public static bool IsDeadOrGone(Dictionary<ulong, List<ActorTimeline>> index, ulong instanceId, DateTime t)
    {
        var actor = At(index, instanceId, t);
        if (actor is null)
            return true;
        return actor.IsDeadAt(t) || !actor.IsAliveAt(t);
    }

    public static bool IsNonPlayerNonPet(ActorType type)
        => type is not (ActorType.Player or ActorType.Pet or ActorType.Chocobo);

    public static bool IsPlayer(Dictionary<ulong, List<ActorTimeline>> index, ulong instanceId)
    {
        if (instanceId == 0 || !index.TryGetValue(instanceId, out var list))
            return false;
        foreach (var actor in list)
        {
            if (actor.Type == ActorType.Player)
                return true;
        }

        return false;
    }

    public static bool OwnerIsPlayer(ActorTimeline actor, Dictionary<ulong, List<ActorTimeline>> index)
        => actor.OwnerId != 0 && IsPlayer(index, actor.OwnerId);

    private static bool FlagAt(IReadOnlyList<(DateTime T, bool Value)> samples, DateTime t)
    {
        if (samples.Count == 0)
            return false;
        var best = -1;
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i].T <= t)
                best = i;
            else
                break;
        }

        return best >= 0 && samples[best].Value;
    }
}
