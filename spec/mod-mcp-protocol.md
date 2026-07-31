# Mod–MCP 行为协议 V1

状态：**V1 候选契约**

本文是 Proto 消息之间的行为规则，不是第二份消息 Schema。线路字段、编号与 `oneof` 只以 [`proto/*.proto`](proto/README.md) 为权威；本文只定义发送顺序、认证、Owner、Fence、幂等、状态机和失败边界。

本协议定义独立 MCP 服务端与 SMAPI Mod 之间的本地连接、认证、能力协商、命令生命周期、取消和结果收敛。线路消息的字段与编号只由 `proto/*.proto` 定义；本文定义字段之间无法单独表达的行为约束。

## 1. 运行拓扑

```text
MCP 客户端 -> MCP stdio 服务端 -> loopback TCP -> SMAPI Mod -> 游戏主线程
```

Mod 必须只监听 IPv4/IPv6 loopback，MCP 必须作为 Client 主动连接。V1 不支持远程地址、WebSocket、JSON 业务帧、命令文件、状态文件、事件文件或运行时 Rendezvous。传输选择及 Spike 证据见 [`decisions/0002-local-transport.md`](decisions/0002-local-transport.md)。

## 2. TCP 帧

每个线路帧由以下两部分组成：

```text
4 字节大端无符号整数 N
N 字节序列化 TransportFrame protobuf
```

- `N` 的有效范围为 `1..1,048,576`；超出范围属于致命协议错误，接收方必须立即关闭连接。
- TCP 读取可能短读；接收方必须循环读取完整 Header 和 Payload，EOF 不能当作完整帧。
- 未知 protobuf 字段按 Proto 兼容规则保留或忽略，但不得影响认证、授权、幂等比较、副作用或当前版本输出；发送方只能发送协商版本定义的字段。
- 解析失败、缺少 `oneof body` 或方向错误属于致命协议错误。恶意线路重复编码同一普通字段或多个 `oneof` 分支时，接收方遵循标准 Proto “最后一个值生效”语义，不要求普通生成 API 还原已经被覆盖的值。

## 3. 版本

V1 线路版本由 `ProtocolVersion { major, minor }` 表示，当前为 `1.0`。`ServerHello.version` 表示服务端最高版本，`ClientHello.requested_version` 表示客户端在该 Major 下支持的最高版本；Major 不同必须拒绝，Major 相同时 Server 选择双方 Minor 的较小值。双方都必须支持同一 Major 下不高于自身最高 Minor 的版本。未来 Minor 只能增加旧端可安全忽略且不会扩大权限的字段或消息。

`capabilities/manifest.yaml` 中的 `protocol_version: 1.0.0` 是同一版本的 SemVer 表达。Patch 只表示文档、fixture 或不影响线路解释的修正，不在线路握手中协商。

## 4. 共享秘密与 HMAC

安装流程必须使用 OS CSPRNG 生成至少 32 个随机字节，并以 Base64 形式分别提供给 Mod 静态配置和 MCP 进程环境。生产秘密不得在线路上传输，也不得写入日志、错误、Fixture 或诊断输出；公开 HMAC 测试向量中的固定测试密钥不属于生产秘密。

下列辅助函数用于构造唯一字节串：

```text
LP(x)  = U32BE(len(x)) || x
U8(x)  = 1 字节无符号整数
U32(x) = 4 字节大端无符号整数
U64(x) = 8 字节大端无符号整数
```

字符串先按 UTF-8 编码；Nonce 使用原始字节。所有 HMAC 都使用 HMAC-SHA256，比较必须使用常量时间算法。

Client Authentication Tag：

```text
HMAC(secret,
  LP("stardew-valley-mcp/v1/client-auth") ||
  LP(mod_instance_id) || LP(client_instance_id) ||
  LP(server_nonce) || LP(client_nonce) ||
  U32(requested_major) || U32(requested_minor) ||
  LP(resume_session_id_or_empty))
```

Server Authentication Tag：

