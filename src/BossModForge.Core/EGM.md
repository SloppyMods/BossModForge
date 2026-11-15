# BossModForge.Core

## Overview
Core library project (`BossModForge.Core.csproj`) with no direct `.cs` source files; all code lives in the subdirectories below. Flow: Replay parses recordings into ops; Inference derives pulls, participants, abilities, shapes, mechanics and timelines; Model builds and serializes the `EncounterModel` and applies overrides; Emit renders C# modules; Validation checks the model against recordings; Pipeline orchestrates the stages; Polish applies allowlisted patches. Model and Emit reference each other (`Model/EncounterTimelineBuilder` calls `Emit` planners).

## Subdirectories & Boundaries
- Replay: [./Replay/EGM.md](./Replay/EGM.md) - parses text/BLOG/BLCB recordings into typed ops, actor timelines and a `Recording`.
- GameData: [./GameData/EGM.md](./GameData/EGM.md) - `IGameData` lookups, null/cached implementations, key collection from a `Recording`.
- Inference: [./Inference/EGM.md](./Inference/EGM.md) - pull segmentation, classification, ability cataloging, bounds/shape fitting, mechanic classification (children: [Fitters](./Inference/Fitters/EGM.md), [Timeline](./Inference/Timeline/EGM.md)).
- Model: [./Model/EGM.md](./Model/EGM.md) - immutable `EncounterModel`, builder, JSON (de)serialization/validation, overrides applier.
- Emit: [./Emit/EGM.md](./Emit/EGM.md) - flavor-profile based C# module and ForgeMd rendering.
- Validation: [./Validation/EGM.md](./Validation/EGM.md) - timeline simulation, AOE and coverage validation, `ValidationReport`.
- Pipeline: [./Pipeline/EGM.md](./Pipeline/EGM.md) - `ForgePipeline` orchestration with progress and cancellation.
- Polish: [./Polish/EGM.md](./Polish/EGM.md) - `Polisher.Apply` allowlisted patches and `IPolishProvider`.
