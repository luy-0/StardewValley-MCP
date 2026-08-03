# Proto 权威定义

本目录中的 `.proto` 文件是公开 V1 线路消息结构、字段编号、枚举、能力参数、能力结果、查询事实和 Ref 的唯一权威来源。所有文件使用 package `stardew_valley.mcp.v1`，C# namespace 为 `StardewValleyMcp.Protocol.V1`。

Proto 只定义“可以编码什么”，不单独定义“何时发送、谁可以发送、状态如何转换”。这些跨消息行为只由 [`../mod-mcp-protocol.md`](../mod-mcp-protocol.md) 定义，因此不存在两套可竞争的协议解释。

## 文件职责

| 文件 | 职责 |
|---|---|
| `common.proto` | 公共枚举、错误、位置和执行统计 |
| `refs.proto` | 不透明 Ref 与解析状态 |
| `facts.proto` | 游戏运行时、世界、库存与 UI 事实 |
| `queries.proto` | 五项观察能力的请求与结果 |
| `actions.proto` | 十项操作能力的请求与结果 |
| `capabilities.proto` | 统一命令、结果、取消与状态查询 |
| `transport.proto` | 握手、认证、Session Fence 与线路 Frame |

## 生成约束

- C# 与 Python 产物必须从同一次提交的 Proto 文件生成。
- 生成结果过期时 CI 必须失败。
- MCP Tool 的 Input/Output Schema 只能从 Manifest 选中的 Request/Result 消息生成。
- `message_id`、Session Fence、Command ID 和认证字段不得出现在模型提供的 Tool 参数中。
- 公共 V1 Proto 只描述 Mod 与 MCP 之间的能力和命令协议，不承载外部身份或部署环境字段。

生成文件属于 `mod/` 和 `mcp/` 的构建产物，不得作为契约源码手工修改。
