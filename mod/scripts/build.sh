#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
用法：./mod/scripts/build.sh [--deploy] [--package]

恢复锁定依赖，运行 Protocol 与 Mod 测试，然后构建 Release 版本。
  --deploy   把 Mod 安装到游戏目录的 Mods/StardewValleyMCP/
  --package  生成包含许可证文件的 Mod ZIP
  -h, --help 显示本帮助

默认不写入游戏目录。非标准位置通过 STARDEW_VALLEY_GAME_PATH 指定。
依赖：.NET 6 SDK、Stardew Valley 1.6 与 SMAPI 4.1.0 或更高版本。
USAGE
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
mod_root="$(cd "$script_dir/.." && pwd -P)"
solution="$mod_root/StardewValleyMcp.sln"
project="$mod_root/src/StardewValleyMcp.Mod/StardewValleyMcp.Mod.csproj"
tests="$mod_root/tests/StardewValleyMcp.Protocol.Tests/StardewValleyMcp.Protocol.Tests.csproj"
mod_tests="$mod_root/tests/StardewValleyMcp.Mod.Tests/StardewValleyMcp.Mod.Tests.csproj"

deploy=false
package=false
for argument in "$@"; do
  case "$argument" in
    --deploy) deploy=true ;;
    --package) package=true ;;
    -h|--help) usage; exit 0 ;;
    *) echo "未知参数: $argument" >&2; usage >&2; exit 2 ;;
  esac
done

if ! command -v dotnet >/dev/null 2>&1; then
  echo "缺少 .NET 6 SDK：https://dotnet.microsoft.com/download/dotnet/6.0" >&2
  exit 1
fi

game_path="${STARDEW_VALLEY_GAME_PATH:-}"
if [[ -z "$game_path" && "${OSTYPE:-}" == darwin* ]]; then
  game_path="$HOME/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS"
elif [[ -z "$game_path" && "${OSTYPE:-}" == linux* ]]; then
  game_path="$HOME/.steam/steam/steamapps/common/Stardew Valley"
fi

if [[ ! -f "$game_path/Stardew Valley.dll" || ! -f "$game_path/StardewModdingAPI.dll" ]]; then
  echo "找不到 Stardew Valley + SMAPI。请设置 STARDEW_VALLEY_GAME_PATH。" >&2
  exit 1
fi

if [[ "$deploy" == true ]]; then
  stale_protocol_pdb="$game_path/Mods/StardewValleyMCP/StardewValleyMcp.Protocol.pdb"
  if [[ -f "$stale_protocol_pdb" ]]; then
    rm "$stale_protocol_pdb"
  fi
fi

dotnet restore "$solution" -p:RestoreLockedMode=true
dotnet clean "$solution" -c Release -p:GamePath="$game_path"
dotnet test "$tests" --no-restore -c Release -p:RestoreLockedMode=true
dotnet test "$mod_tests" --no-restore -c Release \
  -p:GamePath="$game_path" \
  -p:RestoreLockedMode=true
dotnet build "$project" --no-restore -c Release \
  -p:GamePath="$game_path" \
  -p:EnableModDeploy="$deploy" \
  -p:EnableModZip="$package" \
  -p:RestoreLockedMode=true

echo "mod_build_ok deploy=$deploy package=$package"
