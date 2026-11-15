---
tags:
  - type/plan
  - project/bossmodforge
  - status/active
type: plan
project: bossmodforge
status: active
milestone: M0
spec: ../specs/2026-09-18-bossmodforge-design.md
plan: ./2026-09-18-bossmodforge-implementation.md
upstream: ../UPSTREAM.md
---

# M0 builder brief — skeleton + replay parser

Tracer bullet: `forge ingest a.log b.log` prints, per file, encoding/version and a per-tag op count table. Two builders work in parallel; §6 lists who owns what and §7 lists the contracts neither may change without the architect.

Read first: `docs/UPSTREAM.md` (all facts about the replay format live there; this brief does not repeat field layouts), `AGENTS.md`, `CONTEXT.md`. Anything marked VERIFY in the spec that touches M0 has been resolved in `UPSTREAM.md` **except** the `Class` enum tail (copy it from `BossMod/Data/ClassRole.cs@1391bc42`).

## 1. Scope

In: solution, `Core`, `Cli`, `Tests`, `scripts/build.sh`, `quality-gate.json`, `.gitignore`, `Directory.Build.props`, `global.json`; replay parser (text + raw binary + Brotli binary), typed ops, `Recording`, `ActorTimeline`, `RecordingBuilder` fixture, CLI `ingest`, tests.

Out: **Plugin project (deferred to M5 — do not create it in M0)**, inference, EM, emitter, `conform` subcommand (deferred; document in `README.md`), Lumina/`IGameData` implementations (interface stub only).

Version support: full for replay versions **23–24**; versions **10–22** parse using the gates in `UPSTREAM.md §6` (best effort, add `ParseIssue` code `OlderVersion`); version **< 10** → stop with `UnsupportedVersion` and return an empty `Recording` with that issue.

## 2. Files to create

```
BossModForge/
  BossModForge.slnx
  global.json                          # { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
  Directory.Build.props
  .gitignore
  quality-gate.json
  scripts/build.sh
  README.md                            # replace design-only stub: build/run/test + FORGE_REAL_LOGS
  src/BossModForge.Core/BossModForge.Core.csproj
  src/BossModForge.Core/Replay/Tag.cs
  src/BossModForge.Core/Replay/Primitives.cs        # Angle, ActionId, ActionType, ActorType, Class, HpMp, PosRot, ActorStatus, ActionEffect(s), CastTarget
  src/BossModForge.Core/Replay/Op.cs                # Op base + RawOp + OpaqueOp
  src/BossModForge.Core/Replay/Ops.World.cs
  src/BossModForge.Core/Replay/Ops.Actor.cs
  src/BossModForge.Core/Replay/Ops.Party.cs
  src/BossModForge.Core/Replay/Recording.cs
  src/BossModForge.Core/Replay/ActorTimeline.cs
  src/BossModForge.Core/Replay/ParseIssue.cs
  src/BossModForge.Core/Replay/ReplayParser.cs      # public entry, encoding detection, Brotli
  src/BossModForge.Core/Replay/IReplayInput.cs      # TextInput / BinaryInput (mirror upstream Input abstraction)
  src/BossModForge.Core/Replay/TextInput.cs
  src/BossModForge.Core/Replay/BinaryInput.cs
  src/BossModForge.Core/Replay/OpReader.cs          # tag → read method table, version gates
  src/BossModForge.Core/GameData/IGameData.cs       # interface + NullGameData
  src/BossModForge.Cli/BossModForge.Cli.csproj
  src/BossModForge.Cli/Program.cs
  src/BossModForge.Cli/Commands/IngestCommand.cs
  tests/BossModForge.Tests/BossModForge.Tests.csproj
  tests/BossModForge.Tests/Fixtures/RecordingBuilder.cs
  tests/BossModForge.Tests/Fixtures/TextRecordingWriter.cs
  tests/BossModForge.Tests/Fixtures/BinaryRecordingWriter.cs
  tests/BossModForge.Tests/Fixtures/SyntheticDungeon.cs  # the shared 3-pull fixture from spec Appendix B
  tests/BossModForge.Tests/Replay/EncodingParityTests.cs
  tests/BossModForge.Tests/Replay/TextParserTests.cs
  tests/BossModForge.Tests/Replay/BinaryParserTests.cs
  tests/BossModForge.Tests/Replay/ActorTimelineTests.cs
  tests/BossModForge.Tests/Replay/RealLogSmokeTests.cs   # skipped unless FORGE_REAL_LOGS set
  tests/BossModForge.Tests/Cli/IngestCommandTests.cs
```

