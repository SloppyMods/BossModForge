---
tags:
  - type/reference
  - project/bossmodforge
  - status/active
type: reference
project: bossmodforge
status: active
---

# BossModForge — Upstream pins and verified facts

Every fact below was read from the pinned commit — vbm (`awgil/ffxiv_bossmod@1391bc42`) on 2026-09-19, bmr (`FFXIV-CombatReborn/BossmodReborn@b919e0970d6559de280c40defb4117dd7275c961`) on 2026-09-20. Everything that was once unconfirmed is listed under **Closed VERIFY** at the end, each with its resolution; nothing outside a resolution's explicit statement may be assumed by builders.

## 1. Pins

| Upstream | Commit | Status |
|---|---|---|
| `awgil/ffxiv_bossmod` (flavor `vbm`) | `1391bc42` | **verified** for everything in this document |
| `FFXIV-CombatReborn/BossmodReborn` (flavor `bmr`) | `b919e0970d6559de280c40defb4117dd7275c961` | **verified** 2026-09-20 for everything in this document |
| `awgil/ffxiv_bossmod` (replay format v25–v31 only) | `6e410ef8bb912fc70e3c1f97eedfb77a7dc4dfc0` | **verified** 2026-11-04 for §12 only (replay reader/writer); module/component API facts above stay at `1391bc42` |
| `FFXIV-CombatReborn/BossmodReborn` (replay format v25–v31 only) | `577675d83e66c8be7d41e7cd1ef004cffe0abd8d` | **verified** 2026-11-04 for §12 only |

Files read at `b919e0970d6559de280c40defb4117dd7275c961` (paths relative to repo root):

| File | Used for |
|---|---|
| `BossMod/BossModule/StateMachineBuilder.cs` | `Cast<TAID>` enum-typed generics, `SimpleState` duplicate-id behaviour |
| `BossMod/BossModule/BossModule.cs`, `BossModuleInfo.cs`, `BossModuleRegistry.cs` | module ctor, `[ModuleInfo]` props, module discovery (no module-pack facility) |
| `BossMod/Components/*.cs` (33 files, incl. `GenericAOEs.cs`, `UnavoidableDamage.cs`, `Knockback.cs`, `Gaze.cs`, `StackSpread.cs`, `SharedTankbuster.cs`) | bmr component ctor signatures (§10) |
| `BossMod/Modules/Global/CrucibleOfTheUnbroken/SecondMasterBoard/{ChimeraPiece,MedusaPiece,DrakePiece}.cs` | `Helper = 0x233C` convention, real `(uint)AID.X` component-call idiom |

Files read at `1391bc42` (paths relative to repo root):

| File | Used for |
|---|---|
| `BossMod/Replay/ReplayRecorder.cs` | `Version`, text/binary writer encodings |
| `BossMod/Replay/ReplayParserLog.cs` | tag dispatch, field layouts, version gates |
| `BossMod/Replay/ReplayLogFormat.cs` | magic values |
| `BossMod/Replay/ReplayBuilder.cs` | op timestamp semantics |
| `BossMod/Util/FourCC.cs` | tag encoding |
| `BossMod/Data/WorldState.cs`, `ActorState.cs`, `Actor.cs`, `ActionID.cs`, `ActionEffect.cs`, `ClassRole.cs`, `ClientState.cs`, `DeepDungeonState.cs` | writer field order, enums, fixed array sizes |
| `BossMod/Framework/Utils.cs` | status text formatting |
| `BossMod/BossModule/BossModuleInfo.cs`, `BossModule.cs`, `StateMachineBuilder.cs`, `ArenaBounds.cs` | M1 emitter targets |
| `BossMod/Modules/Dawntrail/Dungeon/D01Ihuykatumu/D012Drowsie.cs` (+ 10 sibling modules) | `Helper = 0x233C` convention, trivial module shape |

Constants:

| Constant | Value | Source |
|---|---|---|
| `ReplayRecorder.Version` | **24** | `ReplayRecorder.cs:270` (`public const int Version = 24;`) |
| `ReplayRecorder.Version` at the §12 pins | **31** (both vbm and bmr) | `ReplayRecorder.cs:9` at `6e410ef8` / `577675d8`; Forge `OpReader.MaxSupportedVersion = 31` |
| Minimum readable version | **2** (`< 2` throws); no upper bound check | `ReplayParserLog.cs:397` |
| `ReplayLogFormatMagic.CompressedBinary` | `"BLCB"` (bytes `42 4C 43 42`) | `ReplayLogFormat.cs:20` |
| `ReplayLogFormatMagic.RawBinary` | `"BLOG"` (bytes `42 4C 4F 47`) | `ReplayLogFormat.cs:21` |
| `ActionEffects.MaxCount` | **8** | `ActionEffect.cs:114` |
| `DeepDungeonState.NumRooms / NumPartyMembers / NumPomanderSlots / NumChests` | 25 / 4 / 16 / 16 | `DeepDungeonState.cs:24-27` |
| `ClientState.NumBlueMageSpells` | 24 | `ClientState.cs:44` |
| Default QPF for version < 10 | `TimeSpan.TicksPerSecond` = 10 000 000 | `ReplayParserLog.cs:399` |

## 2. Container detection

Read the first 4 bytes of the file:

1. `BLCB` → wrap in `BrotliStream(Decompress)` → binary reader.
2. `BLOG` → binary reader on the raw stream (magic already consumed).
3. anything else → rewind to 0, text reader. `TextCondensed` and `TextVerbose` are the *same* on-disk grammar; verbose only adds `/oid/name/...` suffixes after actor ids, which the parser strips.

`FourCC(uint)` is the 4 bytes reinterpreted as a little-endian `uint` (`FourCC.cs:20-26`). In binary the tag is written as that `uint`; the bytes on disk are therefore the ASCII characters in order.

## 3. Encoding primitives

Text writer = `ReplayRecorder.TextOutput`; text reader = `ReplayParserLog.TextInput`. Binary = `BinaryOutput` / `BinaryInput` (`System.IO.BinaryWriter/Reader`, little-endian).

### Text line grammar

```
<timestamp>|<TAG>|<field>|<field>|...
```

- `timestamp` = `DateTime.ToString("O")`; parsed with `DateTime.Parse(_line[0])` (`ReplayParserLog.cs:75`).
- Lines that are empty or start with `#` are skipped; lines with fewer than 2 `|`-fields or a tag whose length ≠ 4 are skipped (`ReplayParserLog.cs:64-73`).
- The tag is exactly 4 characters incl. trailing spaces (`"VER "`, `"HP  "`, `"LB  "`, `"ER  "`, `"PAR "`, `"RSV "`).
- Each `Emit` writes `|` + value (`ReplayRecorder.cs:118-123`). `Emit()` with no arg writes an **empty field** (`|`), which the reader consumes with `ReadVoid()`.
- `CanRead()` in text = "there are more fields on this line"; in binary it is **always true**. Consequence: every field the writer emits *optionally* in text is *mandatory* in binary.

### Primitive table

