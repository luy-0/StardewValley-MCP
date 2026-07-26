# 快速开始

当前预览版只提供只读 Tool `stardew_query_runtime`。它可以读取当前日期、时间、天气、玩家位置、基础资源和菜单摘要；其他能力仍在重写中。

## 一、安装 Mod

使用发布包时，将 `StardewValleyMCP` 文件夹放入 Stardew Valley 的 `Mods/` 目录。源码构建需要本机已经合法安装 Stardew Valley 1.6 与 SMAPI：

```bash
./mod/scripts/build.sh --package
```

本地开发者可以显式加入 `--deploy`，构建脚本默认不会修改游戏目录。首次启动游戏时，Mod 会创建自己的 `config.json`，默认只监听 `127.0.0.1:24642`，并生成随机的 `SharedSecretBase64`。

## 二、安装 MCP 服务端

开发者可以按仓库锁文件运行：

```bash
uv sync --project mcp --locked
uv run --project mcp stardew-valley-mcp doctor
```

`doctor` 只检查 Python 包和协议生成物，不连接游戏。启动 MCP 前，将 Mod `config.json` 中的 `SharedSecretBase64` 作为进程环境变量传入；不要把该值写入仓库、聊天记录或日志。

需要供 MCP 客户端长期调用时，先从仓库根目录安装命令行工具：

```bash
uv tool install ./mcp
stardew-valley-mcp doctor
```

```bash
export STARDEW_VALLEY_MCP_HOST=127.0.0.1
export STARDEW_VALLEY_MCP_PORT=24642
export STARDEW_VALLEY_MCP_SHARED_SECRET='<Base64 共享秘密>'
uv run --project mcp stardew-valley-mcp serve
```

## 三、配置 MCP 客户端

安装命令行工具后，客户端不需要保存仓库路径。配置可以使用以下结构，并通过客户端的秘密管理能力填写共享秘密。

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

MCP 只有在成功连接并验证 Mod 的 `query_runtime` 能力快照后才会列出 `stardew_query_runtime`。游戏停留在标题界面时，Tool 会返回稳定的 `not_ready`；加载存档后会返回 `succeeded` 和结构化 Snapshot。

## 四、本地验证

不部署 Mod 的完整静态与自动化门禁：

```bash
./scripts/verify.sh
```

本机具有 Stardew Valley 与 SMAPI 时，可以附带真实 Mod 编译和打包：

```bash
./scripts/verify.sh --with-mod
```

## 五、排障

- Tool 列表为空：确认游戏进程已经加载 Mod、24642 端口正在监听，并且共享秘密一致。
- 返回 `not_ready`：加载一个可操作的存档后重试。
- 返回 `unauthenticated`：重新读取新 Mod 自身的 `config.json`，不要使用旧 StarCoPlay 配置。
- Mod 无法启动：查看 SMAPI 的 `SMAPI-latest.txt`，搜索 `Stardew Valley MCP`；错误信息不会输出共享秘密。
- 端口冲突：把 Mod `config.json` 的 `Port` 与 MCP 的 `STARDEW_VALLEY_MCP_PORT` 同步修改，然后重启游戏。
