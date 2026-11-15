using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Lumina.Excel;
using BossModForge.Core.GameData;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaBNpcName = Lumina.Excel.Sheets.BNpcName;
using LuminaStatus = Lumina.Excel.Sheets.Status;
using LuminaTerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace BossModForge.Plugin.Services;

public sealed class DalamudGameData
{
    private readonly IDataManager _data;

    public DalamudGameData(IDataManager data) => _data = data;

    public CachedGameData Snapshot(GameDataKeys keys)
    {
        var actions = ImmutableSortedDictionary.CreateBuilder<uint, ActionRow>();
        var bnpc = ImmutableSortedDictionary.CreateBuilder<uint, string>();
        var statuses = ImmutableSortedDictionary.CreateBuilder<uint, string>();
        var content = ImmutableSortedDictionary.CreateBuilder<uint, (uint CfcId, string Name)>();
        var places = ImmutableSortedDictionary.CreateBuilder<uint, string>();

        var actionSheet = Sheet<LuminaAction>();
        if (actionSheet is not null)
        {
            foreach (var id in keys.ActionIds)
            {
                var row = Row(actionSheet, id);
                if (row is not { } action)
                    continue;
                actions[id] = new ActionRow(
                    action.RowId,
                    ExtractName(action),
                    ReadCastType(action),
                    ReadEffectRange(action),
                    ReadXAxisModifier(action),
                    ReadCast100ms(action),
                    ReadOmenId(action),
                    ReadOmenPath(action));
            }
        }

        var bnpcSheet = Sheet<LuminaBNpcName>();
        if (bnpcSheet is not null)
        {
            foreach (var id in keys.BNpcNameIds)
            {
                var row = Row(bnpcSheet, id);
                if (row is not { } nameRow)
                    continue;
                var name = NullIfEmpty(ExtractSingular(nameRow));
                if (name is not null)
                    bnpc[id] = name;
            }
        }

        var statusSheet = Sheet<LuminaStatus>();
        if (statusSheet is not null)
        {
            foreach (var id in keys.StatusIds)
            {
                var row = Row(statusSheet, id);
                if (row is not { } status)
                    continue;
                var name = NullIfEmpty(ExtractStatusName(status));
                if (name is not null)
                    statuses[id] = name;
            }
        }

        var territorySheet = Sheet<LuminaTerritoryType>();
        if (territorySheet is not null)
        {
            foreach (var tt in keys.TerritoryTypes)
            {
                var row = Row(territorySheet, tt);
                if (row is not { } territory)
                    continue;
                var cfcId = ReadCfcId(territory);
                var cfcName = NullIfEmpty(ReadCfcName(territory));
                if (cfcName is not null)
                    content[tt] = (cfcId, cfcName);
                var place = NullIfEmpty(ReadPlaceName(territory));
                if (place is not null)
                    places[tt] = place;
            }
        }

        return new CachedGameData(
            actions.ToImmutable(),
            bnpc.ToImmutable(),
            statuses.ToImmutable(),
            content.ToImmutable(),
            places.ToImmutable());
    }

    private ExcelSheet<T>? Sheet<T>() where T : struct, IExcelRow<T>
    {
        try
        {
            return _data.GetExcelSheet<T>();
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, $"GetExcelSheet<{typeof(T).Name}> failed");
            return null;
        }
    }

    private static T? Row<T>(ExcelSheet<T> sheet, uint id) where T : struct, IExcelRow<T>
        => sheet.GetRowOrDefault(id);

    // VERIFY-1: Action.Name.ExtractText()
    private static string ExtractName(LuminaAction row) => row.Name.ExtractText();

    // VERIFY-1: Action.CastType
    private static byte ReadCastType(LuminaAction row) => row.CastType;

    // VERIFY-1: Action.EffectRange
    private static byte ReadEffectRange(LuminaAction row) => row.EffectRange;

    // VERIFY-1: Action.XAxisModifier
    private static byte ReadXAxisModifier(LuminaAction row) => row.XAxisModifier;

    // VERIFY-1: Action.Cast100ms
    private static ushort ReadCast100ms(LuminaAction row) => row.Cast100ms;

    // VERIFY-1: Action.Omen.RowId
    private static uint ReadOmenId(LuminaAction row) => row.Omen.RowId;

    // VERIFY-2: Omen.ValueNullable?.Path.ExtractText() for OmenParser.TryParseFan
    private static string? ReadOmenPath(LuminaAction row)
        => NullIfEmpty(row.Omen.ValueNullable?.Path.ExtractText());

    private static string ExtractSingular(LuminaBNpcName row) => row.Singular.ExtractText();

    private static string ExtractStatusName(LuminaStatus row) => row.Name.ExtractText();

    // VERIFY-3: TerritoryType.ContentFinderCondition.RowId
    private static uint ReadCfcId(LuminaTerritoryType row) => row.ContentFinderCondition.RowId;

    // VERIFY-3: TerritoryType.ContentFinderCondition.ValueNullable?.Name.ExtractText()
    private static string? ReadCfcName(LuminaTerritoryType row)
        => row.ContentFinderCondition.ValueNullable?.Name.ExtractText();

    // VERIFY-3: TerritoryType.PlaceName.ValueNullable?.Name.ExtractText()
    private static string? ReadPlaceName(LuminaTerritoryType row)
        => row.PlaceName.ValueNullable?.Name.ExtractText();

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}