| Type | Text | Binary |
|---|---|---|
| `string` | field verbatim | `BinaryWriter.Write(string)` (7-bit length prefix, UTF-8) |
| `bool` | `True`/`False` (`bool.Parse`) | 1 byte |
| `sbyte/short/int/long` | decimal | native width |
| `byte/ushort/uint/ulong` | decimal **or hex** depending on writer format (`"d"`, `"X2"`, `"X4"`, `"X8"`, `"X16"`) — reader's `Read*(hex: bool)` picks | native width, hex flag ignored |
| `float` | `ToString("g9")` (round-trips) unless writer passes `f2`/`f3` | 4 bytes |
| `double` | `ToString("g17")` unless `f3` | 8 bytes |
| `Vector3` | `x/y/z` each `f3` | 3 × float |
| `Angle` | **degrees**, `f3` | **radians**, float |
| `byte[]` | hex pairs, no separator | `int32 length` + bytes |
| `ActionID` | `"{Type} {ID} '{Name}'"`; reader splits on space and uses `[0]`=`ActionType` name, `[1]`=id (`ReplayParserLog.cs:111-117`) | `uint32 Raw` |
| `Class` | enum **name** (`Enum.Parse<Class>`) | 1 byte |
| actor id (`EmitActor`) | `{InstanceID:X8}` optionally followed by `/{OID:X}/{Name}/{Type}/{X}/{Y}/{Z}/{Rot}`; reader takes hex prefix before first `/` (`ReplayParserLog.cs:155-159`) | `uint64` |
| `ActorStatus` | `"{ID} '{Name}'"` (reader takes prefix before first space), `Extra` as `X4`, remaining seconds `f3` relative to the **line timestamp**, then source actor id | `uint32 ID`, `uint16 Extra`, `int64 ExpireAt.Ticks`, `uint64 SourceID` |
| float pair | `"{t1:f3}/{t2:f3}"` | 2 × float |
| time pair (legacy, v<17) | `"{seconds:f3}/{total:f3}"`, seconds relative to line timestamp | `int64 ticks` + float |
| `ActionEffects` (8 × ulong) | 8 fields `X16` | 8 × uint64 |
| cast-event target list | remaining fields, each `"{targetId:X8}!{eff:X16}!{eff:X16}..."` — **only non-zero effects are written** (`ReplayRecorder.cs:92-102`) | `int32 count`, then per target `uint64 id` + **always 8 × uint64** |

`ActionID.Raw = (Type << 24) | ID` (`ActionID.cs:35-40`). `ActionType`: `None=0, Spell=1, Item=2, KeyItem=3, Ability=4, General=5, Buddy=6, MainCommand=7, Companion=8, CraftAction=9, PetAction=11, Mount=13, PvPAction=14, Waymark=15, ChocoboRaceAbility=16, ChocoboRaceItem=17, SquadronAction=19, Ornament=20, BozjaHolsterSlot0=0xE0, BozjaHolsterSlot1=0xE1, Pomander=0xE2, Magicite=0xE3`.

`ActionEffect` (8 bytes, `ActionEffect.cs:93-109`, sequential pack 1): `byte Type (ActionEffectType)`, `byte Param0..Param4`, `ushort Value`. Derived: `FromTarget = Param4 & 0x20`, `AtSource = Param4 & 0x80`, `DamageType = Param1 & 0x0F`, `DamageElement = Param1 >> 4`, `DamageHealValue = Value + ((Param4 & 0x40) != 0 ? Param3 * 0x10000 : 0)`.

`ActionEffectType` (byte): `Nothing=0, Miss=1, FullResist=2, Damage=3, Heal=4, BlockedDamage=5, ParriedDamage=6, Invulnerable=7, NoEffectText=8, FailMissingStatus=9, MpLoss=10, MpGain=11, TpLoss=12, TpGain=13, ApplyStatusEffectTarget=14, ApplyStatusEffectSource=15, RecoveredFromStatusEffect=16, LoseStatusEffectTarget=17, LoseStatusEffectSource=18, StatusNoEffect=20, ThreatPosition=24, EnmityAmountUp=25, EnmityAmountDown=26, StartActionCombo=27, Retaliation=28, Knockback=31, Mount=39, ReviveLB=50, FullResistStatus=55, VFX=59, Resource=62, SetModelState=73, SetHP=74, PartialInvulnerable=75, Interrupt=76`. Knockback direction sub-enum: `AwayFromSource=0, Arg=1, Random=2, SourceForward=3, SourceRight=4, SourceLeft=5`. (Values 19, 21-23, 29-30, 32-38, 40-49, ... are absent from the enum at this commit.)

`ActorType` (ushort, `Actor.cs:4-26`): `None=0, Player=0x104, Part=0x201, Pet=0x202, Chocobo=0x203, Enemy=0x205, DutySupport=0x209, Helper=0x20B, EventNpc=0x300, Treasure=0x400, Aetheryte=0x500, GatheringPoint=0x600, EventObj=0x700, MountType=0x800, Companion=0x900, Retainer=0xA00, Area=0xB00, Housing=0xC00, Cutscene=0xD00, CardStand=0xE00`.

`Class` (byte, `BossMod/Data/ClassRole.cs:3+`): `None=0, GLA=1, PGL=2, MRD=3, LNC=4, ARC=5, CNJ=6, ...` — builders copy the full enum from that file verbatim (text format serialises the *names*).

## 4. Timestamp semantics

- Header `VER ` is **not** emitted as an op by BossMod (`ParseVersion` returns null). It sets `_tsStart`: text → the `VER ` line's timestamp; binary → an extra trailing `int64 ticks` field written only by `BinaryOutput` (`ReplayRecorder.cs:300-302`, `ReplayParserLog.cs:401`).
- Every op's `Timestamp` in BossMod = the **current frame timestamp** at the time the op is executed (`WorldState.cs:41`, `ReplayBuilder.cs:78-84`). Ops before the first `FRAM` get `default(DateTime)` in BossMod.
- `FRAM` timestamp (v ≥ 10, both text and binary): first frame → `_tsStart` and `_qpcStart = frame.QPC`; later frames → `_tsStart + (QPC − _qpcStart) / QPF` seconds (`ReplayParserLog.cs:413-428`).
- The text line timestamp is only used by BossMod for `VER `, legacy (< v5) frame synthesis, and status/time-pair relative seconds. The recorder writes each line's timestamp as `op.Timestamp`, so for a well-formed v24 log the line timestamp equals the frame timestamp.

**Forge rule (frozen in M0 brief):** text → `Op.Timestamp` = line timestamp; binary → ops before first `FRAM` get `Meta.StartTime`, `FRAM` and everything after get the frame timestamp computed with integer tick math. Same recorder input therefore yields identical timestamps in both encodings.

## 5. Complete tag list (80 tags, `ReplayParserLog.cs:284-372`)

Legacy = only ever written by versions ≤ 4 (tethers) or ≤ 3 (party); still dispatched.

