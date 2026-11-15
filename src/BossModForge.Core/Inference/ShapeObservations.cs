using System.Collections.Immutable;
using System.Numerics;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

/// <summary>
/// Public hit/miss observation builder matching <c>ShapeInference.Infer</c> snapshotting.
/// Lives in a new file so <c>ShapeInference.cs</c> can stay untouched while another builder works.
/// </summary>
public static class ShapeObservations
{
    public static ImmutableArray<ShapeObservation> Observe(
        Ability ability,
        Recording recording,
        ImmutableArray<Pull> pulls,
        float lagSeconds)
    {
        if (ability.Instances.IsDefaultOrEmpty)
            return [];

        var list = ImmutableArray.CreateBuilder<ShapeObservation>();
        foreach (var inst in ability.Instances)
        {
            if (pulls.IsDefaultOrEmpty || inst.PullIndex < 0 || inst.PullIndex >= pulls.Length)
                continue;
            var pull = pulls[inst.PullIndex];
            if (!HasPlayerDamage(inst, pull))
                continue;
            list.Add(ObserveInstance(inst, recording, pull, lagSeconds));
        }

        return list.ToImmutable();
    }

    public static bool HasPlayerDamage(AbilityInstance inst, Pull pull)
    {
        foreach (var hit in inst.Hits.IsDefaultOrEmpty ? [] : inst.Hits)
        {
            if (!pull.PlayerInstanceIds.Contains(hit.TargetInstanceId))
                continue;
            if (IsDamaging(hit))
                return true;
        }

        return false;
    }

    public static bool IsDamaging(AbilityHit hit)
        => hit.Effects.NonEmpty().Any(e =>
            e.Type is ActionEffectType.Damage or ActionEffectType.BlockedDamage or ActionEffectType.ParriedDamage);

    private static ShapeObservation ObserveInstance(AbilityInstance inst, Recording recording, Pull pull, float delta)
    {
        var t0 = inst.CastEnd ?? inst.EffectTime;
        var tSnap = t0 + TimeSpan.FromSeconds(delta);
        var origin = inst.Origin;
        if (origin == Vector3.Zero)
        {
            var src = recording.ActorAt(inst.SourceInstanceId, tSnap);
            origin = src?.PosRotAt(tSnap).Pos ?? origin;
        }

        Vector2? targetActor = null;
        if (inst.MainTargetId != 0)
        {
            var actor = recording.ActorAt(inst.MainTargetId, tSnap);
            if (actor is not null)
                targetActor = Geometry2D.Xz(actor.PosRotAt(tSnap).Pos);
        }

        var players = ImmutableArray.CreateBuilder<(Vector2 Pos, bool Hit)>();
        foreach (var pid in pull.PlayerInstanceIds.IsDefaultOrEmpty ? [] : pull.PlayerInstanceIds)
        {
            var actor = recording.ActorAt(pid, tSnap);
            if (actor is null || actor.IsDeadAt(tSnap) || !actor.IsAliveAt(tSnap))
                continue;
            var hit = inst.Hits.Any(h => h.TargetInstanceId == pid && IsDamaging(h));
            players.Add((Geometry2D.Xz(actor.PosRotAt(tSnap).Pos), hit));
        }

        return new ShapeObservation(
            Geometry2D.Xz(origin),
            inst.Rotation,
            Geometry2D.Xz(inst.TargetPos),
            targetActor,
            players.ToImmutable());
    }
}
