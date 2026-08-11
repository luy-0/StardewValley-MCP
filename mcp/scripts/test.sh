#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
mcp_root="$(cd "$script_dir/.." && pwd -P)"
uv_bin="${UV_BIN:-uv}"

if ! command -v "$uv_bin" >/dev/null 2>&1; then
  echo "缺少 uv：https://docs.astral.sh/uv/" >&2
  exit 1
fi

"$uv_bin" sync --project "$mcp_root" --locked --extra dev
"$uv_bin" run --project "$mcp_root" pytest
"$uv_bin" build --project "$mcp_root"
"$uv_bin" run --project "$mcp_root" python "$mcp_root/scripts/check_distribution.py"
echo "mcp_build_ok"
