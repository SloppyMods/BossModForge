---
tags:
  - type/plan
  - project/bossmodforge
  - status/draft
type: plan
project: bossmodforge
status: frozen
milestone: M2
spec: ../specs/2026-09-18-bossmodforge-design.md
plan: ./2026-09-18-bossmodforge-implementation.md
upstream: ../UPSTREAM.md
depends_on: ./m1-builder-brief.md
---

# M2 builder brief — arena bounds, shape inference, `SimpleAOEs`

Tracer bullet: `forge infer a.log -o em.json && forge emit em.json --flavor vbm -o out/` now writes a fitted `ArenaBounds*`, an `AbilityModel.Shape` per damaging ability, and one component class per `TelegraphedAOE|HiddenAOE|Raidwide` ability (`Components.SimpleAOEs` / `SimpleAOEGroups` / `RaidwideCast`) activated by `TrivialPhase().ActivateOnEnter<…>()` — still no timeline (M4), no other mechanic classes (M3).

Starts from M1 merged (35 tests green). Algorithms: spec §5.4 (shape), §5.8 (bounds), §7.3 (component rows for the three classes only). This brief fixes files and public APIs; where it says "freeze" the signature is not negotiable, everything else is the builder's call as long as tests in §5 pass.

## 1. Scope

In: `BoundsFitter`, `OmenParser`, `ShapeInference` (sheet prior × empirical fit, δ search, circle/donut/cone/rect/cross fitters, raidwide detection, scoring, snapping, evidence), `AbilityModel.Class` ∈ `{Unclassified, TelegraphedAOE, HiddenAOE, Raidwide}`, `AbilityModel.Shape`, fitted `ArenaModel`, `IEmitterProfile.ComponentCtor` for kinds `SimpleAOEs|SimpleAOEGroups|RaidwideCast`, `ArenaBounds` for `Circle|Square|Rect|Custom`, component classes + `ActivateOnEnter` chain in the emitted file, FORGE.md shape table, schema/golden updates.

Out: timeline/state machine (`TrivialPhase` stays), per-phase bounds (detect + review only), tankbuster/spread/stack/knockback/any other class, `maxCasts` (needs timeline), `LuminaGameData` (still `NullGameData`; `Ability.Sheet` is honoured when present — tests plant it), plugin, validate harness, merge.

Ownership: **one builder (Grok)** owns every file in §2 and nothing else. Do not touch `Replay/**`, `Cli/**`, `Directory.Build.props`, `Confidence.cs`, `PullSegmenter.cs`, `ParticipantClassifier.cs`, `AbilityCataloger.cs`, `Identifiers.cs`, `EncounterModelJson.cs`. The only edit to `GameData/IGameData.cs` is the one line in §3.0.

## 2. Files

```
src/BossModForge.Core/
  GameData/IGameData.cs                 ActionRow gains trailing `string? OmenPath = null` (only edit)
  Inference/Shape.cs                    ShapeKind, ShapeOrigin, ShapeDirection, Shape, ShapePrior, ShapeObservation, ShapeFit
  Inference/OmenParser.cs
  Inference/BoundsFitter.cs             + BoundsFitterOptions, ArenaKind, ArenaFit
  Inference/ShapeInference.cs           + ShapeInferenceOptions, MechanicClass; internal fitters may live in Inference/Fitters/*.cs
  Model/EncounterModel.cs               AbilityModel.Shape, ArenaModel widened, BuildOptions widened (see §3.4)
  Model/EncounterModelBuilder.cs        calls BoundsFitter.Fit then ShapeInference.Infer per ability
  Emit/ShapeCSharp.cs                   Shape → C# expression via flavor templates
  Emit/VbmProfile.cs                    ComponentCtor, ArenaBounds for all kinds; fix StatesClass chain bug (§4)
  Emit/BmrProfile.cs                    delegates as in M1
  Emit/ModuleEmitter.cs                 component classes + ActivateOnEnter list
  Emit/ForgeMdWriter.cs                 "Shapes" and "Arena" sections
flavors/vbm.json, flavors/bmr.json      new templates (§4)
schema/em.v1.schema.json                additive: abilities[].shape, arena.* (§3.4)
docs/UPSTREAM.md                        resolve §6 VERIFY rows before coding the affected line
tests/BossModForge.Tests/
  Fixtures/ShapeFixture.cs              seeded planted-shape generator → ShapeObservation[] and Ability/Recording variants
  Fixtures/ArenaFixture.cs              seeded player walks inside circle / square / L-shape
  Fixtures/SyntheticShapes.cs           RecordingBuilder fixture: boss with cone + circle + raidwide casts, 8 moving players
  Fixtures/OmenCorpus.cs                table of (omenPath, expected) rows
  Inference/OmenParserTests.cs
  Inference/BoundsFitterTests.cs
  Inference/ShapeInferenceTests.cs      per-fitter seeds, ambiguity, δ search, classification
  Model/EncounterModelBuilderShapeTests.cs
  Emit/ModuleEmitterComponentsTests.cs
  Golden/SyntheticDungeon.em.json / .vbm.cs   (regenerated — inspect the diff)
  Golden/SyntheticShapes.em.json / .vbm.cs    (new)
```