### Project files

`Directory.Build.props` (repo root of BossModForge):

```xml
<Project>
  <PropertyGroup>
    <LangVersion>13</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
    <TreatWarningsAsErrors Condition="'$(MSBuildProjectName)' == 'BossModForge.Core'">true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

| Project | SDK / TFM | Package refs (latest stable via `dotnet add package`, no invented versions) | Notes |
|---|---|---|---|
| `BossModForge.Core` | `Microsoft.NET.Sdk`, `net10.0` | **none** in M0 (System.IO.Compression.Brotli is in-box) | `RootNamespace=BossModForge.Core`. Never references Dalamud/ImGui/BossMod.dll/Lumina in M0. |
| `BossModForge.Cli` | `Microsoft.NET.Sdk`, `net10.0`, `OutputType=Exe`, `AssemblyName=forge` | `System.CommandLine` (current stable 2.x) | `ProjectReference` Core. |
| `BossModForge.Tests` | `Microsoft.NET.Sdk`, `net10.0` | `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | `ProjectReference` Core and Cli. `IsPackable=false`. |

Plugin project: **skipped** in M0. When created in M5 it uses `Dalamud.NET.Sdk/15.0.0`, `net10.0-windows10.0.26100.0`, `ECommons 3.1.0.13`, matching `../TankCooldownHelper/TankCooldownHelper.csproj`. `Directory.Build.props` must not break that (do not set `TargetFramework` there).

### `scripts/build.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore BossModForge.slnx
dotnet build BossModForge.slnx -c Release --no-restore -warnaserror:CS
dotnet test  BossModForge.slnx -c Release --no-build --logger "trx;LogFileName=test.trx"
```

Exit non-zero on any failure. `FORGE_REAL_LOGS` (a directory or `;`-separated file list) is passed through the environment; tests skip when unset.

### `quality-gate.json` (adapted from `../TankCooldownHelper/quality-gate.json`)

```json
{
  "version": 1,
  "project": "bossmodforge",
  "profile": "dotnet-classlib",
  "paths": { "report_dir": ".quality-gate/report", "run_dir": ".quality-gate/runs", "log_dir": ".quality-gate/logs" },
  "levels": {
    "smoke":   { "required": ["changed-files"] },
    "normal":  { "required": ["changed-files", "build", "test"], "conditional": ["markdown-audit-if-docs-changed"] },
    "release": { "required": ["changed-files", "build", "test", "test-hack-detector"] }
  },
  "commands": {
    "build": { "shell": "bash", "command": "dotnet build BossModForge.slnx -c Release", "timeout_seconds": 300 },
    "test":  { "shell": "bash", "command": "dotnet test BossModForge.slnx -c Release", "timeout_seconds": 600 }
  },
  "changed_file_rules": {
    "docs":  { "patterns": ["**/*.md"], "checks": ["markdown-audit-if-docs-changed"] },
    "core":  { "patterns": ["src/BossModForge.Core/**/*.cs"], "risk": "core-critical" },
    "gate":  { "patterns": ["quality-gate.json", "AGENTS.md", "*.slnx", "**/*.csproj", "Directory.Build.props", "global.json", "scripts/**"], "risk": "gate-critical" }
  },
  "exceptions": { "allow_with_expiry": true, "max_days": 14 }
}
```

Drop the Windows PowerShell markdown-audit command from the TankCooldownHelper file; this repo runs on Linux.

### `.gitignore`

```
bin/
obj/
.vs/
*.user
TestResults/
.quality-gate/
*.log
!docs/**/*.log
fixtures/real/
```

Real recordings are never committed; `RecordingBuilder` fixtures are generated at test time, not checked in as files.

## 3. Parser strategy

1. **Text first.** `RecordingBuilder.ToText()` is the reference producer; `TextInput` + `OpReader` are the reference consumer. Get `EncodingParityTests` green for text → text round trip, then add binary.
2. **Binary second.** `BinaryInput` implements the same `IReplayInput`; `OpReader` is shared and must not branch on encoding except through `IReplayInput.CanRead()` (always `true` for binary) and the `VER ` start-ticks field.
3. **Brotli third.** `ReplayParser.Parse` wraps in `BrotliStream` when the magic is `BLCB`. Nothing else changes.
4. **Unknown tags.** Text: `RawOp(tag, fields)` + `ParseIssue(Info, "UnknownTag")` once per distinct tag; continue. Binary: `ParseIssue(Degraded, "UnknownBinaryTag")` and **stop** (payload length is unknown); `Recording.Ops` keeps everything read so far.
5. **Opaque tags.** Client/deep-dungeon/network tags (`UPSTREAM.md §6` "opaque" tables) are consumed field-by-field per layout and emitted as `OpaqueOp(tag)` — no payload retained. They still count in `TagCounts`.
6. **Per-op fault isolation (text only).** A malformed text line → `ParseIssue(Warning, "MalformedLine", line)` and skip the line. Binary cannot recover; treat like unknown tag.
7. **Timestamps.** Text: line timestamp parsed with `DateTime.Parse(s, InvariantCulture, RoundtripKind)`. Binary: `Meta.StartTime = new DateTime(startTicks)`; ops before the first `FRAM` get `StartTime`; `FRAM` timestamp = `StartTime.AddTicks((long)((UInt128)(qpc - qpcStart) * TimeSpan.TicksPerSecond / qpf))` with `qpcStart` = first frame's QPC; subsequent ops take the latest frame timestamp. **Integer math only.**
8. **Angles** are stored in radians (`Angle.Rad`); text converts degrees → radians on read. **Positions** are `System.Numerics.Vector3` as written (BossMod X/Y/Z; Y is height).
9. **Culture**: every `Parse`/`ToString` uses `CultureInfo.InvariantCulture`. `InvariantGlobalization` is on.

## 4. CLI

`forge ingest <files...> [--json]`

```
/path/to/a.log
  encoding: binary+brotli   version: 24   qpf: 10000000   game: 2026.08.20.0000.0000
  start: 2026-09-19T20:00:00.0000000   ops: 184203   actors: 57   issues: 0
  tag    count
  FRAM   61234
  MOVE   58812
  ...
