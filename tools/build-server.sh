#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
task_output="${1:-$task_root/.build/server}"
for task_project in LoginSrv DBSrv GameSrv LoginGate SelGate GameGate Storeages/DBSvr.Storage.MySQL; do
  dotnet build "$task_root/server/src/$task_project/$(basename "$task_project").csproj" \
    -c Release -t:Rebuild -p:Mode=Dev -p:UseSharedCompilation=false \
    -p:PublishTrimmed=false -p:PublishSingleFile=false \
    -o "$task_output/$(basename "$task_project")"
done