| Tag | Upstream parse method | Group |
|---|---|---|
| `VER ` | ParseVersion (header, no op) | world |
| `FRAM` | ParseFrameStart | world |
| `UMRK` | ParseUserMarker | world |
| `RSV ` | ParseRSVData | world |
| `ZONE` | ParseZoneChange | world |
| `DIRU` | ParseDirectorUpdate | world |
| `ENVC` | ParseEnvControl | world |
| `SLOG` | ParseSystemLog | world |
| `WAY+` / `WAY-` | ParseWaymarkChange(true/false) | waymark |
| `ACT+` / `ACT-` | ParseActorCreate / ParseActorDestroy | actor |
| `NAME` | ParseActorRename | actor |
| `CLSR` | ParseActorClassChange | actor |
| `MOVE` | ParseActorMove | actor |
| `ACSZ` | ParseActorSizeChange | actor |
| `HP  ` | ParseActorHPMP | actor |
| `ATG+` / `ATG-` | ParseActorTargetable(true/false) | actor |
| `ALLY` | ParseActorAlly | actor |
| `DIE+` / `DIE-` | ParseActorDead(true/false) | actor |
| `COM+` / `COM-` | ParseActorCombat(true/false) | actor |
| `NENP` | ParseActorAggroPlayer | actor |
| `MDLS` | ParseActorModelState | actor |
| `EVTS` | ParseActorEventState | actor |
| `TARG` | ParseActorTarget | actor |
| `MNTD` | ParseActorMount | actor |
| `FORA` | ParseActorForay | actor |
| `TETH` | ParseActorTether(true) | actor |
| `TET+` / `TET-` | ParseActorTether(true/false) — legacy | actor |
| `CST+` / `CST-` | ParseActorCastInfo(true/false) | actor |
| **`CST!`** | **ParseActorCastEvent — the action-effect op** | actor |
| `ER  ` | ParseActorEffectResult | actor |
| `STA+` / `STA-` / `STA!` | ParseActorStatus(true/false/true) | actor |
| `AIE+` / `AIE-` | ParseActorIncomingEffect(true/false) | actor |
| `ICON` | ParseActorIcon | actor |
| `ESTA` | ParseActorEventObjectStateChange | actor |
| `EANM` | ParseActorEventObjectAnimation | actor |
| `PATE` | ParseActorPlayActionTimelineEvent | actor |
| `NYEL` | ParseActorEventNpcYell | actor |
| `OPNT` | ParseActorEventOpenTreasure | actor |
| `PAR ` | ParsePartyModify | party |
| `PAR+` / `PAR!` | ParsePartyModify — legacy | party |
| `PAR-` | ParsePartyLeave — legacy | party |
| `LB  ` | ParsePartyLimitBreak | party |
| `CLAR` | ParseClientActionRequest | client |
| `CLRJ` | ParseClientActionReject | client |
| `CDN+` / `CDN-` | ParseClientCountdown(true/false) | client |
| `CLAL` | ParseClientAnimationLock | client |
| `CLCB` | ParseClientCombo | client |
| `CLST` | ParseClientPlayerStats | client |
| `CLMV` | ParseClientMovespeed | client |
| `CLCD` | ParseClientCooldown | client |
| `CLDA` | ParseClientDutyActions | client |
| `CLBH` | ParseClientBozjaHolster | client |
| `CBLU` | ParseClientBlueMageSpells | client |
| `CLVL` | ParseClientClassJobLevels | client |
| `CLAF` | ParseClientActiveFate | client |
| `CPET` | ParseClientActivePet | client |
| `CLFT` | ParseClientFocusTarget | client |
| `CLFD` | ParseClientForcedMovementDirection | client |
| `CLKV` | ParseClientContentKVData | client |
| `FATE` | ParseClientFateInfo | client |
| `DDPG` / `DDMP` / `DDPT` / `DDIT` / `DDCT` / `DDMG` | ParseDeepDungeon{Progress,Map,Party,Pomanders,Chests,Magicite} | deep dungeon |
| `IPCI` / `IPCS` | ParseNetworkIDScramble / ParseNetworkServerIPC | network |

Unknown tag → BossMod throws `InvalidOperationException("Replay contains unsupported tag …")` (`ReplayParserLog.cs:385`). **Binary payloads carry no length prefix**, so an unknown binary tag cannot be skipped — parsing must stop there. Text unknown tags can be preserved as `RawOp` (fields are `|`-delimited).

## 6. Field layouts

Notation: `T name` in reader order; `[v≥N]` = only present when version ≥ N; `(hex)` = text is hex; `(opt)` = writer always emits at v24, reader guards with `CanRead()` — **mandatory in binary**. Text field order equals reader order (writer and reader were cross-checked). Version gates are listed so that v ≥ 10 logs can be read; v < 10 uses compound `a/b/c` strings for `ACT+`, `NAME`, `MOVE`, HPMP and is **out of scope for Forge**.

### Header

**`VER `**: `int version` · `ulong qpf [v≥10]` · `string gameVersion [v≥11]` · `long startTicks` **binary only**. Text: start = line timestamp. Text v24 example: `2026-09-19T20:00:00.0000000|VER |24|10000000|2026.08.20.0000.0000`.

### World

**`FRAM`**: `double prevUpdateMs` (text `f3`) · **void** (empty field; `ReadVoid`) · `ulong gaugeLow (hex X16, opt)` · `ulong gaugeHigh (hex X16) [v≥18]` · `[v≥10]:` `ulong qpc` · `uint index` · `float durationRaw` · `float duration` · `float tickSpeedMultiplier` · `Angle cameraAzimuth [v≥16]`. Writer: `WorldState.cs:90-100`.

**`ZONE`**: `ushort zone` · `ushort cfcId [v≥13]`. Decimal in text.

**`DIRU`**: 6 × `uint (hex X8)`: `directorId, updateId, param1, param2, param3, param4`.

**`ENVC`**: `[v<11: uint directorId (hex), discarded]` · `byte index (hex X2)` · `uint state (hex X8)`.

**`RSV `**: `string key` · `string value`. **`UMRK`**: `string text`. **`SLOG`**: `uint messageId` · `int argCount` · `argCount × int`.

**`WAY+`**: `byte waymark [v≥10]` (v<10: enum name string) · `Vector3 pos`. **`WAY-`**: `byte waymark` only.

### Actors

**`ACT+`** (v≥10; writer `ActorState.cs:137-157`): `ulong instanceId (hex X8)` · `uint oid (hex X)` · `int spawnIndex` · `string name` · `uint nameId [v≥13]` · `ushort type (hex X4)` · `Class class` · `int level [v≥12]` · `Vector3 pos` · `Angle rot` · `float hitboxRadius (f3)` · `uint curHp` · `uint maxHp` · `uint shield` · `uint curMp` · `uint maxMp [v≥24]` (else 10000) · `bool targetable` · `bool ally` · `ulong ownerId (actor)` · `uint fateId [v≥14]`.

HPMP order is `CurHP, MaxHP, Shield, CurMP, MaxMP` (`Actor.cs:64`; reader `ReplayParserLog.cs:776`). Same 5-tuple used by **`HP  `**: `ulong actor` · HPMP.

**`ACT-`**: `ulong actor`. **`NAME`** (v≥10): `ulong actor` · `string name` · `uint nameId [v≥13]`. **`CLSR`**: `ulong actor` · **void** · `Class` · `int level [v≥12]`.

**`MOVE`** (v≥10): `ulong actor` · `Vector3 pos` · `Angle rot`. Writer `ActorState.cs:227`.

**`ACSZ`**: `ulong actor` · `float hitboxRadius (f3)`. **`ATG+`/`ATG-`**, **`DIE+`/`DIE-`**, **`COM+`/`COM-`**, **`OPNT`**: `ulong actor` (value from tag). **`ALLY`**, **`NENP`**: `ulong actor` · `bool`. **`TARG`**: `ulong actor` · `ulong target`. **`MNTD`**: `ulong actor` · `uint mountId`. **`FORA`**: `ulong actor` · `byte` · `byte`. **`EVTS`**: `ulong actor` · `byte state`. **`MDLS`**: `ulong actor` · `byte modelState` · `byte animState1 (opt)` · `byte animState2 (opt)`.

**`TETH`** / **`TET+`**: `ulong actor` · `uint tetherId` · `ulong target`. **`TET-`**: `ulong actor`.

**`CST+`** (writer `ActorState.cs:393`): `ulong actor` · `ActionID action` · `ulong target` · `Vector3 location` · `[v≥17]` float pair `elapsed/total` (v<17: time pair `finishAt/total`) · `bool interruptible (opt)` · `Angle rotation (opt)`. **`CST-`**: `ulong actor`.

**`CST!`** — action effect / cast event (writer `ActorState.cs:410-420`, reader `:608-618`): `ulong sourceActor` · `ActionID action` · `ulong mainTarget` · `float animationLock (f2)` · `uint maxTargets` · `Vector3 targetPos [v≥6]` · `uint globalSequence [v≥7]` · `uint sourceSequence [v≥9]` · `Angle rotation [v≥21]` · **targets** (see primitive table: text = remaining `id!eff!eff…` fields; binary = `int32 count` + per target `uint64 id` + 8 × `uint64`).