```

- One block per file, files in argument order; tags sorted by count descending, then tag ascending.
- `--json` emits one JSON object per file (`path, encoding, version, qpf, gameVersion, startTime, opCount, actorCount, issues[], tagCounts{}`) with sorted keys.
- Exit code: `0` all files parsed with no `Degraded` issue; `1` any `Degraded`; `2` any file unreadable/exception. Exceptions are caught per file and reported as `error:` lines; the other files still run.
- Implemented with `System.CommandLine`; `Program.cs` builds a `RootCommand("forge")` with `ingest` only. Leave a `Commands/` folder so M1 can add `infer`/`emit`.

## 5. Tests (xunit)

**Encoding parity (the M0 acceptance test):** build one `Recording` with `RecordingBuilder` (use `SyntheticDungeon.Build()` — the 3-pull fixture from spec Appendix B, AID 40001 cone, plus at least one of every op the builder supports, plus a `Raw("ZZZZ", …)` line only in the text variant), serialise to text, raw binary and Brotli binary, parse all three, then assert with `OpEquivalence`:

- same op count **after removing `RawOp`s** from the text stream (the binary variants cannot contain `ZZZZ`);
- pairwise same runtime type, same `Tag`, same `Timestamp.Ticks`;
- all integral/id/enum/string fields exactly equal;
- `Vector3` components within `5e-4`, `Angle.Rad` within `2e-5` (text writes degrees `f3`), `ActorStatus.ExpireAt` within 1 ms (text writes remaining seconds `f3`), other floats exact (`g9` round-trips).

Other required tests: `VER ` header variants (text with/without game version → `OlderVersion`), unknown text tag → `RawOp`, unknown binary tag → `Degraded` + partial ops, version 9 → `UnsupportedVersion`, `CST!` with 0/1/3 targets and effects containing zeros in the middle (text drops zero effects — verify positions are preserved only as a *set of non-zero effects*, see contract note on `CastTarget`), `ActorTimeline.PosRotAt` before first sample / between samples / after destroy, instance-id reuse after `ACT-`, `IngestCommand` text and `--json` output golden (byte-exact), real-log smoke (`FORGE_REAL_LOGS`: zero exceptions, `RawOp`/opaque-ratio reporting, skipped when unset).

## 6. File ownership

**Builder A (Composer)** — solution, csproj, `Directory.Build.props`, `global.json`, `.gitignore`, `quality-gate.json`, `scripts/build.sh`, `README.md`, `src/BossModForge.Cli/**`, `tests/BossModForge.Tests/Cli/**`, `tests/BossModForge.Tests/BossModForge.Tests.csproj`, `src/BossModForge.Core/GameData/IGameData.cs`.

**Builder B (Grok)** — `src/BossModForge.Core/Replay/**`, `tests/BossModForge.Tests/Fixtures/**`, `tests/BossModForge.Tests/Replay/**`.

Rules: A creates the csproj files **first** (empty projects that build) and commits; B adds files without touching csproj (SDK globbing). Neither edits the other's files; if a contract in §7 needs to change, stop and ask the architect. A codes `IngestCommand` against the §7 signatures and may stub `ReplayParser.Parse` locally under `#if` only in tests — never commit a stub into `Core`.

## 7. Frozen contracts (both builders copy these verbatim)

Namespace `BossModForge.Core.Replay` unless stated. All records are immutable; collections exposed as `IReadOnlyList<T>`/`ImmutableArray<T>`.

```csharp
public readonly record struct Tag(uint Value)
{
    public Tag(ReadOnlySpan<byte> fourAscii) : this(BitConverter.ToUInt32(fourAscii)) { }
    public static Tag Parse(string fourChars);          // throws if length != 4
    public override string ToString();                  // 4 chars, trailing spaces kept
    public static readonly Tag Ver, Fram, Zone, ActPlus, ActMinus, Move, CstPlus, CstMinus, CstBang, StaPlus, StaMinus, StaBang,
                               Teth, Icon, Envc, Diru, Par, DiePlus, DieMinus, ComPlus, ComMinus, Targ, Name, Hp; // etc., one per tag in UPSTREAM §5
}

public enum ReplayEncoding { Text, Binary, BinaryBrotli }
public enum IssueSeverity { Info, Warning, Degraded }
public sealed record ParseIssue(IssueSeverity Severity, string Code, string Message, int? Line = null, long? ByteOffset = null);

public sealed record RecordingMeta(string Path, ReplayEncoding Encoding, int Version, ulong Qpf, string GameVersion, DateTime StartTime);

public abstract record Op(DateTime Timestamp)
{
    public abstract Tag Tag { get; }
}
public sealed record RawOp(DateTime Timestamp, Tag RawTag, ImmutableArray<string> Fields) : Op(Timestamp) { public override Tag Tag => RawTag; }
public sealed record OpaqueOp(DateTime Timestamp, Tag OpaqueTag) : Op(Timestamp) { public override Tag Tag => OpaqueTag; }

// Primitives
public readonly record struct Angle(float Rad)
{
    public float Deg => Rad * (180f / MathF.PI);
    public static Angle FromDeg(float deg) => new(deg * (MathF.PI / 180f));
}
public enum ActionType : byte { /* UPSTREAM §3 list */ }
public readonly record struct ActionId(uint Raw)
{
    public ActionId(ActionType type, uint id) : this(((uint)type << 24) | id) { }
    public ActionType Type => (ActionType)(Raw >> 24);
    public uint Id => Raw & 0x00FFFFFFu;
    public static ActionId Spell(uint id) => new(ActionType.Spell, id);
}
public enum ActorType : ushort { /* UPSTREAM §3 list */ }
public enum Class : byte { /* copy from BossMod/Data/ClassRole.cs@1391bc42 */ }
public enum ActionEffectType : byte { /* UPSTREAM §3 list */ }
public readonly record struct ActionEffect(ulong Raw)
{
    public ActionEffectType Type => (ActionEffectType)(byte)Raw;
    public byte Param0 => (byte)(Raw >> 8); public byte Param1 => (byte)(Raw >> 16); public byte Param2 => (byte)(Raw >> 24);
    public byte Param3 => (byte)(Raw >> 32); public byte Param4 => (byte)(Raw >> 40);
    public ushort Value => (ushort)(Raw >> 48);
    public bool FromTarget => (Param4 & 0x20) != 0;
    public bool AtSource => (Param4 & 0x80) != 0;
    public int DamageHealValue => Value + (((Param4 & 0x40) != 0) ? Param3 * 0x10000 : 0);
    public static ulong Pack(ActionEffectType type, byte p0 = 0, byte p1 = 0, byte p2 = 0, byte p3 = 0, byte p4 = 0, ushort value = 0);
}
public readonly record struct ActionEffects(ImmutableArray<ulong> Raw)   // Raw.Length == 8 always; zero = empty slot
{
    public const int MaxCount = 8;
    public static readonly ActionEffects Empty;
    public static ActionEffects From(ReadOnlySpan<ulong> upToEight);    // pads with zeros
    public IEnumerable<ActionEffect> NonEmpty();                          // in slot order, skips zeros
    public bool Equals(ActionEffects other);                              // element-wise (override; ImmutableArray is reference-equal by default)
}
public readonly record struct CastTarget(ulong InstanceId, ActionEffects Effects);
public readonly record struct ActorStatus(uint Id, ushort Extra, DateTime ExpireAt, ulong SourceId);
public readonly record struct HpMp(uint CurHp, uint MaxHp, uint Shield, uint CurMp, uint MaxMp);
public readonly record struct PosRot(Vector3 Pos, Angle Rot);
public readonly record struct FrameInfo(ulong Qpc, uint Index, float DurationRaw, float Duration, float TickSpeedMultiplier);

