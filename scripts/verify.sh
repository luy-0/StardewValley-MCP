#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
uv_bin="${UV_BIN:-uv}"
with_mod=false

for argument in "$@"; do
  case "$argument" in
    --with-mod) with_mod=true ;;
    *) echo "未知参数: $argument" >&2; exit 2 ;;
  esac
done

if ! command -v "$uv_bin" >/dev/null 2>&1; then
  echo "缺少 uv：https://docs.astral.sh/uv/" >&2
  exit 1
fi

"$uv_bin" sync --project "$repo_root/mcp" --locked --extra dev
"$uv_bin" run --project "$repo_root/mcp" python "$repo_root/scripts/generate_protocol.py" --check
"$uv_bin" run --project "$repo_root/mcp" python "$repo_root/spec/conformance/verify.py"
"$uv_bin" run --project "$repo_root/mcp" python "$repo_root/spec/conformance/transport-spike/run_spike.py"
"$uv_bin" run --project "$repo_root/mcp" python "$repo_root/scripts/check_public_boundaries.py"
dotnet test "$repo_root/mod/tests/StardewValleyMcp.Protocol.Tests/StardewValleyMcp.Protocol.Tests.csproj" \
  --configuration Release \
  -p:RestoreLockedMode=true
UV_BIN="$uv_bin" "$repo_root/mcp/scripts/test.sh"

if [[ "$with_mod" == true ]]; then
  "$repo_root/mod/scripts/build.sh" --package
fi

audit_arguments=()
if [[ "$with_mod" == true ]]; then
  audit_arguments+=(--with-mod)
fi
"$uv_bin" run --project "$repo_root/mcp" python "$repo_root/scripts/audit_packages.py" "${audit_arguments[@]}"

echo "repository_verify_ok with_mod=$with_mod"
