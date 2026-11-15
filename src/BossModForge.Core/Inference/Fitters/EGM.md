# Fitters

## Overview
Per-shape AoE fitting primitives used by `ShapeInference`. Each fitter scans hit/miss `ShapeObservation`s (player position + hit flag), derives the largest "hit" extent and smallest "miss" extent per shape parameter, and returns a raw (midpoint) and a snapped `FitProposal`. The single file `AoeFitters.cs` lives in namespace `BossModForge.Core.Inference` (not a `.Fitters` sub-namespace) and everything is `internal`.

## Interface
- `FitProposal(Shape Raw, Shape Snapped, float CombinedMargin)` — internal readonly record struct [AoeFitters.cs:FitProposal]
- `AoeFitters.OriginOf(ShapeObservation, ShapeOrigin) -> Vector2` — `CastLocation` falls back to `Source` when it is `Vector2.Zero`; `TargetActor` falls back to `Source` when null [AoeFitters.cs:AoeFitters.OriginOf]
- `AoeFitters.DirectionOf(ShapeObservation, Vector2 origin, ShapeDirection) -> Angle` — `ToTarget` uses `TargetActor` (or `CastLocation` if it differs from origin) when that point != origin; otherwise returns `SourceRotation` [AoeFitters.cs:AoeFitters.DirectionOf]
- `AoeFitters.Circle(observations, originMode, prior, options) -> FitProposal?` [AoeFitters.cs:AoeFitters.Circle]
- `AoeFitters.Donut(observations, originMode, prior, options) -> FitProposal?` [AoeFitters.cs:AoeFitters.Donut]
- `AoeFitters.Cone(observations, originMode, dirMode, prior, options) -> FitProposal?` [AoeFitters.cs:AoeFitters.Cone]
- `AoeFitters.Rect(observations, originMode, dirMode, prior, options) -> FitProposal?` (also fits `LengthBack`) [AoeFitters.cs:AoeFitters.Rect]
- `AoeFitters.Cross(observations, originMode, dirMode, prior, options) -> FitProposal?` [AoeFitters.cs:AoeFitters.Cross]
- `AoeFitters.SnapRadial(maxHit, minMiss, prior, snapY) -> float` [AoeFitters.cs:AoeFitters.SnapRadial]
- `AoeFitters.SnapAngle(maxHit, minMiss, prior, snapDeg) -> float` — snaps to the fixed list `[15, 22.5, 30, 45, 60, 67.5, 90, 105, 135]` [AoeFitters.cs:AoeFitters.SnapAngle]
- Subdirectories: None

## Invariants
- Circle/Donut/Cone/Rect/Cross each return `null` when no player has `hit == true` in any observation [AoeFitters.cs:AoeFitters.Circle, AoeFitters.Donut, AoeFitters.Cone, AoeFitters.Rect, AoeFitters.Cross]
- Margin is `minMiss - maxHit` when `minMiss > maxHit`, else 0 [AoeFitters.cs:AoeFitters.Margin]; `CombinedMargin` is the minimum across the fitted parameters (Rect: length and half-width only) [AoeFitters.cs:AoeFitters.Donut, AoeFitters.Cone, AoeFitters.Rect, AoeFitters.Cross]
- Raw radius is the midpoint of `maxHit`/`minMiss` when separable (`maxHit < minMiss`), otherwise `maxHit` [AoeFitters.cs:AoeFitters.Circle]
- `SnapRadial` returns `prior` when `maxHit <= prior < minMiss`; when there are no misses (`minMiss` is +∞) it returns `prior` if `prior >= maxHit`, else `Max(maxHit, RoundTenths(maxHit))`; when hits and misses overlap (`maxHit >= minMiss`) it returns `prior` whenever non-null (no range check), else the tenths-rounded midpoint; otherwise it picks the multiple of `snapY` inside (`maxHit`, `minMiss`) closest to the midpoint, falling back to tenths-rounded midpoint [AoeFitters.cs:AoeFitters.SnapRadial]
- Donut snapped inner radius is forced `> maxInnerMiss` (via `MathF.BitIncrement`) and `< snapOut` (else `max(0, snapOut - RadiusSnapY)`) [AoeFitters.cs:AoeFitters.Donut]
- Cone miss-radius only considers misses with `d <= maxHitR + 0.25`; miss-angle only considers misses with `ang <= maxHitAng + 1` [AoeFitters.cs:AoeFitters.Cone]
- Rect `LengthBack` is nonzero only when min forward hit offset `< -0.05`; snapped back length rounds to tenths [AoeFitters.cs:AoeFitters.Rect]
- `SnapAngle` order: `prior` in `[maxHit, minMiss)` wins; with no misses, `prior >= raw - snapDeg` wins (raw = midpoint, or `maxHit` when overlapping); else the first list snap within `snapDeg` of raw that lies inside `[maxHit, minMiss)`; else the first snap within `snapDeg`; else `Max(maxHit, RoundTenths(raw))` [AoeFitters.cs:AoeFitters.SnapAngle]
- Rect miss selection: a forward miss counts when `lat <= maxLat + 0.25` and `fwd > 0`; a lateral miss counts when `-lengthBackRaw - 0.25 <= fwd <= maxFwd + 0.25` [AoeFitters.cs:AoeFitters.Rect]
- Donut misses are split by distance: only misses with `d < minHit` bound the inner radius, only misses with `d > maxHit` bound the outer radius; misses between are ignored [AoeFitters.cs:AoeFitters.Donut]
- Cross uses `near = min(|fwd|,|lat|)` and `far = max(|fwd|,|lat|)` of each point, with 0.5 tolerance when selecting misses [AoeFitters.cs:AoeFitters.Cross]
- Prior values are only used when `prior.Kind` equals the fitted kind [AoeFitters.cs:AoeFitters.Circle, AoeFitters.Donut, AoeFitters.Cone, AoeFitters.Rect, AoeFitters.Cross]. Rect takes only length (`Radius`) and `HalfWidth` from the prior; `LengthBack` has no prior and is left out of `CombinedMargin`, which is `Min(length margin, half-width margin)` [AoeFitters.cs:AoeFitters.Rect]

## Side Effects
- None (pure static functions; no I/O, no static mutable state, no throws in code) [AoeFitters.cs:AoeFitters]

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from repo root)
