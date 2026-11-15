---
tags:
  - type/spec
  - project/bossmodforge
  - status/draft
type: spec
project: bossmodforge
status: draft
author: architect (Claude)
audience: builder agents
---

# BossModForge — Design Specification

**One line:** Turn recorded fights that have no BossMod module into a reviewed, validated, compilable BossMod module by inferring the encounter model (actors, abilities, AOE shapes, targeting, timeline) from replay data.

**Status:** v1. All upstream facts were verified against the pinned commits recorded in `docs/UPSTREAM.md` on 2026-09-20; no open VERIFY items remain.

---

## 0. Reading guide for builders

| You are building… | Read |
|---|---|
| Anything | §1, §2, §3, §11 |
| Replay ingestion | §4, §5.1, ADR-002 |
| Inference engine | §5, §6, Appendix A/B |
| Code emitter | §7, ADR-003 |
| Validation harness | §8 |
| In-game UI | §9 |
| Milestones / acceptance criteria | `docs/plans/2026-09-18-bossmodforge-implementation.md` |

Terminology (see also `CONTEXT.md` once created):

- **Replay / recording** — a BossMod `*.log` world-state recording (binary-compressed, binary, or text).
- **Pull** — one attempt at an encounter, from engage to wipe/kill.
- **Encounter** — a boss identified by `(zone/CFC id, primary boss OID)`; may span many pulls across many recordings.
- **Encounter Model (EM)** — Forge's canonical, JSON-serialisable description of an encounter (§6). The single source of truth; C# is derived from it.
- **Module** — a BossMod boss module: `OID/AID/SID/IconID/TetherID` enums + `*States : StateMachineBuilder` + component classes + `BossModule` subclass.
- **Flavor** — which BossMod codebase the emitted C# targets: `vbm` (awgil/ffxiv_bossmod) or `bmr` (FFXIV-CombatReborn/BossmodReborn).

---

## 1. Problem and goals

### 1.1 Problem

BossMod/BossModReborn only help in fights that have a hand-written module. Writing one today is: record a replay → open Replay Analysis → manually read casts, plot hit/miss scatter per ability, guess shapes → hand-write enums, state machine and components → rebuild BossMod → replay to verify → iterate. Most of that is mechanical and derivable from the recording. BossMod's built-in tools stop at "generate enum + trivial stub."

### 1.2 Goals

1. **G1 — From recordings to a module draft with zero hand-typed geometry.** Given ≥1 recording of an unmodded encounter, produce a module where enums, arena bounds, the cast timeline/state machine, and standard AOE/tankbuster/raidwide/stack/spread/knockback/gaze components are filled in from evidence.
2. **G2 — Every inference carries evidence and a confidence.** The builder (human or LLM) reviews a table, not raw logs. Low-confidence items become explicit `// TODO(forge)` markers in the emitted code, never silent guesses.
3. **G3 — Validation is closed-loop.** The emitted module is compiled against a pinned BossMod checkout and re-run against the same recordings; state-transition timing and AOE hit prediction are scored automatically before anyone installs it.
4. **G4 — The truth is data, not code.** The Encounter Model JSON is editable and re-emittable; switching flavor (`vbm` ↔ `bmr`) or regenerating after an upstream API change never requires re-analysis.
5. **G5 — Improves with more pulls.** Merging additional recordings tightens timings, disambiguates shapes, and reveals branches/phases.

### 1.3 Non-goals (v1)

- Video/screen-recording input. Only structured replay data (see ADR-001).
- Inferring *semantics* that are not in the data: debuff pairings, "which player should go where" strategies, mechanic names beyond the game's action names. These are emitted as review items.
- Replacing BossMod's runtime. Forge never draws hints itself; output is a module for BossMod.
- Runtime hot-loading of modules into BossMod/BMR. Checked at both pins (vbm `1391bc42`, bmr `b919e09`): neither ships a module-pack/hot-load facility — modules are compiled into the assembly. Output remains source the user compiles into their fork.
- Fights that are already modded (Forge can still run for comparison but it is not a goal).

---

## 2. System overview

```
                 ┌────────────────────────────────────────────────────────┐
                 │  BossMod / BMR (unmodified)                            │
                 │  ReplayRecorder ──▶ %AppData%/.../bossmod/replays/*.log│
                 └───────────────┬────────────────────────────────────────┘
                                 │ files
        ┌────────────────────────▼──────────────────────────────────────────┐
        │ BossModForge.Core  (pure .NET class library, no Dalamud refs)     │
        │                                                                   │
        │  Ingest      ──▶  Normalized Op Stream  (§4)                      │
        │  Segment     ──▶  Pulls, Participants   (§5.1–5.2)                │
        │  Catalog     ──▶  Ability / Status / Tether / Icon tables (§5.3)  │
        │  Infer       ──▶  Shapes, Targeting, Mechanic classes (§5.4–5.6) │
        │  Sequence    ──▶  Aligned timeline, phases, state machine (§5.7) │
        │  Bounds      ──▶  Arena bounds (§5.8)                             │
        │  Assemble    ──▶  Encounter Model JSON (§6)                       │
        │  Emit        ──▶  C# module (flavor-specific templates) (§7)      │
        └───────┬───────────────────────────────┬───────────────────────────┘
                │                               │
   ┌────────────▼──────────────┐     ┌──────────▼──────────────────────────┐
   │ BossModForge (Dalamud     │     │ forge (CLI, .NET console)           │
   │ plugin) — in-game UI (§9) │     │ ingest / infer / emit / validate    │
   │ review, tweak, export,    │     │ headless; used by builders & CI (§8)│
   │ Lumina via IDataManager   │     │ Lumina via game path                │
   └───────────────────────────┘     └──────────┬──────────────────────────┘
                                                │ compiles into
                                     ┌──────────▼──────────────────────────┐
                                     │ Pinned BossMod/BMR checkout          │
                                     │ + ReplayValidator (§8)               │
                                     └──────────────────────────────────────┘
```

