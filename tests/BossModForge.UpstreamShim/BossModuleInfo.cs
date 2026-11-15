namespace BossMod;

public static class BossModuleInfo
{
    public enum Maturity
    {
        WIP,
        Contributed,
        Verified
    }

    public enum Expansion
    {
        RealmReborn,
        Heavensward,
        Stormblood,
        Shadowbringers,
        Endwalker,
        Dawntrail,
        Global,
        Count
    }

    public enum Category
    {
        Uncategorized,
        Dungeon,
        Trial,
        Extreme,
        Raid,
        Savage,
        Ultimate,
        Unreal,
        Alliance,
        Chaotic,
        Foray,
        Criterion,
        DeepDungeon,
        FATE,
        Hunt,
        Quest,
        TreasureHunt,
        PVP,
        MaskedCarnivale,
        GoldSaucer,
        Count
    }

    public enum GroupType
    {
        None,
        CFC,
        MaskedCarnivale,
        RemovedUnreal,
        Quest,
        Fate,
        Hunt,
        BozjaCE,
        BozjaDuel,
        EurekaNM,
        GoldSaucer,
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleInfoAttribute(BossModuleInfo.Maturity maturity) : Attribute
{
    public Type? StatesType { get; set; }
    public Type? ConfigType { get; set; }
    public Type? ObjectIDType { get; set; }
    public Type? ActionIDType { get; set; }
    public Type? StatusIDType { get; set; }
    public Type? TetherIDType { get; set; }
    public Type? IconIDType { get; set; }
    public uint PrimaryActorOID { get; set; }
    public BossModuleInfo.Maturity Maturity { get; } = maturity;
    public string Contributors { get; set; } = "";
    public BossModuleInfo.Expansion Expansion { get; set; } = BossModuleInfo.Expansion.Count;
    public BossModuleInfo.Category Category { get; set; } = BossModuleInfo.Category.Count;
    public BossModuleInfo.GroupType GroupType { get; set; } = BossModuleInfo.GroupType.None;
    public uint GroupID { get; set; }
    public uint NameID { get; set; }
    public int SortOrder { get; set; }
    public int PlanLevel { get; set; }
}
