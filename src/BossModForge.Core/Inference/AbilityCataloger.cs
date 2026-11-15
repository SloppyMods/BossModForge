using System.Collections.Immutable;
using System.Numerics;
using BossModForge.Core.GameData;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

public sealed record AbilityHit(ulong TargetInstanceId, Vector3 TargetPos, ActionEffects Effects);

public sealed record AbilityInstance(
    int PullIndex,
    ulong SourceInstanceId,
    DateTime? CastStart,
    DateTime? CastEnd,
    DateTime EffectTime,
    Vector3 Origin,
    Angle Rotation,
    ulong MainTargetId,
    Vector3 TargetPos,
    ImmutableArray<AbilityHit> Hits);

public sealed record Ability(
    uint SourceOid,
    ActionId Action,
    string Name,
    bool HasCastBar,
    float CastTime,
    bool Interruptible,
    int InstanceCount,
    ImmutableArray<AbilityInstance> Instances,
    ActionRow? Sheet,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence);

public sealed record StatusEntry(
    uint StatusId,
    string Name,
    ImmutableArray<uint> SourceOids,
    int Applications,
    ImmutableArray<ushort> ExtraValues);

public sealed record TetherEntry(
    uint TetherId,
    ImmutableArray<(uint SourceOid, uint TargetOid)> Pairs,
    int Count);

public sealed record IconEntry(uint IconId, ImmutableArray<uint> TargetOids, int Count);

public sealed record AbilityCatalog(
    ImmutableArray<Ability> Abilities,
    ImmutableArray<StatusEntry> Statuses,
    ImmutableArray<TetherEntry> Tethers,
    ImmutableArray<IconEntry> Icons);

public sealed record CatalogOptions(TimeSpan MatchWindowBefore, TimeSpan MatchWindowAfter, bool IncludePlayerActions)
{
    public static readonly CatalogOptions Default = new(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(2.5), false);
}

public static class AbilityCataloger
{
    public static AbilityCatalog Catalog(
        Recording recording,
        ImmutableArray<Pull> pulls,
        ImmutableArray<Participant> participants,
        IGameData gameData,
        CatalogOptions? options = null)
    {
        _ = participants;
        options ??= CatalogOptions.Default;
        if (pulls.IsDefaultOrEmpty)
        {
            return new AbilityCatalog(
                ImmutableArray<Ability>.Empty,
                ImmutableArray<StatusEntry>.Empty,
                ImmutableArray<TetherEntry>.Empty,
                ImmutableArray<IconEntry>.Empty);
        }

        var index = InferenceActors.Index(recording.Actors);
        var abilities = new Dictionary<(uint Oid, uint ActionRaw), AbilityAccum>();
        var pending = new Dictionary<(ulong InstanceId, uint ActionRaw), List<PendingCast>>();
        var statuses = new Dictionary<uint, StatusAccum>();
        var tethers = new Dictionary<uint, TetherAccum>();
        var icons = new Dictionary<uint, IconAccum>();

        foreach (var op in recording.Ops)
        {
            var pullIndex = PullIndexAt(pulls, op.Timestamp);
            switch (op)
            {
                case ActorDestroyOp destroy:
                    ClearPending(pending, destroy.InstanceId);
                    break;
                case ActorCastStartOp cst when pullIndex is not null:
                    ObserveCastStart(cst, pullIndex.Value, index, gameData, options, abilities, pending);
                    break;
                case ActorCastEventOp ev when pullIndex is not null:
                    ObserveCastEvent(ev, pullIndex.Value, index, gameData, options, abilities, pending);
                    break;
                case ActorStatusOp sta when pullIndex is not null && sta.Status is { } status:
                    ObserveStatus(sta, status, index, gameData, statuses);
                    break;
                case ActorTetherOp tet when pullIndex is not null && tet.TetherId != 0:
                    ObserveTether(tet, index, tethers);
                    break;
                case ActorIconOp icon when pullIndex is not null:
                    ObserveIcon(icon, index, icons);
                    break;
            }
        }

        foreach (var list in pending.Values)
        {
            foreach (var cast in list)
            {
                if (cast.Matched)
                    continue;
                var accum = GetAbility(abilities, cast.SourceOid, cast.Op.Action, gameData);
                accum.Evidence.Add(new Evidence("UnmatchedCast", $"Unmatched CST+ {cast.Op.Action} at {cast.Op.Timestamp:O}"));
            }
        }

        return new AbilityCatalog(
            BuildAbilities(abilities),
            BuildStatuses(statuses),
            BuildTethers(tethers),
            BuildIcons(icons));
    }

