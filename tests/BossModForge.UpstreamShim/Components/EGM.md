# BossModForge.UpstreamShim.Components

## Overview
Stub subclasses of upstream BossMod `BossMod.Components` types, declared with constructor signatures only (most are empty primary-constructor chains; `GenericSharedTankbuster`/`CastSharedTankbuster` add a second constructor and `Knockback` has a nested enum) so that generated/golden boss-module code compiles. Every class ultimately derives from `BossComponent` via `CastCounter`.

## Interface
Namespace `BossMod.Components`:
- `CastCounter(BossModule, Enum? aid)` : `BossComponent` [CastCounter.cs:CastCounter]
- `CastHint(BossModule, Enum? aid, string hint, bool showCastTimeLeft=false)` : `CastCounter` [CastHint.cs:CastHint]
- `GenericGaze(module, Enum? aid=default, bool inverted=false)`, `CastGaze(module, Enum aid, bool inverted=false, float range=10000)` [Gaze.cs:GenericGaze, Gaze.cs:CastGaze]
- `GenericAOEs(module, Enum? aid=default, string warningText="GTFO from aoe!")`, `SelfTargetedAOEs(module, Enum aid, AOEShape shape, int maxCasts=int.MaxValue)`, `LocationTargetedAOEs(module, Enum aid, float radius, string warningText="GTFO from puddle!", int maxCasts=int.MaxValue)`, `StandardAOEs(module, Enum aid, AOEShape shape, int maxCasts=int.MaxValue)`, `GroupedAOEs(module, Enum[] aids, AOEShape shape, int maxCasts=int.MaxValue)` [GenericAOEs.cs:GenericAOEs, GenericAOEs.cs:SelfTargetedAOEs, GenericAOEs.cs:LocationTargetedAOEs, GenericAOEs.cs:StandardAOEs, GenericAOEs.cs:GroupedAOEs]
- `Knockback(module, Enum? aid=default, bool ignoreImmunes=false, int maxCasts=int.MaxValue, bool stopAtWall=false)` with nested `enum Kind { None, AwayFromOrigin, TowardsOrigin, DirForward, DirLeft, DirRight }`; `KnockbackFromCastTarget(module, Enum aid, float distance, bool ignoreImmunes=false, int maxCasts=int.MaxValue, AOEShape? shape=null, Kind kind=Kind.AwayFromOrigin, float minDistance=0, bool minDistanceBetweenHitboxes=false, bool stopAtWall=false)` [Knockback.cs:Knockback, Knockback.cs:Knockback.Kind, Knockback.cs:KnockbackFromCastTarget]
- `GenericSharedTankbuster(module, Enum? aid, AOEShape shape, bool originAtTarget=false)` plus extra ctor `(module, Enum? aid, float radius)` delegating to `AOEShapeCircle(radius)` with `originAtTarget: true`; `CastSharedTankbuster` has the same two ctor shapes [SharedTankbuster.cs:GenericSharedTankbuster, SharedTankbuster.cs:CastSharedTankbuster]
- `CastStackSpread(module, Enum? stackAID, Enum? spreadAID, float stackRadius, float spreadRadius, int minStackSize=2, int maxStackSize=int.MaxValue, bool alwaysShowSpreads=false)` (base aid is `stackAID ?? spreadAID`); `SpreadFromCastTargets(module, Enum aid, float radius, bool drawAllSpreads=true)` (passes `drawAllSpreads` as `alwaysShowSpreads`, `stackAID = default`, `stackRadius = 0`); `StackWithCastTargets(module, Enum aid, float radius, int minStackSize=2, int maxStackSize=int.MaxValue)` (passes `spreadAID = default`, `spreadRadius = 0`) [StackSpread.cs:CastStackSpread, StackSpread.cs:SpreadFromCastTargets, StackSpread.cs:StackWithCastTargets]
- `GenericTowers(module, Enum? aid=default)`, `CastTowers(module, Enum aid, float radius, int minSoakers=1, int maxSoakers=1)` [Towers.cs:GenericTowers, Towers.cs:CastTowers]
- `RaidwideCast(module, Enum aid, string hint="Raidwide")`, `SingleTargetCast(module, Enum aid, string hint="Tankbuster")` : `CastHint` [UnavoidableDamage.cs:RaidwideCast, UnavoidableDamage.cs:SingleTargetCast]
- Subdirectories: None.

## Invariants
- Constructor parameter lists mirror upstream BossMod signatures; changing them breaks compilation of code that targets them [all files in directory].
- `Knockback.cs` uses `using static BossMod.Components.Knockback;` so `Kind` resolves unqualified in `KnockbackFromCastTarget` [Knockback.cs:KnockbackFromCastTarget].
- No class stores or uses its parameters (no fields, methods or guards); they are stubs only. Several parameters are not even forwarded to the base class: `SelfTargetedAOEs` and `LocationTargetedAOEs` drop `maxCasts` (so `StandardAOEs` does too); `GroupedAOEs` drops `aids`, `shape` and `maxCasts` and calls `GenericAOEs(module)`; `SelfTargetedAOEs`/`StandardAOEs`/`GroupedAOEs` take no `warningText`; `CastGaze` drops `range`; `CastTowers` drops `radius`, `minSoakers`, `maxSoakers`; `CastHint` drops `hint` and `showCastTimeLeft` [GenericAOEs.cs, Gaze.cs:CastGaze, Towers.cs:CastTowers, CastHint.cs:CastHint].
- Guards/throws: None.

## Side Effects
- None. No file, network, static-state or disk access in any file.

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repository root)
