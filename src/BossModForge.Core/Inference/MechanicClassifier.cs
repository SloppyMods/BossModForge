using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

public enum MechanicClass
{
    Unclassified,
    Raidwide,
    Tankbuster,
    SharedTankbuster,
    Spread,
    Stack,
    Knockback,
    Attract,
    Gaze,
    Towers,
    Chasing,
    TelegraphedAOE,
    HiddenAOE,
    Instant,
    Interrupt,
}

public sealed record MechanicClassification(
    MechanicClass Class,
    string RuleId,
    ImmutableArray<Evidence> Evidence,
    Confidence? Confidence = null);

public static class MechanicClassifier
{
    public const string RuleKind = "MechanicRule";
    public const string ParamsKind = "MechanicParams";
    public const string BaitKind = "BaitRule";

    public static MechanicClassification Classify(
        Ability ability,
        Recording recording,
        ImmutableArray<Pull> pulls,
        ArenaFit arena,
        ShapeFit? fit,
        MechanicClass shapeClass)
    {
        _ = arena;
        var evidence = ImmutableArray.CreateBuilder<Evidence>();
        AddBait(ability, recording, pulls, evidence);

        if (shapeClass == MechanicClass.Raidwide)
            return Done(MechanicClass.Raidwide, "RW-1", evidence);

        if (TryTankbuster(ability, recording, pulls, out var tank))
            return Done(tank.Class, tank.RuleId, evidence, tank.Extra, tank.Confidence);

        if (TrySpread(ability, pulls, out var spread))
            return Done(MechanicClass.Spread, "SP-1", evidence, spread);

        if (TryStack(ability, recording, pulls, out var stack, out var stackConf))
            return Done(MechanicClass.Stack, "ST-1", evidence, stack, stackConf);

        if (TryKnockback(ability, recording, pulls, out var knock))
            return Done(MechanicClass.Knockback, "KB-1", evidence, knock);

        if (TryGaze(ability, recording, pulls))
            return Done(MechanicClass.Gaze, "GZ-1", evidence);

        if (TryTowers(ability, recording, pulls, fit))
            return Done(MechanicClass.Towers, "TW-1", evidence);

        if (TryChasing(ability))
            return Done(MechanicClass.Chasing, "CH-1", evidence);

        if (shapeClass == MechanicClass.TelegraphedAOE)
            return Done(MechanicClass.TelegraphedAOE, "TA-1", evidence);

        if (shapeClass == MechanicClass.HiddenAOE)
            return Done(MechanicClass.HiddenAOE, "HA-1", evidence);

        if (!ability.HasCastBar)
            return Done(MechanicClass.Instant, "IN-1", evidence);

        if (ability.Interruptible)
            return Done(MechanicClass.Interrupt, "IR-1", evidence);

        return Done(MechanicClass.Unclassified, "UC-1", evidence);
    }

    private static MechanicClassification Done(
        MechanicClass cls,
        string ruleId,
        ImmutableArray<Evidence>.Builder evidence,
        string? extraParams = null,
        Confidence? confidence = null)
    {
        evidence.Insert(0, new Evidence(RuleKind, ruleId));
        if (!string.IsNullOrEmpty(extraParams))
            evidence.Add(new Evidence(ParamsKind, extraParams));
        return new MechanicClassification(cls, ruleId, evidence.ToImmutable(), confidence);
    }

    private static bool TryTankbuster(
        Ability ability,
        Recording recording,
        ImmutableArray<Pull> pulls,
        out (MechanicClass Class, string RuleId, string? Extra, Confidence? Confidence) result)
    {
        result = default;
        if (ability.Instances.IsDefaultOrEmpty)
            return false;
        var castType = ability.Sheet?.CastType ?? 0;
        if (castType is not (0 or 1 or 5))
            return false;

        var shared = 0;
        var single = 0;
        var targMatch = 0;
        foreach (var inst in ability.Instances)
        {
            var pull = PullOf(inst, pulls);
            if (pull is null)
                continue;
            var hits = DamageHits(inst, pull);
            var t = inst.CastStart ?? inst.EffectTime;
            var targ = BossTargetAt(recording, inst.SourceInstanceId, t);
            var focus = inst.MainTargetId != 0 ? inst.MainTargetId : targ;
            if (targ != 0 && (inst.MainTargetId == targ || hits.Any(h => h.TargetInstanceId == targ)))
                targMatch++;
            if (hits.Count == 1)
                single++;
            else if (hits.Count is >= 2 and <= 3)
            {
                var origin = TargetPos(recording, focus, t, inst);
                if (hits.All(h => Vector2.Distance(Geometry2D.Xz(h.TargetPos), origin) <= 3f))
                    shared++;
            }
        }

        var n = ability.Instances.Length;
        if (n == 0 || targMatch * 2 < n)
            return false;
        if (single * 2 >= n && shared == 0)
        {
            result = (MechanicClass.Tankbuster, "TB-1", null, null);
            return true;
        }

        if (shared * 2 >= n)
        {
            result = (MechanicClass.SharedTankbuster, "TB-2", "radius=3", null);
            return true;
        }

        return false;
    }