**`ER  `**: `ulong actor` · `uint globalSequence` · `int targetIndex`.

**`STA+`** / **`STA!`**: `ulong actor` · `int index` · `ActorStatus` (`uint id` · `ushort extra (hex X4)` · remaining seconds `f3` / `int64 ticks` · `ulong source`). **`STA-`**: `ulong actor` · `int index`.

**`AIE+`**: `ulong actor` · `int index` · `uint globalSequence` · `int targetIndex` · `ulong sourceActor` · `ActionID` · `ActionEffects` (8 × ulong, text `X16`). **`AIE-`**: `ulong actor` · `int index`.

**`ICON`**: `ulong actor` · `uint iconId` · `ulong target [v≥22]`.

**`ESTA`**: `ulong actor` · `ushort state (hex X4)`. **`EANM`**: `ulong actor` · `ushort p1 (hex X4)` · `ushort p2 (hex X4)`. **`PATE`**: `ulong actor` · `ushort actionTimelineId (hex X4)`. **`NYEL`**: `ulong actor` · `ushort yellId` (decimal).

### Party

**`PAR `** / **`PAR+`** / **`PAR!`**: `int slot` · `ulong contentId (hex)` · `ulong instanceId (hex)` · `bool inCutscene [v≥15]` · `string name [v≥15]`. **`PAR-`**: `int slot`. **`LB  `**: `int cur` · `int max`.

### Client (Forge treats as opaque but must consume exactly)

| Tag | Layout |
|---|---|
| `CLAR` | `ActionID` · `ulong target` · `Vector3` · `uint sourceSequence` · `float initialAnimLock` · float pair · float pair |
| `CLRJ` | `ActionID` · `uint sourceSequence` · float pair · `uint logMessageId` |
| `CDN+` | `float` ; `CDN-` : nothing |
| `CLAL`, `CLMV` | `float` |
| `CLCB` | `uint action` · `float remaining` |
| `CLST` | 3 × `int` |
| `CLCD` | `bool reset` · `byte count` · count × (`byte group` · `float elapsed` · `float total`) (v<19 swapped) |
| `CLDA` | 2 × (`ActionID` · `byte [v≥20]` · `byte [v≥20]`) |
| `CLBH` | `byte count` · count × (`byte id` · `byte qty`) |
| `CBLU` | `byte count` · count × `uint` |
| `CLVL` | `byte count` · count × `short` |
| `CLAF` | `uint fateId` · `Vector3` · `float` |
| `CPET` | `ulong (hex)` · `byte` · `byte` |
| `CLFT` | `ulong (hex)` |
| `CLFD` | `Angle` |
| `CLKV` | 6 × `uint` |
| `FATE` | `uint` · `long` |

### Deep dungeon / network (opaque)

| Tag | Layout |
|---|---|
| `DDPG` | `byte type` · 8 × `byte` |
| `DDMP` | v<23: `byte[]` (length-prefixed) ; v≥23: 25 × `byte (hex)` |
| `DDPT` | 4 × (`ulong actor` · `byte`) |
| `DDIT` | 16 × (`byte` · `byte (hex)`) |
| `DDCT` | 16 × (`byte` · `byte`) |
| `DDMG` | `byte[]` |
| `IPCI` | `uint` |
| `IPCS` | `int packetId` · `ushort` · `uint` · `uint (hex)` · `long ticks` · `byte[]` |

## 7. Helper OID

- `Helper = 0x233C` is confirmed as the **module-author convention** in `OID` enums (every one of 11 checked Dawntrail dungeon modules has `Helper = 0x233C, // R0.500, …, Helper type`). It is not a framework constant. Confirmed also in BMR modules at `b919e097` (`CrucibleOfTheUnbroken/SecondMasterBoard/{ChimeraPiece,MedusaPiece,DrakePiece}.cs` all define `Helper = 0x233C`).
- Framework-level marker: `ActorType.Helper = 0x20B` (`Actor.cs:13`). Forge should classify on **both** (`type == Helper || oid == 0x233C`).

## 8. vbm module API confirmed for M1

- `[ModuleInfo(BossModuleInfo.Maturity maturity)]` attribute (`BossModuleInfo.cs:88-107`), settable props: `StatesType, ConfigType, ObjectIDType, ActionIDType, StatusIDType, TetherIDType, IconIDType` (`Type?`, default `ns.xxxStates / ns.xxxConfig / ns.OID / ns.AID / ns.SID / ns.TetherID / ns.IconID`), `uint PrimaryActorOID` (default `OID.Boss`), `string Contributors`, `Expansion` (default: 2nd namespace level), `Category` (default: 3rd namespace level), `GroupType`, `uint GroupID`, `uint NameID`, `int SortOrder`, `int PlanLevel`.
- `BossModuleInfo.Maturity { WIP, Contributed, Verified }`; `GroupType { None, CFC, MaskedCarnivale, RemovedUnreal, Quest, Fate, Hunt, BozjaCE, BozjaDuel, EurekaNM, GoldSaucer }`; `Expansion { RealmReborn, Heavensward, Stormblood, Shadowbringers, Endwalker, Dawntrail, Global, Count }`; `Category { Uncategorized, Dungeon, Trial, Extreme, Raid, Savage, Ultimate, Unreal, Alliance, Chaotic, Foray, Criterion, DeepDungeon, FATE, Hunt, Quest, TreasureHunt, PVP, MaskedCarnivale, GoldSaucer, Count }` (`BossModuleInfo.cs:16-69` at `1391bc42`).
- `protected BossModule(WorldState ws, Actor primary, WPos center, ArenaBounds bounds)` (`BossModule.cs:83`).
- `ArenaBoundsCircle(float Radius, float MapResolution = 0.5f)`, `ArenaBoundsSquare(float Radius, …)`, `ArenaBoundsRect(float HalfWidth, float HalfHeight, Angle Rotation = default, …)`, `ArenaBoundsCustom(float Radius, RelSimplifiedComplexPolygon Poly, …)` (`ArenaBounds.cs:100-182`).
- `StateMachineBuilder(BossModule module)`; `Phase TrivialPhase(uint seqID = 0, float enrage = 10000)`; `Phase SimplePhase(uint seqID, Action<uint> buildState, string name, float dur = -1)`; `State Timeout(uint id, float duration, string name = "")`; `State Cast<AID>(uint id, AID aid, float delay, float castTime, string name = "", bool interruptible = false)`; `ActivateOnEnter<C>()` on both `Phase` and `State` (`StateMachineBuilder.cs:33,64,103,126,154,348`).
- Confirmed minimal module (`D012Drowsie.cs`):

```csharp
namespace BossMod.Dawntrail.Dungeon.D01Ihuykatumu.D012Drowsie;

public enum OID : uint
{
    Boss = 0x4195, // R5.000, x1
    Helper = 0x233C, // R0.500, x10, Helper type
}

class D012DrowsieStates : StateMachineBuilder
{
    public D012DrowsieStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Uppercut>();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.Verified, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 826, NameID = 12716)]
public class D012Drowsie(WorldState ws, Actor primary) : BossModule(ws, primary, new(80, 53), new ArenaBoundsCircle(20));
```

### 8.1 StateMachineBuilder confirmed for M4 (`BossMod/BossModule/StateMachineBuilder.cs`, `BossMod/BossModule/StateMachine.cs`, `BossMod/Data/Actor.cs` at `1391bc42`)