// World ops
public sealed record FrameStartOp(DateTime Timestamp, FrameInfo Frame, TimeSpan PrevUpdateTime, ulong GaugeLow, ulong GaugeHigh, Angle CameraAzimuth) : Op(Timestamp);
public sealed record ZoneChangeOp(DateTime Timestamp, ushort Zone, ushort CfcId) : Op(Timestamp);
public sealed record DirectorUpdateOp(DateTime Timestamp, uint DirectorId, uint UpdateId, uint Param1, uint Param2, uint Param3, uint Param4) : Op(Timestamp);
public sealed record EnvControlOp(DateTime Timestamp, byte Index, uint State) : Op(Timestamp);
public sealed record RsvDataOp(DateTime Timestamp, string Key, string Value) : Op(Timestamp);
public sealed record UserMarkerOp(DateTime Timestamp, string Text) : Op(Timestamp);
public sealed record SystemLogOp(DateTime Timestamp, uint MessageId, ImmutableArray<int> Args) : Op(Timestamp);
public sealed record WaymarkOp(DateTime Timestamp, byte Waymark, Vector3? Position) : Op(Timestamp);   // Position null == WAY-

// Actor ops (InstanceId first on every one)
public sealed record ActorCreateOp(DateTime Timestamp, ulong InstanceId, uint Oid, int SpawnIndex, string Name, uint NameId, ActorType Type, Class Class, int Level,
                                   PosRot PosRot, float HitboxRadius, HpMp HpMp, bool IsTargetable, bool IsAlly, ulong OwnerId, uint FateId) : Op(Timestamp);