## 3. Frozen public APIs

### 3.0 GameData

```csharp
public sealed record ActionRow(uint RowId, string Name, byte CastType, byte EffectRange, byte XAxisModifier, ushort Cast100ms, uint OmenId,
                               string? OmenPath = null);   // additive; NullGameData still returns null rows
```

### 3.1 Shape (`BossModForge.Core.Inference`, `Shape.cs`)

```csharp
public enum ShapeKind { Circle, Donut, Cone, Rect, Cross, RaidwideNoShape, SingleTarget }
public enum ShapeOrigin { Source, CastLocation, TargetActor }
public enum ShapeDirection { SourceRotation, ToTarget, CastRotation }

// Parameter use per kind (unused fields are 0):
//   Circle: Radius | Donut: InnerRadius, Radius(outer) | Cone: Radius, HalfAngleDeg
//   Rect: Radius(=length front), HalfWidth, LengthBack | Cross: Radius(=length), HalfWidth
//   RaidwideNoShape / SingleTarget: no parameters
public sealed record Shape(ShapeKind Kind, float Radius, float InnerRadius, float HalfAngleDeg, float HalfWidth, float LengthBack,
                           ShapeOrigin Origin, ShapeDirection Direction)
{
    public static Shape Circle(float r, ShapeOrigin origin = ShapeOrigin.Source);
    public static Shape Donut(float rIn, float rOut, ShapeOrigin origin = ShapeOrigin.Source);
    public static Shape Cone(float r, float halfAngleDeg, ShapeOrigin origin = ShapeOrigin.Source, ShapeDirection dir = ShapeDirection.SourceRotation);
    public static Shape Rect(float length, float halfWidth, float lengthBack = 0, ShapeOrigin origin = ShapeOrigin.Source, ShapeDirection dir = ShapeDirection.SourceRotation);
    public static Shape Cross(float length, float halfWidth, ShapeOrigin origin = ShapeOrigin.Source, ShapeDirection dir = ShapeDirection.SourceRotation);
    public static readonly Shape Raidwide;      // Kind RaidwideNoShape
    public static readonly Shape SingleTarget;
    public bool Contains(Vector2 point, Vector2 origin, Angle direction);   // BossMod convention: player hitbox NOT added
}

// Sheet prior derived from ActionRow per spec §5.4 table. Null members = "unknown, fit decides".
public sealed record ShapePrior(ShapeKind Kind, float? Radius, float? InnerRadius, float? HalfAngleDeg, float? HalfWidth,
                                ShapeOrigin Origin, ShapeDirection Direction, bool FromOmen /* angle/inner came from OmenParser */);

// One cast instance, already sampled at a snapshot time.
public sealed record ShapeObservation(Vector2 Source, Angle SourceRotation, Vector2 CastLocation, Vector2? TargetActor,
                                      ImmutableArray<(Vector2 Pos, bool Hit)> Players);

public sealed record ShapeFit(Shape Shape, Shape RawShape /* pre-snap */, double Score, double FitConsistency, double PriorMatch, double Ambiguity,
                              float MinMargin /* y, or deg for angle-limited */, float LagSeconds, int Instances,
                              Confidence Confidence, ImmutableArray<Evidence> Evidence);
```

`Shape` uses only enums and floats, so record equality is value equality — `ModuleEmitter` groups by it. `Vector2` = `System.Numerics` (X, Z of the game `Vector3`).

### 3.2 OmenParser

