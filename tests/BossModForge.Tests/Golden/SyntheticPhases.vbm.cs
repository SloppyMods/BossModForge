namespace BossMod.Forge.SyntheticPhases;

public enum OID : uint
{
    Boss = 0x4444, // R5.000, x1
}

public enum AID : uint
{
    A = 40201,
    B = 40202,
    C = 40203,
    D = 40204,
}

class A(BossModule module) : Components.RaidwideCast(module, AID.A);
class B(BossModule module) : Components.RaidwideCast(module, AID.B);
class C(BossModule module) : Components.RaidwideCast(module, AID.C);
class D(BossModule module) : Components.RaidwideCast(module, AID.D);

class SyntheticPhasesStates : StateMachineBuilder
{
    public SyntheticPhasesStates(BossModule module) : base(module)
    {
        SimplePhase(0, Phase0, "P0")
            .Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed || Module.PrimaryActor.HPRatio <= 0.5f;
        DeathPhase(1, Phase1)
            .ActivateOnEnter<C>()
            .ActivateOnEnter<D>();
    }

    private void Phase0(uint id)
    {
        Cast(id, AID.A, 2, 4, "A")
            .ActivateOnEnter<A>();
        Cast(id + 0x10000, AID.B, 2, 4, "B")
            .ActivateOnEnter<B>();
        Cast(id + 0x20000, AID.A, 2, 4, "A")
            .DeactivateOnExit<A>();
        Cast(id + 0x30000, AID.B, 2, 4, "B")
            .DeactivateOnExit<B>();
    }

    private void Phase1(uint id)
    {
        Targetable(id, false, 25.1f, "Boss disappears");
        Targetable(id + 0x10000, true, 12, "Boss reappears");
        Rotation1(id + 0x20000, 1);
        Rotation1(id + 0x40000, 2);
        SimpleState(id + 0xFF0000, 10, "???");
    }

    private void Rotation1(uint id, float delay)
    {
        Cast(id, AID.C, delay, 5, "C");
        Cast(id + 0x10000, AID.D, 2, 4, "D");
    }
}

// TODO(forge:E1) Set Expansion/Category
[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 826, NameID = 12716)]
public class SyntheticPhases(WorldState ws, Actor primary) : BossModule(ws, primary, new(100, 100), new ArenaBoundsCircle(20));