public sealed record ActorDestroyOp(DateTime Timestamp, ulong InstanceId) : Op(Timestamp);
public sealed record ActorRenameOp(DateTime Timestamp, ulong InstanceId, string Name, uint NameId) : Op(Timestamp);
public sealed record ActorClassChangeOp(DateTime Timestamp, ulong InstanceId, Class Class, int Level) : Op(Timestamp);
public sealed record ActorMoveOp(DateTime Timestamp, ulong InstanceId, PosRot PosRot) : Op(Timestamp);
public sealed record ActorSizeChangeOp(DateTime Timestamp, ulong InstanceId, float HitboxRadius) : Op(Timestamp);
public sealed record ActorHpMpOp(DateTime Timestamp, ulong InstanceId, HpMp HpMp) : Op(Timestamp);
public sealed record ActorTargetableOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp);
public sealed record ActorAllyOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp);
public sealed record ActorDeadOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp);
public sealed record ActorCombatOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp);
public sealed record ActorAggroPlayerOp(DateTime Timestamp, ulong InstanceId, bool Value) : Op(Timestamp);
public sealed record ActorModelStateOp(DateTime Timestamp, ulong InstanceId, byte ModelState, byte AnimState1, byte AnimState2) : Op(Timestamp);
public sealed record ActorEventStateOp(DateTime Timestamp, ulong InstanceId, byte State) : Op(Timestamp);
public sealed record ActorTargetOp(DateTime Timestamp, ulong InstanceId, ulong TargetId) : Op(Timestamp);
public sealed record ActorMountOp(DateTime Timestamp, ulong InstanceId, uint MountId) : Op(Timestamp);
public sealed record ActorForayOp(DateTime Timestamp, ulong InstanceId, byte Level, byte Element) : Op(Timestamp);
public sealed record ActorTetherOp(DateTime Timestamp, ulong InstanceId, uint TetherId, ulong TargetId) : Op(Timestamp);   // TetherId == 0 && TargetId == 0 == untether (TET-)
public sealed record ActorCastStartOp(DateTime Timestamp, ulong InstanceId, ActionId Action, ulong TargetId, Vector3 Location, float ElapsedTime, float TotalTime, bool Interruptible, Angle Rotation) : Op(Timestamp);
public sealed record ActorCastStopOp(DateTime Timestamp, ulong InstanceId) : Op(Timestamp);
public sealed record ActorCastEventOp(DateTime Timestamp, ulong InstanceId, ActionId Action, ulong MainTargetId, float AnimationLock, uint MaxTargets, Vector3 TargetPos,
                                      uint GlobalSequence, uint SourceSequence, Angle Rotation, ImmutableArray<CastTarget> Targets) : Op(Timestamp);   // tag CST!