```csharp
public static class OmenParser
{
    public static bool TryParseFan(string? omenPath, out float halfAngleDeg);          // Regex fan(\d{3}) → total/2; "gl_fan090_1bf" → 45. false on null/no match/000.
    public static bool TryParseDonut(string? omenPath, out float innerOverOuter);      // sircle_(\d)(\d{3}) heuristic; returns false until UPSTREAM.md VERIFY row is resolved
}
```

Omen-derived numbers are a weak prior: `ShapePrior.FromOmen = true` and the param term of `PriorMatch` is weighted 0.3 (spec §5.4).

### 3.3 BoundsFitter

```csharp
public sealed record BoundsFitterOptions(float RadiusSnapTolerance = 1.0f, float RectPadding = 1.0f, double CircleFillMin = 0.7, double BoxFillMin = 0.8,
                                         float PolygonEpsilon = 0.5f, TimeSpan? KnockbackExclusion = null /* 2 s */, TimeSpan? DeathExclusion = null /* 2 s */,
                                         TimeSpan? SampleStep = null /* 0.5 s */)
{ public static readonly BoundsFitterOptions Default = new(); }

public enum ArenaKind { Circle, Square, Rect, Custom }
public sealed record ArenaFit(ArenaKind Kind, Vector2 Center, float Radius /* Circle; else minimal enclosing radius */, float HalfWidth, float HalfHeight,
                              ImmutableArray<Vector2> Vertices /* Custom only */, int SampleCount, Confidence Confidence, ImmutableArray<Evidence> Evidence);

public static class BoundsFitter
{
    public static ArenaFit Fit(Recording recording, ImmutableArray<Pull> pulls, uint primaryOid, BoundsFitterOptions? options = null);
    public static ArenaFit Fit(ImmutableArray<Vector2> samples, Vector2? bossSpawn, BoundsFitterOptions? options = null);   // pure; tests use this
}
```

Rules (spec §5.8): samples = every alive player's `PosRotAt(t)` at `SampleStep` ticks across all pulls, dropping `[t, t+2 s]` after any `Knockback` effect on that player and `[death−2 s, death]`. Circle: minimal enclosing circle; radius snapped to `{10,15,19,20,25,30}` when within `RadiusSnapTolerance`, else raw rounded to 0.5. Rect: axis-aligned bounding box + padding; `Square` when |halfWidth − halfHeight| ≤ 1 y (use the mean), else `Rect`. Choose rect over circle when `hullArea/circleArea < CircleFillMin && hullArea/boxArea > BoxFillMin` (hull = convex hull of samples). Neither (`hull/circle < 0.7 && hull/box ≤ 0.8`) → `Custom` with Douglas–Peucker(`PolygonEpsilon`) hull, `Confidence.LowBecause("custom polygon")`. Centre cross-check: `bossSpawn` = primary's first `PosRotSamples` position in pull 0; `Evidence("CenterOffset", d)`; d > 3 y lowers Circle/Square confidence one band. Confidence: `High` when snapped and `SampleCount ≥ 200`, `Medium` when snapped with fewer samples or unsnapped, `Low` for Custom or `SampleCount < 20` (fallback: Circle r = 20 at boss spawn, `LowBecause("insufficient samples")`). Per-phase change: if the running hull radius steps by > 3 y between two consecutive 30 s windows, add `Evidence("ArenaChange", t)`; `EncounterModelBuilder` turns it into `ReviewItem(Medium, "arena", "Arena appears to change at …")`. Single bounds are still emitted (per-phase is M4).

### 3.4 ShapeInference and classification

```csharp
public sealed record ShapeInferenceOptions(float LagMinSeconds = -0.2f, float LagMaxSeconds = 1.0f, float LagStepSeconds = 0.1f,
                                           double PriorWeight = 0.35, double FitWeight = 0.55, double AmbiguityWeight = 0.10,
                                           float AmbiguityMarginY = 1.0f, float AmbiguityMarginDeg = 3f,
                                           double RaidwideFraction = 0.9, float RaidwideArenaRatio = 0.95,
                                           float RadiusSnapY = 0.5f, float AngleSnapDeg = 3f)
{ public static readonly ShapeInferenceOptions Default = new(); }

public static class MechanicClass
{
    public const string Unclassified = "Unclassified", TelegraphedAOE = "TelegraphedAOE", HiddenAOE = "HiddenAOE", Raidwide = "Raidwide";
}

public static class ShapeInference
{
    public static ShapePrior? PriorFor(ActionRow? sheet);                                                  // spec §5.4 table only; null for null row / unlisted CastType
    public static ShapeFit? Fit(ImmutableArray<ShapeObservation> observations, ShapePrior? prior, float arenaRadius, ShapeInferenceOptions? options = null);   // pure, no δ
    public static ShapeFit? Infer(Ability ability, Recording recording, ImmutableArray<Pull> pulls, ArenaFit arena, ShapeInferenceOptions? options = null);      // δ search over Fit
    public static string Classify(Ability ability, ShapeFit? fit, ArenaFit arena, ShapeInferenceOptions? options = null);                                        // → MechanicClass constant
}
```

