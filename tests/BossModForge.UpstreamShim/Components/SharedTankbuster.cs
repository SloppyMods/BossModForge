namespace BossMod.Components;

public class GenericSharedTankbuster(BossModule module, Enum? aid, AOEShape shape, bool originAtTarget = false) : CastCounter(module, aid)
{
    public GenericSharedTankbuster(BossModule module, Enum? aid, float radius) : this(module, aid, new AOEShapeCircle(radius), true) { }
}

public class CastSharedTankbuster(BossModule module, Enum aid, AOEShape shape, bool originAtTarget = false) : GenericSharedTankbuster(module, aid, shape, originAtTarget)
{
    public CastSharedTankbuster(BossModule module, Enum aid, float radius) : this(module, aid, new AOEShapeCircle(radius), true) { }
}