    private static void ObserveCastStart(
        ActorCastStartOp cst,
        int pullIndex,
        Dictionary<ulong, List<ActorTimeline>> index,
        IGameData gameData,
        CatalogOptions options,
        Dictionary<(uint Oid, uint ActionRaw), AbilityAccum> abilities,
        Dictionary<(ulong InstanceId, uint ActionRaw), List<PendingCast>> pending)
    {
        _ = pullIndex;
        var source = InferenceActors.At(index, cst.InstanceId, cst.Timestamp);
        if (source is null)
            return;
        if (!options.IncludePlayerActions && source.Type == ActorType.Player)
            return;

        var accum = GetAbility(abilities, source.Oid, cst.Action, gameData);
        accum.HasCastBar = true;
        accum.Interruptible |= cst.Interruptible;
        var castEnd = cst.Timestamp + TimeSpan.FromSeconds(cst.TotalTime - cst.ElapsedTime);
        var key = (cst.InstanceId, cst.Action.Raw);
        if (!pending.TryGetValue(key, out var list))
            pending[key] = list = [];
        list.Add(new PendingCast(cst, castEnd, source.Oid, Matched: false));
    }

    private static void ObserveCastEvent(
        ActorCastEventOp ev,
        int pullIndex,
        Dictionary<ulong, List<ActorTimeline>> index,
        IGameData gameData,
        CatalogOptions options,
        Dictionary<(uint Oid, uint ActionRaw), AbilityAccum> abilities,
        Dictionary<(ulong InstanceId, uint ActionRaw), List<PendingCast>> pending)
    {
        var source = InferenceActors.At(index, ev.InstanceId, ev.Timestamp);
        if (source is null)
            return;
        if (!options.IncludePlayerActions && source.Type == ActorType.Player)
            return;

        var accum = GetAbility(abilities, source.Oid, ev.Action, gameData);
        DateTime? castStart = null;
        DateTime? castEnd = null;
        var key = (ev.InstanceId, ev.Action.Raw);
        if (pending.TryGetValue(key, out var list))
        {
            for (var i = 0; i < list.Count; i++)
            {
                var cast = list[i];
                if (cast.Matched)
                    continue;
                var windowStart = cast.CastEnd - options.MatchWindowBefore;
                var windowEnd = cast.CastEnd + options.MatchWindowAfter;
                if (ev.Timestamp < windowStart || ev.Timestamp > windowEnd)
                    continue;
                list[i] = cast with { Matched = true };
                castStart = cast.Op.Timestamp;
                castEnd = cast.CastEnd;
                accum.MatchedCastTimes.Add(cast.Op.TotalTime);
                accum.Interruptible |= cast.Op.Interruptible;
                break;
            }
        }

        var origin = source.PosRotAt(ev.Timestamp).Pos;
        var hits = ImmutableArray.CreateBuilder<AbilityHit>(ev.Targets.Length);
        foreach (var target in ev.Targets)
        {
            var victim = InferenceActors.At(index, target.InstanceId, ev.Timestamp);
            var pos = victim?.PosRotAt(ev.Timestamp).Pos ?? Vector3.Zero;
            hits.Add(new AbilityHit(target.InstanceId, pos, target.Effects));
        }

        accum.Instances.Add(new AbilityInstance(
            pullIndex,
            ev.InstanceId,
            castStart,
            castEnd,
            ev.Timestamp,
            origin,
            ev.Rotation,
            ev.MainTargetId,
            ev.TargetPos,
            hits.ToImmutable()));
    }