```text
HMAC(secret,
  LP("stardew-valley-mcp/v1/server-auth") ||
  LP(mod_instance_id) || LP(client_instance_id) ||
  LP(server_nonce) || LP(client_nonce) ||
  U32(selected_major) || U32(selected_minor) ||
  LP(session_id) || U64(lease_epoch) || LP(capability_digest) ||
  U32(result_retention_ms) || U32(reconnect_grace_ms))
```

`server_nonce` 与 `client_nonce` 必须各为 OS CSPRNG 生成的 32 个随机字节，并且不能在同一 Mod 启动周期内复用。认证失败后 Mod 必须发送脱敏的 `HandshakeRejected`（如果线路仍可安全写入）并关闭连接；同一进程最多保留 8 个未认证连接，每个来源连续 5 次认证失败后至少延迟 5 秒再接受新握手。

## 5. 握手

握手严格按以下顺序执行：

1. TCP 建立后，Mod 在 5 秒内发送 `ServerHello`；其中的 `mod_instance_id` 是本次游戏进程生成的规范小写 UUIDv4。
2. MCP 在 5 秒内验证版本并发送 `ClientHello`。`client_instance_id` 在一个 MCP 进程内稳定，跨进程不得复用。
3. Mod 验证版本、Nonce、HMAC 和 Owner 规则。
4. 成功时 Mod 发送 `ServerReady`；MCP 必须从完整 Descriptor 列表重新计算摘要，拒绝重复 ID、无效枚举和非规范顺序，再验证 Server Authentication Tag、保留期和超时下限，然后才能发送认证后消息。
5. 任意超时、顺序错误或验证失败都关闭连接，不得降级到其他协议。

握手帧不得携带 `SessionFence`。认证后的每个帧都必须携带与当前连接完全一致的 `session_id`、`lease_epoch` 和 `capability_digest`。

## 6. Owner、Lease 与重连

一个 Mod 实例同一时刻只允许一个 Owner Session。新 Session 使用规范小写 UUIDv4 `session_id`，其 `lease_epoch` 必须大于本次 Mod 启动中所有先前新 Session；恢复同一 Session 不增加 Epoch。Session 在首次认证时永久绑定该 `client_instance_id`。

连接断开后 Session 进入 `SUSPENDED`：

- Mod 至少保留 10 秒 Grace Period，并通过 `ServerReady.reconnect_grace_ms` 公布实际值。
- 只有同时提供相同 `resume_session_id`、原 `client_instance_id` 且通过新 Challenge HMAC 的 Client 才能在 Grace Period 内恢复。
- 恢复成功必须原子替换同一 Session 的旧 Transport；同一 Session 不能同时拥有两条活动连接。
- 有变更命令仍在运行时，即使 Grace Period 已结束，也不得把 Owner 转移给新 Session；必须等待命令到达终态。
- 没有活动变更命令且 Grace Period 已结束后，Mod 可以建立新 Session，并增加 `lease_epoch`。
- 旧连接或旧 Fence 的任何帧必须以 `STALE_LEASE` 拒绝并关闭连接。

连接断开不会自动取消已接受命令。Mod 继续执行到终态并缓存结果，MCP 将本地等待状态标记为 `UNKNOWN`，重连后使用状态查询收敛。

## 7. 能力快照与摘要

`ServerReady.capability_snapshot` 必须来自 Mod 编译期 Registry，并与公共 Manifest 的实现交集一致。每个 Descriptor 包含 ID、契约版本、Request/Result 类型、副作用、执行模式、是否可取消、超时范围、Scope、排序后的风险标签和 Destructive 策略。

摘要按以下算法计算：

1. 以能力 ID 的 UTF-8 字节升序排列 Descriptor。
2. 对每个 Descriptor 依次编码：

```text
LP(id) || LP(contract_version) ||
U8(side_effect) || U8(execution) || U8(cancellable ? 1 : 0) ||
U32(default_timeout_ms) || U32(max_timeout_ms) ||
LP(request_type) || LP(result_type) || LP(required_scope) ||
U32(risk_count) || LP(risk_1) ... LP(risk_n) || U8(destructive ? 1 : 0)
```

3. 连接全部编码后计算 SHA-256，并输出 64 个小写十六进制字符。

