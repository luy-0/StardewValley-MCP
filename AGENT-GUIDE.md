# Agent 安装与验收指南

本指南面向从干净源码检出开始工作的 Agent。目标是在不接触任何私有仓库的前提下，安装 Stardew Valley MCP，完成第一次只读查询和第一次受控操作，并留下可复核证据。

## 一、完成标准

只有同时满足以下条件，才可以报告安装完成：

1. Mod 从本仓库源码构建并被 SMAPI 成功加载。
2. MCP 从 `mcp/` 安装，`doctor` 返回 `doctor_ok`。
3. MCP 客户端发现 `stardew_query_runtime`；启用写权限时发现 `stardew_face`。
4. `stardew_query_runtime` 返回 `succeeded` 和当前存档的结构化状态。
5. `stardew_face` 改变玩家朝向，随后再次查询得到目标方向。
6. 证据中不包含共享秘密、本机绝对路径、用户目录或完整配置文件。

Windows 当前只要求安装文档与命令静态可审查，不属于本阶段实机验收门禁；不要把它报告为已经过 Windows 实机验证。

## 二、安全规则

- 只使用本仓库的 `mod/`、`mcp/`、`spec/`、`skill/` 和 `docs/`；不得查找或导入旧 StarCoPlay、Hosted Platform 或私有 Runtime。
- `SharedSecretBase64` 只能进入本机 MCP 进程环境或客户端秘密存储，不得写入仓库、命令记录、聊天、截图或日志。
- 文档和提交只能使用仓库相对路径、环境变量或平台通用路径，禁止写入当前机器的绝对用户目录。
- 默认以只读方式启动 MCP。只有在用户明确允许游戏变更后，才加入 `--allow-write`。
- 不修改或上传存档。首次受控操作使用 `face`，不消耗物品、金钱或体力，也不推进时间。

## 三、前置条件

两端共同需要：

- 合法安装的 Stardew Valley 1.6；
- SMAPI 4.1.0 或更高版本；
- Git；
- .NET 6 SDK；
- Python 3.11 或更高版本；
- `uv`；
- 支持本地 stdio MCP Server 的客户端。

macOS 常见 Steam 游戏目录：

```bash
export STARDEW_VALLEY_GAME_PATH="$HOME/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS"
```

Windows PowerShell 常见 Steam 游戏目录：

```powershell
$env:STARDEW_VALLEY_GAME_PATH = "C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley"
```

非 Steam 安装应把变量改为同时包含 `Stardew Valley.dll` 与 `StardewModdingAPI.dll` 的实际游戏目录。

## 四、检出与静态门禁

从干净目录检出仓库，进入仓库根目录后执行：

```bash
./scripts/verify.sh
```

该入口会同步锁定的 MCP 开发环境，检查生成协议、Spec、边界、Skill、Mod Protocol、MCP 测试和发行包。失败时先解决当前错误，不要跳过门禁后继续安装。

## 五、构建并安装 Mod

### macOS

确认 `STARDEW_VALLEY_GAME_PATH` 后执行：

```bash
./mod/scripts/build.sh --deploy
```

脚本会按锁文件恢复依赖，运行 Protocol 与 Mod 测试，构建 Release 版本，并安装到游戏的 `Mods/StardewValleyMCP/`。

### Windows（当前仅静态核对）

安装 Git for Windows 后，在 Git Bash 中设置游戏路径并调用同一入口：

```bash
export STARDEW_VALLEY_GAME_PATH='C:/Program Files (x86)/Steam/steamapps/common/Stardew Valley'
./mod/scripts/build.sh --deploy
```

如果使用 PowerShell 或非 Steam 路径，必须保证传给构建的 `GamePath` 指向包含游戏和 SMAPI DLL 的目录。当前阶段不以 Windows 实机结果作为完成证据。

## 六、启动游戏并取得本地连接配置

1. 通过 SMAPI 启动游戏并加载一个存档。
2. 在 `Mods/StardewValleyMCP/config.json` 确认 `Host`、`Port` 和 `SharedSecretBase64` 已生成。
3. 只在本机读取共享秘密并注入客户端的秘密存储；Agent 不得在回复中复述该值，也不得提交 `config.json`。
4. 从 SMAPI 日志确认出现 `Stardew Valley MCP` 的加载记录。游戏仍在标题界面时查询返回 `not_ready` 是正常行为，加载存档后才应成功。

## 七、安装 MCP

从仓库根目录执行：

```bash
uv tool install ./mcp
stardew-valley-mcp doctor
```

如果桌面 MCP 客户端不继承终端的 `PATH`，先执行 `uv tool dir --bin` 取得 Tool 可执行目录，再把其中 `stardew-valley-mcp` 的完整路径填入客户端 `command`；该本机路径不得写回仓库文档。

预期输出至少包含：

```text
doctor_ok package=0.1.0a1 protocol=stardew_valley.mcp.v1
```

需要重复安装当前工作树时，使用：

```bash
uv tool install --force ./mcp
```

## 八、配置 MCP 客户端

只读配置：

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

需要进行受控操作时，在用户明确授权后把 `args` 改为：

```json
["serve", "--allow-write"]
```

端口不是默认值时，必须同时修改客户端环境变量。客户端最终看到的 Tool 是公共 Manifest、MCP 支持集、Mod 公告集和本地权限策略的交集。

## 九、第一次只读调用

调用：

```text
stardew_query_runtime {}
```

验收证据必须包含：

- Tool 结果为 `succeeded`；
- 当前日期、时间、玩家地图与坐标；
- 结果中没有共享秘密、绝对路径或堆栈。

若返回 `not_ready`，先确认已经加载存档；若返回 `unauthenticated`，在本机重新核对共享秘密，不要把双方配置打印到回复中。

## 十、第一次受控操作

1. 经用户授权后，以 `--allow-write` 重启 MCP Server。
2. 先调用 `stardew_query_runtime` 记录当前 `facing`。
3. 选择一个与当前方向不同的方向，调用：

```text
stardew_face {"direction":"left"}
```

4. 再次调用 `stardew_query_runtime`，确认 `facing` 与请求一致。

不能只凭 `stardew_face` 没有报错就判定成功；后置查询才是游戏状态已经改变的直接证据。

## 十一、常见故障

- `doctor` 不存在：执行 `uv tool dir --bin` 确认 Tool 可执行目录已经加入 `PATH`，然后重新执行 `uv tool install --force ./mcp`。
- Tool 列表为空：确认 Mod 已加载、游戏进程仍在运行、Host/Port 一致，并重新启动 MCP 客户端连接。
- `not_ready`：进入存档，等待玩家可以操作后重试。
- `unauthenticated`：Mod 可能重新生成了秘密；在本机更新 MCP 环境变量后重启客户端。
- `connection_failed` 或连接被拒绝：检查 Mod 日志是否已经监听配置端口，以及端口是否被其他进程占用。
- Mod 构建找不到游戏：重新设置 `STARDEW_VALLEY_GAME_PATH`，确认目录内存在游戏与 SMAPI DLL。
- Mod 代码或 DLL 发生变化：重新执行构建并重启游戏；仅 MCP Python 代码变化不要求重启游戏。

## 十二、验收记录

最终报告应简洁记录：源码提交、操作系统、Mod 构建结果、SMAPI 加载证据、`doctor` 输出、只读 Tool 结果摘要、受控操作前后朝向，以及执行过的验证命令。共享秘密必须写成“已配置，未披露”，所有本机路径必须改写为仓库相对路径或平台通用说明。