Three deliverable projects in one solution (`BossModForge.slnx`):

| Project | Type | Depends on | Purpose |
|---|---|---|---|
| `BossModForge.Core` | classlib, `net10.0` | — (Lumina lives in Cli), System.Text.Json | All parsing, inference, model, emission. **No Dalamud, no ImGui, no BossMod reference.** Fully unit-testable. |
| `BossModForge.Plugin` | Dalamud plugin (`Dalamud.NET.Sdk/15.x`, `net10.0-windows`) | Core, Dalamud, ECommons (Svc pattern as in `TankCooldownHelper`) | In-game UI, replay discovery, game-data provider, export. |
| `BossModForge.Cli` | console, `net10.0` | Core, (optionally) BossMod.dll of pinned checkout for parser conformance tests | `forge ingest|infer|emit|validate|diff`. |
| `BossModForge.Tests` | xunit | Core | Golden replays → golden EM JSON → golden C#. |

Repo conventions to follow: ECommons `Svc` static accessor, all game-state access inside `Framework.Update`, `docs/specs` + `docs/plans`, `AGENTS.md` child contract for the project folder, `quality-gate.json`. Match `TankCooldownHelper.csproj` for SDK/TFM/LangVersion (`Dalamud.NET.Sdk/15.0.0`, `net10.0-windows10.0.26100.0`, LangVersion 13, `Nullable` enable).

---

## 3. Key architectural decisions (summary; full ADRs in `docs/adr/`)

| ADR | Decision | Why |
|---|---|---|
| **ADR-001** Input = BossMod replay logs; ACT network logs are a v2 adapter; video is out of scope | Replay logs contain per-frame positions of every actor, casts with location/rotation, action effects with per-target results, statuses, tethers, icons, env-controls, director updates — everything shape inference needs. ACT logs lack continuous positions for non-casting actors, making hit/miss geometry noisier (design-time assessment, not independently verified). Video has none of it. |
| **ADR-002** Core has **its own replay parser** into a Forge op model; parser conformance is tested in CLI against BossMod's `ReplayParserLog` | Referencing `BossMod.dll` from a Dalamud plugin duplicates a large assembly in a second load context and hard-couples the plugin to BossMod's internal API churn. The log format is small (≈40 four-char tags, versioned header `VER `), deterministic, and stable at the op level. Cost: track `ReplayRecorder.Version` bumps; mitigated by the conformance test. |
| **ADR-003** Encounter Model JSON is canonical; C# is emitted from templates per flavor | Decouples analysis from upstream API churn (BMR and vbm differ in `uint` vs enum-typed IDs, component ctor signatures). Lets users hand-edit the model and re-emit. Enables an LLM "polish" pass over structured data rather than code. |
| **ADR-004** Shape inference = **sheet prior × empirical fit**, never one alone | The `Action` sheet gives `CastType`, `EffectRange`, `XAxisModifier`, and the `Omen` VFX path encodes cone angle (`gl_fan090_…`). Empirical hit/miss fits verify and refine (e.g., cast type says circle but radius in sheet is 0 → fit radius). Where they disagree, confidence drops and both are reported. |
| **ADR-005** Emit only **standard BossMod components**; anything non-standard becomes a `Review` item | A generated custom component is unverifiable and unmaintainable. Standard components (`SimpleAOEs`, `RaidwideCast`, `SingleTargetCast`/`BaitAwayCast`, `SpreadFromCastTargets`, `StackWithCastTargets`/`StackWithIcon`, `KnockbackFromCastTarget`, `CastGaze`, `GenericTowers`, `ChasingAOEs`) cover the large majority of dungeon/trial/raid mechanics. |
| **ADR-006** Validation runs the emitted module inside a real BossMod build against the source recordings, headlessly | Only BossMod's own `StateMachine`/component code can tell whether the module is right. Forge's job is to make that loop automatic (§8). |
| **ADR-007** Deterministic inference first; LLM assistance optional and last | Reproducibility and testability. LLM pass (naming, choosing between ambiguous components, writing `Comment` strings) consumes the EM JSON + evidence, and its edits go back into the EM, never directly into C#. |

---

## 4. Ingestion

### 4.1 Sources

