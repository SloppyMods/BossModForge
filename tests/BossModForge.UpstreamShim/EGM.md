# BossModForge.UpstreamShim

## Overview
Pinned API stub of upstream BossMod (project `BossModForge.UpstreamShim.csproj`: `net10.0`, `RootNamespace` `BossMod`, `Nullable` and `ImplicitUsings` enabled, `IsPackable` false, `NoWarn` `CS9113`). It exposes the minimum types and signatures needed to compile golden outputs; method bodies are empty or return placeholder instances and have no behavior. Component stubs live in `./Components/`.

## Interface
Namespace `BossMod` unless noted:
- `Actor` (sealed): settable `bool IsDeadOrDestroyed`, `float HPRatio` [Actor.cs:Actor]
- `Angle` (record struct, `float Rad`): consts `RadToDeg`, `DegToRad`; `Deg`, `FromDirection(WDir)`, `ToDirection()`, `+ - * /` operators, `Abs/Sin/Cos/Tan`, `Asin/Acos`, `ToString()` (Deg formatted "f0"); `AngleExtensions.Radians(float)`, `Degrees(float)`, `Degrees(int)` [Angle.cs:Angle, Angle.cs:AngleExtensions]
- `AOEShape` (abstract) with sealed records `AOEShapeCone(Radius, HalfAngle, DirectionOffset)`, `AOEShapeCircle(Radius)`, `AOEShapeDonut(InnerRadius, OuterRadius)`, `AOEShapeRect(LengthFront, HalfWidth, LengthBack=0, DirectionOffset)`, `AOEShapeCross(Length, HalfWidth, DirectionOffset)` [AOEShapes.cs]
- `ArenaBounds(Radius, MapResolution)` abstract, with `ArenaBoundsCircle`, `ArenaBoundsSquare`, `ArenaBoundsRect(HalfWidth, HalfHeight, Rotation, Radius=0, MapResolution=0.5f)`, empty `RelSimplifiedComplexPolygon`, `ArenaBoundsCustom(Radius, Poly, MapResolution)` [ArenaBounds.cs]
- `BossComponent(BossModule module)` with `readonly Module` [BossComponent.cs:BossComponent]
- `BossModule` (abstract): `PrimaryActor`; protected ctor `(WorldState ws, Actor primary, WPos center, ArenaBounds bounds)` [BossModule.cs:BossModule]
- `BossModuleInfo` static class with enums `Maturity`, `Expansion`, `Category`, `GroupType`; `ModuleInfoAttribute(Maturity)` (class-only, `Inherited=false`) with get-only `Maturity` (from the constructor) and settable `StatesType`, `ConfigType`, `ObjectIDType`, `ActionIDType`, `StatusIDType`, `TetherIDType`, `IconIDType` (all `Type?`), `PrimaryActorOID` (`uint`), `Contributors`, `Expansion`, `Category`, `GroupType`, `GroupID`, `NameID`, `SortOrder`, `PlanLevel` properties [BossModuleInfo.cs:BossModuleInfo, BossModuleInfo.cs:ModuleInfoAttribute]
- `StateMachine` with nested `State { Func<float,int>? Update }` and `Phase { Func<bool>? Update }` [StateMachine.cs:StateMachine]
- `StateMachineBuilder(BossModule)` with nested `Phase` (`Raw`, `OnEnter`, `OnExit`, `ActivateOnEnter<C>`, `DeactivateOnEnter<C>`, `DeactivateOnExit<C>`) and `State` (`Raw`, `OnEnter`, `OnExit`, `ActivateOnEnter<C>`, `DeactivateOnExit<C>`, `ExecOnEnter<C>`, `ExecOnExit<C>`; no `DeactivateOnEnter`) fluent chains, and builder methods `SimplePhase, DeathPhase, TrivialPhase, SimpleState(uint id, float duration, string name), Timeout, ComponentCondition<T>, CastStart<AID>, CastEnd, Cast<AID>, CastStartMulti<AID>, CastMulti<AID>, Targetable` [StateMachineBuilder.cs:StateMachineBuilder]
- `WorldState` (sealed, empty); `WDir(X, Z)`, `WPos(X, Z)` record structs [WorldState.cs:WorldState, WPosDir.cs:WDir, WPosDir.cs:WPos]
- Subdirectories: [Components](./Components/EGM.md)

## Invariants
- `ModuleInfoAttribute.Expansion` defaults to `Expansion.Count`, `Category` to `Category.Count`, `GroupType` to `GroupType.None`, `Contributors` to `""` [BossModuleInfo.cs:ModuleInfoAttribute].
- `StateMachineBuilder` fluent methods return the same shared instance (`_phase`/`_state`) for every call; conditions and actions are ignored [StateMachineBuilder.cs:StateMachineBuilder].
- `SimplePhase` invokes `buildState(seqID << 24)` exactly once and ignores `name` and `dur` (default `-1`); `DeathPhase` passes name "Boss death"; `TrivialPhase` builds one "Enrage" state with default enrage 10000 [StateMachineBuilder.cs:SimplePhase, DeathPhase, TrivialPhase].
- `ArenaBoundsRect` radius is `Radius` if >0, otherwise `max(HalfWidth, HalfHeight)` [ArenaBounds.cs:ArenaBoundsRect].
- `BossModule` constructor stores only `primary`; `ws`, `center`, `bounds` are ignored [BossModule.cs:BossModule].
- `Angle.FromDirection` uses `Atan2(dir.X, dir.Z)`; `ToDirection` returns `(Sin, Cos)` [Angle.cs:Angle].
- Throws/guards: None.

## Side Effects
- None. No file, network, disk or static mutable state in any file.

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repository root)
