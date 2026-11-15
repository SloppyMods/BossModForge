# ReplayValidator

## Overview
Template host program (`Program.cs`, top-level statements, plus `ForgeReplayValidator.csproj`) that `Validation/BossmodBuildValidator` extracts from embedded resources into a BossMod checkout and runs there. It replays logs through BossMod's own parser, drives an emitted module, and prints JSON event lines on stdout. The header comment states it is modeled on upstream tooling and has not been compiled here until run against a BossMod checkout [Program.cs:header comment]. `ForgeReplayValidator.csproj` targets `net9.0` (the CLI targets net10.0), `LangVersion` 13, `Nullable` enabled, `InvariantGlobalization` true, `AssemblyName` `ForgeReplayValidator`, and has a `ProjectReference` to `..\BossMod\BossMod.csproj` [ForgeReplayValidator.csproj:12-22].

## Interface
- CLI: `ForgeReplayValidator <moduleTypeName> <replay.log> [more logs...]` [Program.cs]
- Stdout: one JSON object per line: `{"replay","event":"transition","t","state"}` and `{"replay","event":"aoe","component","aid","count"}` [Program.cs:EmitEvent]
- Exit codes: 2 for fewer than 2 args (usage to stderr), 0 on completion [Program.cs]

## Invariants
- Requires at least 2 args; module type resolved from the BossMod assembly or any loaded assembly, else `InvalidOperationException` [Program.cs].
- Throws `InvalidOperationException` if a replay has no actors [Program.cs].
- Primary actor is the first non-dead, non-destroyed `Enemy`, else the first actor [Program.cs].
- `module.Update()` runs after every `op.Exec(ws)`, so the state check happens per op [Program.cs].
- `t` in a `transition` event is milliseconds since `ws.CurrentTime` captured before the first op [Program.cs].
- The `aoe` check calls `comp.ActiveAOEs(0, ws.Party.Player() ?? primary)` for each module component [Program.cs].
- Module is constructed via `Activator.CreateInstance(moduleType, ws, primary)`, so emitted module constructor arity must be `(WorldState, Actor)` [Program.cs].
- A `transition` event is emitted only when the active state ID changes (initial last state -1) [Program.cs]; `aoe` events are emitted only on `ActorCastEvent` ops for components with at least one active AOE [Program.cs].

## Side Effects
- Reads each replay log via `ReplayParser.Parse`; writes JSON lines to stdout; usage text to stderr [Program.cs].
- Executes each replay op against the world state (`op.Exec(ws)`) and updates the module [Program.cs].

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
