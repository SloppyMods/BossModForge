namespace BossMod.Forge.SyntheticRotation;

public enum OID : uint
{
    Boss = 0x4444, // R5.000, x1
}

public enum AID : uint
{
    Opener = 40101,
    Circle = 40102,
    Cone = 40103,
    Raidwide2 = 40104,
    Circle2 = 40105,
    Buster = 40106,
    Raidwide3 = 40107,
    ConeAlt = 40108,
}

class Opener(BossModule module) : Components.RaidwideCast(module, AID.Opener);
class CircleAndCircle2(BossModule module) : Components.GroupedAOEs(module, [AID.Circle, AID.Circle2], new AOEShapeCircle(6));
class ConeAndConeAlt(BossModule module) : Components.GroupedAOEs(module, [AID.Cone, AID.ConeAlt], new AOEShapeCone(20, 45.Degrees()));
class Raidwide2(BossModule module) : Components.RaidwideCast(module, AID.Raidwide2);
class Buster(BossModule module) : Components.SingleTargetCast(module, AID.Buster);
class Raidwide3(BossModule module) : Components.RaidwideCast(module, AID.Raidwide3);

class SyntheticRotationStates : StateMachineBuilder
{
    public SyntheticRotationStates(BossModule module) : base(module)
    {
        DeathPhase(0, Phase0)
            .ActivateOnEnter<Buster>()
            .ActivateOnEnter<CircleAndCircle2>()
            .ActivateOnEnter<ConeAndConeAlt>()
            .ActivateOnEnter<Raidwide2>()
            .ActivateOnEnter<Raidwide3>();
    }

    private void Phase0(uint id)
    {
        Cast(id, AID.Opener, 2, 5, "Opener")
            .ActivateOnEnter<Opener>()
            .DeactivateOnExit<Opener>();
        // TODO(forge:R2, Medium): branch: pulls diverge (Cone 2/3, ConeAlt 1/3)
        CastMulti(id + 0x10000, [AID.Cone, AID.ConeAlt], 3.2f, 4, "Cone / ConeAlt");
        Rotation0(id + 0x20000, 6);
        Rotation0(id + 0x80000, 6);
        Cast(id + 0xE0000, AID.Circle, 6, 3, "Circle");
        Cast(id + 0xF0000, AID.Raidwide2, 4.5f, 4, "Raidwide2");
        Cast(id + 0x100000, AID.Cone, 5, 4, "Cone");
        SimpleState(id + 0xFF0000, 10, "???");
    }

    private void Rotation0(uint id, float delay)
    {
        Cast(id, AID.Circle, delay, 3, "Circle");
        Cast(id + 0x10000, AID.Raidwide2, 4.5f, 4, "Raidwide2");
        Cast(id + 0x20000, AID.Cone, 5, 4, "Cone");
        Cast(id + 0x30000, AID.Circle2, 7, 3, "Circle2");
        Cast(id + 0x40000, AID.Buster, 4, 4, "Buster");
        Cast(id + 0x50000, AID.Raidwide3, 5.5f, 5, "Raidwide3");
    }
}

// TODO(forge:E1) Set Expansion/Category
[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 826, NameID = 12716)]
public class SyntheticRotation(WorldState ws, Actor primary) : BossModule(ws, primary, new(100, 100), new ArenaBoundsCircle(20));