`Infer`: instances with no `Damage|BlockedDamage|ParriedDamage` effect on any `Player` hit → excluded; no instances left → `null`. For each δ in the grid plus `EffectTime` itself, `t_snap = (CastEnd ?? EffectTime) + δ`; one `ShapeObservation` per instance: `Source` = `Origin` (fallback source timeline `PosRotAt(t_snap)` if zero), `SourceRotation` = `Rotation`, `CastLocation` = `TargetPos`, `TargetActor` = position of `MainTargetId` at `t_snap` (null if 0/no timeline), `Players` = every `Pull.PlayerInstanceIds` timeline alive at `t_snap` with `Hit = TargetInstanceId ∈ Hits with damage`. Pick the δ whose `Fit` has the highest `(FitConsistency, MinMargin)`; ties → δ closest to 0. `LagSeconds` = chosen δ.

`Fit` (spec §5.4): (a) raidwide check first — all alive players hit in ≥ `RaidwideFraction` of observations → `Shape.Raidwide`, `FitConsistency` = that fraction, `Score = FitConsistency`, capped at 0.84 when `Instances < 2`. (b) Otherwise run every fitter over the origin/direction modes: prior present → only the prior's mode; no prior → Circle × {Source, CastLocation, TargetActor}, Donut × {Source, CastLocation}, Cone/Rect/Cross × {Source} × {SourceRotation, ToTarget}. Fitter contracts: Circle `r̂ = mid(maxHit, minMiss)` when consistent, margin `minMiss − maxHit`; Donut same on ordered distances for `[rIn, rOut]`; Cone: angle/dist frame, half-angle from max |hit angle| vs min |miss angle| within radius, snapped to `{15, 22.5, 30, 45, 60, 67.5, 90, 105, 135}` if within `AngleSnapDeg`; Rect: length from max forward hit vs min forward miss inside the width, halfWidth from max |lateral hit| vs min |lateral miss| inside the length, `LengthBack` > 0 only when hits appear behind the origin; Cross = two Rects at 0°/90° sharing length/halfWidth. Parameters are estimated per observation and combined across observations as `[max over hits, min over misses]`; `FitConsistency` = fraction of observations the final shape explains with zero label errors; `MinMargin` = min over observations of the hit/miss gap in the limiting dimension. (c) Snap: prefer the prior value when it lies inside `(maxHit, minMiss)`; else nearest `RadiusSnapY` multiple inside the interval; else raw rounded to 0.1. `RawShape` keeps the pre-snap values. (d) Score: `PriorMatch = 0.7·[Kind == prior.Kind] + 0.3·[all non-null prior params within snap tolerance]` (param term = 1 when the prior has no numeric params); with a prior `S = 0.35·PriorMatch + 0.55·FitConsistency − 0.10·Ambiguity`, without a prior `S = 0.90·FitConsistency − 0.10·Ambiguity`; `Ambiguity` = fraction of observations with margin below `AmbiguityMarginY` / `AmbiguityMarginDeg`. **Hard cap**: `MinMargin < AmbiguityMarginY` (or `< AmbiguityMarginDeg` for angle-limited fits) ⇒ `Score = min(Score, 0.84)`. (e) Choose the highest `S`; ties → prior kind, then Circle < Cone < Rect < Donut < Cross. `FitConsistency == 0` for every candidate and no prior ⇒ `null`; with a prior return the prior shape as `LowBecause("no consistent fit")`. `Confidence = Confidence.Of(Score, reasons…)` — bands come from `Confidence.Of` (High ≥ 0.85). Evidence: `Evidence("ShapeFit", …)` with raw params, per-δ table, per-kind scores (`Data` JSON).

