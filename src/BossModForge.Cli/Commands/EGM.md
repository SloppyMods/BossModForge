# Commands

## Overview
One static class per `System.CommandLine` subcommand of the `forge` CLI. Each exposes `Create()` (builds the `Command`) and a testable `Run(..., TextWriter stdout, TextWriter stderr) -> int` returning an exit code. Pipeline logic is delegated to `BossModForge.Core`.

## Interface
- `IngestCommand` (`ingest <files...> [--json]`): `Run(paths, json, stdout, stderr)` — per-tag op counts [IngestCommand.cs]. Exit 0; 1 if any recording is `Degraded`; 2 on a parse exception [IngestCommand.cs:Run].
- `InferCommand` (`infer <log> [-o] [--module-name] [--namespace] [--game-path]`): `Run(logPath, outputPath, moduleName, namespaceName, gamePath, stdout, stderr, IGameData? gameData = null)` [InferCommand.cs]. Exit 0; 2 if the model has a High review item; 3 schema-invalid; 4 error.
- `EmitCommand` (`emit <em.json> --flavor vbm|bmr [-o] [--flavors]`): `Run(emPath, flavorName, outputDir, flavorsDir, stdout, stderr)` [EmitCommand.cs]. Exit 0; 2 High review; 3 schema-invalid; 4 error.
- `ValidateCommand` (`validate --em --recordings... [--out] [--write-em] [--bossmod] [--flavor]`): `Run(emPath, recordings, outDir, writeEm, bossmodPath, stdout, stderr, ValidationOptions? options = null, string? flavorName = null)` [ValidateCommand.cs]. Exit 0 passed; 3 failed; 4 setup/IO error.
- `MergeCommand` (`merge <logs...> --out [--em] [--select-boss <oid>]`): `Run(logPaths, outputPath, emPath, selectBoss, stdout, stderr)` [MergeCommand.cs]. Exit 0; 2 High review; 3 schema-invalid; 4 error.
- `PolishCommand` (`polish <em.json> --provider file|http [--patches] [--endpoint] [--model] [--api-key-env] [--out]`): `Run(emPath, providerName, patchesPath, endpoint, model, apiKeyEnv, outPath, stdout, stderr)`; `AllowlistText` (internal const) [PolishCommand.cs]. Exit 0; 3 invalid input/output model; 4 error.
- `ConformCommand` (`conform <logs...> [--bossmod-dll] [--json] [--selftest]`): `Run(logPaths, bossModDllPath, json, selftest, stdout, stderr)` [ConformCommand.cs]. Exit 0 match; 3 any diff; 4 error.
- `IngestCommand.EncodingName(ReplayEncoding)` is `internal` [IngestCommand.cs].

## Invariants
- `NotImplementedException` is rethrown (not mapped to an exit code) in `InferCommand` and `MergeCommand` [InferCommand.cs:Run, MergeCommand.cs:Run].
- `emit` and `polish` validate input JSON via `EncounterModelJson.Validate` (exit 3 on errors); `validate` returns 4 on the same errors [EmitCommand.cs:Run, PolishCommand.cs:Run, ValidateCommand.cs:Run].
- `infer`/`merge` validate the serialized model before writing it [InferCommand.cs:Run, MergeCommand.cs:Run]; `polish` validates the polished model before output [PolishCommand.cs:Run].
- `polish` accepts `--provider` exactly `file` or `http` (ordinal); `file` requires `--patches`, `http` requires `--endpoint`; `--api-key-env` set but empty variable -> exit 4 [PolishCommand.cs:Run]. `AllowlistText` limits paths to abilities/participants enumName/name/comment and review resolution [PolishCommand.cs:AllowlistText].
- `ValidateCommand` with `--bossmod` checks checkout shape first (exit 4 on failure); bossmod build failure (3) marks the report `Passed = false` and adds a failure message [ValidateCommand.cs:Run]. `--flavor` accepts only `vbm`/`bmr` (case-insensitive), default vbm [ValidateCommand.cs:RunBossmodBuild].
- `InferCommand` warns on stderr and falls back to `NullGameData.Instance` for an unreadable `--game-path` [InferCommand.cs:Run].
- `ConformCommand` requires `--bossmod-dll` (also for `--selftest`) else exit 4 [ConformCommand.cs:Run].
- `MergeCommand` requires `--out` and at least one log; `--em` only carries over existing `Overrides` when non-empty [MergeCommand.cs:Run].

## Side Effects
- `emit`: creates the output dir and writes each emitted file plus `FORGE.md` [EmitCommand.cs:Run].
- `infer`: writes JSON to `-o` or stdout [InferCommand.cs:Run]; may open game data via Lumina [InferCommand.cs:Run].
- `merge`: writes the merged EM to `--out` [MergeCommand.cs:Run].
- `polish`: reads em and patch files; `http` provider does a network POST and reads an env var; writes `--out` or stdout [PolishCommand.cs:Run].
- `validate`: reads em and logs; writes `validation-report.json` and `validation-report.md` to the output dir (default cwd) and the markdown to stdout; `--write-em` overwrites the input EM file; `--bossmod` creates a temp dir `forge-bossmod-module-*` (deleted in `finally`) and triggers builds/process spawns in the checkout via `BossmodBuildValidator` [ValidateCommand.cs:Run, RunBossmodBuild].
- `conform`: loads BossMod.dll and parses logs through it [ConformCommand.cs:Run].
- `ingest`: reads logs, writes to stdout/stderr only [IngestCommand.cs:Run].
- `merge`: also writes `merged N recordings, P pulls, S sources -> <out>` to stdout on success [MergeCommand.cs:Run].
- `polish`: after the model output, writes `accepted: <item>` / `rejected: <item>` lines and `polish: X accepted, Y rejected` (plus ` -> <out>` when `--out` is set) to stdout; without `--out` the JSON and these status lines share stdout [PolishCommand.cs:Run].
- `infer`: disposes the Lumina game data (`lumina?.Dispose()`) only on the success path; an exception in the pipeline skips it [InferCommand.cs:Run].

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
