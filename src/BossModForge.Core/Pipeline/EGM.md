# Pipeline

## Overview
Single static facade, `ForgePipeline`, that chains the replay parser, pull segmenter, participant classifier, ability cataloger, encounter-model builder, overrides applier and module emitter. It holds no logic of its own beyond orchestration, progress reporting and cancellation checks.

## Interface
- `PipelineStage` enum: `Parsing, Segmenting, Classifying, Cataloging, Building, ApplyingOverrides, Done` [Pipeline/ForgePipeline.cs:PipelineStage]
- `PipelineProgress(PipelineStage Stage, float Fraction)` [Pipeline/ForgePipeline.cs:PipelineProgress]
- `PipelineOptions(Parse, Segmenter, Catalog, Build)`, all optional [Pipeline/ForgePipeline.cs:PipelineOptions]
- `AnalysisResult(Recording, Pulls, Participants, Catalog, Inferred, Effective)` [Pipeline/ForgePipeline.cs:AnalysisResult]
- `ForgePipeline.Parse(string path, ...)` and `Parse(Stream, string displayPath, ...)` -> `Recording` [Pipeline/ForgePipeline.cs:ForgePipeline.Parse]
- `ForgePipeline.CollectKeys(Recording)` -> `GameDataKeys` [Pipeline/ForgePipeline.cs:ForgePipeline.CollectKeys]
- `ForgePipeline.Analyze(Recording, IGameData, options, overrides, progress, ct)` and `Analyze(IReadOnlyList<Recording>, ...)` -> `AnalysisResult` [Pipeline/ForgePipeline.cs:ForgePipeline.Analyze]
- `ForgePipeline.WithOverrides(AnalysisResult, Overrides)` -> `AnalysisResult` [Pipeline/ForgePipeline.cs:ForgePipeline.WithOverrides]
- `ForgePipeline.Emit(EncounterModel effective, Flavor, string flavorsDirectory, EmitOptions? = null)` -> `EmitResult` [Pipeline/ForgePipeline.cs:ForgePipeline.Emit]
- Subdirectories: None

## Invariants
- `Parse` uses `new ParseOptions(KeepRawOps: false)` when `options?.Parse` is null [Pipeline/ForgePipeline.cs:ForgePipeline.Parse]
- Analysis stage order is Segment -> Classify -> Catalog -> Build -> ApplyOverrides; progress fractions are 0.15, 0.30, 0.45, 0.60, 0.85, 1.0 [Pipeline/ForgePipeline.cs:ForgePipeline.AnalyzeCore]
- `ct.ThrowIfCancellationRequested()` runs before every stage and once more before `Done` [Pipeline/ForgePipeline.cs:ForgePipeline.AnalyzeCore]
- The list overload throws `ArgumentNullException` for a null list and `ArgumentException` for an empty one; it analyzes `Recording.Concat(recordings)` and passes the originals as `sources` to the model builder [Pipeline/ForgePipeline.cs:ForgePipeline.Analyze]
- The single-recording overload passes `sources = null` [Pipeline/ForgePipeline.cs:ForgePipeline.Analyze]
- `Effective` equals `Inferred` when overrides are null or `IsEmpty`; otherwise it is `OverridesApplier.Apply(inferred with { Overrides = overrides })`. `Inferred` is never mutated [Pipeline/ForgePipeline.cs:ForgePipeline.ApplyOverrides]
- `Emit` defaults `EmitOptions` to `new EmitOptions(flavor, effective.Encounter.Namespace)` [Pipeline/ForgePipeline.cs:ForgePipeline.Emit]
- `Parsing` is declared in `PipelineStage` but `AnalyzeCore` never reports it [Pipeline/ForgePipeline.cs:PipelineStage, ForgePipeline.AnalyzeCore]

## Side Effects
- `Parse(string path, ...)` and `Emit` delegate to `ReplayParser.Parse` and `ModuleEmitter.Emit`/`ProfileFor` (the latter takes `flavorsDirectory`); file I/O occurs there, not in this file [Pipeline/ForgePipeline.cs:ForgePipeline.Parse, ForgePipeline.Emit]
- Invokes `IProgress<PipelineProgress>.Report` when a progress sink is supplied [Pipeline/ForgePipeline.cs:ForgePipeline.AnalyzeCore]
- No static mutable state, no locks [Pipeline/ForgePipeline.cs:ForgePipeline]

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from repo root)
