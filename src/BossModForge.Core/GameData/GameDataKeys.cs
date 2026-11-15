using System.Collections.Immutable;
using BossModForge.Core.Replay;

namespace BossModForge.Core.GameData;

public sealed record GameDataKeys(
    ImmutableSortedSet<uint> ActionIds,
    ImmutableSortedSet<uint> BNpcNameIds,
    ImmutableSortedSet<uint> StatusIds,
    ImmutableSortedSet<ushort> TerritoryTypes)
{
    public static GameDataKeys Collect(Recording recording)
    {
        var actions = ImmutableSortedSet.CreateBuilder<uint>();
        var bnpc = ImmutableSortedSet.CreateBuilder<uint>();
        var statuses = ImmutableSortedSet.CreateBuilder<uint>();
        var territories = ImmutableSortedSet.CreateBuilder<ushort>();

        foreach (var actor in recording.Actors)
        {
            if (actor.Type != ActorType.Player && actor.NameId != 0)
                bnpc.Add(actor.NameId);
        }

        foreach (var op in recording.Ops)
        {
            switch (op)
            {
                case ActorCastStartOp cast when cast.Action.Type == ActionType.Spell:
                    actions.Add(cast.Action.Id);
                    break;
                case ActorCastEventOp ev when ev.Action.Type == ActionType.Spell:
                    actions.Add(ev.Action.Id);
                    break;
                case ActorRenameOp rename when rename.NameId != 0:
                    bnpc.Add(rename.NameId);
                    break;
                case ActorStatusOp status when status.Status is { } sta && sta.Id != 0:
                    statuses.Add(sta.Id);
                    break;
                case ZoneChangeOp zone:
                    territories.Add(zone.Zone);
                    break;
            }
        }

        return new GameDataKeys(actions.ToImmutable(), bnpc.ToImmutable(), statuses.ToImmutable(), territories.ToImmutable());
    }
}
