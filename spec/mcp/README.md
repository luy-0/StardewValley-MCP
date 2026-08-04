# MCP 投影契约

状态：**公开 V1 候选契约**

MCP 服务端将已授权的公共能力投影为 MCP Tool。Agent Skill 由 Agent Runtime 读取并调用这些 Tool，不由 MCP 挂载。MCP 是公开能力契约之上的 Adapter，不拥有游戏状态，不重新定义 Proto 语义，也不把 Mod 线路协议版本当成 MCP 自身的协议版本。

MCP 端必须遵循其所声明支持的正式 MCP 版本。Tool 的 `inputSchema`、`outputSchema`、`structuredContent` 和 Annotation 行为以 [MCP 官方 Tool 规范](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)为上位协议；本文件只规定 StardewValley MCP 的确定性投影。

## Tool 命名与能力面

- 原子能力 Tool 名固定为 `stardew_<capability_id>`，例如 `stardew_query_runtime`。
- `title` 与 `description` 直接来自公共 Manifest，不得由实现侧元数据覆盖。
- Tool 列表等于公共 Manifest、Mod 能力快照、MCP Projection、本地 Scope 与风险策略的交集。
- 当前 Session 内能力快照变化时，MCP 必须断开 Mod Session、重新握手并通知 MCP 客户端 Tool List 已变化；不得原地静默增加 Tool。

## Input Schema

Tool `inputSchema` 从对应 Proto Request 消息生成，但必须移除传输字段。模型永远不能提供以下内容：

```text
message_id, reply_to, command_id, timeout_ms, session_id,
lease_epoch, capability_digest, client_instance_id, shared secret,
nonce, auth_tag, endpoint
```

Tool 参数对象必须封闭，未知字段返回 `invalid_arguments`。Proto Enum 在 MCP Schema 中使用不含公共前缀的小写字符串，例如 `DIRECTION_UP` 投影为 `up`；生成器必须保存双向映射并用 Fixture 验证。

MCP 可以在 Server 配置中设置调用超时，但模型不得直接扩大 Manifest 的 `max_timeout_ms`。如果未来某项 Tool 允许用户选择更短超时，必须以独立、受限的公开参数显式加入能力契约。

机器可读权威产物为 [`tool-schemas.json`](tool-schemas.json)。它不是手写副本，而是由一致性工具 [`../conformance/generate_mcp_tool_schemas.py`](../conformance/generate_mcp_tool_schemas.py) 确定性生成。生成器不是新的契约权威，只负责组合以下输入：

| 输入 | 决定什么 |
|---|---|
| `proto/*.proto` Descriptor | 字段、类型、嵌套消息、Enum 与 `oneof` |
| `capabilities/manifest.yaml` | 公开能力集合、Request/Result 对应、说明和 Annotation 策略 |
| [`schema-policy.yaml`](schema-policy.yaml) | 普通 proto3 无法表达的必填、范围、默认值、跨字段条件和成功结果不变量 |
| [`error-map.yaml`](error-map.yaml) | Proto Error 到 MCP Tool Error 的稳定投影 |

其中 Schema Policy 是显式契约，不是藏在 Python 分支中的能力硬编码；生成器会拒绝不存在的消息、字段和映射。能力数量由 Manifest 决定，不再在生成器中固定为 15。生成结果还保存所有 Proto Enum 的双向 JSON 映射，提交时由一致性验证器逐字节重建比较。

重新生成或检查生成物：

```bash
uv sync --project mcp --locked
uv run --project mcp python spec/conformance/generate_mcp_tool_schemas.py
uv run --project mcp python spec/conformance/generate_mcp_tool_schemas.py --check
```

生成结果使用 JSON Schema Draft 2020-12。所有输入对象均设置 `additionalProperties: false`；Proto `int64`/`uint64` 投影为十进制字符串，避免 JSON Number 精度丢失。

## Output Schema

每个原子 Tool 的 `outputSchema` 是以下判别联合：

```text
Succeeded { status: "succeeded", commandId, output: <CapabilityResult> }
Failed    { status: "failed", commandId, error: StardewToolError }
Unknown   { status: "unknown", commandId, error: StardewToolError }
```

