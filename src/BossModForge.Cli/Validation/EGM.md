# Validation

## Overview
CLI-side in-checkout validation for the `validate --bossmod` path: copies an emitted module into a BossMod checkout, runs `dotnet build`, then runs the embedded ReplayValidator host; build output is mapped back to Encounter Model targets.

## Interface
- `BossmodBuildValidator.DefaultScratchNamespace = "BossMod.ForgeScratch"` [BossmodBuildValidator.cs]
- `BossmodBuildValidator.ValidateCheckoutShape(string bossmodCheckout)` [BossmodBuildValidator.cs:ValidateCheckoutShape]
- `BossmodBuildValidator.Run(string moduleDir, string bossmodCheckout, EncounterModel em, IReadOnlyList<string> recordings, string scratchNamespace = Default) -> BossmodBuildResult` [BossmodBuildValidator.cs:Run]
- `record BossmodBuildResult(bool BuildSucceeded, ImmutableArray<CompileErrorItem> Errors, string? ValidatorHostOutput)` [BossmodBuildValidator.cs]
- `CompileErrorMapper.Map(string buildOutput, EncounterModel em) -> ImmutableArray<CompileErrorItem>` [CompileErrorMapper.cs:Map]
- `record CompileErrorItem(string File, int Line, string Code, string Message, string? EmTarget)` [CompileErrorMapper.cs]

## Invariants
- `ValidateCheckoutShape` throws `InvalidOperationException` unless the directory exists, has a `BossMod/` subdirectory, and a `.sln`/`.slnx` at its root (first in ordinal order) [BossmodBuildValidator.cs:ValidateCheckoutShape, FindSolutionFile].
- `Run` rethrows every `InvalidOperationException`, not only the shape-check one: the catch is `catch (Exception ex) when (ex is not InvalidOperationException)`, so timeout, "failed to start" and missing-resource errors raised inside the try also propagate (callers such as `ValidateCommand.RunBossmodBuild` catch them and exit 4); any other exception is caught and returned as a failed result with code `FORGE` [BossmodBuildValidator.cs:Run]. Build failure with no mappable errors yields a synthetic `BUILD` item; host non-zero exit yields `FORGE-HOST` with the last output line [BossmodBuildValidator.cs:Run].
- Timeouts: build 600 000 ms, host 300 000 ms; on timeout the process tree is killed and `InvalidOperationException` thrown [BossmodBuildValidator.cs:RunProcess].
- Module files are copied to `<checkout>/BossMod/Modules/<last namespace segment>/<ModuleName>` in ordinal order, overwriting [BossmodBuildValidator.cs:Run].
- Embedded resources `BossModForge.Cli.ReplayValidator.ForgeReplayValidator.csproj` and `...Program.cs` must exist, else `InvalidOperationException`; CRLF is normalized to LF on write [BossmodBuildValidator.cs:ExtractResource].
- `Map` parses lines matching `file(line,col): error CSxxxx: msg`; `EmTarget` is only set for files whose path contains `ForgeScratch` [CompileErrorMapper.cs:Map, IsScratchFile]. Target rules: constructor errors or `CS1729` -> `flavor-profile`; `OID.` prefix -> `participant:<id>`; `SID`/`TETHERID`/`ICONID` prefix -> null; otherwise ability (`ability:<id>`) then participant match [CompileErrorMapper.cs:MapTarget]. Name tokens are extracted with three regexes: `The name '<tok>'`, `does not contain a definition for '<tok>'`, and `'<type>' does not contain a constructor` [CompileErrorMapper.cs:NameToken, CompileErrorMapper.cs:DefinitionToken, CompileErrorMapper.cs:CtorToken]. Matching: prefix match with min length 2, else Levenshtein <= 3, candidates ordered by name ordinal [CompileErrorMapper.cs:IsMatch, MatchEnum].

## Side Effects
- Creates/overwrites files under the BossMod checkout: scratch module dir and `<checkout>/ForgeReplayValidator/{ForgeReplayValidator.csproj,Program.cs}` [BossmodBuildValidator.cs:Run, ExtractValidatorHost].
- Spawns `dotnet build "<solution>" -c Release` and `dotnet run --project <hostDir> -c Release -- <moduleType> <recordings...>` with the checkout as working directory, capturing stdout+stderr [BossmodBuildValidator.cs:Run, RunProcess].
- `CompileErrorMapper`: None.

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
