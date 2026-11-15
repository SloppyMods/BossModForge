# ADR-002: Core owns its replay parser; conformance-tested against BossMod's

**Status:** Proposed
**Date:** 2026-09-18

## Context
The Dalamud plugin must parse replays in-game. Referencing `BossMod.dll` would load a second large copy of BossMod inside the plugin's load context and couple Forge to BossMod's internal API churn. The log format itself is small (about 40 four-character tags), versioned (`ReplayRecorder.Version`, 24 at commit `1391bc42`), and stable at the op level.

## Decision
`BossModForge.Core` implements its own parser into Forge `Op` records. Unknown tags are preserved as `RawOp`. The CLI runs a conformance test that compares op counts and key field checksums against BossMod's own `ReplayParserLog` at the pinned commit.

## Consequences
- Forge must track recorder version bumps; unknown versions degrade gracefully rather than fail.
- Core stays free of Dalamud/BossMod dependencies and is fully unit-testable.
- Conformance drift is caught in CI, not by users.

## Alternatives rejected
- **Reference BossMod.dll from the plugin** — assembly duplication and tight API coupling.
- **Build Forge inside a BossMod fork** — users would have to run the fork.
- **Require text-format logs only** — still needs a parser and loses existing binary recordings.
