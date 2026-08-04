# 快速开始

当前预览版提供二十项公共 Tool：五项观察能力和十五项需要明确授权的变更能力，其中 `transfer_inventory_item` 用于当前受支持箱子菜单中的单项原子转移，`set_equipment_slot` 用于当前原版背包页面中的穿戴、替换与取下，`move_inventory_item` 用于玩家背包页内的整件移动或交换，`craft_item` 与 `purchase_shop_item` 分别负责按当前 UI Ref 制作和购买。MCP 默认只暴露只读能力；只有显式加入 `--allow-write`，才会暴露当前 Mod 同时公告的操作能力。

当前采用源码优先发布方式，不提供预编译 Mod 或安装器。Agent 从源码自动安装时，请使用根目录的 [`AGENT-GUIDE.md`](../AGENT-GUIDE.md)。

## 一、前置条件

- Stardew Valley 1.6；
- SMAPI 4.1.0 或更高版本；
- Git 与 .NET 6 SDK；
- Python 3.11 或更高版本；
- [`uv`](https://docs.astral.sh/uv/)；
- 支持本地 stdio MCP Server 的客户端。

构建 Mod 的游戏目录必须同时包含 `Stardew Valley.dll` 和 `StardewModdingAPI.dll`。
Mod 产物必须继续以 `net6.0` 匹配游戏宿主；CI 使用受支持的新版 SDK 构建，但仍用 .NET 6 Runtime 运行兼容性测试。原因与升级门槛见 [.NET 构建工具与游戏宿主兼容性](runtime-compatibility.md)。

## 二、安装 Mod

### macOS

Steam 默认位置可以直接使用构建脚本的自动发现；也可以显式设置：

```bash
export STARDEW_VALLEY_GAME_PATH="$HOME/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS"
./mod/scripts/build.sh --deploy
```

脚本会恢复锁定依赖、运行 Mod 与 Protocol 测试、构建 Release 版本，并安装到游戏的 `Mods/StardewValleyMCP/`。

### Windows

安装 Git for Windows 后，在 Git Bash 中执行：

```bash
export STARDEW_VALLEY_GAME_PATH='C:/Program Files (x86)/Steam/steamapps/common/Stardew Valley'
./mod/scripts/build.sh --deploy
```

如果游戏不在 Steam 默认目录，请替换环境变量。PowerShell 用户也可以把相同目录设置为 `$env:STARDEW_VALLEY_GAME_PATH`，再通过 Git Bash 调用构建脚本。

### 只生成 Mod ZIP

不直接安装时使用：

```bash
./mod/scripts/build.sh --package
```

生成的 ZIP 包含 Mod、Protocol、`Google.Protobuf.dll`、项目许可证及第三方许可证声明。构建脚本默认不会修改游戏目录，只有 `--deploy` 会执行安装。

## 三、启动游戏

通过 SMAPI 启动游戏并加载一个存档。Mod 第一次加载时会在 `Mods/StardewValleyMCP/config.json` 中：

- 默认监听 `127.0.0.1:24642`；
- 生成随机 `SharedSecretBase64`；
- 保持自动存档加载功能关闭。

共享秘密只应进入本机 MCP 进程环境或客户端秘密存储。不要把完整 `config.json`、共享秘密或本机绝对路径提交到仓库、粘贴到聊天或写入日志。

## 四、安装 MCP 服务端

从仓库根目录安装命令行工具：

```bash
uv tool install ./mcp
stardew-valley-mcp doctor
```

如果桌面 MCP 客户端找不到命令，执行 `uv tool dir --bin` 查看 Tool 可执行目录，并在客户端 `command` 中使用其中 `stardew-valley-mcp` 的完整本机路径；不要把该路径提交到仓库。

`doctor` 只检查 Python 包和协议生成物，不连接游戏。需要覆盖已经安装的同版本工作树时执行：

```bash
uv tool install --force ./mcp
```

开发者也可以不做全局 Tool 安装，直接使用锁定环境：

```bash
uv sync --project mcp --locked
uv run --project mcp stardew-valley-mcp doctor
```

## 五、配置 MCP 客户端

把 Mod `config.json` 中的连接信息通过客户端秘密管理能力填入以下结构：

```json
{
  "mcpServers": {
    "stardew-valley": {
      "command": "stardew-valley-mcp",
      "args": ["serve"],
      "env": {
        "STARDEW_VALLEY_MCP_HOST": "127.0.0.1",
        "STARDEW_VALLEY_MCP_PORT": "24642",
        "STARDEW_VALLEY_MCP_SHARED_SECRET": "<Base64 共享秘密>"
      }
    }
  }
}
```

以上配置保持只读。用户明确允许操作游戏时，把 `args` 改为：

```json
["serve", "--allow-write"]
```

MCP 只会暴露公共 Manifest、MCP 支持能力、Mod 公告能力和权限策略的交集。游戏停留在标题界面时，查询会返回稳定的 `not_ready`；加载存档后才会返回结构化 Snapshot。

## 六、第一次验证

### 只读

在 MCP 客户端调用：

```text
stardew_query_runtime {}
```

成功结果应为 `succeeded`，并包含当前日期、时间、玩家地图和坐标。

### 受控操作

使用 `--allow-write` 启动 MCP 后，先查询当前朝向，再调用一个不同方向：

```text
stardew_face {"direction":"left"}
```

随后再次调用 `stardew_query_runtime`，确认玩家 `facing` 已变为目标方向。不能只凭动作 Tool 没有报错判定成功。

`query_world`、`query_inventory` 与 `query_ui` 返回的 Ref 是不透明值。调用方只能原样交给明确接受 Ref 的 Tool，不应解析或自行构造。

## 七、本地开发门禁

不部署 Mod 的完整静态与自动化门禁：

```bash
./scripts/verify.sh
```

本机具有 Stardew Valley 与 SMAPI 时，同时构建并审计 Mod ZIP：

```bash
./scripts/verify.sh --with-mod
```

## 八、排障

- Tool 列表为空：确认游戏通过 SMAPI 启动、Mod 已加载、端口正在监听，并重启 MCP 客户端连接。
- 返回 `not_ready`：加载一个可操作的存档后重试。
- 返回 `unauthenticated`：在本机重新读取当前 Mod 自身的 `config.json`，更新秘密后重启 MCP 客户端；不要公开双方配置。
- 连接被拒绝：检查 Mod 日志中的监听状态，并确认客户端 Host/Port 与 Mod 一致。
- Mod 无法启动：查看 SMAPI 日志中带有 `Stardew Valley MCP` 的错误；错误信息不应包含共享秘密。
- 端口冲突：同时修改 Mod `config.json` 中的 `Port` 与 MCP 客户端的 `STARDEW_VALLEY_MCP_PORT`，然后重启游戏和 MCP 连接。
- Mod 构建找不到游戏：重新设置 `STARDEW_VALLEY_GAME_PATH`，并确认目录中存在游戏与 SMAPI DLL。
