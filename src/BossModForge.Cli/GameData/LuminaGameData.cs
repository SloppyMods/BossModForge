using BossModForge.Core.GameData;
using Lumina;
using Lumina.Excel;
using SheetAction = Lumina.Excel.Sheets.Action;
using SheetBNpcName = Lumina.Excel.Sheets.BNpcName;
using SheetStatus = Lumina.Excel.Sheets.Status;
using SheetTerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace BossModForge.Cli.GameData;

/// <summary>
/// Disk-based <see cref="IGameData"/> backed by a local FFXIV install via Lumina 5.
/// The ctor (and every member) never throws: an unreadable path yields
/// <see cref="Available"/> == false and null lookups, matching <see cref="NullGameData"/>.
/// </summary>
public sealed class LuminaGameData : IGameData, IDisposable
{
    private readonly Lumina.GameData? _gameData;
    private readonly ExcelSheet<SheetAction>? _actions;
    private readonly ExcelSheet<SheetBNpcName>? _bnpcNames;
    private readonly ExcelSheet<SheetStatus>? _statuses;
    private readonly ExcelSheet<SheetTerritoryType>? _territories;

    public LuminaGameData(string gamePath)
    {
        try
        {
            _gameData = new Lumina.GameData(gamePath);
            _actions = _gameData.Excel.GetSheet<SheetAction>();
            _bnpcNames = _gameData.Excel.GetSheet<SheetBNpcName>();
            _statuses = _gameData.Excel.GetSheet<SheetStatus>();
            _territories = _gameData.Excel.GetSheet<SheetTerritoryType>();
            Available = true;
        }
        catch (Exception)
        {
            // Not a readable FFXIV install — degrade to the NullGameData behaviour.
            Available = false;
        }
    }

    public bool Available { get; }

    public ActionRow? Action(uint id)
    {
        if (!Available || _actions is not { } actions)
            return null;
        try
        {
            if (actions.GetRowOrDefault(id) is not { } row)
                return null;
            var omenPath = row.Omen.ValueNullable?.Path.ExtractText();
            return new ActionRow(
                row.RowId,
                row.Name.ExtractText(),
                row.CastType,
                row.EffectRange,
                row.XAxisModifier,
                row.Cast100ms,
                row.Omen.RowId,
                string.IsNullOrEmpty(omenPath) ? null : omenPath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? BNpcName(uint nameId)
    {
        if (!Available || _bnpcNames is not { } names)
            return null;
        try
        {
            return names.GetRowOrDefault(nameId)?.Singular.ExtractText();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? StatusName(uint id)
    {
        if (!Available || _statuses is not { } statuses)
            return null;
        try
        {
            return statuses.GetRowOrDefault(id)?.Name.ExtractText();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public (uint CfcId, string Name)? ContentForTerritory(uint territoryType)
    {
        if (!Available || _territories is not { } territories)
            return null;
        try
        {
            if (territories.GetRowOrDefault(territoryType) is not { } territory)
                return null;
            var cfc = territory.ContentFinderCondition.ValueNullable;
            if (cfc == null)
                return null;
            var name = cfc.Value.Name.ExtractText();
            return string.IsNullOrEmpty(name) ? null : (cfc.Value.RowId, name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? PlaceName(uint territoryType)
    {
        if (!Available || _territories is not { } territories)
            return null;
        try
        {
            if (territories.GetRowOrDefault(territoryType) is not { } territory)
                return null;
            var place = territory.PlaceName.ValueNullable;
            if (place == null)
                return null;
            var name = place.Value.Name.ExtractText();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose() => _gameData?.Dispose();
}