`Classify`: `fit == null || Kind == SingleTarget` → `Unclassified`. `Kind == RaidwideNoShape`, or `Circle` with `Origin == Source && Radius ≥ RaidwideArenaRatio·arena.Radius` → `Raidwide`. Otherwise `Kind ∈ {Circle, Donut, Cone, Rect, Cross}`: `!ability.HasCastBar` → `Unclassified` (Instant is M3); `Sheet == null` → `TelegraphedAOE` with `Evidence("OmenUnknown")`; `Sheet.OmenId == 0 && string.IsNullOrEmpty(Sheet.OmenPath)` → `HiddenAOE`; else `TelegraphedAOE`. `PriorFor` maps exactly the eleven CastType rows of spec §5.4 (1 SingleTarget/TargetActor; 2 Circle(EffectRange)/Source; 3 Cone(EffectRange, omen fan or null)/Source/SourceRotation; 4 Rect(EffectRange, XAxisModifier/2)/Source/SourceRotation; 5 Circle/TargetActor; 7 Circle/CastLocation; 8 Rect(null length, XAxisModifier/2)/Source/ToTarget; 10 Donut(null, EffectRange)/Source; 11 Cross(EffectRange, XAxisModifier/2)/Source; 12 Rect/Source/SourceRotation; 13 Cone(EffectRange, null)/Source/ToTarget) — **no other CastType values**.

Model (`BossModForge.Core.Model`, additive; `schemaVersion` stays 1, schema updated, nulls omitted on write):

```csharp
public sealed record AbilityModel(…, string Class, Shape? Shape, ShapeFitSummary? ShapeFit, Confidence Confidence, ImmutableArray<Evidence> Evidence);   // both null when Unclassified with no fit
public sealed record ShapeFitSummary(double Score, double FitConsistency, float MinMargin, float LagSeconds, int Instances);                       // full ShapeFit detail goes into Evidence("ShapeFit")
public sealed record ArenaModel(string Kind /* Circle|Square|Rect|Custom */, float CenterX, float CenterZ, float Radius, float HalfWidth, float HalfHeight,
                                ImmutableArray<PointModel> Vertices, Confidence Confidence, ImmutableArray<Evidence> Evidence);
public sealed record PointModel(float X, float Z);
public sealed record BuildOptions(string? ModuleName = null, string? Namespace = null, ShapeInferenceOptions? Shape = null, BoundsFitterOptions? Bounds = null);
```

`EncounterModelBuilder.Build` (signature unchanged): after the catalog, `arena = BoundsFitter.Fit(recording, pulls, primaryOid, options.Bounds)`; for each `Ability` with `ActionType.Spell`, `fit = ShapeInference.Infer(...)`, `Class = ShapeInference.Classify(...)`, `Shape = fit?.Shape`; ability `Confidence` = min(catalog confidence, fit confidence) when a fit exists; `Low` shape fits add `ReviewItem(Medium, "shape", "<EnumName>: shape uncertain (<kind>, score …)", suggested = other candidate kinds)`. `ArenaModel` is a straight projection of `ArenaFit` (Custom keeps vertices).

## 4. Emission (vbm; bmr delegates and keeps its High review item)

Resolve the §6 VERIFY rows first and record them in `UPSTREAM.md`; templates then carry the verified idiom so code never hardcodes it:

```json
"componentClass":      "class {name}(BossModule module) : {ctor};",
"componentSimpleAOEs": "Components.SimpleAOEs(module, {aid}, {shape})",
"componentSimpleAOEGroups": "Components.SimpleAOEGroups(module, [{aids}], {shape})",
"componentRaidwideCast": "Components.RaidwideCast(module, {aid})",
"shapeCircle": "new AOEShapeCircle({r})",  "shapeDonut": "new AOEShapeDonut({rIn}, {rOut})",
"shapeCone": "new AOEShapeCone({r}, {halfAngle}.Degrees())",
"shapeRect": "new AOEShapeRect({length}, {halfWidth})",  "shapeRectBack": "new AOEShapeRect({length}, {halfWidth}, {lengthBack})",
"shapeCross": "new AOEShapeCross({length}, {halfWidth})",
"boundsSquare": "new ArenaBoundsSquare({halfWidth})",  "boundsRect": "new ArenaBoundsRect({halfWidth}, {halfHeight})",
"boundsCustom": "new ArenaBoundsCustom([{vertices}])"
```