public sealed record ActorEffectResultOp(DateTime Timestamp, ulong InstanceId, uint GlobalSequence, int TargetIndex) : Op(Timestamp);
public sealed record ActorStatusOp(DateTime Timestamp, ulong InstanceId, int Index, ActorStatus? Status, bool IsUpdate) : Op(Timestamp);   // Status null == STA-; IsUpdate == STA!
public sealed record ActorIncomingEffectOp(DateTime Timestamp, ulong InstanceId, int Index, uint GlobalSequence, int TargetIndex, ulong SourceId, ActionId Action, ActionEffects Effects, bool Added) : Op(Timestamp);
public sealed record ActorIconOp(DateTime Timestamp, ulong InstanceId, uint IconId, ulong TargetId) : Op(Timestamp);
public sealed record EventObjectStateOp(DateTime Timestamp, ulong InstanceId, ushort State) : Op(Timestamp);
public sealed record EventObjectAnimationOp(DateTime Timestamp, ulong InstanceId, ushort Param1, ushort Param2) : Op(Timestamp);
public sealed record PlayActionTimelineOp(DateTime Timestamp, ulong InstanceId, ushort ActionTimelineId) : Op(Timestamp);
public sealed record NpcYellOp(DateTime Timestamp, ulong InstanceId, ushort YellId) : Op(Timestamp);
public sealed record OpenTreasureOp(DateTime Timestamp, ulong InstanceId) : Op(Timestamp);

// Party ops
public sealed record PartyModifyOp(DateTime Timestamp, int Slot, ulong ContentId, ulong InstanceId, bool InCutscene, string Name) : Op(Timestamp);   // PAR-/leave == all zero/empty
public sealed record PartyLimitBreakOp(DateTime Timestamp, int Current, int Max) : Op(Timestamp);
```

`Tag` for each op record is a static, e.g. `ActorCastEventOp.Tag => Tag.CstBang`. Ops that fold two tags (`ActorTetherOp`, `ActorStatusOp`, `ActorDeadOp`, …) report the tag they were **read from** so `TagCounts` matches the file; store it in a private field set by the reader (`Op` gets a `protected init` hook — Builder B decides, but `TagCounts` must equal the on-disk tag histogram).

```csharp
public sealed class Recording
{
    public RecordingMeta Meta { get; }
    public IReadOnlyList<Op> Ops { get; }
    public IReadOnlyList<ParseIssue> Issues { get; }
    public bool Degraded { get; }                                    // any Issue with Severity == Degraded
    public IReadOnlyDictionary<Tag, int> TagCounts { get; }          // on-disk tag histogram incl. RawOp/OpaqueOp
    public IReadOnlyList<ActorTimeline> Actors { get; }              // creation order; one entry per ACT+ (instance ids may repeat after ACT-)
    public ActorTimeline? ActorAt(ulong instanceId, DateTime t);      // the incarnation alive at t (Created <= t && (Destroyed == null || t < Destroyed))
    public IReadOnlyList<ZoneChangeOp> Zones { get; }
    public DateTime Start => Meta.StartTime;
    public DateTime End { get; }                                      // last op timestamp
}

