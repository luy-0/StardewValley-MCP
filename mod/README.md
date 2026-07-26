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

## 自动进入指定存档

`AutoLoadSave` 是默认关闭的一次性启动能力。启用后，Mod 会等待标题菜单，复用游戏原生存档扫描菜单精确匹配 `AutoLoadSaveName`，并在 `SaveLoaded` 事件中核对实际载入的存档目录；它不会模拟鼠标点击，也不会选择“最近存档”。加载期间会临时关闭失焦暂停，完成或失败后恢复原设置。

```json
{
  "AutoLoadSave": true,
  "AutoLoadSaveName": "Player_123456789",
  "AutoLoadTimeoutSeconds": 180
}
```

本机重编译后的隔离验证流程：

```bash
./mod/scripts/build.sh
./mod/scripts/launch-autoload-smapi.sh --save Player_123456789
```

启动脚本会创建临时 Mods 目录、选择未占用的 loopback 端口并启动新的 SMAPI 进程，只聚焦该新 PID 以通过 macOS 的早期初始化。脚本不会复用、终止或向其他游戏进程发送输入；超时时也会保留新进程并打印其 PID、运行目录与专用日志路径，供调用者明确处置。