    private static bool TrySpread(Ability ability, ImmutableArray<Pull> pulls, out string extra)
    {
        extra = "radius=6";
        if (ability.Instances.IsDefaultOrEmpty || ability.Instances.Length < 4)
            return false;
        var castType = ability.Sheet?.CastType ?? 0;
        if (castType is not (0 or 1 or 5))
            return false;

        var times = ability.Instances.Select(i => i.CastStart ?? i.EffectTime).OrderBy(t => t).ToArray();
        for (var i = 0; i < times.Length; i++)
        {
            var window = times[i] + TimeSpan.FromSeconds(0.5);
            var cluster = ability.Instances.Where(inst =>
            {
                var t = inst.CastStart ?? inst.EffectTime;
                return t >= times[i] && t <= window;
            }).ToArray();
            if (cluster.Length < 4)
                continue;
            var targets = cluster.Select(c => c.MainTargetId).Where(id => id != 0).Distinct().Count();
            if (targets >= 4)
            {
                extra = RadiusParam(ability, pulls, 6);
                return true;
            }
        }

        return false;
    }

    private static bool TryStack(
        Ability ability,
        Recording recording,
        ImmutableArray<Pull> pulls,
        out string extra,
        out Confidence? confidence)
    {
        extra = "radius=6";
        confidence = null;
        if (ability.Instances.IsDefaultOrEmpty)
            return false;

        var castType = ability.Sheet?.CastType ?? 0;
        var clustered = 0;
        var singleWithIcon = 0;
        var icons = 0;
        var radii = new List<float>();
        foreach (var inst in ability.Instances)
        {
            var pull = PullOf(inst, pulls);
            if (pull is null)
                continue;
            var hits = DamageHits(inst, pull);
            var t = inst.EffectTime;
            var focus = inst.MainTargetId != 0 ? inst.MainTargetId : (hits.Count > 0 ? hits[0].TargetInstanceId : 0UL);
            var origin = TargetPos(recording, focus, t, inst);
            if (HasIconNear(recording, focus, t))
                icons++;
            if (hits.Count == 1)
            {
                if (HasIconNear(recording, focus, t))
                    singleWithIcon++;
                continue;
            }

            if (hits.Count < 2)
                continue;
            var max = hits.Max(h => Vector2.Distance(Geometry2D.Xz(h.TargetPos), origin));
            if (max <= 8f)
            {
                clustered++;
                radii.Add(MathF.Max(3f, MathF.Ceiling(max + 0.5f)));
            }
        }

        var targeted = castType is 1 or 5 || icons * 2 >= ability.Instances.Length;
        if (!targeted)
            return false;

        if (clustered * 2 >= ability.Instances.Length && clustered > 0)
        {
            extra = "radius=" + radii.Average().ToString("0.#", CultureInfo.InvariantCulture);
            return true;
        }

        if (singleWithIcon * 2 >= ability.Instances.Length && singleWithIcon > 0)
        {
            extra = RadiusParam(ability, pulls, 6);
            confidence = Confidence.LowBecause("single hit stack");
            return true;
        }

        return false;
    }