public sealed class ActorTimeline
{
    public ulong InstanceId { get; }
    public int Incarnation { get; }                                   // 0-based per instance id
    public uint Oid { get; }
    public string Name { get; }                                       // latest NAME, else ACT+
    public uint NameId { get; }
    public ActorType Type { get; }
    public Class Class { get; }
    public int Level { get; }
    public float HitboxRadius { get; }                                // latest ACSZ, else ACT+
    public int SpawnIndex { get; }
    public ulong OwnerId { get; }
    public uint FateId { get; }
    public DateTime Created { get; }
    public DateTime? Destroyed { get; }
    public IReadOnlyList<(DateTime T, PosRot Value)> PosRotSamples { get; }   // ACT+ then every MOVE, ascending T
    public IReadOnlyList<(DateTime T, bool Value)> Targetable { get; }        // ACT+ then ATG±
    public IReadOnlyList<(DateTime T, bool Value)> Dead { get; }              // DIE±
    public IReadOnlyList<(DateTime T, bool Value)> InCombat { get; }          // COM±
    public IReadOnlyList<(DateTime T, HpMp Value)> HpMp { get; }              // ACT+ then HP
    public PosRot PosRotAt(DateTime t);          // nearest sample with T <= t; before first sample returns first sample; no interpolation
    public bool IsTargetableAt(DateTime t);      // same rule, default from ACT+
    public bool IsDeadAt(DateTime t);            // default false
    public bool IsAliveAt(DateTime t) => Created <= t && (Destroyed is null || t < Destroyed);
}

public sealed record ParseOptions(IProgress<float>? Progress = null, bool KeepRawOps = true);

public static class ReplayParser
{
    public static Recording Parse(string path, ParseOptions? options = null, CancellationToken ct = default);
    public static Recording Parse(Stream stream, string displayPath, ParseOptions? options = null, CancellationToken ct = default);   // stream must be seekable for text fallback (rewind after magic)
    public static ReplayEncoding DetectEncoding(ReadOnlySpan<byte> firstFourBytes);
}
```

`BossModForge.Core.GameData` (stub, Builder A):

```csharp
public sealed record ActionRow(uint RowId, string Name, byte CastType, byte EffectRange, byte XAxisModifier, ushort Cast100ms, uint OmenId);
public interface IGameData
{
    ActionRow? Action(uint id);
    string? BNpcName(uint nameId);
    string? StatusName(uint id);
    (uint CfcId, string Name)? ContentForTerritory(uint territoryType);
    string? PlaceName(uint territoryType);
}
public sealed class NullGameData : IGameData { /* every member returns null */ public static readonly NullGameData Instance = new(); }
```

`BossModForge.Tests.Fixtures` (Builder B; `Cli` tests reuse it):

```csharp
public sealed class RecordingBuilder
{
    public RecordingBuilder(DateTime start, ulong qpf = 10_000_000, string gameVersion = "2026.08.20.0000.0000", int version = 24);
    public DateTime Now { get; }                       // advances only via Frame/Advance; whole milliseconds
    public RecordingBuilder Frame(double dtSeconds = 1.0 / 60);          // emits FRAM, advances Now, QPC = (Now-Start).Ticks * qpf / TicksPerSecond
    public RecordingBuilder Advance(TimeSpan by, double dtSeconds = 1.0 / 60);   // repeated Frame until Now >= start+by
    public RecordingBuilder Zone(ushort zone, ushort cfcId);
    public RecordingBuilder DirectorUpdate(uint directorId, uint updateId, uint p1 = 0, uint p2 = 0, uint p3 = 0, uint p4 = 0);
    public RecordingBuilder EnvControl(byte index, uint state);
    public RecordingBuilder Actor(ulong id, uint oid, string name, ActorType type, Vector3 pos, Angle rot, float radius = 0.5f, Class cls = Class.None, int level = 0,
                                  uint nameId = 0, bool targetable = true, bool ally = false, ulong owner = 0, int spawnIndex = -1, HpMp? hpmp = null, uint fateId = 0);
    public RecordingBuilder Player(ulong id, string name, Class cls, Vector3 pos, Angle rot = default, int level = 100);   // Type=Player, ally=true, targetable=true, hp 100000
    public RecordingBuilder Helper(ulong id, Vector3 pos, Angle rot = default);                                            // oid 0x233C, Type=Helper, untargetable
    public RecordingBuilder Destroy(ulong id);
    public RecordingBuilder Rename(ulong id, string name, uint nameId);
    public RecordingBuilder Move(ulong id, Vector3 pos, Angle rot);
    public RecordingBuilder Targetable(ulong id, bool value);
    public RecordingBuilder Dead(ulong id, bool value);
    public RecordingBuilder Combat(ulong id, bool value);
    public RecordingBuilder HpMp(ulong id, HpMp value);
    public RecordingBuilder Target(ulong id, ulong target);
    public RecordingBuilder CastStart(ulong id, ActionId action, ulong target, Vector3 location, float totalTime, Angle rot, float elapsed = 0, bool interruptible = false);
    public RecordingBuilder CastStop(ulong id);
    public RecordingBuilder CastEvent(ulong id, ActionId action, ulong mainTarget, Vector3 targetPos, Angle rot, params CastTarget[] targets);   // animLock 0.1, maxTargets = targets.Length, sequences auto-increment
    public RecordingBuilder StatusGain(ulong id, int index, uint statusId, ushort extra, float durationSeconds, ulong source);
    public RecordingBuilder StatusLose(ulong id, int index);
    public RecordingBuilder Tether(ulong id, uint tetherId, ulong target);
    public RecordingBuilder Untether(ulong id);
    public RecordingBuilder Icon(ulong id, uint iconId, ulong target = 0);
    public RecordingBuilder Party(int slot, ulong contentId, ulong instanceId, string name, bool inCutscene = false);
    public RecordingBuilder Raw(string tag, params string[] fields);   // text only; ignored by binary writers

