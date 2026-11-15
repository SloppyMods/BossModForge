namespace BossMod.Forge.SyntheticShapes;

public enum OID : uint
{
    Boss = 0x4321, // R5.000, x1
}

public enum AID : uint
{
    Ability41001 = 41001,
    Ability41002 = 41002,
    Ability41003 = 41003,
}

class Ability41001(BossModule module) : Components.StandardAOEs(module, AID.Ability41001, new AOEShapeCone(18, 40.Degrees()));
class Ability41002(BossModule module) : Components.StandardAOEs(module, AID.Ability41002, new AOEShapeCircle(7.5f));
class Ability41003(BossModule module) : Components.RaidwideCast(module, AID.Ability41003);

class SyntheticShapesStates : StateMachineBuilder
{
    public SyntheticShapesStates(BossModule module) : base(module)
    {
        DeathPhase(0, Phase0);
    }

    private void Phase0(uint id)
    {
        Cast(id, AID.Ability41001, 6.1f, 2, "Ability41001")
            .ActivateOnEnter<Ability41001>()
            .DeactivateOnExit<Ability41001>();
        Cast(id + 0x10000, AID.Ability41002, 4.1f, 2, "Ability41002")
            .ActivateOnEnter<Ability41002>()
            .DeactivateOnExit<Ability41002>();
        Cast(id + 0x20000, AID.Ability41003, 4.1f, 2, "Ability41003")
            .ActivateOnEnter<Ability41003>()
            .DeactivateOnExit<Ability41003>();
        SimpleState(id + 0xFF0000, 10, "???");
    }
}

// TODO(forge:E1) Set Expansion/Category
[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 826, NameID = 12716)]
public class SyntheticShapes(WorldState ws, Actor primary) : BossModule(ws, primary, new(100, 100), new ArenaBoundsCircle(19));
