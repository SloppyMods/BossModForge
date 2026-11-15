# BossModForge Blueprint

## System Overview
BossModForge generates BossMod/BossModReborn boss modules from BossMod replay recordings (source: `AGENTS.md`). `src/BossModForge.Core` parses recordings, infers an encounter model and emits C#; `src/BossModForge.Cli` exposes this as the `forge` tool; `src/BossModForge.Plugin` is a Dalamud front end over the same Core. `tests/BossModForge.UpstreamShim` is a pinned API stub of BossMod that golden outputs compile against. The existing `README.md` is kept as is; this file is the EGM contract.

## Subdirectories & System Boundaries
- src: [./src/EGM.md](./src/EGM.md) - Core library, Cli and Plugin projects.
- tests/BossModForge.UpstreamShim: [./tests/BossModForge.UpstreamShim/EGM.md](./tests/BossModForge.UpstreamShim/EGM.md) - stub of the upstream BossMod API used to compile golden outputs.
- scripts: [./scripts/EGM.md](./scripts/EGM.md) - `build.sh` (build + test) and `build-plugin.ps1` (plugin build).

## Global Invariants
- Per `AGENTS.md`: `BossModForge.Core` must not reference Dalamud, ImGui or `BossMod.dll`.
- Per `AGENTS.md`: output must be deterministic; the same inputs and options give byte-identical Encounter Model JSON and C#.
- Per `AGENTS.md`: emitted C# uses only standard BossMod components; anything else becomes a review item.
- Per `AGENTS.md`: `docs/specs/2026-09-18-bossmodforge-design.md` is authoritative on architecture; changing a decision needs a matching spec and ADR update.

## Global Side Effects
- `forge validate --bossmod` writes into a BossMod checkout and spawns `dotnet build/run` (see `src/BossModForge.Cli/Validation/EGM.md`).
- `forge polish` with the http provider makes a network POST (see `src/BossModForge.Cli/Commands/EGM.md`).
- The plugin persists configuration and writes export files (see `src/BossModForge.Plugin/Services/EGM.md`).

## System Verification
- Run: `dotnet test BossModForge.slnx -c Release` from the repo root (or `scripts/build.sh`, which runs `dotnet build -c Release` then `dotnet test --no-build`) [scripts/build.sh:6-7]. `BossModForge.slnx` covers Core, Cli, `tests/BossModForge.Tests`, `tests/BossModForge.GoldenCompile` and `tests/BossModForge.UpstreamShim`; it does NOT include `src/BossModForge.Plugin`, which is built via `BossModForge.Plugin.slnx` / `scripts/build-plugin.ps1` [BossModForge.slnx:2-6].
