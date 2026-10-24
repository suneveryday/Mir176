#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
for task_test in gm-level actor-message-order; do
  task_out="$task_root/.build/tests/$task_test"
  task_exe=GmLevel
  if [[ "$task_test" == actor-message-order ]]; then task_exe=ActorMessageOrder; fi
  dotnet build "$task_root/tests/$task_test/$task_exe.csproj" -c Release -p:NuGetAudit=false -p:UseSharedCompilation=false -o "$task_out"
  cp "$task_root"/.build/server/GameSrv/*.dll "$task_out/"
  task_exe=GmLevel
  if [[ "$task_test" == actor-message-order ]]; then task_exe=ActorMessageOrder; fi
  dotnet "$task_out/$task_exe.dll"
done

task_out="$task_root/.build/tests/classic-content"
dotnet build "$task_root/tests/classic-content/ClassicRuntime.csproj" -c Release -p:NuGetAudit=false -o "$task_out"
cp "$task_root"/.build/server/GameSrv/*.dll "$task_out/"
dotnet "$task_out/ClassicRuntime.dll" "$task_root/.build/content/GameSrv/classic-policy.json"
