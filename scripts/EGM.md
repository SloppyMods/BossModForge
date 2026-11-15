# scripts

## Overview
Two build helper scripts: `build.sh` (Linux/bash: build and test `BossModForge.slnx`) and `build-plugin.ps1` (PowerShell: build `BossModForge.Plugin.slnx` after checking for Dalamud).

## Interface
- `build.sh` steps: `set -euo pipefail`; prepend `${HOME}/.dotnet` to `PATH`; export `DOTNET_ROOT=${HOME}/.dotnet`; `cd` to the parent of the script's directory; `dotnet build BossModForge.slnx -c Release`; `dotnet test BossModForge.slnx -c Release --no-build` [build.sh]
- `build-plugin.ps1` steps: set `$ErrorActionPreference='Stop'`; `$repo` = parent of `$PSScriptRoot`; `$dalamud` = `$env:APPDATA\XIVLauncher\addon\Hooks\dev\Dalamud.dll`; if missing, `Write-Error` with install message and `exit 1`; else `dotnet build <repo>\BossModForge.Plugin.slnx -c Release` and `exit $LASTEXITCODE` [build-plugin.ps1]
- Subdirectories: None.

## Invariants
- `build.sh` aborts on any command error, unset variable or pipe failure; `dotnet test` relies on the preceding build (`--no-build`) [build.sh].
- `build.sh` works from any caller directory because it resolves the repo root from `$0` [build.sh].
- `build-plugin.ps1` never builds when `Dalamud.dll` is absent at the hardcoded path [build-plugin.ps1].
- The `.ps1` exits with the exit code of `dotnet build` [build-plugin.ps1].

## Side Effects
- `build.sh`: modifies process `PATH` and `DOTNET_ROOT`; changes working directory; runs dotnet build (writes bin/obj output) and tests.
- `build-plugin.ps1`: reads `$env:APPDATA` and tests file existence of `Dalamud.dll`; runs dotnet build (writes build output); may write an error and exit 1.

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repository root)