    private static bool TryKnockback(
        Ability ability,
        Recording recording,
        ImmutableArray<Pull> pulls,
        out string extra)
    {
        extra = "distance=10";
        var distances = new List<int>();
        var away = 0;
        var samples = 0;
        foreach (var inst in ability.Instances.IsDefault ? [] : ability.Instances)
        {
            var pull = PullOf(inst, pulls);
            var origin = Geometry2D.Xz(inst.Origin);
            foreach (var hit in inst.Hits)
            {
                foreach (var effect in hit.Effects.NonEmpty())
                {
                    if (effect.Type != ActionEffectType.Knockback)
                        continue;
                    distances.Add(effect.Param0);
                    if (pull is null || !pull.PlayerInstanceIds.Contains(hit.TargetInstanceId))
                        continue;
                    var before = recording.ActorAt(hit.TargetInstanceId, inst.EffectTime)?.PosRotAt(inst.EffectTime).Pos;
                    var after = recording.ActorAt(hit.TargetInstanceId, inst.EffectTime + TimeSpan.FromSeconds(1.5))?.PosRotAt(inst.EffectTime + TimeSpan.FromSeconds(1.5)).Pos;
                    if (before is null || after is null)
                        continue;
                    var disp = Geometry2D.Xz(after.Value) - Geometry2D.Xz(before.Value);
                    if (disp.LengthSquared() < 0.25f)
                        continue;
                    samples++;
                    var expected = Geometry2D.Xz(before.Value) - origin;
                    if (expected.LengthSquared() < 1e-4f)
                        continue;
                    expected = Vector2.Normalize(expected);
                    if (Vector2.Dot(Vector2.Normalize(disp), expected) > 0.5f)
                        away++;
                }
            }
        }

        if (distances.Count == 0)
            return false;
        distances.Sort();
        var dist = distances[distances.Count / 2];
        var dir = samples > 0 && away * 2 >= samples ? "AwayFromOrigin" : "Directional";
        extra = "distance=" + dist.ToString(CultureInfo.InvariantCulture) + " direction=" + dir;
        return true;
    }

    private static bool TryGaze(Ability ability, Recording recording, ImmutableArray<Pull> pulls)
    {
        if (ability.Instances.IsDefaultOrEmpty)
            return false;
        var ok = 0;
        var n = 0;
        var hitFacing = 0;
        var missAway = 0;
        foreach (var inst in ability.Instances)
        {
            var pull = PullOf(inst, pulls);
            if (pull is null)
                continue;
            var origin = Geometry2D.Xz(inst.Origin);
            var t = inst.CastEnd ?? inst.EffectTime;
            var damaged = inst.Hits.Where(h => IsDamage(h)).Select(h => h.TargetInstanceId).ToHashSet();
            foreach (var pid in pull.PlayerInstanceIds)
            {
                var actor = recording.ActorAt(pid, t);
                if (actor is null || !actor.IsAliveAt(t) || actor.IsDeadAt(t))
                    continue;
                var pr = actor.PosRotAt(t);
                var pos = Geometry2D.Xz(pr.Pos);
                var toOrigin = origin - pos;
                if (toOrigin.LengthSquared() < 1e-4f)
                    continue;
                var facing = Vector2.Dot(Geometry2D.Facing(pr.Rot), Vector2.Normalize(toOrigin)) > 0.9f;
                var hit = damaged.Contains(pid);
                n++;
                if (facing == hit)
                    ok++;
                if (facing && hit)
                    hitFacing++;
                if (!facing && !hit)
                    missAway++;
            }
        }

        return n >= 4 && hitFacing > 0 && missAway > 0 && ok >= 0.85 * n;
    }

    private static bool TryTowers(Ability ability, Recording recording, ImmutableArray<Pull> pulls, ShapeFit? fit)
    {
        if (fit?.Shape.Kind is not ShapeKind.Circle || ability.Instances.IsDefaultOrEmpty)
            return false;
        var counts = new HashSet<int>();
        foreach (var inst in ability.Instances)
        {
            var pull = PullOf(inst, pulls);
            if (pull is null)
                return false;
            var hits = DamageHits(inst, pull).Count;
            if (hits is < 1 or > 3)
                return false;
            counts.Add(hits);
        }

        if (counts.Count != 1)
            return false;
        foreach (var op in recording.Ops)
        {
            if (op is EnvControlOp)
                return true;
        }

        return false;
    }

    private static bool TryChasing(Ability ability)
    {
        if (ability.Instances.IsDefaultOrEmpty || ability.Instances.Length < 3)
            return false;
        var inst = ability.Instances.OrderBy(i => i.EffectTime).ToArray();
        var dts = new List<double>();
        var dots = 0;
        Vector2? prevDelta = null;
        for (var i = 1; i < inst.Length; i++)
        {
            dts.Add((inst[i].EffectTime - inst[i - 1].EffectTime).TotalSeconds);
            var delta = Geometry2D.Xz(inst[i].Origin) - Geometry2D.Xz(inst[i - 1].Origin);
            if (delta.Length() < 1f)
                return false;
            if (prevDelta is { } p)
            {
                if (Vector2.Dot(Vector2.Normalize(p), Vector2.Normalize(delta)) > 0.7f)
                    dots++;
            }

            prevDelta = delta;
        }

        if (dts.Count == 0)
            return false;
        var mean = dts.Average();
        if (mean < 0.4 || mean > 8)
            return false;
        return dts.All(d => Math.Abs(d - mean) <= 0.4) && dots >= inst.Length - 2;
    }

