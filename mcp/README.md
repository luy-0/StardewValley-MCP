# MCP 服务端

本目录存放可运行的 MCP 服务端。

## 职责

- 向兼容 MCP 的客户端暴露 Tool、Resource 和 Prompt；
- 根据公共规范校验调用；
- 解析允许暴露的能力集合；
- 将命令路由到已连接的 Mod；
- 返回稳定的结果与错误；
- 生成保留在本地且保护隐私的诊断信息。

## 非职责

MCP 服务端不拥有游戏状态，不嵌入私有平台身份，也不要求本地开源链路依赖托管的 StarCoPlay 平台。

## 当前实现状态

当前已建立独立 Python 包、CLI 和由 `../spec/proto/` 生成的协议类型。`serve` 会启动真正的 MCP stdio Server；它连接本地 Mod、验证 HMAC 与能力摘要，并按公共 Manifest、MCP 支持集、Mod 公告集和权限策略的交集暴露 Tool。默认只允许 `game:read`；需要操作游戏时必须显式增加 `--allow-write`。

推荐使用 `uv` 按锁文件安装、测试并构建：

```bash
./mcp/scripts/test.sh
uv run --project mcp stardew-valley-mcp doctor
uv run --project mcp stardew-valley-mcp serve
# 显式允许操作游戏
uv run --project mcp stardew-valley-mcp serve --allow-write
```

只重新生成或检查协议代码：

```bash
python3 scripts/generate_protocol.py
python3 scripts/generate_protocol.py --check
```

Python 包不依赖私有平台仓库。当前稳定依赖锁定在 MCP Python SDK v1 系列，避免自动升级到不兼容的主版本。

启动 `serve` 前必须设置 Mod `config.json` 中对应的 Base64 共享秘密：

```bash
export STARDEW_VALLEY_MCP_HOST=127.0.0.1
export STARDEW_VALLEY_MCP_PORT=24642
export STARDEW_VALLEY_MCP_SHARED_SECRET='<Mod config.json 中的值>'
```

完整安装和 MCP 客户端配置参见 [`../docs/getting-started.md`](../docs/getting-started.md)。