    private static void ObserveStatus(
        ActorStatusOp sta,
        ActorStatus status,
        Dictionary<ulong, List<ActorTimeline>> index,
        IGameData gameData,
        Dictionary<uint, StatusAccum> statuses)
    {
        _ = sta;
        if (!statuses.TryGetValue(status.Id, out var accum))
        {
            statuses[status.Id] = accum = new StatusAccum
            {
                Name = gameData.StatusName(status.Id) ?? $"Status{status.Id}",
            };
        }

        accum.Applications++;
        accum.Extras.Add(status.Extra);
        if (status.SourceId != 0)
        {
            var src = InferenceActors.At(index, status.SourceId, sta.Timestamp);
            if (src is not null && src.Oid != 0)
                accum.SourceOids.Add(src.Oid);
        }
    }

    private static void ObserveTether(
        ActorTetherOp tet,
        Dictionary<ulong, List<ActorTimeline>> index,
        Dictionary<uint, TetherAccum> tethers)
    {
        if (!tethers.TryGetValue(tet.TetherId, out var accum))
            tethers[tet.TetherId] = accum = new TetherAccum();
        accum.Count++;
        var src = InferenceActors.At(index, tet.InstanceId, tet.Timestamp);
        var dst = InferenceActors.At(index, tet.TargetId, tet.Timestamp);
        accum.Pairs.Add((src?.Oid ?? 0, dst?.Oid ?? 0));
    }

    private static void ObserveIcon(
        ActorIconOp icon,
        Dictionary<ulong, List<ActorTimeline>> index,
        Dictionary<uint, IconAccum> icons)
    {
        if (!icons.TryGetValue(icon.IconId, out var accum))
            icons[icon.IconId] = accum = new IconAccum();
        accum.Count++;
        var on = InferenceActors.At(index, icon.InstanceId, icon.Timestamp);
        icons[icon.IconId].TargetOids.Add(on?.Oid ?? 0);
    }

    private static AbilityAccum GetAbility(
        Dictionary<(uint Oid, uint ActionRaw), AbilityAccum> abilities,
        uint oid,
        ActionId action,
        IGameData gameData)
    {
        var key = (oid, action.Raw);
        if (!abilities.TryGetValue(key, out var accum))
        {
            var sheet = gameData.Action(action.Id);
            abilities[key] = accum = new AbilityAccum
            {
                SourceOid = oid,
                Action = action,
                Sheet = sheet,
                Name = sheet?.Name is { Length: > 0 } name ? name : $"Ability{action.Id}",
            };
        }

        return accum;
    }

    private static void ClearPending(
        Dictionary<(ulong InstanceId, uint ActionRaw), List<PendingCast>> pending,
        ulong instanceId)
    {
        var keys = new List<(ulong InstanceId, uint ActionRaw)>();
        foreach (var key in pending.Keys)
        {
            if (key.InstanceId == instanceId)
                keys.Add(key);
        }

        foreach (var key in keys)
            pending.Remove(key);
    }

