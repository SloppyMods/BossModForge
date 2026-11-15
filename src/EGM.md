# src

## Overview
Holds the three production projects of BossModForge: the Dalamud-free parsing/inference/emit library, the `forge` command-line tool, and the Dalamud plugin. The directory has no direct source files.

## Subdirectories & Boundaries
- BossModForge.Core: [./BossModForge.Core/EGM.md](./BossModForge.Core/EGM.md) - replay parsing, inference, encounter model, C# emit, validation, pipeline, polish patching.
- BossModForge.Cli: [./BossModForge.Cli/EGM.md](./BossModForge.Cli/EGM.md) - `forge` entry point registering seven subcommands (ingest, infer, emit, validate, merge, polish, conform).
- BossModForge.Plugin: [./BossModForge.Plugin/EGM.md](./BossModForge.Plugin/EGM.md) - Dalamud plugin composition root, configuration, services and ImGui windows.
