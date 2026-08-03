# Agent 安装指南

本指南供自动化 Agent 从源码安装 Stardew Valley MCP。完成后，Agent 应能够通过 MCP 客户端读取当前游戏状态，并在用户授权后执行一次不会消耗资源的朝向操作。

## 一、安装前确认

需要准备：

- Stardew Valley 1.6；
- SMAPI 4.1.0 或更高版本；
- Git；
- .NET 6 SDK；
- Python 3.11 或更高版本；
- [`uv`](https://docs.astral.sh/uv/)；
- 支持本地 stdio MCP Server 的客户端。

开始前遵守以下安全要求：

- 默认以只读方式启动 MCP；只有用户明确允许操作游戏时才使用 `--allow-write`。
- `SharedSecretBase64` 只能进入本机 MCP 进程环境或客户端的秘密存储，不得提交到 Git 或显示在回复、截图和日志中。
- 不上传或直接编辑存档。首次操作使用 `face`，避免消耗物品、金钱或体力。

## 二、取得源码

克隆仓库后进入仓库根目录。后续命令除非特别说明，均从仓库根目录执行。

如果需要先确认源码、契约与自动化测试一致，可以运行：

```bash
./scripts/verify.sh
```

## 三、构建并安装 Mod

### macOS

Steam 常见游戏目录如下：

```bash
export STARDEW_VALLEY_GAME_PATH="$HOME/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS"
./mod/scripts/build.sh --deploy
```

### Windows

安装 Git for Windows 后，在 Git Bash 中执行：

```bash
export STARDEW_VALLEY_GAME_PATH='C:/Program Files (x86)/Steam/steamapps/common/Stardew Valley'
./mod/scripts/build.sh --deploy
```

也可以先在 PowerShell 中设置路径，再打开 Git Bash：

```powershell
$env:STARDEW_VALLEY_GAME_PATH = "C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley"
```

非 Steam 安装应把 `STARDEW_VALLEY_GAME_PATH` 改为同时包含 `Stardew Valley.dll` 与 `StardewModdingAPI.dll` 的实际游戏目录。构建脚本会恢复锁定依赖、运行 Protocol 与 Mod 测试、构建 Release 版本，并安装到 `Mods/StardewValleyMCP/`。

只需要生成安装包、不直接写入游戏目录时执行：

```bash
./mod/scripts/build.sh --package
```

## 四、启动游戏

1. 通过 SMAPI 启动 Stardew Valley。
2. 加载一个存档并等待玩家可以操作。
3. 确认 `Mods/StardewValleyMCP/config.json` 已生成。
4. 从该配置中读取 `Host`、`Port` 和 `SharedSecretBase64`，通过客户端的秘密管理能力传给 MCP。

游戏停留在标题界面时，查询可能返回 `not_ready`；加载存档后再重试即可。

## 五、安装 MCP 服务端

从仓库根目录执行：

```bash
uv tool install ./mcp
stardew-valley-mcp doctor
```

成功时会输出类似内容：

```text
doctor_ok package=0.1.0a1 protocol=stardew_valley.mcp.v1
```

需要覆盖已安装的同版本时执行：

```bash
uv tool install --force ./mcp
```

如果桌面 MCP 客户端找不到命令，执行 `uv tool dir --bin` 取得 Tool 可执行目录，并把其中 `stardew-valley-mcp` 的完整路径填入客户端 `command`。

## 六、配置 MCP 客户端

先使用只读配置：

```json
{
  "mcpServers": {
    "stardew-valley": {
      "command": "stardew-valley-mcp",
      "args": ["serve"],
      "env": {
        "STARDEW_VALLEY_MCP_HOST": "127.0.0.1",
        "STARDEW_VALLEY_MCP_PORT": "24642",
        "STARDEW_VALLEY_MCP_SHARED_SECRET": "<从 Mod 配置安全注入>"
      }
    }
  }
}
```

如果 Mod 使用了其他端口，同步修改 `STARDEW_VALLEY_MCP_PORT`。只有用户明确允许操作游戏时，才把 `args` 改为：

```json
["serve", "--allow-write"]
```

## 七、验证第一次读取

调用：

```text
stardew_query_runtime {}
```

成功结果应满足：

- `status` 为 `succeeded`；
- 返回当前日期、时间、地图、坐标和玩家状态；
- 结果中没有共享秘密、配置内容或本机路径。

## 八、验证第一次操作

1. 获得用户授权后，以 `--allow-write` 启动 MCP Server。
2. 调用 `stardew_query_runtime` 读取当前 `facing`。
3. 选择一个不同方向，例如：

```text
stardew_face {"direction":"left"}
```

4. 再次调用 `stardew_query_runtime`，确认 `facing` 已变为请求方向。

动作调用返回成功后仍应执行后置查询；后置查询才是游戏状态已经改变的确认。

## 九、常见故障

- `stardew-valley-mcp` 不存在：执行 `uv tool dir --bin`，确认 Tool 可执行目录已经加入 `PATH`，然后重新安装。
- Tool 列表为空：确认游戏通过 SMAPI 启动、Mod 已加载，并重启 MCP 客户端连接。
- `not_ready`：进入存档并等待玩家可以操作后重试。
- `unauthenticated`：重新读取当前 Mod 配置中的共享秘密，在本机更新 MCP 环境变量并重启连接。
- `connection_failed` 或连接被拒绝：检查 Mod 是否监听配置端口，以及端口是否被其他进程占用。
- Mod 构建找不到游戏：重新设置 `STARDEW_VALLEY_GAME_PATH`，确认目录中存在游戏与 SMAPI DLL。
- Mod DLL 更新后：重新构建并重启游戏；只有 MCP Python 代码变化时不需要重启游戏。

## 十、完成检查

安装完成时应能确认：Mod 已由 SMAPI 加载、`doctor` 通过、只读查询成功，以及用户授权后的 `face` 操作通过后置查询确认。任何输出都不应包含共享秘密或完整配置文件。
