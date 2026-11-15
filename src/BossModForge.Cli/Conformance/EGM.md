# Conformance

## Overview
Support code for the `conform` command: loads a real `BossMod.dll` by reflection, counts replay ops through BossMod's own parser keyed by Forge's four-char tags, and diffs those counts against Forge's counts. The file header comment states Core stays free of BossMod, so this is only called from the CLI [BossModReflection.cs:header comment].

## Interface
- `BossModReflection.CountOps(string bossModDllPath, string logPath) -> IReadOnlyDictionary<string,int>` (ordinal-sorted tag -> count) [BossModReflection.cs:CountOps]
- `BossModReflection.SelfTest(string bossModDllPath) -> string` report of assembly, replay type and resolved op-type -> tag map [BossModReflection.cs:SelfTest]
- `ConformComparer.Compare(IReadOnlyDictionary<string,int> forgeCounts, IReadOnlyDictionary<string,int> bossModCounts) -> ImmutableArray<TagCountDiff>` [ConformComparer.cs:Compare]
- `record TagCountDiff(string Tag, int ForgeCount, int BossModCount)` [ConformComparer.cs:TagCountDiff]

## Invariants
- DLL must exist; otherwise `InvalidOperationException`. The assembly must contain `BossMod.Replay.Replay` or `BossMod.Replay.ReplayParserLog`; otherwise `InvalidOperationException` [BossModReflection.cs:LoadAndResolve].
- Load failures, replay-construction failures (wrapped with the type name) and a missing/non-enumerable public `Ops` property surface as `InvalidOperationException` [BossModReflection.cs:LoadAndResolve, CountOps].
- Tag resolution: parser dispatch-map entries (4-char keys with delegate values, handler name stripped of `Parse` and an Actor/Party/Client/DeepDungeon/Network prefix) take priority; the static `OpNameToTag` table fills gaps via `TryAdd` [BossModReflection.cs:TryParserDispatchMap, TryMapParseMethod, LoadAndResolve].
- Op type name has a leading `Op` stripped; an op with no mapping throws `InvalidOperationException` [BossModReflection.cs:ResolveTag].
- Tags ending in `±` are resolved via the first existing field/property among `Value`, `Active`, `Action`, `Status`: bool -> `+`/`-`, null -> `-`, any other value -> `+`; if none exists, `InvalidOperationException` [BossModReflection.cs:ResolveSign, TryGetMember].
- `Compare` returns only keys whose counts differ, over the ordinal-sorted union of keys; a missing key counts as 0 [ConformComparer.cs:Compare].

## Side Effects
- `Assembly.LoadFrom` loads the BossMod DLL into the process and `Activator.CreateInstance(replayType, logPath)` makes BossMod parse the log file (file read) [BossModReflection.cs:LoadAndResolve, CountOps].
- Reflection reads static fields of the replay type and its nested types [BossModReflection.cs:TryParserDispatchMap].
- `ConformComparer`: None.

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