- `ComponentCtor(kind, args)`: kinds `"SimpleAOEs"` (`aid`, `shape`), `"SimpleAOEGroups"` (`aids` comma-joined, `shape`), `"RaidwideCast"` (`aid`); `aid` values arrive already through `IdCast("AID", member)`; `shape` arrives from `ShapeCSharp.Render(Shape, FlavorProfile)`. Unknown kind → `NotSupportedException`. `IdCast` stays `(uint)AID.X` unless VERIFY says `ActionID.MakeSpell(AID.X)` — then change the method body, not call sites.
- `ShapeCSharp.Render` (namespace `BossModForge.Core.Emit`): `public static string Render(Shape shape, FlavorProfile profile)`; numbers invariant, ≤ 1 decimal, no trailing `.0` (`6`, `6.5`, `22.5`); `Rect` uses `shapeRectBack` only when `LengthBack > 0`; throws for `RaidwideNoShape`/`SingleTarget`.
- `ModuleEmitter`: after enums, before the States class, one `componentClass` line per unit, ordered by ascending `ActionId` of the (first) ability: `Raidwide` → `RaidwideCast`; `TelegraphedAOE|HiddenAOE` grouped by `Shape` equality — single → `SimpleAOEs`, ≥ 2 → `SimpleAOEGroups` named by joining the members' `EnumName`s with `And` (≤ 3 members) or `<First>AndOthers`, deduped against all class names with `CSharpIdentifier.Dedupe`. Single-ability component name = the ability `EnumName`. `StatesClass(moduleName, componentNames)` receives the same order.
- **Fix the M1 `VbmProfile.StatesClass` bug**: with ≥ 1 component it currently emits `.ActivateOnEnter<A>\n.ActivateOnEnter<B>();` — each link must be `.ActivateOnEnter<A>()`, the chain ends with `;`. Assert the exact text in `ModuleEmitterComponentsTests`.
- `ArenaBounds(ArenaModel)`: `Circle` → `boundsCircle`, `Square` → `boundsSquare`, `Rect` → `boundsRect`, `Custom` → `boundsCustom` with `new({x}, {z})` vertices when verified; if the `ArenaBoundsCustom` VERIFY is unresolved, emit `boundsCircle` with the enclosing radius plus `ReviewItem(High, "arena", "Custom arena — ArenaBoundsCustom unverified")` (`E`-numbered, as in M1).
- `ForgeMdWriter`: "Arena" (kind, centre, size, confidence, evidence) and "Shapes" table (enum name, class, shape, score, lag, consistency, confidence) — deterministic order = EM order.

## 5. Tests / acceptance