Descriptor 列表按 ID 排序，每个 Descriptor 内的 Risk 按 UTF-8 升序排列且不得重复。MCP 必须先从收到的完整列表重新计算摘要，再把它与 `snapshot.digest`、Server HMAC 和本地公共 Manifest/投影结果交叉验证。未知能力不能动态创建 Tool；缺少预期能力只会缩小暴露面。当前 Session 内能力快照如果变化，Mod 必须终止 Session 并要求重新握手，不得原地扩大权限。

## 8. Frame 关联规则

- `message_id` 必须为 `1..64` 个可打印 ASCII 字符，并在当前连接内唯一。
- 直接响应必须在 `reply_to` 中回显请求的 `message_id`。
- `CommandEvent` 可以作为直接响应，也可以作为主动进度或终态事件；主动事件的 `reply_to` 可以为空。
- 未知 `reply_to` 不改变命令状态；实现应记录脱敏诊断并忽略该关联。
- `Ping` 必须得到相同 Sequence 的 `Pong`；它只验证连接活性，不续租、不证明游戏线程健康。

### Body 方向矩阵

| Body | 发送方 | 状态 | Fence | `reply_to` |
|---|---|---|---|---|
| `ServerHello` | Mod | TCP 已连接 | 禁止 | 空 |
| `ClientHello` | MCP | 收到 ServerHello | 禁止 | ServerHello ID |
| `ServerReady` / `HandshakeRejected` | Mod | 收到 ClientHello | 禁止 | ClientHello ID |
| `CommandRequest`、Cancel/Status Request | MCP | 已认证 | 必须 | 空 |
| `CommandEvent`、Cancel/Status Response | Mod | 已认证 | 必须 | 直接响应时为请求 ID |
| `Ping`、`Pong`、`ProtocolError` | 双向 | 已认证 | 必须 | 响应时为请求 ID |

违反发送方、状态或 Fence 规则是致命协议错误并关闭连接。能够关联到合法 Command ID 的参数或业务错误不关闭连接，而是按命令接受边界返回稳定错误。

## 9. 命令提交与幂等性

`CommandRequest.command_id` 是唯一幂等身份，不再另设 `request_id` 或 `idempotency_key`。它必须为 OS CSPRNG 生成的规范小写 UUIDv4，并在一个 Mod 启动周期内全局唯一。

Mod 收到命令后依次执行：

1. 验证 Frame、Fence、能力快照和请求 `oneof`。
2. 根据 Manifest 和 [`capabilities/behavior.md`](capabilities/behavior.md) 校验参数及超时；`timeout_ms=0` 使用默认值，超过最大值必须拒绝，不能静默截断。
3. 对变更能力执行当前 Owner 与 Lease 的最终检查。
4. 原子写入 Command ID、已知字段的规范请求副本和 `ACCEPTED` 状态；这个时刻是命令的唯一“接受点”。
5. 将已接受命令排入游戏主线程，并返回 `ACCEPTED` 事件。

接受点之前的结构、Fence、能力、参数或 Busy 失败使用关联请求的 `ProtocolError`，不消耗 Command ID。接受点之后的所有失败必须形成该命令的不可变终态。

重复 `command_id` 的处理：

- 若解析后的请求与第一次请求逐字段相等，Mod 返回已经缓存的当前事件或终态，不得再次执行。
- 若请求不同，Mod 返回 `CONFLICT`，原命令继续保持原状态。
- 幂等比较只比较协商版本定义的已知字段；安全忽略的未知字段不参与。
- 终态 Result 可以在公布的保留期后淘汰，但已使用 Command ID 的 Tombstone 与请求摘要必须保留到本次 Mod 进程结束。Result 已淘汰的重复提交通过关联该请求帧的 `ProtocolError(ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED)` 返回，绝不能重新执行。

## 10. 并发与主线程

所有 Stardew Valley 状态读取和写入都必须在游戏主线程执行。Transport 线程只负责收发、认证和入队，不得直接访问游戏对象。

V1 同一时刻最多运行一个变更命令。只读命令可以在游戏主线程的安全点穿插执行，但不能观察到某个 Tick 内的中间写入状态。存在活动变更命令时，新变更命令在接受点前返回 `BUSY`，不得进入隐藏的第二个 Scheduler。认证后待处理 Frame 队列上限为 128，游戏主线程命令队列上限为 64；达到上限返回 `BUSY`，不能无限占用内存。

