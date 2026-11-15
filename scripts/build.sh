#!/usr/bin/env bash
set -euo pipefail
export PATH="${HOME}/.dotnet:${PATH}"
export DOTNET_ROOT="${HOME}/.dotnet"
cd "$(dirname "$0")/.."
dotnet build BossModForge.slnx -c Release
dotnet test BossModForge.slnx -c Release --no-build