- Fields: `protected BossModule Module`; `_phases`, `_curInitial`, `_lastState`, `_states` (`StateMachineBuilder.cs:93-97`).
- `Phase SimplePhase(uint seqID, Action<uint> buildState, string name, float dur = -1)` — calls `buildState(seqID << 24)`; throws when nested or when the builder created no states; `phase.Exit += () => Module.ClearComponents(comp => !comp.KeepOnPhaseChange)` (`:103-115`).
- `Phase DeathPhase(uint seqID, Action<uint> buildState)` — `SimplePhase(seqID, buildState, "Boss death")` then `phase.Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed || Module.PrimaryActor.HPMP.CurHP == 0` (`:118-123`).
- `Phase TrivialPhase(uint seqID = 0, float enrage = 10000) => DeathPhase(seqID, id => SimpleState(id, enrage, "Enrage"))` (`:126`).
- `State SimpleState(uint id, float duration, string name)` — the only linker: duplicate id throws; `_lastState.NextStates = [state]`; when `(_lastState.ID & 0xFFFF0000) == (id & 0xFFFF0000)` sets `StateHint.GroupWithNext` on the previous state; first state becomes `_curInitial`; **no `Update`** (raw state never transitions on its own) (`:129-151`).
- `State Timeout(uint id, float duration, string name = "")` — `Update = t => t >= Duration ? 0 : -1`, comment `"Timeout"`, no hints (`:154-160`).
- `State ComponentCondition<T>(uint id, float expected, Func<T, bool> condition, string name = "", float maxOverdue = 1, float checkDelay = 0) where T : BossComponent` (`:223-249`).
- `State CastStart<AID>(uint id, AID aid, float delay, string name = "") where AID : Enum` → `ActorCastStart(id, () => Module.PrimaryActor, aid, delay, true, name)` (`:289`); `State CastEnd(uint id, float castTime, string name = "", bool interruptible = false)` (`:336`).
- `State Cast<AID>(uint id, AID aid, float delay, float castTime, string name = "", bool interruptible = false) where AID : Enum` — `CastStart(id, aid, delay, "")` + `return CastEnd(id + 1, castTime, name, interruptible)`; **two states, `id` and `id + 1`** (`:348-353`). `State CastMulti<AID>(uint id, IEnumerable<AID> aids, float delay, float castTime, …)` identical shape (`:364-369`); call-site idiom `CastMulti(id + 0x10, [AID.A, AID.B], 2, 5)` (`RM01SBlackCatStates.cs:78`). The AID parameter is the **enum**, never `(uint)` — `IdCast` does not apply to state builders.
- `State Targetable(uint id, bool targetable, float delay, string name = "", float checkDelay = 0)` — primary actor; sets `DowntimeEnd` (true) / `DowntimeStart` (false) (`:381-383`).
- Wrappers: `Phase.ActivateOnEnter<C>()`, `Phase.DeactivateOnEnter<C>()`, `Phase.DeactivateOnExit<C>()`, `Phase.SetHint`, `Phase.Raw` (`:14-44`); `State.ActivateOnEnter<C>()`, `State.DeactivateOnExit<C>()`, `State.ExecOnEnter<C>(Action<C>)`, `State.ExecOnExit<C>`, `State.SetHint/ClearHint`, `State.Raw` (`:46-80`). All return the wrapper → chainable. `StateMachine.Phase.Update` is `Func<bool>?`; `StateMachine.State.Update` is `Func<float, int>?`, `NextStates` is `State[]?` (`StateMachine.cs:37-57`).
- **No loop API.** `NextStates` is forward-only. Upstream unrolls repeated rotations as successive method calls (`Method(id + 0x10000 * k, delay)`) and ends open-ended phases with `SimpleState(id + 0xFF0000, 10, "???");` (`Endwalker/Alliance/A10RhalgrEmissary/A10RhalgrEmissaryStates.cs:21`, `A22AlthykNymeiaStates.cs:43`, many others; some use `100`).
- HP/condition phases: `SimplePhase(0, Phase1, "P1").Raw.Update = () => Module.PrimaryActor.IsDeadOrDestroyed || …;` (`Dawntrail/Extreme/Ex3Sphene/Ex3SpheneStates.cs:10-13`); `.ActivateOnEnter<X>()` may precede `.Raw.Update =` (`A22AlthykNymeiaStates.cs:9-11`). `Actor.HPRatio => (float)HPMP.CurHP / HPMP.MaxHP` (`Data/Actor.cs:137`); `public record struct ActorHPMP(uint CurHP, uint MaxHP, uint Shield, uint CurMP, uint MaxMP)` (`Data/Actor.cs:64`).
- Placement idiom: `Cast(id, AID.DestructiveStatic, delay, 8, "Frontal cleave").ActivateOnEnter<DestructiveStatic>().DeactivateOnExit<DestructiveStatic>();` (`A10RhalgrEmissaryStates.cs:26-28`); mechanics are spaced `id + 0x10000`, sub-steps inside a mechanic `id + 0x10` (`Ex1ValigarmandaStates.cs:13-22`, `RM01SBlackCatStates.cs:13-17,45-49`).
- Delay/cast-time literals: `5.7f` for non-integers, bare ints allowed (`float` parameters; a bare `7.5` double literal does not compile).

### 8.2 Component ctors at `1391bc42` (`BossMod/Components/*.cs`) — VERIFY closed

All take **`Enum aid`** (not `uint`, not `ActionID`). Emit `AID.X` with no `(uint)` cast. Classes live in `BossMod.Components`, so `Components.StandardAOEs(...)` is the call-site idiom inside `namespace BossMod.*`. **`SimpleAOEs` / `SimpleAOEGroups` do not exist** at this commit.

| Class | Signature |
|---|---|
| `StandardAOEs` | `(BossModule module, Enum aid, AOEShape shape, int maxCasts = int.MaxValue)` (`GenericAOEs.cs`) |
| `GroupedAOEs` | `(BossModule module, Enum[] aids, AOEShape shape, int maxCasts = int.MaxValue)` — collection expression `[AID.A, AID.B]` targets `Enum[]` |
| `SelfTargetedAOEs` | same as `StandardAOEs` |
| `LocationTargetedAOEs` | `(BossModule module, Enum aid, float radius, …)` |
| `RaidwideCast` | `(BossModule module, Enum aid, string hint = "Raidwide")` (`UnavoidableDamage.cs`) |
| `SingleTargetCast` | `(BossModule module, Enum aid, string hint = "Tankbuster")` |
| `SpreadFromCastTargets` | `(BossModule module, Enum aid, float radius, bool drawAllSpreads = true)` (`StackSpread.cs`) |
| `StackWithCastTargets` | `(BossModule module, Enum aid, float radius, int minStackSize = 2, int maxStackSize = int.MaxValue)` |
| `KnockbackFromCastTarget` | `(BossModule module, Enum aid, float distance, …)` (`Knockback.cs`) |
| `CastGaze` | `(BossModule module, Enum aid, bool inverted = false, float range = 10000)` (`Gaze.cs`) |
| `CastSharedTankbuster` | `(BossModule module, Enum aid, AOEShape shape, bool originAtTarget = false)` (`SharedTankbuster.cs`) |
| `CastTowers` | `(BossModule module, Enum aid, float radius, int minSoakers = 1, int maxSoakers = 1)` (`Towers.cs`) |

Forge emit: `StandardAOEs` / `GroupedAOEs` for telegraphed/hidden AOEs; `LocationTargetedAOEs` when `Shape.Origin == CastLocation` and `Kind == Circle` (radius); `RaidwideCast` / `SingleTargetCast` / spread / stack / knockback / gaze as before. `CastSharedTankbuster` / `CastTowers` signatures confirmed; emitter still emits `// TODO(forge)` until those kinds are wired. Attract has no `AttractFromCastTarget` at the pin — see §9 (vbm: `KnockbackFromCastTarget(..., kind: Kind.TowardsOrigin)`; bmr: `SimpleKnockbacks` with `kind: Kind.TowardsOrigin`).


