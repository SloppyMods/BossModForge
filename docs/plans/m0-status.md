---
tags:
  - type/status
  - project/bossmodforge
  - status/done
type: status
project: bossmodforge
status: done
milestone: M0
brief: ./m0-builder-brief.md
next: ./m1-builder-brief.md
---

# M0 status — done (2026-09-19)

- `dotnet test BossModForge.slnx -c Release`: 16/16 pass (text, binary, brotli parity, `ActorTimeline`, `ingest` CLI; real-log smoke is gated on `FORGE_REAL_LOGS`).
- `forge ingest <log…> [--json]` parses text/binary/brotli recordings and prints per-tag op counts. Exit codes are `0` ok / `1` degraded / `2` error — this predates spec §10 (`4` = parse error); `infer`/`emit` follow §10, `ingest` is left as-is until M4.
- Contracts frozen for M1 (read-only): `Recording`, `RecordingMeta`, `Op` records (`Ops.Actor/World/Party.cs`), `ActorTimeline`, `Tag`, primitives, `IGameData`/`NullGameData`/`ActionRow`.
- Deferred, as planned in the M0 brief: Plugin project (M5), `conform` subcommand (documented in `README.md`), Lumina `IGameData` implementation (interface stub only).
- Process: Composer routing failed for Builder A, so the parent (Grok) wrote the solution/csproj/CLI files directly; Builder B delivered `Replay/**` and fixtures.
- Architecture review findings (no code changes in M0):
  - `Recording.ActorAt` scans every actor per call; inference stages must build a local `Dictionary<ulong, List<ActorTimeline>>` index instead of calling it in hot loops.
  - `ActorTimeline` drops `ActorCreateOp.IsAlly` and never applies `ActorAllyOp`; M1 classifies on `ActorType` only (spec §5.1 "non-ally" is a follow-up).
  - `ActorTimeline` carries no status/tether/cast/target history; `AbilityCataloger` reads `Recording.Ops` directly. Acceptable for M1.
  - `SyntheticDungeon` pulls are ~7 s with 1 s gaps, shorter than the M1 default `SegmenterOptions`; M1 tests pass fixture-specific options (see M1 brief §4).
