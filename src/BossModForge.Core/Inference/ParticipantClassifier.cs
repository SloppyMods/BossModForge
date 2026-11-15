using System.Collections.Immutable;
using BossModForge.Core.GameData;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

public enum ParticipantRole
{
    Boss,
    Helper,
    Add,
    Caster,
    EventObject,
    Player,
    Pet,
    Unknown,
}

public sealed record Participant(
    uint Oid,
    string Name,
    uint NameId,
    ActorType Type,
    ParticipantRole Role,
    float HitboxRadius,
    int InstanceCount,
    int PullCount,
    bool SpawnsDuringFight,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence);

public static class ParticipantClassifier
{
    public const uint HelperOid = 0x233C;

    public static ImmutableArray<Participant> Classify(
        Recording recording,
        ImmutableArray<Pull> pulls,
        IGameData gameData)
    {
        if (pulls.IsDefaultOrEmpty)
            return ImmutableArray<Participant>.Empty;

        var index = InferenceActors.Index(recording.Actors);
        var byOid = new Dictionary<uint, List<ActorTimeline>>();
        foreach (var actor in recording.Actors)
        {
            if (!OverlapsAny(actor, pulls))
                continue;
            if (!byOid.TryGetValue(actor.Oid, out var list))
                byOid[actor.Oid] = list = [];
            list.Add(actor);
        }

        var oids = new uint[byOid.Count];
        byOid.Keys.CopyTo(oids, 0);
        Array.Sort(oids);

        var result = ImmutableArray.CreateBuilder<Participant>(oids.Length);
        foreach (var oid in oids)
        {
            var instances = byOid[oid];
            instances.Sort(static (a, b) =>
            {
                var c = a.Created.CompareTo(b.Created);
                if (c != 0)
                    return c;
                c = a.InstanceId.CompareTo(b.InstanceId);
                return c != 0 ? c : a.Incarnation.CompareTo(b.Incarnation);
            });

            var representative = instances[0];
            var nameId = representative.NameId;
            var name = gameData.BNpcName(nameId) ?? representative.Name;
            var radius = 0f;
            foreach (var instance in instances)
            {
                if (instance.HitboxRadius > radius)
                    radius = instance.HitboxRadius;
            }

            var pullCount = 0;
            foreach (var pull in pulls)
            {
                if (OverlapsAny(instances, pull))
                    pullCount++;
            }

            var firstCreated = instances[0].Created;
            var spawnsDuringFight = false;
            foreach (var pull in pulls)
            {
                if (firstCreated > pull.Start)
                {
                    spawnsDuringFight = true;
                    break;
                }
            }

            var (role, confidence, evidence) = ClassifyRole(oid, instances, pulls, index, recording);
            result.Add(new Participant(
                oid,
                name,
                nameId,
                representative.Type,
                role,
                radius,
                instances.Count,
                pullCount,
                spawnsDuringFight,
                confidence,
                evidence));
        }

        return result.ToImmutable();
    }

    private static (ParticipantRole Role, Confidence Confidence, ImmutableArray<Evidence> Evidence) ClassifyRole(
        uint oid,
        List<ActorTimeline> instances,
        ImmutableArray<Pull> pulls,
        Dictionary<ulong, List<ActorTimeline>> index,
        Recording recording)
    {
        if (AnyType(instances, ActorType.Player))
            return High(ParticipantRole.Player, "Type is Player");
        if (AnyType(instances, ActorType.Pet) || AnyType(instances, ActorType.Chocobo) || AnyOwnerIsPlayer(instances, index))
            return High(ParticipantRole.Pet, "Pet, chocobo, or player-owned");
        if (AnyType(instances, ActorType.Helper) || oid == HelperOid)
            return High(ParticipantRole.Helper, "Helper type or OID 0x233C");
        if (AnyType(instances, ActorType.EventObj))
            return High(ParticipantRole.EventObject, "Type is EventObj");

        var isPrimary = false;
        foreach (var pull in pulls)
        {
            if (pull.PrimaryOid == oid)
            {
                isPrimary = true;
                break;
            }
        }

        if (isPrimary)
            return High(ParticipantRole.Boss, "OID matches pull primary");

        var isEnemy = AnyType(instances, ActorType.Enemy);
        var everTargetable = EverTargetable(instances);
        var hasCasts = HasCasts(instances, recording, pulls);
        if (isEnemy && hasCasts && !everTargetable)
        {
            return (
                ParticipantRole.Caster,
                Confidence.Of(0.75, "untargetable enemy that casts"),
                [new Evidence("Role", "Caster")]);
        }

        if (isEnemy && everTargetable)
            return High(ParticipantRole.Add, "targetable enemy");

        return (
            ParticipantRole.Unknown,
            Confidence.LowBecause("unclassified"),
            [new Evidence("Role", "Unknown")]);
    }

    private static (ParticipantRole, Confidence, ImmutableArray<Evidence>) High(ParticipantRole role, string reason)
        => (role, Confidence.Of(0.9, reason), [new Evidence("Role", role.ToString())]);

    private static bool AnyType(List<ActorTimeline> instances, ActorType type)
    {
        foreach (var instance in instances)
        {
            if (instance.Type == type)
                return true;
        }

        return false;
    }

    private static bool AnyOwnerIsPlayer(List<ActorTimeline> instances, Dictionary<ulong, List<ActorTimeline>> index)
    {
        foreach (var instance in instances)
        {
            if (InferenceActors.OwnerIsPlayer(instance, index))
                return true;
        }

        return false;
    }

    private static bool EverTargetable(List<ActorTimeline> instances)
    {
        foreach (var instance in instances)
        {
            foreach (var sample in instance.Targetable)
            {
                if (sample.Value)
                    return true;
            }
        }

        return false;
    }

    private static bool HasCasts(List<ActorTimeline> instances, Recording recording, ImmutableArray<Pull> pulls)
    {
        var ids = new HashSet<ulong>(instances.Count);
        foreach (var instance in instances)
            ids.Add(instance.InstanceId);

        foreach (var op in recording.Ops)
        {
            if (!InAnyPull(op.Timestamp, pulls))
                continue;
            var src = op switch
            {
                ActorCastStartOp s => s.InstanceId,
                ActorCastEventOp e => e.InstanceId,
                _ => 0UL,
            };
            if (src != 0 && ids.Contains(src))
                return true;
        }

        return false;
    }

    private static bool OverlapsAny(ActorTimeline actor, ImmutableArray<Pull> pulls)
    {
        foreach (var pull in pulls)
        {
            if (Overlaps(actor, pull))
                return true;
        }

        return false;
    }

    private static bool OverlapsAny(List<ActorTimeline> instances, Pull pull)
    {
        foreach (var instance in instances)
        {
            if (Overlaps(instance, pull))
                return true;
        }

        return false;
    }

    private static bool Overlaps(ActorTimeline actor, Pull pull)
    {
        var destroyed = actor.Destroyed ?? DateTime.MaxValue;
        return actor.Created <= pull.End && destroyed > pull.Start;
    }

    private static bool InAnyPull(DateTime t, ImmutableArray<Pull> pulls)
    {
        foreach (var pull in pulls)
        {
            if (t >= pull.Start && t <= pull.End)
                return true;
        }

        return false;
    }
}
