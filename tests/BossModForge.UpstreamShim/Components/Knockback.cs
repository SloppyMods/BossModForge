using static BossMod.Components.Knockback;

namespace BossMod.Components;

public class Knockback(BossModule module, Enum? aid = default, bool ignoreImmunes = false, int maxCasts = int.MaxValue, bool stopAtWall = false) : CastCounter(module, aid)
{
    public enum Kind
    {
        None,
        AwayFromOrigin,
        TowardsOrigin,
        DirForward,
        DirLeft,
        DirRight,
    }
}

public class KnockbackFromCastTarget(BossModule module, Enum aid, float distance, bool ignoreImmunes = false, int maxCasts = int.MaxValue, AOEShape? shape = null, Kind kind = Kind.AwayFromOrigin, float minDistance = 0, bool minDistanceBetweenHitboxes = false, bool stopAtWall = false)
    : Knockback(module, aid, ignoreImmunes, maxCasts, stopAtWall);