- `OmenParserTests`: `OmenCorpus` rows (`gl_fan090_1bf → 45`, `gl_fan120_0c → 60`, `gl_fan180_… → 90`, `gl_circle_5001bf → false`, `null → false`, `"" → false`); `TryParseDonut` returns `false` for every row until the VERIFY row is closed.
- `ShapeInferenceTests` (pure `Fit`, `ShapeFixture`, `Random(seed)`, 8 players uniformly in r = 15 around the origin, 10 observations per ability — the fixture may raise observation count, never the tolerances): for each of Circle(6), Donut(5, 12), Cone(30, 45°), Rect(40, 3), Cross(40, 3): **200 seeds, ≥ 98 % recover kind and every parameter within 0.5 y / 3°**, with and without a planted `ShapePrior`. With a matching prior ≥ 98 % are `High`. Ambiguity: seeds constructed so the closest miss is < 1 y outside the boundary (fixture places one player at `r + 0.6`) never produce `High`. Raidwide: all players hit in 10/10 → `RaidwideNoShape`, `High`; 8/10 → `Medium`/`Low`, never `High`; 1 observation → capped `Medium`. Mismatched prior (planted Circle 6, prior Cone) → `Low` and `ReviewItem` after `Build`.
- δ search (`Infer` on a `RecordingBuilder` recording, `SyntheticShapes`-style, players `Move` 1 y per 0.1 s): planted snapshot lag 0.3 s → `LagSeconds` within 0.1 s; planted 0.0 s → `|Lag| ≤ 0.1`.
- `Classify`: `Sheet == null` + cone + cast bar → `TelegraphedAOE`; `Sheet(OmenId 0, OmenPath null)` → `HiddenAOE`; `Sheet(OmenPath "gl_fan090_1bf")` → `TelegraphedAOE` and prior half-angle 45; circle r ≥ 19 in r = 20 arena → `Raidwide`; no cast bar → `Unclassified`; CastType 1 → `SingleTarget` → `Unclassified`; CastType 6/9/14 → `PriorFor == null`.
- `BoundsFitterTests` (`ArenaFixture`, pure `Fit(samples, spawn)`): planted circle r = 20 (uniform disc, 500 samples) → `Circle`, radius 20, centre within 0.5 y, `High`; planted 40 × 40 square → `Square`, halfWidth 20 ± 1; planted 40 × 30 → `Rect`; L-shape (two overlapping 20 × 40 boxes) → `Custom` with ≥ 6 vertices and `Low`; 10 samples → fallback circle r = 20 `Low`; knockback exclusion drops samples in the 2 s window (recording-based overload).
- `EncounterModelBuilderShapeTests`: catalog with `Ability.Sheet` planted via `with { Sheet = new ActionRow(…, CastType: 3, EffectRange: 40, …, OmenPath: "gl_fan090_1bf") }` → `AbilityModel.Shape` cone 40/45, `Class TelegraphedAOE`; `Serialize(Deserialize(x)) == x` with shape and arena present; `Validate` passes.
- `ModuleEmitterComponentsTests` (hand-built EM): Raidwide + two abilities sharing `Circle(6)` + one `Cone(40, 45)` → exactly the lines `class Raidwide(BossModule module) : Components.RaidwideCast(module, (uint)AID.Raidwide);`, `class AAndB(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.A, (uint)AID.B], new AOEShapeCircle(6));`, `class C(BossModule module) : Components.SimpleAOEs(module, (uint)AID.C, new AOEShapeCone(40, 45.Degrees()));` (idiom adjusted to the verified templates) and `TrivialPhase()\n            .ActivateOnEnter<Raidwide>()\n            .ActivateOnEnter<AAndB>()\n            .ActivateOnEnter<C>();`; `Unclassified` abilities emit nothing; `Square`/`Rect`/`Custom` arenas render their templates.
- Goldens: `SyntheticDungeon` regenerated (ability 40001 hits all 4 players every instance → `Raidwide` → `RaidwideCast`; arena from 4 static players → `Low` fallback circle) — inspect the diff, do not just accept it. New `SyntheticShapes` golden (`Fixtures/SyntheticShapes.cs`: 3 pulls, 8 players moving, boss casts a 45° cone, a 6 y circle and a raidwide; `NullGameData`) must contain one `SimpleAOEs` cone, one `SimpleAOEs` circle, one `RaidwideCast`, and a `High` fitted circle arena. Regenerate via `FORGE_UPDATE_GOLDEN=1`.
- CLI tests from M1 still pass unchanged; `forge infer` output validates against the updated schema.
- Manual, recorded in `UPSTREAM.md`: both goldens' `.vbm.cs` compile inside `awgil/ffxiv_bossmod@1391bc42` under `BossMod/Modules/Forge/`.
- `scripts/build.sh` green; determinism: two `infer`+`emit` runs on the same log are byte-identical.

## 6. VERIFY before coding the affected line (record in `docs/UPSTREAM.md`)

| Item | Blocks |
|---|---|
| `SimpleAOEs`, `SimpleAOEGroups`, `RaidwideCast` ctor signatures at `1391bc42`; whether the AID param is `uint` or `ActionID` | `component*` templates, `IdCast` body |
| `AOEShapeCircle/Donut/Cone/Rect/Cross` ctor parameter order; `Angle` literal idiom (`45.Degrees()` vs `45f.Degrees()`) | `shape*` templates, `ShapeCSharp` number format |
| `ArenaBoundsSquare/Rect/Custom` ctors (`ArenaBoundsCustom` vertex type) | `bounds*` templates; Custom fallback rule in §4 |
| `TrivialPhase()` return type exposes chainable `ActivateOnEnter<T>()` | `StatesClass` fix |
| Omen path encodings (`fan(\d{3})`, `sircle_…`) on a real corpus | `TryParseDonut` unlock; weight stays 0.3 |
| Recorder `MOVE` throttling — is hold-last `PosRotAt` adequate at 0.1 s δ steps? | δ grid; if not, interpolate between samples in `Infer` and note it |
| CastType table vs BossMod action-definition helpers | `PriorFor` — table may be corrected, never extended |
