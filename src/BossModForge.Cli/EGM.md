# BossModForge.Cli

## Overview
`forge` command-line entry point. `Program.Main` builds a `System.CommandLine` `RootCommand` and registers the seven subcommands (`ingest`, `infer`, `emit`, `validate`, `merge`, `polish`, `conform`), then parses and invokes args. All pipeline logic lives in `BossModForge.Core`; this project adds CLI plumbing, Lumina game data, HTTP/file polish providers, BossMod reflection and in-checkout build validation.

## Local Interface
- `Program.Main(string[] args) -> int` returns the invoked command's exit code [Program.cs:Main]

## Local Invariants
- Subcommands registered: Ingest, Infer, Emit, Validate, Merge, Polish, Conform (each via `Commands.<X>Command.Create()`) [Program.cs:Main].
- Project is an `Exe` named `forge` (`AssemblyName`), targets `net10.0`, references Core plus `Lumina` 5.8.0, `Lumina.Excel` 7.2.3 and `System.CommandLine` 2.0.12; `flavors/*.json` from the repo root are copied to the output as `flavors/`; `ReplayValidator/*.cs` is excluded from compilation and `ForgeReplayValidator.csproj` and `Program.cs` are embedded as resources [BossModForge.Cli.csproj].

## Local Side Effects
- None directly in `Program.cs` (side effects live in the subdirectories).

## Subdirectories & Boundaries
- Commands: [./Commands/EGM.md](./Commands/EGM.md) - one class per subcommand with `Create()`/`Run()` and exit-code contracts.
- Polish: [./Polish/EGM.md](./Polish/EGM.md) - file and HTTP patch providers and patch JSON parser.
- Conformance: [./Conformance/EGM.md](./Conformance/EGM.md) - reflection over BossMod.dll and tag-count comparison.
- GameData: [./GameData/EGM.md](./GameData/EGM.md) - Lumina-backed `IGameData`.
- Validation: [./Validation/EGM.md](./Validation/EGM.md) - in-checkout build/host validation and compile-error mapping.
- ReplayValidator: [./ReplayValidator/EGM.md](./ReplayValidator/EGM.md) - template host program embedded and deployed into a BossMod checkout.

## Global Invariants
- Commands return numeric exit codes from their `Run` methods; Program passes through the parse/invoke result [Program.cs:Main; Commands/EGM.md].
- `Conformance` is the only place that reflects over BossMod.dll; per its comment, Core stays free of BossMod [Conformance/EGM.md].

## Global Side Effects
- File reads/writes of logs, EM JSON and reports; optional network POST (polish http); process spawns (`dotnet build`/`run`) in a BossMod checkout; loading BossMod.dll and a local FFXIV install (see child contracts).

## System Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
