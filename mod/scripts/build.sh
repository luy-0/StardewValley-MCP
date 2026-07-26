#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
mod_root="$(cd "$script_dir/.." && pwd -P)"
solution="$mod_root/StardewValleyMcp.sln"
project="$mod_root/src/StardewValleyMcp.Mod/StardewValleyMcp.Mod.csproj"
tests="$mod_root/tests/StardewValleyMcp.Protocol.Tests/StardewValleyMcp.Protocol.Tests.csproj"

deploy=false
package=false
for argument in "$@"; do
  case "$argument" in
    --deploy) deploy=true ;;
    --package) package=true ;;
    *) echo "未知参数: $argument" >&2; exit 2 ;;
  esac
done

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
dotnet build "$project" --no-restore -c Release \
  -p:GamePath="$game_path" \
  -p:EnableModDeploy="$deploy" \
  -p:EnableModZip="$package" \
  -p:RestoreLockedMode=true

echo "mod_build_ok deploy=$deploy package=$package"