### 8.3 Compile shim (`tests/BossModForge.UpstreamShim/`, pin `1391bc42`)

Stand-in for building real BossMod (needs .NET 9 + Dalamud libs; not available here). `tests/BossModForge.GoldenCompile` compiles `tests/BossModForge.Tests/Golden/*.vbm.cs` against the shim. Bodies are empty / throw / return default; signatures match the pin.

Mirrored files:

- `BossMod/BossModule/StateMachineBuilder.cs`, `StateMachine.cs`, `BossModule.cs`, `BossComponent.cs`, `AOEShapes.cs`, `ArenaBounds.cs`, `BossModuleInfo.cs`
- `BossMod/Util/Angle.cs`, `WPosDir.cs`
- `BossMod/Data/Actor.cs`, `WorldState.cs`
- `BossMod/Components/CastCounter.cs`, `CastHint.cs`, `GenericAOEs.cs`, `UnavoidableDamage.cs`, `StackSpread.cs`, `Knockback.cs`, `Gaze.cs`, `SharedTankbuster.cs`, `Towers.cs`

VERIFY closed in this pass (copied from the pin, not invented):

- **Expansion** `{ RealmReborn, Heavensward, Stormblood, Shadowbringers, Endwalker, Dawntrail, Global, Count }`
- **Category** `{ Uncategorized, Dungeon, Trial, Extreme, Raid, Savage, Ultimate, Unreal, Alliance, Chaotic, Foray, Criterion, DeepDungeon, FATE, Hunt, Quest, TreasureHunt, PVP, MaskedCarnivale, GoldSaucer, Count }`
- **ArenaBounds ctors:** `ArenaBoundsCircle(float Radius, float MapResolution = 0.5f)`; `ArenaBoundsSquare(float Radius, float MapResolution = 0.5f)` (parameter is named `Radius`, i.e. half-extent); `ArenaBoundsRect(float HalfWidth, float HalfHeight, Angle Rotation = default, float Radius = 0, float MapResolution = 0.5f)`; `ArenaBoundsCustom(float Radius, RelSimplifiedComplexPolygon Poly, float MapResolution = 0.5f)`. Emitter still prints `new ArenaBoundsCustom([WPos…])` (not this ctor) and a High review — that mapping is an emit gap, not an API unknown.
- **AOEShape ctors:** `AOEShapeCircle(float Radius)`; `AOEShapeDonut(float InnerRadius, float OuterRadius)`; `AOEShapeCone(float Radius, Angle HalfAngle, Angle DirectionOffset = default)`; `AOEShapeRect(float LengthFront, float HalfWidth, float LengthBack = 0, Angle DirectionOffset = default)`; `AOEShapeCross(float Length, float HalfWidth, Angle DirectionOffset = default)` (`AOEShapes.cs`).
- **`Degrees()`:** `public static Angle Degrees(this float degrees)` and `public static Angle Degrees(this int degrees)` on `AngleExtensions` (`Angle.cs:64-66`); also `Radians(this float)`.

## 9. Closed VERIFY (all resolved 2026-09-20)

| Item | Resolution |
|---|---|
| BMR pin commit, `ModuleInfo` shape (`Contributors`, `PrimaryActorOID`), `BossModule` ctor incl. `SharedBounds`/`ArenaBoundsComplex`, module-pack loading | BMR pinned at `b919e097`. `[ModuleInfo]` `Contributors` / `PrimaryActorOID` / `SortOrder` confirmed settable; `BossModule` ctor is vbm's plus an optional `onlyLoadIfTargetable = false` — **no** `SharedBounds`/`ArenaBoundsComplex` exists at the pin; **no** module-pack/hot-load facility — `BossModuleRegistry` maps compiled-in assembly types. Full record in §10. |
| `AttractFromCastTarget` type name | No such class at either pin. Attract = pull towards origin: vbm `KnockbackFromCastTarget(module, aid, distance, kind: Knockback.Kind.TowardsOrigin)` (`Components/Knockback.cs` at `1391bc42`); bmr has no `KnockbackFromCastTarget` at all — `Components.SimpleKnockbacks(module, (uint)aid, distance, kind: Kind.TowardsOrigin)` (`Components/Knockback.cs` at `b919e097`). |
| Emitter `ArenaBoundsCustom([WPos…])` vs pin ctor | Ctor confirmed §8.3: `ArenaBoundsCustom(float Radius, RelSimplifiedComplexPolygon Poly, float MapResolution = 0.5f)`. The WPos-list template is still wrong against that ctor; this is an **emit gap** tracked as a High review item, not an API unknown. |
| `TrivialPhase().ActivateOnEnter<T>()` chain | Confirmed §8/§8.1: `.ActivateOnEnter<C>()` on `Phase` (and `State`), chainable, chain ends with `;`. |
| Omen `fan(\d{3})` / `sircle_…` | `fan(\d{3})` → total angle implemented (`gl_fan090_1bf` → 90°). **`TryParseDonut` stays false** — `sircle_…` encoding not verified on a real corpus; donut radii come from empirical fit only. |
| Recorder `MOVE` throttling vs 0.1 s δ | Held: hold-last `PosRotAt` and the δ grid are unchanged; no systematic lag bias has been observed. Interpolate only if real logs show one. |
| CastType table vs BossMod helpers | Forge's `ShapeInference.PriorFor` implements the eleven rows (CastType 6/9/14 → no prior), matching the community table; cross-checked against BossMod action-info helpers at the pin — no conflicting mapping. |
| Whether BossMod interpolates actor positions between `MOVE` samples | No. BossMod applies `MOVE` ops directly (`ActorState.cs` writer / `Actor.PosRot` updated per op; no interpolation in `ReplayBuilder`/`ReplayParserLog` at `1391bc42`); replay analysis samples the latest position. Forge's nearest-previous sample matches. |
| Exact `BossMod.Class` enum tail (values after `CNJ=6`) | Copied from `BossMod/Data/ClassRole.cs` verbatim at M0. |
| Legacy (< v10) compound-string layouts | Out of scope by design: Forge rejects `version < 10` with an `UnsupportedVersion` issue. |

## 10. BMR (`bmr`) verification record — `b919e0970d6559de280c40defb4117dd7275c961`, read 2026-09-20

Facts read directly from the pinned tree (file list in §1). BMR diverges from vbm mainly by taking `uint` aid in components and by subclassing hint components.

