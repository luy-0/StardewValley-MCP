# Mod

本目录存放可运行的 SMAPI Mod。

## 职责

- 观察权威游戏状态；
- 在正确的游戏线程执行游戏操作；
- 提供 `../spec/` 公共契约定义的 Handler；
- 与 MCP 端协商双方支持的能力；
- 在游戏边界执行命令身份、兼容性和安全规则。

## 非职责

Mod 不实现 MCP，不加载 Skill，不拥有智能体身份，也不定义私有平台概念。

## 当前实现状态

当前已建立独立的 .NET 6 Solution、公共 Proto 生成项目和 SMAPI Host。Host 只在配置的 loopback 地址启动 Proto TCP Listener，通过共享秘密完成 HMAC 认证，并且当前只注册 `query_runtime` Handler。

生成并测试公共协议：

```bash
python3 -m pip install -r spec/conformance/requirements.txt
python3 scripts/generate_protocol.py --check
dotnet test mod/tests/StardewValleyMcp.Protocol.Tests/StardewValleyMcp.Protocol.Tests.csproj -p:RestoreLockedMode=true
```

构建 Mod 需要本机合法安装 Stardew Valley 1.6 与 SMAPI。构建脚本默认不会写入游戏目录：

```bash
./mod/scripts/build.sh --package
```

只有明确需要本地部署时才使用 `--deploy`。非标准游戏位置通过 `STARDEW_VALLEY_GAME_PATH` 指定；新脚本不会调用或引用旧仓库。

首次加载 Mod 时会在自身 `config.json` 中生成至少 32 字节的随机共享秘密。该文件不得提交、公开或写入日志；MCP 通过 `STARDEW_VALLEY_MCP_SHARED_SECRET` 读取同一个 Base64 值。