## 11. 生命周期

Mod 只允许以下状态转换：

| 当前状态 | 允许下一状态 | 发生条件 |
|---|---|---|
| `ACCEPTED` | `RUNNING` | 长时 Handler 开始执行 |
| `ACCEPTED` | `SUCCEEDED`、`FAILED` | 即时 Handler 完成 |
| `ACCEPTED` | `CANCELLED` | 可取消命令在开始前完成取消 |
| `ACCEPTED` | `TIMED_OUT` | 排队期间到达 Deadline |
| `RUNNING` | `SUCCEEDED`、`FAILED` | 后置条件确认或执行失败 |
| `RUNNING` | `CANCELLED` | Handler 确认取消且停止后续副作用 |
| `RUNNING` | `TIMED_OUT` | Handler 确认 Deadline 收敛且停止后续副作用 |

`SUCCEEDED`、`FAILED`、`CANCELLED`、`TIMED_OUT` 是不可变终态，没有合法下一状态。取消与 Deadline 在同一个游戏 Tick 竞争时，以 Mod 主线程首先观察到并写入的终态原因为准；写入终态后忽略另一信号。

事件约束：

- `SUCCEEDED` 必须携带与请求能力同分支的 `CapabilityResult`，且不得携带 Error。
- `FAILED`、`CANCELLED`、`TIMED_OUT` 必须携带 Error，且不得携带 Result。`CANCELLED` 只能携带 `ERROR_CODE_CANCELLED`，`TIMED_OUT` 只能携带 `ERROR_CODE_DEADLINE_EXCEEDED`，`FAILED` 不得携带这两个专用错误码。
- `ACCEPTED`、`RUNNING` 不得携带 Outcome。
- `progress_percent` 只能是 `0..100`，仅作观测；调用方不能据此推断终态。
- Mod 不使用 `UNKNOWN` 作为命令状态。`UNKNOWN` 是 MCP 在失去线路证据时的本地观测状态。
- `GetCommandStatusResponse.found=true` 时 `current` 必须存在；`found=false` 时 `current` 必须缺省。
- `CancelCommandResponse.accepted=true` 时 `current` 必须存在且 Error 缺省；`accepted=false` 时 Error 必须存在，Current 可以回显已知状态但不能和 Error 矛盾。

## 12. Deadline

命令 Deadline 从 Mod 接受命令时开始，以单调时钟计算。它不依赖双方墙上时钟，因此线路中不传绝对时间戳。

达到 Deadline 后，Mod 必须请求 Handler 停止并最终返回 `TIMED_OUT`。如果底层游戏调用无法立即中断，Handler 仍必须阻止后续步骤，并且不得先报告 `TIMED_OUT` 后继续产生新的游戏副作用。

MCP 自己的等待 Deadline 可以更短；本地等待超时只产生 `UNKNOWN`，不能伪造 Mod 的 `TIMED_OUT`。

## 13. 取消

只有 Manifest 声明 `cancellable: true`、尚未越过能力不可逆提交点的活动命令接受取消。`cancellable: true` 表示命令至少存在一个可取消阶段，不表示已经产生的游戏效果可以回滚。`CancelCommandResponse.accepted=true` 只表示取消意图已经记入命令账本，最终状态仍以 `CommandEvent` 为准；Handler 在游戏主线程完成输入或控制清理后才能写入 `CANCELLED`。

未知 Command ID 返回 `accepted=false/NOT_FOUND`。Manifest 不可取消、当前阶段已越过提交点或命令已经终态时返回 `accepted=false/CONFLICT`；已知命令必须在 `current` 回显当前事件。多个相同取消请求必须幂等，不得把已成功或已经提交效果的命令改写为 `CANCELLED`。取消语义的架构裁决见 [`decisions/0003-unified-command-runtime.md`](decisions/0003-unified-command-runtime.md)。

## 14. 状态查询与结果保留