- **Primary:** BossMod replay directory (default `<Dalamud pluginConfigs>/BossMod/replays/`; also BMR's directory; configurable list). Files: `*.log`.
- **Format detection:** first 4 bytes: `CompressedBinary` magic → Brotli → binary reader; `RawBinary` magic → binary reader; otherwise text (`TextCondensed`/`TextVerbose`). Mirror `ReplayParserLog.Parse` (magics `BLCB`/`BLOG` and `ReplayRecorder.Version = 24` confirmed at `1391bc42`, see `docs/UPSTREAM.md` §1–2).
- **Version handling:** parse `VER ` entry: `(Version, QPF, GameVersion[, Ticks])`. Support N and N-1 versions; on unknown version, parse leniently and mark recording `Degraded` with the list of unknown tags.

### 4.2 Normalized Op Stream

Core defines `Op` records (one per replay tag) with a `Timestamp` (`DateTime`) and payload. Only tags needed for inference are modeled richly; all others are preserved as `RawOp(tag, bytes|text)` so nothing is lost and round-tripping stays possible.

Required tags (names and field layouts from `ReplayParserLog.cs` at `1391bc42`, transcribed in `docs/UPSTREAM.md` §5–6):

| Tag | Meaning | Used for |
|---|---|---|
| `VER `, `FRAM` | header, frame boundary (time, dt) | time base, interpolation |
| `ZONE` | zone / CFC id change | encounter identity |
| `ACT+`, `DIE+`, `MOVE`, `NAME`, `TARG`, `ATG+` (targetable), `ACSZ` (hitbox radius), `ALLY`, `MDLS`, `EANM`, `RSV `, `PATE`, `NENP`, `ESTA` | actor lifecycle, position/rotation, name, targetability, size, model/event state, event obj anim | participants, hit geometry, phase cues |
| `CST+` | cast start/stop (action id, target, location, rotation, total time, interruptible) | cast catalog, state machine, shape origin |
| `ACT!`/action-effect op (tag `CST!`, the one carrying `ActionEffects` per target) | source, action, main target, targets[] with effect entries (type, value, params) | hit sets, damage falloff, knockback detection |
| `STA+`, `STA-` | status apply/remove (id, extra, expiry, source) | SID enum, mechanic cues (vuln, doom, debuff markers) |
| `TET+`, `TETH` | tether apply/remove (id, target) | TetherID enum, tether-driven mechanics |
| `ICON` | icon on actor (id, target) | IconID enum, stack/spread/bait classification |
| `ENVC` | env-control (index, state) | arena transforms, towers/exaflares cues |
| `DIRU` | director update (params) | phase boundaries, wipe/kill detection |
| `PAR+`, `PAR `, `CLST`, `CLCD` … | party slots, client state (combat, countdown) | pull segmentation, player set |

Everything is loaded into a `Recording` object: `Meta{path, version, gameVersion, qpf}`, `List<Op>`, plus derived indexes (`ActorTimeline` per instance id with piecewise-constant pos/rot samples supporting `PosRotAt(t)` via nearest-previous sample; matches BossMod, which applies move ops directly with no interpolation — checked at `1391bc42`).

### 4.3 Game data provider

`IGameData` interface in Core:

```csharp
public interface IGameData {
  ActionRow? Action(uint id);        // Name, CastType, EffectRange, XAxisModifier, Range, OmenPath, CastTime(100ms), Cooldown
  string? BNpcName(uint nameId);
  string? StatusName(uint id);
  (uint cfcId, string name)? ContentForTerritory(uint territoryType);
  string? PlaceName(uint territoryType);
}
```

Implementations: `DalamudGameData` (plugin, via `IDataManager.GetExcelSheet<T>()`) and `LuminaGameData` (CLI, needs `--game-path`; pins Lumina 5.x in Cli only — ADR-009). Missing data must degrade gracefully (names fall back to `Actor{OID:X}` / `Ability{AID}`).

---

## 5. Inference engine

Every stage is a pure function `Stage(input, options) → output + List<Evidence> + Confidence`. Confidence is `0..1` with reasons; §6 defines its serialisation. All thresholds live in `InferenceOptions` with the defaults below.

### 5.1 Pull segmentation

Input: `Recording`. Output: `List<Pull>`.

1. Candidate boss actors: `BattleNpc`, targetable at some point, non-ally, `NameID != 0`, max HP in top quantile of NPCs in the zone (or explicitly selected by the user in UI).
2. Pull **start** = earliest of: client `InCombat` rising edge with the boss targetable; first enemy `CST+`; first enemy action-effect hitting a player; countdown end (`CLCD`) if present.
3. Pull **end** = earliest of: all players dead (`DIE+` for the whole party) ⇒ `Wipe`; boss `DIE+`/HP 0 with no re-spawn of same-OID targetable actor within 10 s ⇒ `Kill`; `ZONE` change ⇒ `Abandoned`; `InCombat` falling edge held > 8 s ⇒ `Wipe`/`Abandoned`.
4. Encounter identity = `(cfcId or territoryType, bossOID)`. Multiple pulls in one recording and across recordings with the same identity merge into one `Encounter`.

Edge cases to test: door bosses (two OIDs in same CFC → two encounters), adds phases where boss is untargetable (do *not* end the pull), trash packs (filtered by HP quantile; UI override), reset without wipe (Abandoned).

### 5.2 Participant classification

For each `OID` seen during pulls, aggregate: names, spawn count per pull, targetable ratio, HP, hitbox radius, whether it casts, whether it is ever a cast target, whether it is a `Helper` (OID `0x233C` — confirmed in both vbm §7 and BMR modules at the bmr pin), event-object (no HP, `EANM` ops), player (party slot).

Roles: `Boss` (primary), `Helper`, `Add`(targetable, HP, dies), `Caster`(untargetable, casts), `EventObject`, `Player`, `Pet`, `Unknown`. Emitted into `OID` enum with names from `BNpcName` (PascalCase, deduped by suffixing `_2` etc.; `Boss` always the primary).

### 5.3 Ability catalog

For each `(sourceOID, AID)` with ≥1 cast or action-effect from an enemy during pulls:

- `castTimeSec` (median of observed `CST+` total time), `hasCast`, `omenPresent` (sheet), `castCountPerPull` distribution.
- **Instances**: each cast + the matching action-effect(s). Match rule: same source instance id & AID; effect timestamp within `[castEnd − 0.2 s, castEnd + 2.5 s]` (server snapshot vs client effect latency; ±window is an option). Casts with no effect → `Visual/Interrupted`; effects with no cast → `Instant`.
- Per instance: `origin` (cast location if non-zero else source pos at cast end), `rotation`, `targets[]` (instance ids + effect entry types/values), `aliveAt(t)` player positions.
- Effect entry classification via BossMod's `ActionEffectType` names (full byte table in `docs/UPSTREAM.md` §3): `Damage`, `Heal`, `Knockback`, `Attract*`, `ApplyStatus*`, `Interrupt`, `Nothing`. Knockback/attract entries carry distance/direction params → §5.5.

Statuses, tethers, icons applied by enemies are catalogued similarly (`SID`, `TetherID`, `IconID` candidates with counts and typical targets).

### 5.4 AOE shape inference (ADR-004)

Goal: for each ability with damage effects, produce `Shape ∈ {Circle(r), Donut(rIn,rOut), Cone(r, halfAngle), Rect(length, halfWidth[, offset]), Cross(length, halfWidth), RaidwideNoShape, SingleTarget}` plus `Origin ∈ {Source, CastLocation, TargetActor}` and `Direction ∈ {SourceRotation, ToTarget, CastRotation}`.

**Step A — Sheet prior.** From `Action.CastType`/`EffectRange`/`XAxisModifier`/Omen (Forge's `ShapeInference.PriorFor` implements the eleven rows below; CastType 6/9/14 → no prior. Cross-checked against BossMod action-info helpers at the pin — no conflicting mapping; `docs/UPSTREAM.md` §9):

| CastType | Prior shape | Origin | Notes |
|---|---|---|---|
| 1 | SingleTarget | TargetActor | tankbuster/spread candidate |
| 2 | Circle(EffectRange) | Source | raidwide if r ≥ arena radius |
| 3 | Cone(EffectRange, angle from Omen) | Source, SourceRotation | `gl_fan090_…` → 90° total ⇒ halfAngle 45° |
| 4 | Rect(EffectRange, XAxisModifier/2) | Source, SourceRotation | line AOE |
| 5 | Circle(EffectRange) | TargetActor | targeted circle |
| 7 | Circle(EffectRange) | CastLocation | ground-targeted |
| 8 | Rect(dist to target, XAxisModifier/2) | Source, ToTarget | charge/line to target |
| 10 | Donut(?, EffectRange) | Source | inner radius from Omen path digits or fit |
| 11 | Cross(EffectRange, XAxisModifier/2) | Source | |
| 12 | Rect(EffectRange, XAxisModifier/2) | Source, SourceRotation | wide rect |
| 13 | Cone(EffectRange, angle) | Source, ToTarget | |

Unknown/other → no prior (fit-only). Omen parsing: regex `fan(\d{3})` → total angle (verified, `gl_fan090_1bf` → 90°); `sircle_…` inner/outer encodings remain unverified on a real corpus and are treated as no-prior (weight 0 path removed — donut radii come from empirical fit only).

**Step B — Empirical fit.** For each instance, sample all alive players' positions at `t_snap = castEnd + δ` where δ is chosen per ability by maximizing separability over a grid `δ ∈ {−0.2 … +1.0 s}` step 0.1 (the server snapshot lag is not fixed; BossMod's analyses use the effect timestamp — provide that as the default candidate too). Player hitbox radius is *not* added (BossMod convention: shapes are tested against player position; note the TODO in upstream `ConeAnalysis`).

Fit per candidate shape family using hit/miss labels:

- Circle: `r̂ = midpoint(max hit dist, min miss dist)` if `max hit < min miss`, else inconsistent. Margin = `min miss − max hit`.
- Donut: same on `[rIn, rOut]` from the ordered distance list.
- Cone: transform points to `(angle, dist)` in the origin/direction frame (like upstream `ConeAnalysis`, try all three `Targeting` modes); `halfAngle` and `r` chosen as the smallest rectangle in (angle, dist) space enclosing all hits and no misses; snap to common angles `{15, 22.5, 30, 45, 60, 67.5, 90, 105, 135}` deg half-angles if within 3°.
- Rect: project onto direction; `length` from max forward hit; `halfWidth` from max lateral hit vs min lateral miss; also test offset rects (origin behind source) if hits appear behind.
- Raidwide: all alive players hit in ≥90% of instances regardless of position ⇒ `RaidwideNoShape`.

**Step C — Combine.** Score each candidate `S = w_prior·PriorMatch + w_fit·FitConsistency − w_amb·Ambiguity`, defaults `0.35 / 0.55 / 0.10`. `FitConsistency` = fraction of instances (across all pulls) where the shape explains all labels; `Ambiguity` grows when hit/miss margin < 1.0 y. Confidence bands: `≥0.85 High`, `0.6–0.85 Medium`, `<0.6 Low → Review`. Snap final radii to `0.5` y and angles to standard values when within tolerance; keep raw fit in evidence.

Evidence stored: chosen δ, per-instance labelled point sets (compact), the fit margins, and the sheet prior. The UI renders the same scatter BossMod's analysis shows.

### 5.5 Targeting and mechanic classification

Per ability, derive `Mechanic` using rules (first match wins; store the matched rule id as evidence):

| Class | Rule |
|---|---|
| `Raidwide` | Shape `RaidwideNoShape`, or Circle with `r ≥ 0.95·arenaRadius`, single instance per cast, all players damaged |
| `Tankbuster` | CastType 1, target is the boss's current `TARG` at cast start, damage ≥ 3× median single-target damage of that pull, or applies a defensive-cue status; also detect `SharedTankbuster` if ≥2 players hit within 3 y of target |
| `Spread` | ≥4 simultaneous CastType-1/5 casts or icons on distinct players with damage falloff hits near targets |
| `Stack` | Icon on one player + shared damage entries on nearby players (upstream `DamageFalloffAnalysis` semantics), or single cast whose hits cluster around one target |
| `Knockback` | Any `Knockback` effect entry; distance from entry param; origin = source or cast location; `AwayFromOrigin` vs `Directional` by comparing player displacement vectors in the 1.5 s after effect |
| `Attract` | `Attract*` entries |
| `Gaze` | Players hit ⇔ facing origin (dot of player rotation to origin > 0.9) across instances; misses when facing away |
| `Towers` | `ENVC`/event-object cue + Circle hits where hitting exactly N players is safe and 0 triggers a different (raidwide) effect — flagged `Review` unless clear |
| `Chasing/Exaflare` | Same AID repeating at fixed interval with origin advancing along a line |
| `TelegraphedAOE` | Has Omen + fitted shape, not matched above |
| `HiddenAOE` | No Omen + fitted shape (still emitted as `SimpleAOEs` from cast since a cast exists) |
| `Instant` | No cast; effects only → cannot be predicted; emit as comment |
| `Interrupt` | Cast is `interruptible == true` |

Targeting for baits: if the main target across instances is consistently `nearest`/`farthest` player to source (compute rank), record `BaitRule`.

### 5.6 Phase and branch detection

- **Phase cue events:** boss `ATG+` false→true transitions, boss `DIE+`/HP-threshold (e.g., stops casting at 50 %, untargetable, new OID spawns), `DIRU` params that appear exactly once per pull at consistent progress points, `ENVC` bursts, `MDLS` changes, zone-wide `ACT+` waves (adds spawn).
- **Phase** = maximal interval between cues that recurs across pulls with a consistent cast vocabulary.
- **Branch** = position in the aligned timeline where pulls diverge into ≥2 different next-cast sets with each branch seen ≥2 times (or ≥1 if only 1–2 pulls, flagged `Low`).

### 5.7 Timeline alignment → state machine

1. **Anchor events** per pull: enemy casts with `castTime ≥ 0.5 s` (or any cast if none), ordered by start; plus phase cues.
2. **Alignment:** progressive multi-sequence alignment over AID symbols (affine-gap Needleman–Wunsch, match score by AID equality with a small bonus for similar relative timing). Output: consensus sequence with per-position support count and per-gap timing stats `{median, MAD}` of `start_i − start_{i−1}`.
3. **Fold repeats:** detect the longest repeating suffix block (loop) via suffix-array on the consensus; emit it as the rotation block with `SimpleState` timeouts back-edge to loop start. BossMod has no loop API (confirmed §8.1): upstream unrolls repeated rotations as successive method calls (`Method(id + 0x10000·k, …)`) and ends open-ended phases with `SimpleState(id + 0xFF0000, 10, "???")`; Forge emits the unrolled form.
4. **Emit builder calls** (see §7): `Cast(id, AID, delay=median gap − castTime, castTime, name)`; if MAD > 1.5 s or support < 60 % → `Timeout`/`ComponentCondition` with `Review` marker. Consecutive casts from helpers within 0.5 s → grouped via `CastMulti`. Untargetable spans → `Targetable(id, false/true, delay, name)`. Phases → `DeathPhase(0, Phase0)` chain / `SimplePhase` with HP condition when a HP threshold cue was detected (confirmed §8.1 idiom, same at both pins: `SimplePhase(0, Phase1, "P1").Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed || Module.PrimaryActor.HPRatio <= {ratio};`).
5. State ids: `0x0001_0000 * phaseIndex + 0x100 * stepIndex` style monotonic hex (mirror upstream style).

### 5.8 Arena bounds

Collect all alive player positions during pulls (excluding the 2 s after any knockback or before death). Fit:

- Circle: center = minimal enclosing circle center; radius snapped to `{10,15,19,20,25,30}` if within 1.0 y else raw.
- Square/Rect: axis-aligned bounding box with 1.0 y padding; prefer if hull fill ratio of circle < 0.7 and box ratio > 0.8.
- Custom polygon: only when neither fits (`Review`), emit `ArenaBoundsCustom` with hull vertices simplified (Douglas–Peucker 0.5 y).

Center is also cross-checked against the boss spawn position and the median of `CST+` locations for centered casts. Arena *changes* (env-control + step change in hull) create per-phase bounds.

---

## 6. Encounter Model (EM) — canonical JSON

Versioned (`"schemaVersion": 1`). Written to `<forge-dir>/encounters/<cfcId>-<bossOID>.em.json`. Key shape (abbreviated; the builder writes the full JSON Schema in `schema/em.v1.schema.json`):

```jsonc
{
  "schemaVersion": 1,
  "identity": { "cfcId": 1234, "territoryType": 1180, "bossOid": 17000, "bossNameId": 12345, "name": "…", "contentType": "Dungeon|Trial|Raid|Unreal|Other" },
  "sources": [ { "path": "…log", "recorderVersion": 24, "gameVersion": "…", "pulls": [ { "start": "…", "end": "…", "outcome": "Wipe|Kill|Abandoned" } ] } ],
  "participants": [ { "oid": 17000, "enumName": "Boss", "role": "Boss", "names": ["…"], "hitboxRadius": 7.0, "spawnsPerPull": 1 } ],
  "abilities": [ {
    "aid": 40000, "enumName": "Raidwide", "sourceOid": 17000, "castTime": 5.0, "omen": true,
    "shape": { "kind": "Circle", "radius": 40, "origin": "Source", "direction": null },
    "mechanic": { "class": "Raidwide", "rule": "RW-1" },
    "confidence": { "value": 0.93, "band": "High", "reasons": ["sheet:castType2", "fit:12/12 consistent"] },
    "evidence": { "snapshotDelta": 0.3, "instances": 12, "fitMargin": 4.2, "sheet": { "castType": 2, "effectRange": 40 } },
    "review": null
  } ],
  "statuses": [ { "sid": 2970, "enumName": "…", "appliedBy": 17000, "count": 4 } ],
  "tethers": [ … ], "icons": [ … ],
  "arena": { "phases": [ { "index": 0, "bounds": { "kind": "Circle", "center": [100,100], "radius": 20 } } ] },
  "timeline": {
    "phases": [ { "index": 0, "name": "P1", "endCue": { "kind": "BossHp", "pct": 0 } } ],
    "steps": [ { "id": "0x00000100", "kind": "Cast|CastMulti|Timeout|Targetable|ComponentCondition", "aid": 40000, "delay": 6.1, "castTime": 5.0, "timingMad": 0.2, "support": 5, "of": 5, "name": "Raidwide", "activate": ["Raidwide"], "deactivate": [], "review": null } ],
    "loop": { "fromStepIndex": 8, "toStepIndex": 20 }
  },
  "components": [ { "name": "Raidwide", "kind": "RaidwideCast", "aid": 40000 } ],
  "review": [ { "id": "R1", "severity": "High|Medium|Low", "target": "ability:40012", "message": "Cone angle ambiguous (60° vs 90°); only 2 instances", "suggested": ["…"] } ],
  "emit": { "flavor": "bmr", "namespace": "BossMod.Dawntrail.Dungeon.D01…", "className": "…", "maturity": "WIP" }
}
```

Rules: every inferred value has `confidence`; every `Low` confidence yields a `review[]` entry; `enumName`s are unique and C#-identifier-safe; hand edits are preserved by an `"overrides"` block that the inference stage never overwrites (three-way merge: `inferred`, `overrides`, `effective`).

---

## 7. Code emission

### 7.1 Output layout (mirrors upstream module folders)

```
<Namespace path>/<ModuleName>/
  <ModuleName>Enums.cs      // OID, AID, SID, IconID, TetherID
  <ModuleName>States.cs     // class <Name>States : StateMachineBuilder
  <ModuleName>.cs           // components + [ModuleInfo] BossModule subclass
  FORGE.md                  // human summary: confidence table, review items, source recordings
```

Every file starts with a header comment: `// Generated by BossModForge <ver> from <N> recordings (<hash of EM>). Edit the .em.json and re-emit, or edit here and run `forge diff`.`

### 7.2 Flavor profiles (ADR-003)

`IEmitterProfile { Flavor; string StateCast(...); string ComponentCtor(kind, args); string ModuleInfoAttr(...); string ArenaBounds(...); string IdCast(enum, member) }`. Two profiles, each pinned to an upstream commit in `flavors/<flavor>.json`:

| Concern | `vbm` (awgil) | `bmr` (CombatReborn) |
|---|---|---|
| Cast state | `Cast(id, AID.X, delay, castTime, "Name")` (enum-typed generic) | same — `Cast<TAID>(uint id, TAID aid, …) where TAID : unmanaged, Enum` (enum-typed at `b919e097`, not `(uint)`) |
| Component ctor | `Components.SimpleAOEs(module, AID.X, shape)` vs `ActionID.MakeSpell(AID.X)` | `Components.SimpleAOEs(module, (uint)AID.X, shape)` (radius overload exists), `Components.SimpleAOEGroups(module, [(uint)AID.A, (uint)AID.B], shape)`, `Components.RaidwideCast(module, (uint)AID.X, "hint")` — no `StandardAOEs`/`GroupedAOEs` at the pin |
| AOE shapes | `AOEShapeCircle(r)`, `AOEShapeCone(r, halfAngle.Degrees())`, `AOEShapeRect(len, halfW)`, `AOEShapeDonut(rIn,rOut)`, `AOEShapeCross(len, halfW)` | same names; some BMR shapes accept extra params |
| Module ctor | `BossModule(ws, primary, center, bounds)` | same (plus optional `onlyLoadIfTargetable = false`); no `SharedBounds`/`ArenaBoundsComplex` at the pin |
| ModuleInfo | `[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = …CFC, GroupID = …, NameID = …)]` | same, plus confirmed `Contributors`, `PrimaryActorOID`, `SortOrder` |

One behavioural difference recorded for completeness: BMR `SimpleState` auto-increments duplicate state ids with a log where vbm throws — irrelevant for Forge (ids are unique).

Every row was verified against the pins on 2026-09-20 (`docs/UPSTREAM.md` §8, §10); the profile files exist precisely so this churn is data. Templates are Scriban or raw `StringBuilder` (builder's choice; prefer `StringBuilder` for zero deps, as upstream does).

### 7.3 Component mapping (ADR-005)

| Mechanic class | Emitted component |
|---|---|
| Raidwide | `RaidwideCast` |
| Tankbuster (single) | `SingleTargetCast` (+ `BaitAwayCast` when hits cluster) |
| SharedTankbuster | `CastSharedTankbuster` (§8.2 signature `(BossModule, Enum aid, AOEShape, bool originAtTarget = false)`; BMR variant takes `uint aid` and subclasses it) |
| TelegraphedAOE / HiddenAOE | `SimpleAOEs(aid, shape[, maxCasts])`; `SimpleAOEGroups` when several AIDs share a shape |
| Spread | `SpreadFromCastTargets(aid, radius)` or `SpreadFromIcon(icon, aid, radius, delay)` |
| Stack | `StackWithCastTargets` / `StackWithIcon` |
| Knockback | `KnockbackFromCastTarget(aid, distance[, shape])` or `SimpleKnockbacks` (BMR) |
| Attract | `KnockbackFromCastTarget(aid, distance, kind: Knockback.Kind.TowardsOrigin)` (no separate attract component exists at the pin; BMR: `SimpleKnockbacks` with `kind: Kind.TowardsOrigin`) |
| Gaze | `CastGaze(aid)` |
| Chasing | `StandardChasingAOEs` (`Review` for parameters) |
| Towers | `GenericTowers` subclass stub + `Review` |
| Instant / unknown | comment block listing observations |

Each generated component is `ActivateOnEnter`'d at the state where its first cast starts and `DeactivateOnExit`'d after its last instance in that block, unless it recurs throughout, in which case it is activated once in the phase entry state.

### 7.4 `Review` markers in code

```csharp
// TODO(forge:R3, Low 0.41): cone half-angle ambiguous between 30° and 45° (2 instances, margin 0.6y). Using 45°.
```

Never emit uncommented guesses. `forge emit --strict` fails if any `High`-severity review remains unresolved.

---

## 8. Validation harness (ADR-006)

`forge validate --module <dir> --recordings <files> --bossmod <checkout> --flavor bmr`:

1. Copies the emitted files into the pinned BossMod checkout under a scratch namespace, builds it (`dotnet build`), fails fast on compile errors and maps them back to EM items where possible (enum member unknown, component ctor mismatch → flavor profile bug).
2. Runs a small **ReplayValidator** host (a console project inside the checkout, modeled on upstream `UIDev`/replay tooling) that loads each recording through BossMod's own `ReplayParserLog`, instantiates the module for the encounter, steps the world state, and records per frame: active state id, expected vs actual transition times, each component's `ActiveAOEs`/forbidden zones vs. where players actually were when hit.
3. Scores:
   - **Transition timing**: for each emitted step, `|actual − expected|`; pass if p90 ≤ 1.0 s and no transition exceeds `Timeout` (desync). Report which steps drifted.
   - **AOE precision/recall**: for each `SimpleAOEs`-like component, at each action-effect: `predictedInside(playerPos) == wasHit`. Pass ≥ 0.95 precision and recall; below → downgrade EM confidence and add `Review`.
   - **Coverage**: fraction of damaging enemy casts covered by any component.
   - **No exceptions** thrown by the module across all recordings.
4. Emits `validation-report.json` + markdown; the plugin UI shows the same report.
5. Also runs the **parser conformance** test (ADR-002): op counts and key field checksums from Forge's parser vs. BossMod's for each recording. Conformance requires a `BossMod.dll` built from the pinned checkout, passed via `forge conform <log…> --bossmod-dll <path>`; when `--bossmod-dll` is absent the command prints `error: conformance requires --bossmod-dll (a build of BossMod.dll from the pinned checkout); see docs/UPSTREAM.md` and exits 4 (documented choice per M0 acceptance).

Definition of "done" for a generated module = validate passes on all source recordings with zero `High` review items.

**Gated-environment note (M4b):** the in-checkout `dotnet build` and ReplayValidator host run (steps 1–2) require a BossMod/BMR checkout that actually builds on the machine (Dalamud libs; typically Windows). Everything else — checkout-shape validation, deterministic module staging, compile-error mapping to EM items, report wiring — is implemented and unit-tested on Linux. Without a valid checkout `--bossmod` exits 4 with `does not look like a BossMod checkout`; on a build failure mapped errors go to stderr, `validation-report.{json,md}` is still written with a `failures` entry, and the command exits 3. See `docs/UPSTREAM.md` §11.

---

## 9. In-game plugin (BossModForge.Plugin)

Command: `/forge`. Windows (ImGui, Dalamud `WindowSystem`):

1. **Recordings** — lists replay dirs, files with parsed meta (zone, boss guess, pulls, duration, size, recorder version). Multi-select → "Analyze". Detects BossMod/BMR via IPC (`BossMod.HasModuleByDataId`, as in `MarkerMind/Core/Adapter/BossmodBridge.cs`) and shows "Recording: on/off" hint; optional convenience: open BossMod's replay settings (no own recorder in v1).
2. **Encounter** — identity, pulls, participants table (OID, name, role, counts, enum name — editable).
3. **Abilities** — the review table: AID, name, source, cast time, shape (kind/params), mechanic class, confidence band with colour, instances, actions: *accept*, *override shape*, *mark as ignore*, *open evidence*. Evidence panel draws the angle/distance scatter (like upstream `ConeAnalysis`) with an overlaid fitted shape, and a mini top-down arena view of one instance (reuse the approach in `MarkerMind/Core/Renderer/MinimapRenderer.cs`).
4. **Timeline** — aligned consensus with per-pull lanes, support %, timing MAD, phase/loop markers; click to see the pull's raw casts around that step.
5. **Export** — flavor, namespace, class name, maturity; "Write EM JSON", "Emit C#", "Copy to clipboard", "Open output folder", and the last `validation-report`.
6. **Settings** — replay dirs, output dir, flavor + pinned commit path, inference thresholds (advanced).

Threading: parsing/inference runs on a `Task` off the framework thread with a cancellation token and progress; UI reads immutable snapshots. Game-data lookups (Lumina via `IDataManager`) are thread-safe reads but should be pre-fetched into a cache on the framework thread once per analysis.

---

## 10. CLI (BossModForge.Cli)

```
forge ingest   <log…> [--out recording.json]            # parse + dump normalized ops (debug)
forge infer    <log…> --game-path <ffxiv> [--select-boss OID] --out <em.json>
forge emit     <em.json> --flavor bmr|vbm --profile flavors/bmr.json --out <dir> [--strict]
forge validate --module <dir> --recordings <log…> --bossmod <checkout> --flavor …
forge diff     <em.json> <module dir>                    # detect hand edits in C# vs EM (for round-trip warnings)
forge merge    <log…> --out <em.json> [--em <em.json>] [--select-boss OID]
                                                         # merge recordings of one encounter;
                                                         # --em carries overrides, --select-boss filters pulls by primary OID
forge polish   <em.json> --provider file|http [--patches f] [--endpoint u] [--model m]
                                                         # [--api-key-env n] [--out f]
                                                         # apply allowlisted LLM/user patches (enumName/name/comment/
                                                         # review resolution); rejected patches are listed, never fatal
```

Exit codes: `0` ok, `2` review items at `High`, `3` validation failed, `4` parse error.

---

## 11. Upstream pinning

Builders must pin: `awgil/ffxiv_bossmod@1391bc42` (flavor `vbm`) and `FFXIV-CombatReborn/BossmodReborn@b919e0970d6559de280c40defb4117dd7275c961` (flavor `bmr`) in `flavors/*.json` and `docs/UPSTREAM.md`, and record the `ReplayRecorder.Version` they implement against (Forge implements **24**). `docs/UPSTREAM.md` is the verification log for every upstream fact in this spec; it was fully reconciled against both pins on 2026-09-20 and its open-item list (§9 "Closed VERIFY") is empty.

---

## 12. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Replay format bump breaks parser | Ingest fails | Version gate + lenient `RawOp` fallback; conformance test in CI against pinned upstream; degrade rather than crash |
| Snapshot-lag ambiguity makes shapes look inconsistent | Wrong radius/angle | Per-ability δ search (§5.4-B); report margin; more pulls |
| Few pulls (1) → weak timeline | Desync in-game | Emit `Timeout`s with wide tolerance + `Review`; encourage merging more recordings (G5) |
| Upstream component API churn | Emitted code fails to compile | Flavor profiles as data + validate step catches at build time |
| Users expect it to "just work" for complex mechanics | Disappointment / unsafe hints | Strict mode refuses export with `High` reviews; FORGE.md lists what is *not* covered; maturity forced to `WIP` |
| In-game analysis stalls the client | Bad UX | Background task, chunked parsing, progress + cancel |

---

## 13. Appendix A — Evidence record shapes

- `ShapeEvidence { delta, instanceCount, labelledPoints: [{inst, angleDeg, dist, lateral, forward, hit}], candidateScores: [{kind, params, prior, fit, ambiguity, score}], sheet }`
- `TimelineEvidence { perPull: [{pull, startOffsets: [..]}], gapStats: {median, mad}, support }`
- `BoundsEvidence { hullVertices, circleFit: {center, r, fillRatio}, boxFit: {min, max, fillRatio} }`

## 14. Appendix B — Worked example (synthetic)

Recording: 3 pulls of a dungeon boss. Catalog finds AID 40001 "Heavy Slash" (CastType 3, EffectRange 40, Omen `gl_fan090_1bf`, 9 instances). Prior: Cone(40, 45°). Fit at δ = 0.3 s: all hits within 43°, all misses beyond 47° in all 9 instances → FitConsistency 1.0, margin 4°. Score 0.97 → `High`. Mechanic: `TelegraphedAOE` → `SimpleAOEs((uint)AID.HeavySlash, new AOEShapeCone(40, 45.Degrees()))`. Timeline: Heavy Slash follows "Raidwide" by 8.2 s (MAD 0.15, support 3/3) → `Cast(id, (uint)AID.HeavySlash, 3.2, 5, "Heavy Slash")` with `.ActivateOnEnter<HeavySlash>()`. Validate: transitions p90 0.3 s, AOE precision/recall 1.0/1.0 → done.
