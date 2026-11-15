namespace BossMod;

public abstract class BossModule
{
    public Actor PrimaryActor { get; }

    protected BossModule(WorldState ws, Actor primary, WPos center, ArenaBounds bounds)
    {
        PrimaryActor = primary;
    }
}
