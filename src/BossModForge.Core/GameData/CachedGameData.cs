using System.Collections.Immutable;

namespace BossModForge.Core.GameData;

public sealed class CachedGameData : IGameData
{
    public static readonly CachedGameData Empty = new(
        ImmutableSortedDictionary<uint, ActionRow>.Empty,
        ImmutableSortedDictionary<uint, string>.Empty,
        ImmutableSortedDictionary<uint, string>.Empty,
        ImmutableSortedDictionary<uint, (uint CfcId, string Name)>.Empty,
        ImmutableSortedDictionary<uint, string>.Empty);

    private readonly ImmutableSortedDictionary<uint, ActionRow> _actions;
    private readonly ImmutableSortedDictionary<uint, string> _bnpcNames;
    private readonly ImmutableSortedDictionary<uint, string> _statusNames;
    private readonly ImmutableSortedDictionary<uint, (uint CfcId, string Name)> _contentByTerritory;
    private readonly ImmutableSortedDictionary<uint, string> _placeNames;

    public CachedGameData(
        ImmutableSortedDictionary<uint, ActionRow> actions,
        ImmutableSortedDictionary<uint, string> bnpcNames,
        ImmutableSortedDictionary<uint, string> statusNames,
        ImmutableSortedDictionary<uint, (uint CfcId, string Name)> contentByTerritory,
        ImmutableSortedDictionary<uint, string> placeNames)
    {
        _actions = actions;
        _bnpcNames = bnpcNames;
        _statusNames = statusNames;
        _contentByTerritory = contentByTerritory;
        _placeNames = placeNames;
        Count = actions.Count + bnpcNames.Count + statusNames.Count + contentByTerritory.Count + placeNames.Count;
    }

    public int Count { get; }

    public ActionRow? Action(uint id) => _actions.TryGetValue(id, out var row) ? row : null;

    public string? BNpcName(uint nameId) => _bnpcNames.TryGetValue(nameId, out var name) ? name : null;

    public string? StatusName(uint id) => _statusNames.TryGetValue(id, out var name) ? name : null;

    public (uint CfcId, string Name)? ContentForTerritory(uint territoryType)
        => _contentByTerritory.TryGetValue(territoryType, out var row) ? row : null;

    public string? PlaceName(uint territoryType) => _placeNames.TryGetValue(territoryType, out var name) ? name : null;
}
