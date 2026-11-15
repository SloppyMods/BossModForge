# GameData

## Overview
Disk-backed `IGameData` (interface from `BossModForge.Core.GameData`) for the CLI, using Lumina to read a local FFXIV install's Excel sheets (Action, BNpcName, Status, TerritoryType).

## Interface
- `LuminaGameData(string gamePath) : IGameData, IDisposable` [LuminaGameData.cs:LuminaGameData]
- `bool Available` [LuminaGameData.cs:Available]
- `ActionRow? Action(uint id)` (RowId, Name, CastType, EffectRange, XAxisModifier, Cast100ms, Omen RowId, omen path or null) [LuminaGameData.cs:Action]
- `string? BNpcName(uint nameId)`, `string? StatusName(uint id)`, `(uint CfcId, string Name)? ContentForTerritory(uint territoryType)`, `string? PlaceName(uint territoryType)` [LuminaGameData.cs]
- `Dispose()` disposes the underlying `Lumina.GameData` [LuminaGameData.cs:Dispose]

## Invariants
- The constructor and every lookup catch all exceptions: an unreadable path sets `Available = false`; lookups return `null` when unavailable, row missing, or on any exception [LuminaGameData.cs:ctor, Action, BNpcName, StatusName, ContentForTerritory, PlaceName].
- Empty omen path -> `null`; empty content/place name -> `null` [LuminaGameData.cs:Action, ContentForTerritory, PlaceName].

## Side Effects
- Constructor opens the game install at `gamePath` (disk reads of game data files) via `new Lumina.GameData(gamePath)` [LuminaGameData.cs:ctor]; holds it until `Dispose` [LuminaGameData.cs:Dispose].

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