MCP 输出不是 Proto JSON 的逐字透传。投影层必须把 Proto 中所有非 `optional`、非 oneof 的字段正规化为显式 JSON 字段，包括零值、空字符串和空数组；因此生成的 Result Schema 会把这些字段列入 `required`。Proto `optional` 字段与未选中的 oneof 分支保持可省略，线路层是否省略默认值不改变 MCP 输出契约。

Tool 成功时 `structuredContent` 必须使用 `Succeeded` 且通过对应 Result Schema。Mod 返回业务失败、取消或超时时使用 `Failed` 并设置 MCP Tool Result `isError: true`；MCP 等待超时或断线且无法确认终态时使用 `Unknown` 和 `isError: true`。

自然语言 `content` 只作简短说明，不得包含结构化结果中没有的成功断言，也不得替代 `structuredContent`。

## Command ID 与等待

MCP 为每次新的逻辑调用生成 Command UUID，模型不能指定。只要 Mod 已接受命令，MCP 的内部重试、重连和状态查询必须保留同一 ID。

MCP Tool 调用默认等待 Mod 终态。若本地等待期限先到，MCP 返回 `unknown`，后台可以继续收敛，但不得在新的 Tool 调用中自动重放变更能力。只读能力只有在 Mod 明确表示从未接受原命令时，才可以用新 ID 安全重试。

## 稳定 Tool 错误

| 错误码 | 含义 |
|---|---|
| `invalid_arguments` | Tool 参数不符合公开 Schema |
| `unauthenticated` | 本地认证尚未建立或已经失效 |
| `capability_denied` | Scope、风险策略或能力面拒绝调用 |
| `capability_changed` | Mod 与 MCP 的 Capability Digest 不一致 |
| `context_expired` | Session 或 Lease 已经过期 |
| `conflict` | Command ID、Owner 或并发状态冲突 |
| `busy` | 已有变更命令运行 |
| `not_ready` | 游戏或 Mod 尚不能处理该能力 |
| `not_found` | 指定资源或命令不存在 |
| `stale_ref` | Ref 或对应 Revision 已经过期 |
| `out_of_range` | 目标不满足能力的距离或区域约束 |
| `command_timeout` | Mod 确认命令因 Deadline 结束 |
| `command_cancelled` | Mod 确认命令已取消 |
| `route_unavailable` | 本地 Mod 连接不可用 |
| `unknown_outcome` | 当前没有足够证据判断命令终态 |
| `upstream_protocol_error` | Mod 违反公开协议或版本不兼容 |
| `execution_failed` | 游戏侧已确认执行失败 |
| `internal_error` | MCP 内部未分类错误 |

`StardewToolError` 至少包含 `code`、`message`、`retryable`，可选 `retryAfterMs`。MCP Tool JSON 统一使用 `lowerCamelCase`，与官方 Proto JSON Mapping 保持一致。错误不得泄露共享秘密、HMAC、完整配置、绝对路径或堆栈。

当 Mod Error 携带公共结构化上下文时，MCP 在可选的 `error.details` 中投影它。当前仅定义 `details.navigation.lastConfirmedPosition`：它表示失败导航最后一次由 Mod 主线程确认的位置；它不表示命令成功，也不替代 `command_timeout` 等稳定错误码。

Proto `ErrorCode` 到上述错误的唯一、穷尽映射由 [`error-map.yaml`](error-map.yaml) 定义。实现不得根据错误消息文本推断 `tool_code`、`outcome` 或 `retryable`；`unknown` 结果也不得自动重放变更命令。

`route_unavailable` 不来自 Mod，因此作为 `error-map.yaml.local_errors` 中的 MCP 本地错误显式定义；生成器不得在 Python 代码中追加未声明错误。

## Annotation

- `side_effect: read_only` 投影为 `readOnlyHint: true`，否则为 `false`。
- `destructiveHint` 直接使用 Manifest 的 `destructive`，不得从风险数组机械推断。
- V1 的只读能力使用 `idempotentHint: true`，变更能力使用 `idempotentHint: false`。底层 Command ID 防重不改变变更 Tool 的自动重放策略。
- Annotation 只是提示，授权和风险门禁必须由服务端执行。

## 扩展边界

公共 MCP 软件包只提供本地 Mod Transport 和与 Provider 无关的扩展端口。Hosted Identity、Persona、计费、Runtime Manager、私有 Gateway 和特定 Provider 的 Agent Adapter 不属于公开 V1。
