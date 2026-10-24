#!/usr/bin/env bash
set -euo pipefail
if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo 'Usage: run.sh <old-gateway-bin> <candidate-gateway-bin> [output-directory]' >&2
  exit 2
fi
task_root="$(cd "$(dirname "$0")/../.." && pwd)"
task_old="$(cd "$1" && pwd)"
task_new="$(cd "$2" && pwd)"
task_output="${3:-$task_root/.build/gateway-login-tests}"
mkdir -p "$task_output"
task_output="$(cd "$task_output" && pwd)"
for task_variant in baseline candidate; do
  task_input="$task_old"
  if [[ "$task_variant" == candidate ]]; then task_input="$task_new"; fi
  dotnet build "$task_root/tests/gateway-login/GatewayLogin.csproj" \
    -p:ServerBin="$task_input" -p:NuGetAudit=false -p:UseSharedCompilation=false \
    -o "$task_output/$task_variant"
  cp "$task_input"/*.dll "$task_output/$task_variant/"
  if [[ "$task_variant" == baseline ]]; then
    dotnet "$task_output/$task_variant/GatewayLogin.dll" --expect-old
  else
    dotnet "$task_output/$task_variant/GatewayLogin.dll"
  fi
done
dotnet build "$task_root/tests/gateway-login/Abi.csproj" \
  -p:NuGetAudit=false -p:UseSharedCompilation=false -o "$task_output/abi"
dotnet "$task_output/abi/Abi.dll" "$task_old" "$task_new"
