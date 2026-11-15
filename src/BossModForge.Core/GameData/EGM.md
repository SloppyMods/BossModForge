# GameData

## Overview
Abstraction over static game-data lookups (actions, NPC names, statuses, territory/content names) plus a collector that scans a `Recording` for the ids that need resolving. Provides a null implementation and an immutable in-memory implementation; this directory contains no loader/I-O code.

## Interface
- `ActionRow` record (`RowId`, `Name`, `CastType`, `EffectRange`, `XAxisModifier`, `Cast100ms`, `OmenId`, `OmenPath = null`) [GameData/IGameData.cs:ActionRow]
- `IGameData` interface: `Action(uint)`, `BNpcName(uint)`, `StatusName(uint)`, `ContentForTerritory(uint) -> (uint CfcId, string Name)?`, `PlaceName(uint)`; all return nullable [GameData/IGameData.cs:IGameData]
- `NullGameData` with singleton `NullGameData.Instance`; every lookup returns `null` [GameData/IGameData.cs:NullGameData]
- `CachedGameData` implements `IGameData` over five `ImmutableSortedDictionary<uint, ...>` passed to its constructor; `Count` is the sum of all five dictionary counts; static `CachedGameData.Empty` [GameData/CachedGameData.cs:CachedGameData]
- `GameDataKeys` record (`ActionIds`, `BNpcNameIds`, `StatusIds` as `ImmutableSortedSet<uint>`; `TerritoryTypes` as `ImmutableSortedSet<ushort>`) with `static GameDataKeys Collect(Recording)` [GameData/GameDataKeys.cs:GameDataKeys.Collect]
- Subdirectories: None

## Invariants
- `NullGameData` returns `null` from every method [GameData/IGameData.cs:NullGameData]
- `CachedGameData` lookups use `TryGetValue` and return `null` on a missing key; it holds no mutable state after construction [GameData/CachedGameData.cs:CachedGameData]
- `CachedGameData` does not null-check constructor arguments [GameData/CachedGameData.cs:CachedGameData..ctor]
- `GameDataKeys.Collect` includes an actor's `NameId` only when `actor.Type != ActorType.Player` and `NameId != 0` [GameData/GameDataKeys.cs:GameDataKeys.Collect]
- `Collect` includes action ids only from `ActorCastStartOp`/`ActorCastEventOp` whose `Action.Type == ActionType.Spell` [GameData/GameDataKeys.cs:GameDataKeys.Collect]
- `Collect` includes `ActorRenameOp.NameId` when non-zero and `ActorStatusOp` status id when `Status` is non-null and `Id != 0`; every `ZoneChangeOp.Zone` is added to territories [GameData/GameDataKeys.cs:GameDataKeys.Collect]
- Results are sorted, de-duplicated sets [GameData/GameDataKeys.cs:GameDataKeys.Collect]

## Side Effects
- None. No I/O, locks, or mutable static state; the only statics are the readonly singletons `NullGameData.Instance` and `CachedGameData.Empty` [GameData/IGameData.cs:NullGameData, GameData/CachedGameData.cs:CachedGameData.Empty]

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from repo root)
