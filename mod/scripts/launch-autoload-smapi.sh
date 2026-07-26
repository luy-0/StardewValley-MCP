#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
用法：./mod/scripts/launch-autoload-smapi.sh --save <存档目录名> [--port <端口>] [--timeout <秒>]

启动一个只加载当前构建产物的独立 SMAPI 进程，并等待 Mod 自动进入指定存档。
脚本不会终止或复用任何已经运行的游戏进程。
USAGE
}

save_folder=""
port=""
timeout_seconds=240
while (($# > 0)); do
  case "$1" in
    --save)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      save_folder="$2"
      shift 2
      ;;
    --port)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      port="$2"
      shift 2
      ;;
    --timeout)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      timeout_seconds="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "未知参数：$1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

[[ -n "$save_folder" ]] || { echo "必须通过 --save 指定存档目录名。" >&2; exit 2; }
[[ "$timeout_seconds" =~ ^[0-9]+$ ]] && ((timeout_seconds >= 30 && timeout_seconds <= 600)) || {
  echo "--timeout 必须是 30..600 之间的整数。" >&2
  exit 2
}
case "$save_folder" in
  *'"'*|*'\'*|*$'\n'*|*$'\r'*)
    echo "存档目录名不能包含引号、反斜杠或换行符。" >&2
    exit 2
    ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
mod_root="$(cd "$script_dir/.." && pwd -P)"
build_dir="$mod_root/src/StardewValleyMcp.Mod/bin/Release/net6.0"
game_path="${STARDEW_VALLEY_GAME_PATH:-$HOME/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS}"
save_root="${STARDEW_VALLEY_SAVE_PATH:-$HOME/.config/StardewValley/Saves}"

[[ -x "$game_path/StardewModdingAPI" ]] || {
  echo "找不到 StardewModdingAPI，请设置 STARDEW_VALLEY_GAME_PATH。" >&2
  exit 1
}
[[ -d "$save_root/$save_folder" ]] || {
  echo "找不到存档目录：$save_root/$save_folder" >&2
  exit 1
}

artifacts=(
  manifest.json
  StardewValleyMcp.Mod.dll
  StardewValleyMcp.Protocol.dll
  Google.Protobuf.dll
)
for artifact in "${artifacts[@]}"; do
  [[ -f "$build_dir/$artifact" ]] || {
    echo "缺少构建产物 $artifact，请先运行 ./mod/scripts/build.sh。" >&2
    exit 1
  }
done

if [[ -n "$port" ]]; then
  [[ "$port" =~ ^[0-9]+$ ]] && ((port >= 1024 && port <= 65535)) || {
    echo "--port 必须是 1024..65535 之间的整数。" >&2
    exit 2
  }
  if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "端口 $port 已被占用。" >&2
    exit 1
  fi
else
  for candidate in $(seq 24642 24742); do
    if ! lsof -nP -iTCP:"$candidate" -sTCP:LISTEN >/dev/null 2>&1; then
      port="$candidate"
      break
    fi
  done
  [[ -n "$port" ]] || { echo "找不到可用的本地协议端口。" >&2; exit 1; }
fi

runtime_base="${TMPDIR:-/tmp}"
runtime_dir="$(mktemp -d "$runtime_base/stardew-mcp-autoload.XXXXXX")"
mods_dir="$runtime_dir/Mods/StardewValleyMCP"
log_path="$runtime_dir/smapi-console.log"
pid_path="$runtime_dir/smapi.pid"
runner_path="$runtime_dir/run-smapi.command"
mkdir -p "$mods_dir"
for artifact in "${artifacts[@]}"; do
  install -m 0644 "$build_dir/$artifact" "$mods_dir/$artifact"
done

cat > "$mods_dir/config.json" <<CONFIG
{
  "Host": "127.0.0.1",
  "Port": $port,
  "SharedSecretBase64": "",
  "AutoLoadSave": true,
  "AutoLoadSaveName": "$save_folder",
  "AutoLoadTimeoutSeconds": $timeout_seconds
}
CONFIG

escaped_runtime_dir="$(printf '%q' "$runtime_dir")"
escaped_game_path="$(printf '%q' "$game_path")"
cat > "$runner_path" <<RUNNER
#!/usr/bin/env bash
set -euo pipefail
runtime_dir=$escaped_runtime_dir
game_path=$escaped_game_path
cd "\$game_path"
./StardewModdingAPI --mods-path "\$runtime_dir/Mods" >"\$runtime_dir/smapi-console.log" 2>&1 &
game_pid=\$!
printf '%s\n' "\$game_pid" > "\$runtime_dir/smapi.pid"
echo "隔离 SMAPI PID：\$game_pid"
wait "\$game_pid"
RUNNER
chmod 0700 "$runner_path"
/usr/bin/open -a Terminal "$runner_path"

for _ in {1..30}; do
  [[ -s "$pid_path" ]] && break
  sleep 1
done
[[ -s "$pid_path" ]] || {
  echo "Terminal 已打开，但未在 30 秒内写入新 SMAPI PID。" >&2
  exit 1
}
game_pid="$(tr -dc '0-9' < "$pid_path")"
deadline=$((SECONDS + timeout_seconds))

printf 'runtime_dir=%s\npid=%s\nport=%s\nlog=%s\n' "$runtime_dir" "$game_pid" "$port" "$log_path"

# 游戏在 macOS 上必须成为前台应用才能完成早期初始化。这里只聚焦本脚本刚创建的 PID。
while ! grep -Fq "Mods loaded and ready!" "$log_path" 2>/dev/null; do
  kill -0 "$game_pid" 2>/dev/null || {
    echo "新游戏进程在 Mod 加载前退出。" >&2
    tail -80 "$log_path" >&2 || true
    exit 1
  }
  ((SECONDS < deadline)) || {
    echo "等待 Mod 加载超时；进程仍保留，未自动终止。" >&2
    tail -80 "$log_path" >&2 || true
    exit 1
  }
  osascript -e "tell application \"System Events\" to set frontmost of first application process whose unix id is $game_pid to true" >/dev/null 2>&1 || true
  sleep 2
done

success_marker="[AutoLoad] 自动加载完成：'$save_folder'"
while ! grep -Fq "$success_marker" "$log_path" 2>/dev/null; do
  kill -0 "$game_pid" 2>/dev/null || {
    echo "新游戏进程在存档加载完成前退出。" >&2
    tail -100 "$log_path" >&2 || true
    exit 1
  }
  ((SECONDS < deadline)) || {
    echo "等待存档加载超时；进程仍保留，未自动终止。" >&2
    tail -100 "$log_path" >&2 || true
    exit 1
  }
  sleep 2
done

grep -F "$success_marker" "$log_path" | tail -1
echo "autoload_ok pid=$game_pid save=$save_folder runtime_dir=$runtime_dir"