    private static ImmutableArray<Ability> BuildAbilities(Dictionary<(uint Oid, uint ActionRaw), AbilityAccum> abilities)
    {
        var keys = new (uint Oid, uint ActionRaw)[abilities.Count];
        abilities.Keys.CopyTo(keys, 0);
        Array.Sort(keys, static (a, b) =>
        {
            var c = a.Oid.CompareTo(b.Oid);
            return c != 0 ? c : a.ActionRaw.CompareTo(b.ActionRaw);
        });

        var result = ImmutableArray.CreateBuilder<Ability>(keys.Length);
        foreach (var key in keys)
        {
            var accum = abilities[key];
            accum.Instances.Sort(static (a, b) =>
            {
                var c = a.EffectTime.CompareTo(b.EffectTime);
                return c != 0 ? c : a.SourceInstanceId.CompareTo(b.SourceInstanceId);
            });
            var unmatched = 0;
            foreach (var ev in accum.Evidence)
            {
                if (ev.Kind == "UnmatchedCast")
                    unmatched++;
            }

            var confidence = unmatched > 0
                ? Confidence.Of(0.7, "unmatched casts")
                : Confidence.Of(0.9);
            result.Add(new Ability(
                accum.SourceOid,
                accum.Action,
                accum.Name,
                accum.HasCastBar,
                Median(accum.MatchedCastTimes),
                accum.Interruptible,
                accum.Instances.Count,
                accum.Instances.Count == 0 ? ImmutableArray<AbilityInstance>.Empty : [.. accum.Instances],
                accum.Sheet,
                confidence,
                accum.Evidence.Count == 0 ? ImmutableArray<Evidence>.Empty : [.. accum.Evidence]));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<StatusEntry> BuildStatuses(Dictionary<uint, StatusAccum> statuses)
    {
        var ids = new uint[statuses.Count];
        statuses.Keys.CopyTo(ids, 0);
        Array.Sort(ids);
        var result = ImmutableArray.CreateBuilder<StatusEntry>(ids.Length);
        foreach (var id in ids)
        {
            var accum = statuses[id];
            result.Add(new StatusEntry(
                id,
                accum.Name,
                ToSortedImmutable(accum.SourceOids),
                accum.Applications,
                ToSortedImmutable(accum.Extras)));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<TetherEntry> BuildTethers(Dictionary<uint, TetherAccum> tethers)
    {
        var ids = new uint[tethers.Count];
        tethers.Keys.CopyTo(ids, 0);
        Array.Sort(ids);
        var result = ImmutableArray.CreateBuilder<TetherEntry>(ids.Length);
        foreach (var id in ids)
        {
            var accum = tethers[id];
            var pairs = new (uint SourceOid, uint TargetOid)[accum.Pairs.Count];
            accum.Pairs.CopyTo(pairs);
            Array.Sort(pairs, static (a, b) =>
            {
                var c = a.SourceOid.CompareTo(b.SourceOid);
                return c != 0 ? c : a.TargetOid.CompareTo(b.TargetOid);
            });
            result.Add(new TetherEntry(id, pairs.Length == 0 ? ImmutableArray<(uint, uint)>.Empty : [.. pairs], accum.Count));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<IconEntry> BuildIcons(Dictionary<uint, IconAccum> icons)
    {
        var ids = new uint[icons.Count];
        icons.Keys.CopyTo(ids, 0);
        Array.Sort(ids);
        var result = ImmutableArray.CreateBuilder<IconEntry>(ids.Length);
        foreach (var id in ids)
        {
            var accum = icons[id];
            result.Add(new IconEntry(id, ToSortedImmutable(accum.TargetOids), accum.Count));
        }

        return result.ToImmutable();
    }

    private static int? PullIndexAt(ImmutableArray<Pull> pulls, DateTime t)
    {
        foreach (var pull in pulls)
        {
            if (t >= pull.Start && t <= pull.End)
                return pull.Index;
        }

        return null;
    }

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
            return 0f;
        var copy = new float[values.Count];
        values.CopyTo(copy);
        Array.Sort(copy);
        var mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : (copy[mid - 1] + copy[mid]) / 2f;
    }

    private static ImmutableArray<uint> ToSortedImmutable(HashSet<uint> values)
    {
        if (values.Count == 0)
            return ImmutableArray<uint>.Empty;
        var arr = new uint[values.Count];
        values.CopyTo(arr);
        Array.Sort(arr);
        return [.. arr];
    }

    private static ImmutableArray<ushort> ToSortedImmutable(HashSet<ushort> values)
    {
        if (values.Count == 0)
            return ImmutableArray<ushort>.Empty;
        var arr = new ushort[values.Count];
        values.CopyTo(arr);
        Array.Sort(arr);
        return [.. arr];
    }

    private sealed class AbilityAccum
    {
        public uint SourceOid;
        public ActionId Action;
        public string Name = "";
        public bool HasCastBar;
        public bool Interruptible;
        public ActionRow? Sheet;
        public readonly List<float> MatchedCastTimes = [];
        public readonly List<AbilityInstance> Instances = [];
        public readonly List<Evidence> Evidence = [];
    }

    private sealed class StatusAccum
    {
        public string Name = "";
        public int Applications;
        public readonly HashSet<uint> SourceOids = [];
        public readonly HashSet<ushort> Extras = [];
    }

    private sealed class TetherAccum
    {
        public int Count;
        public readonly HashSet<(uint SourceOid, uint TargetOid)> Pairs = [];
    }

    private sealed class IconAccum
    {
        public int Count;
        public readonly HashSet<uint> TargetOids = [];
    }

    private readonly record struct PendingCast(ActorCastStartOp Op, DateTime CastEnd, uint SourceOid, bool Matched);
}
