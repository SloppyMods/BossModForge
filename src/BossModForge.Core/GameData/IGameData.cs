namespace BossModForge.Core.GameData;

public sealed record ActionRow(
    uint RowId,
    string Name,
    byte CastType,
    byte EffectRange,
    byte XAxisModifier,
    ushort Cast100ms,
    uint OmenId,
    string? OmenPath = null);

public interface IGameData
{
    ActionRow? Action(uint id);
    string? BNpcName(uint nameId);
    string? StatusName(uint id);
    (uint CfcId, string Name)? ContentForTerritory(uint territoryType);
    string? PlaceName(uint territoryType);
}

public sealed class NullGameData : IGameData
{
    public static readonly NullGameData Instance = new();

    public ActionRow? Action(uint id) => null;
    public string? BNpcName(uint nameId) => null;
    public string? StatusName(uint id) => null;
    public (uint CfcId, string Name)? ContentForTerritory(uint territoryType) => null;
    public string? PlaceName(uint territoryType) => null;
}