    private static void AddBait(
        Ability ability,
        Recording recording,
        ImmutableArray<Pull> pulls,
        ImmutableArray<Evidence>.Builder evidence)
    {
        if (ability.Instances.IsDefaultOrEmpty)
            return;
        var nearest = 0;
        var farthest = 0;
        var considered = 0;
        foreach (var inst in ability.Instances)
        {
            if (inst.MainTargetId == 0)
                continue;
            var pull = PullOf(inst, pulls);
            if (pull is null)
                continue;
            var t = inst.CastStart ?? inst.EffectTime;
            var source = Geometry2D.Xz(inst.Origin);
            var ranked = new List<(ulong Id, float Dist)>();
            foreach (var pid in pull.PlayerInstanceIds)
            {
                var actor = recording.ActorAt(pid, t);
                if (actor is null || !actor.IsAliveAt(t) || actor.IsDeadAt(t))
                    continue;
                ranked.Add((pid, Vector2.Distance(source, Geometry2D.Xz(actor.PosRotAt(t).Pos))));
            }

            if (ranked.Count < 2)
                continue;
            ranked.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            considered++;
            if (ranked[0].Id == inst.MainTargetId)
                nearest++;
            if (ranked[^1].Id == inst.MainTargetId)
                farthest++;
        }

        if (considered == 0)
            return;
        if (nearest * 5 >= considered * 4)
            evidence.Add(new Evidence(BaitKind, "nearest"));
        else if (farthest * 5 >= considered * 4)
            evidence.Add(new Evidence(BaitKind, "farthest"));
    }

    private static string RadiusParam(Ability ability, ImmutableArray<Pull> pulls, float fallback)
    {
        var radii = new List<float>();
        foreach (var inst in ability.Instances.IsDefault ? [] : ability.Instances)
        {
            var pull = PullOf(inst, pulls);
            if (pull is null)
                continue;
            var hits = DamageHits(inst, pull);
            if (hits.Count == 0)
                continue;
            var origin = Geometry2D.Xz(hits[0].TargetPos);
            radii.Add(hits.Max(h => Vector2.Distance(Geometry2D.Xz(h.TargetPos), origin)));
        }

        var r = radii.Count == 0 ? fallback : MathF.Max(fallback, radii.Max() + 0.5f);
        return "radius=" + r.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static Pull? PullOf(AbilityInstance inst, ImmutableArray<Pull> pulls)
    {
        if (pulls.IsDefaultOrEmpty)
            return null;
        if ((uint)inst.PullIndex < (uint)pulls.Length)
            return pulls[inst.PullIndex];
        return null;
    }

    private static List<AbilityHit> DamageHits(AbilityInstance inst, Pull pull)
        => inst.Hits.Where(h => pull.PlayerInstanceIds.Contains(h.TargetInstanceId) && IsDamage(h)).ToList();

    private static bool IsDamage(AbilityHit hit)
        => hit.Effects.NonEmpty().Any(e => e.Type is ActionEffectType.Damage or ActionEffectType.BlockedDamage or ActionEffectType.ParriedDamage);

    private static ulong BossTargetAt(Recording recording, ulong bossId, DateTime t)
    {
        ulong current = 0;
        foreach (var op in recording.Ops)
        {
            if (op.Timestamp > t)
                break;
            if (op is ActorTargetOp targ && targ.InstanceId == bossId)
                current = targ.TargetId;
        }

        return current;
    }

    private static Vector2 TargetPos(Recording recording, ulong id, DateTime t, AbilityInstance inst)
    {
        if (id != 0)
        {
            var actor = recording.ActorAt(id, t);
            if (actor is not null)
                return Geometry2D.Xz(actor.PosRotAt(t).Pos);
        }

        return Geometry2D.Xz(inst.TargetPos == default ? inst.Origin : inst.TargetPos);
    }

    private static bool HasIconNear(Recording recording, ulong target, DateTime t)
    {
        var window = TimeSpan.FromSeconds(2.5);
        foreach (var op in recording.Ops)
        {
            if (op is ActorIconOp icon && icon.InstanceId == target && (icon.Timestamp - t).Duration() <= window)
                return true;
        }

        return false;
    }
}