Mod 必须在 `ServerReady.result_retention_ms` 公布完整终态 Result 保留时间，V1 最小值为 300,000 毫秒。保留期从终态产生时开始；Command ID Tombstone 和请求摘要仍保留到 Mod 退出。同一 Mod 启动内，授权后的当前 Owner 可以查询旧 Session 创建的命令，以便断线收敛。

状态查询命中仍有完整 Result 的记录时返回 `GetCommandStatusResponse(found=true,current=...)`；命中 Tombstone 但完整 Result 已淘汰时，通过关联状态请求帧的 `ProtocolError(ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED)` 返回。`GetCommandStatusResponse(found=false)` 只用于本次 Mod 启动中从未记录过的 Command ID，不能用于已知 Tombstone。

即使收到 `found=false`，MCP 也必须返回未知结果，不能据此向最终用户断言命令从未执行，因为线路中断或 Mod 重启可能已经丢失证据。Mod 重启会丢失内存缓存；持久化结果不属于 V1。

## 15. Ref、Revision 与过期

所有 `Ref.value` 都是不透明值。完整生命周期、Snapshot 原子性和失效触发器由 [`capabilities/behavior.md`](capabilities/behavior.md) 定义。

## 16. 错误与 MCP 投影

协议级错误使用 `ProtocolError`；某个已接受命令的业务失败通过该命令的终态 `CommandEvent` 返回。Proto Error 到 MCP Error 的穷尽映射由 [`mcp/error-map.yaml`](mcp/error-map.yaml) 定义；重试属性不能由实现自由猜测，也不授权自动重放结果未知的变更命令。错误消息必须脱敏，不能包含共享秘密、HMAC、完整配置、主机绝对路径、堆栈或未公开游戏对象字段。

ErrorCode 的使用上下文必须遵循下表；`ERROR_CODE_UNSPECIFIED` 永远不得发送：

| 上下文 | 允许的 ErrorCode |
|---|---|
| `HandshakeRejected` | `UNAUTHENTICATED`、`UNSUPPORTED_VERSION`、`BUSY`、`INTERNAL` |
| 接受前或控制请求的 `ProtocolError` | `INVALID_ARGUMENT`、`UNAUTHENTICATED`、`PERMISSION_DENIED`、`UNSUPPORTED_CAPABILITY`、`CAPABILITY_SET_CHANGED`、`STALE_LEASE`、`CONFLICT`、`BUSY`、`NOT_READY`、`NOT_FOUND`、`IDEMPOTENCY_RECORD_EXPIRED`、`PROTOCOL_VIOLATION`、`INTERNAL` |
| `CommandEvent(FAILED)` | `INVALID_ARGUMENT`、`NOT_READY`、`NOT_FOUND`、`STALE_REF`、`OUT_OF_RANGE`、`EXECUTION_FAILED`、`INTERNAL` |
| `CommandEvent(CANCELLED)` | 仅 `CANCELLED` |
| `CommandEvent(TIMED_OUT)` | 仅 `DEADLINE_EXCEEDED` |

`IDEMPOTENCY_RECORD_EXPIRED` 只表示 MCP 无法再取得已知执行过的完整终态，因此只能通过 `ProtocolError` 投影为 `unknown`，不得伪装成 Mod 已确认的 `FAILED`。

`INVALID_ARGUMENT` 通常应在接受前返回；但 Ref Kind、Ref 来源以及 Revision 与当前游戏对象的关系只能在游戏主线程安全点解析，这类上下文型非法输入允许在命令已接受后通过 `CommandEvent(FAILED)` 返回 `INVALID_ARGUMENT`。Transport 线程不得为了提前返回错误而读取 Ref Store 或游戏对象。

## 17. 安全边界

- Loopback 不是认证，HMAC 认证不能省略。
- Listener 必须拒绝非 loopback Peer；`127.0.0.0/8`、`::1` 与 IPv4-mapped loopback 可以接受，其他地址一律拒绝。未认证连接数、认证时限和失败速率遵循本文固定上限。
- 一个知道共享秘密的本地进程被视为已授权 Owner；V1 不防御已经控制同一 OS 用户或能读取配置文件的恶意代码。
- 任何远程监听、外部遥测或秘密同步都超出 V1，必须另行设计并显式启用。