    public string ToText();
    public byte[] ToBinary(bool brotli);
    public Stream Open(ReplayEncoding encoding);
    public string WriteTemp(ReplayEncoding encoding);   // file under Path.GetTempPath(), caller deletes
}

public static class Effects   // ulong helpers for CastTarget
{
    public static ulong Damage(uint amount, byte damageType = 0, byte element = 0);
    public static ulong Heal(uint amount);
    public static ulong Knockback(byte distance, byte direction);
    public static ulong ApplyStatusTarget(ushort statusId, byte param0 = 0);
    public static ulong Miss(); public static ulong Nothing();
}

public static class SyntheticDungeon
{
    public static RecordingBuilder Build(DateTime? start = null);   // 3 pulls, boss OID 0x4321 "Test Boss", helpers 0x233C, 4 players, AID 40001 cone cast+event with 2 hits/2 misses, statuses, icons, tether, envc, zone 1234/cfc 826
}
```

Writer rules for the fixture (must match `UPSTREAM.md §3/§6` byte-for-byte for text): timestamps `ToString("O")`; actor ids `X8`; OID `X`; type `X4`; positions `f3`; angles degrees `f3`; floats `g9`; effect ulongs `X16` and only non-zero effects in `CST!` targets; empty void field in `FRAM`; `ActionID` as `"Spell 40001"` (name suffix optional); status as `"{id}"` (name suffix optional). Binary writer mirrors `ReplayRecorder.BinaryOutput` exactly (angle radians, class byte, status ticks, always 8 effects, `count` prefix for targets, `VER ` gets trailing `long` start ticks).

Contract note on `CastTarget` parity: text drops zero effect slots, so after a text round trip the effects are **compacted** to the front. The binary writer in the fixture must therefore also write compacted effects (non-zero first, zero-padded) so both encodings yield identical `ActionEffects.Raw`. Document this in `BinaryRecordingWriter`.

## 8. Definition of done (M0)

- `scripts/build.sh` green on Linux with .NET 10 SDK.
- `forge ingest` on the three encodings of `SyntheticDungeon` prints identical `ops:` and tag tables.
- `EncodingParityTests` and all §5 tests pass; real-log smoke passes or is skipped.
- `README.md` documents build, `forge ingest`, `FORGE_REAL_LOGS`, and that `conform` and the plugin are deferred.
- `AGENTS.md` "Currently design-only" line updated; add `src/`, `tests/` lines to the Child DOX Index only if those folders get their own `AGENTS.md` (not required in M0).
- No file under `src/BossModForge.Core` references Dalamud, ImGui, BossMod.dll or Lumina.