- **State builders are enum-typed, same as vbm.** `State Cast<TAID>(uint id, TAID aid, float delay, float castTime, string name = "", bool interruptible = false) where TAID : unmanaged, Enum` (`StateMachineBuilder.cs:447`), likewise `CastStart`/`CastMulti` (`:354,463`). No `(uint)AID.X` in state builders.
- **`SimpleState` duplicate ids auto-increment with a log** (`while (_states.ContainsKey(id)) { Service.Log($"Duplicate state id {id:X}, incrementing by 1"); ++id; }`, `:162-168`) where vbm throws. Irrelevant for Forge (ids are unique); recorded for completeness.
- **Components take `uint` aid.** `SimpleAOEs(BossModule module, uint aid, AOEShape shape, int maxCasts = …)` (`Components/GenericAOEs.cs:96`) plus a `(uint aid, float radius)` overload (`:98`); `SimpleAOEGroups(BossModule module, uint[] aids, AOEShape shape, …)` (`:208`) plus radius overload (`:211`). **No `StandardAOEs`/`GroupedAOEs` exist at the pin**; vbm's `SelfTargetedAOEs`/`LocationTargetedAOEs` are merged into `SimpleAOEs` (comment at `GenericAOEs.cs:95`). Real-module idiom: `Components.SimpleAOEs(module, (uint)AID.TheRamsRoar, 12.0f)`, `Components.SimpleAOEGroups(module, [(uint)AID.A, (uint)AID.B], new AOEShapeCone(60.0f, 120.0f.Degrees()))` (`SecondMasterBoard/ChimeraPiece.cs:60-62`).
- **Hint components are abstract and subclassed with `(uint)AID.X` + hint string:** `RaidwideCast(BossModule module, uint aid, string hint = "Raidwide", …)` (`Components/UnavoidableDamage.cs:4`), `SingleTargetCast(…, uint aid, string hint = "Tankbuster", …)` (`:211`); e.g. `sealed class ToxicTantrum(BossModule module) : Components.RaidwideCast(module, (uint)AID.ToxicTantrum, "Raidwide + applies poison")` (`ChimeraPiece.cs:59`).
- **`CastSharedTankbuster(BossModule module, uint aid, AOEShape shape, bool originAtTarget = false, …)`** (`Components/SharedTankbuster.cs:156`) plus a `(uint aid, float radius, …)` overload (`:159`).
- **`CastGaze(BossModule module, uint aid, bool inverted = false, float range = 10000f, …)`** (`Components/Gaze.cs:153`); `SpreadFromCastTargets(BossModule module, uint aid, float radius, …)` and `StackWithCastTargets(BossModule module, uint aid, float radius, int minStackSize = 2, …)` (`Components/StackSpread.cs:806,809`).
- **Knockback:** no `KnockbackFromCastTarget` at the pin. `SimpleKnockbacks(BossModule module, uint aid, float distance, …, Kind kind = Kind.AwayFromOrigin, …)` (`Components/Knockback.cs:297`) and `SimpleKnockbackGroups` (`:346`); `Kind.TowardsOrigin` is the attract/pull (`:12`).
- **Module ctor:** `protected BossModule(WorldState ws, Actor primary, WPos center, ArenaBounds bounds, bool onlyLoadIfTargetable = false)` (`BossModule.cs:181`) — vbm's plus one optional flag. No `SharedBounds`/`ArenaBoundsComplex` helpers exist at the pin.
- **`[ModuleInfo]`** (`BossModuleInfo.cs`): same settable props as vbm plus confirmed `uint PrimaryActorOID` (`:113`), `string Contributors` (`:115`), `int SortOrder` (`:121`).
- **Module discovery:** `BossModuleRegistry` maps module `Type`s compiled into the assembly (`RegisteredModules`, `ModuleFactory` delegates, `BossModuleRegistry.cs`). There is no module-pack/hot-load facility at the pin.
- **Helper OID:** `Helper = 0x233C` appears in the `OID` enums of all three read `SecondMasterBoard` modules (matches vbm convention, §7).

## 11. Machine-gated verification ledger

Items that cannot be executed on the Linux dev box (no BossMod build, no FFXIV install, no Dalamud); each has its seam implemented and unit-tested, with runtime verification done elsewhere.

### Plugin compile (`BossModForge.Plugin.slnx`, M5)

The Dalamud plugin (`src/BossModForge.Plugin/**`) was **first compiled on this Linux box 2026-09-21** (Dalamud.NET.Sdk against the local xlcore Dalamud at `~/.xlcore/dalamud/Hooks/dev`, `<EnableWindowsTargeting>true</EnableWindowsTargeting>` added to the csproj) and deployed to `~/.xlcore/devPlugins/BossModForge/`. Compile-surface fixes: this Dalamud binding has no plain `ImGui.InputText` (the `Ui.InputText` helper now calls `InputTextWithHint(label, "", ref string, int maxLength, flags)`); the manifest was renamed `BossModForge.Plugin.json` → `BossModForge.json` to match `AssemblyName` (DalamudPackager requirement). All m5-brief §8 items are now compile-confirmed, but several are only *proven* in-game: Lumina sheet columns at runtime, `RunOnFrameworkThread<T>` behavior, ImGui widget behavior (not just existence), replay dir/file-pattern assumptions, and the BMR IPC name. The m5 brief §10 in-game checklist (analyze a real recording, override → re-emit, validate report, unload/reload) is the remaining verification, executed where the game runs. Recorded in `docs/plans/m5-status.md`.

### Conformance (`forge conform --bossmod-dll`)

