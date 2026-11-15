# ADR-001: Input is BossMod replay logs; ACT logs v2; video out of scope

**Status:** Proposed
**Date:** 2026-09-18

## Context
BossMod's `ReplayRecorder` writes per-frame world state — actor add/remove, `MOVE` position and rotation, casts with location and rotation, action effects with per-target results, statuses, tethers, icons, env-controls, director updates and zone — in binary-compressed, binary or text form behind a versioned `VER ` header. ACT/OverlayPlugin network logs carry only throttled `ActorMove` lines for non-casting actors, so hit/miss geometry derived from them is noisier. Video contains no structured state at all.

## Decision
v1 ingests only BossMod replay logs. An ACT network-log adapter is a v2 item that plugs into the same normalized Op stream. Video is out of scope.

## Consequences
- Users must enable BossMod replay auto-record before fights they want modules for.
- Ingestion is a thin normalizer over an existing, well-defined format.
- A later ACT adapter must synthesize positions between sparse samples and will carry lower confidence.

## Alternatives rejected
- **ACT logs first** — worse geometry for the core shape-inference problem.
- **Video / OCR** — no positions, enormous effort, brittle.
- **Own in-game recorder** — duplicates BossMod's recorder and adds maintenance without adding data.
