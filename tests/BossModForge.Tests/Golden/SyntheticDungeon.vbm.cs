namespace BossMod.Forge.SyntheticDungeon;

public enum OID : uint
{
    Boss = 0x4321, // R5.000, x1
    Helper = 0x233C, // R0.500, x1, Helper type
}

public enum AID : uint
{
    Ability40001 = 40001,
}

public enum SID : uint
{
    Status1234 = 1234,
}

public enum IconID : uint
{
    Icon50 = 50,
}

public enum TetherID : uint
{
    Tether7 = 7,
}

class Ability40001(BossModule module) : Components.RaidwideCast(module, AID.Ability40001);

class SyntheticDungeonStates : StateMachineBuilder
{
    public SyntheticDungeonStates(BossModule module) : base(module)
    {
        DeathPhase(0, Phase0);
    }

    private void Phase0(uint id)
    {
        Cast(id, AID.Ability40001, 0, 5, "Ability40001")
            .ActivateOnEnter<Ability40001>()
            .DeactivateOnExit<Ability40001>();
        SimpleState(id + 0xFF0000, 10, "???");
    }
}

// TODO(forge:E1) Set Expansion/Category
[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 826, NameID = 12716)]
public class SyntheticDungeon(WorldState ws, Actor primary) : BossModule(ws, primary, new(100, 100), new ArenaBoundsCircle(20));