`BossModReflection` reflect-loads a real `BossMod.dll` (`Assembly.LoadFrom`, `BossMod.Replay.Replay`/`ReplayParserLog`, op-type → tag map resolved from the parser's fourCC→handler dispatch when present, else the table transcribed from §5). The reflection happy-path — a real `forge conform` run and `--selftest` op→tag dump against a BossMod build — is **verified manually on a machine with a BossMod build**; no `BossMod.dll` exists on the Linux dev box. The unit tests (`tests/BossModForge.Tests/Cli/ConformTests.cs`) cover the comparer and all error paths (absent `--bossmod-dll`, missing/unloadable DLL, missing log → exit 4).

### LuminaGameData (`forge infer --game-path`)

`LuminaGameData` (Cli, `Lumina 5.8.0` + `Lumina.Excel 7.2.3`) reads typed struct-row sheets (`Lumina.Excel.Sheets.Action/Omen/BNpcName/Status/TerritoryType`) from a disk game install. Runtime verification against a real FFXIV install — sheet availability, `ExtractText()` output, omen path strings on real rows — requires `--game-path` pointing at a game install (Windows box); **no FFXIV install exists on the Linux dev box**. The acceptance here is: compile against the pinned packages plus graceful-degradation coverage — `LuminaGameData_BadPath_DegradesGracefully` (nonexistent path → `Available == false`, all members null, no throw), `Infer_WithBadGamePath_StillRuns` (infer warns on stderr and produces byte-identical EM to the `NullGameData` run), and `FakeGameData_ChangesNames` (injected `IGameData` proves the name wiring is live end-to-end). The column mapping (`Name`, `CastType`, `EffectRange`, `XAxisModifier`, `Cast100ms`, `Omen.RowId`/`Omen.ValueNullable?.Path`) was verified by reflection against the pinned Lumina.Excel 7.2.3 assembly, not against a live sheet.

### M4b `--bossmod` (`forge validate --bossmod <checkout>`)

`--bossmod` orchestration is implemented in full: checkout-shape validation (`BossMod/` dir + a `.sln`/`.slnx`, else exit 4 with "does not look like a BossMod checkout"), deterministic staging of the emitted module into `BossMod/Modules/ForgeScratch/<ModuleName>/`, `CompileErrorMapper` mapping `dotnet build` errors back to EM items (unknown enum member → `ability:{aid}`/`participant:{oid}` via ordinal-sorted case-insensitive-prefix + Levenshtein ≤ 3; component ctor mismatch → `flavor-profile`), and report wiring (build failure → stderr + `failures` entry + exit 3). All of it is unit-tested on Linux (`tests/BossModForge.Tests/Cli/CompileErrorMapperTests.cs`, `tests/BossModForge.Tests/Validation/ValidateCommandBossmodTests.cs`).

Machine-gated: the two subprocess steps — the in-checkout `dotnet build -c Release` of the real BossMod/BMR solution and the `dotnet run` of the embedded ReplayValidator host template (`src/BossModForge.Cli/ReplayValidator/`, excluded from the CLI build and deployed as embedded resources) — require a BossMod/BMR checkout that builds on the machine (Dalamud libs; typically Windows). No such checkout or `BossMod.dll` exists on the Linux dev box, so the host template has never been compiled; its first compile/run against a real checkout must confirm the BossMod API surface it uses (replay parse, module instantiation, state-machine/AOE sampling, modeled on upstream `UIDev`/replay tooling at the vbm pin). Recorded acceptance here: orchestration + error-mapping + checkout gating fully covered by unit tests; end-to-end `--bossmod` execution is verified on a machine with a building BossMod checkout.

## 12. Replay format v25–v31 (vbm `6e410ef8`, bmr `577675d8`, read 2026-11-04)

Read from `BossMod/Replay/ReplayParserLog.cs` + `ReplayRecorder.cs` + the `Write` methods in `BossMod/Data/{ActorState,ClientState,PartyState,WaymarkState,WorldState,NetworkState,DeepDungeonState,ReplayState}.cs` at both pins; per-version history from `git log -G"Version = [0-9]+" -- BossMod/Replay/ReplayRecorder.cs` and the `ReplayParserLog.cs` diff of each bump commit. Header (`VER `), `FRAM`, container/magic and all primitive encodings are **unchanged** from §2–§4 (`ParseVersion`/`ParseFrameStart`/`BinaryInput` not touched since `1391bc42`, apart from bmr's EndOfStream try/catch wrappers which do not change the layout).

### 12.1 Version gates (field layout changes)

| Ver | Commit (vbm, merged into bmr) | Change |
|---|---|---|
| 25 | `9c50bd5f1` 2025-05-29 "add 3 more duty actions" | `CLDA`: v≥25 = `byte count` · count × (`ActionID` · `byte cur` · `byte max`); v<25 keeps the fixed 2-slot layout of §6 |
| 26 | `92a3b2ec8` 2025-06-17 "add layoutid" | `ACT+`: `uint layoutId (hex X)` inserted **after `spawnIndex`, before `name`** |
| 27 | vbm `5f2e57b2e` 2025-10-21 | `CLAF`: + `byte progress` · `byte handInCount` (v≥27) |
| 27 | **bmr only** `7b281f9a3` 2025-07-19 | `ACT+`: + trailing `int renderflags` after `fateId` (v≥27); new tag `RFLG` |
| 28 | vbm `2b7441329` 2025-10-29 | `CLAF`: + `uint objectiveNpc` at v≥28 (vbm). bmr gates the same field at **v≥29** (`38e9638a2` merge) |
| 28 | vbm `92f0375ec` 2025-10-26, reverted by `2b7441329` 3 days later | transient: `ACT+` + trailing `uint eventId`; **not** in HEAD readers of either lineage |
| 29 | `0a2f606b0` 2025-11-13 "add hoard flag to dd state" | `DDPG`: + `bool hoardCurrentFloor`; `DDMP`: rooms become 25 × `ushort (hex X4)` (was `byte`) |
| 30 | `ec4c93b88` 2026-04-07 "fix replay versioning" | `CHOC`: trailing `bool stabled` only at v≥30 (tag itself added unversioned in `d9f0486c1`) |
| 31 | `736de2a9d` 2026-08-11 "replay anonymizer" | `PAR `: member `name` string **dropped** (reader: name present only for `15 ≤ v < 31`); `ACT+` name may be anonymised to `"{Job} L{level}"` (content, not layout) |

### 12.2 Lineage divergence

vbm and bmr share version numbers but not layouts from v27. **No header field identifies the writer** (bmr `ReplayRecorder` only adds in-memory `CFCID`/`LogPath`). Differences in the HEAD readers: (a) bmr `ACT+` v≥27 trailing `int renderflags`; (b) `CLAF` objective-npc gate v28 (vbm) vs v29 (bmr). Forge (`ParseOptions.Lineage`, default `Auto`) detects at the first v≥27 `ACT+`: text → an extra field remains; binary → after the vbm layout, next 4 bytes are a known tag (or EOF) ⇒ vbm, else bytes 4..8 are a known tag ⇒ bmr. Detected lineage is reported as an `Info` `LineageDetected` issue and gates `CLAF`. The 3-day vbm v28 `eventId` window has the same width as bmr `renderflags`, so it is detected as bmr and consumed correctly (its `CLAF` also lacks the uint, matching the bmr v28 gate).

### 12.3 Tags added after v24 (unversioned; consumed as opaque)

| Tag | Layout (reader order) | Lineage / commit |
|---|---|---|
| `PVP+` / `PVP-` | — | both, `766428d1d` |
| `FLY+` / `FLY-` | — | both, `25984e09b` |
| `LEME` | `byte seq (hex X2)` · `byte param (hex X2)` · `byte[]` | both, `7de857903` |
| `SGN+` / `SGN-` | `byte sign` · `ulong target` (+ only) | both, `3792733b1` |
| `VFX ` | `ulong actor` · `uint vfxId` · `ulong target` | both, `132937499` |
| `PATS` | `ulong actor` · `int count` · count × (`ulong actor` · `ushort timeline (hex X4)`) | both, `6f18f4f6c` |
| `AVIS` | `ulong actor` · `ushort` (the `char` from `Visibility.Encode()`, decimal in text) | both, `84689eadf` |
| `CBST` | `byte count` · count × `byte` | both, `21699c95b` |
| `CHOC` | `ulong (hex X8)` · `byte stance` · `float timeLeft` · `bool stabled [v≥30]` | both, `d9f0486c1` |
| `HATE` | `ulong primary` · `int count` · count × (`ulong actor` · `int enmity`) | both, `aa15927f9` |
| `CLPR` | 4 × `float` | both, `a1409462b` |
| `INVT` | `uint itemId` · `uint quantity` | both, `75f9a969f` |
| `FLOS` | `uint actionId` · `ulong target` | both, `bfb6c5635` |
| `IPCX` | 5 × `uint` (`IPCI` kept as legacy 1 × `uint`) | both, `f37753fb9` |
| `RFLG` | `ulong actor` · `int renderflags` | **bmr only**, `7b281f9a3` |
| `ARO+` / `ARO-` | `ulong actor (X8)` · `float rot` (+ only) | **vbm only**, `37ca2edac` |
| `ALGI` | `int alliance` | **vbm only**, `914c3fd6d` |
| `CCRU` | `byte count` · count × `ushort` | **vbm only**, `3837c4573` |
| `RTT ` | `float` | **vbm only**, `febeb808a` |
| `RPRE` | `byte[]` (UTF-8 JSON preset list) | **vbm only**, `2bb1b9c91` |
| `RMOD` | `byte` | **vbm only**, `2bb1b9c91` |
| `RROL` | 8 × `byte` (`PartyState.MaxPartySize = 8`) | **vbm only**, `2bb1b9c91` |
| `RMOV` | 2 × `float` | **vbm only**, `2bb1b9c91` |
| `RACT` | `int count` · count × (`ActionID` · `ulong target` · `float priority` · `float castTime` · `Vector3 targetPos`) | **vbm only**, `2bb1b9c91` |
| `RPRI` | `int count` · count × (`ulong actor` · `int priority` · `int traceCount` · traceCount × (`int` · `string`)) | **vbm only**, `5bf4d8544` |

`ENVC` is renamed `ParseMapEffect` upstream with an identical layout. Forge's dispatch is the union of both lineages (no tag collides).

### 12.4 Corpus check (2026-11-04)

All 43 local recordings (42 vbm v31, 1 bmr v29) parse past the header to full length; lineage detection is correct for every file. Three files (`TenderValley_PCT100_2026_10_03_00_08_10`, `WorqorZormor_BRD100_2026_10_01_21_06_16`, bmr `HellsKier_BLM70_Ma_Ga_2026_03_27_23_33_12`) end with an EndOfStream mid-op; the reference `brotli -d` also reports `corrupt input` on them, so the files themselves are truncated (recording not closed cleanly), not a format gap.
