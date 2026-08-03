# ADR-0002：公开 V1 本地传输

- 状态：已接受
- 日期：2026-07-26

## 背景

MCP 服务端通常由客户端按需启动，而 Mod 与游戏进程具有更稳定的生命周期。本地传输需要在 macOS 与 Windows 上使用同一连接模型，并在不引入额外 Endpoint 发现服务的情况下完成认证、单 Owner 管理和断线恢复。

## 备选方案

### A. MCP Listener，Mod Client

优点是 MCP 可以直接控制连接生命周期。缺点是 MCP 通常由客户端按需启动，每次端口变化都需要额外发现机制；如果不使用固定端口，就需要另一个控制面协调 Endpoint。

### B. Mod Listener，MCP Client

Mod 与游戏共同启动，天然拥有稳定生命周期。MCP 可以在任意时刻主动连接固定配置的 loopback Endpoint。Mod 还可以在最接近游戏执行的位置维护单 Owner Lease 和命令结果缓存。

### C. 命名管道或 Unix Domain Socket

它们能减少 TCP 暴露，但 Windows、macOS 和 Linux 的路径、权限及库行为不同，会扩大首个公开版本的安装与测试矩阵。

## 决策

采用方案 B：Mod 持有 loopback TCP Listener，MCP 作为 Client。线路使用 4 字节大端无符号长度前缀加一个序列化的 `TransportFrame` protobuf；帧长必须在 `1..1,048,576` 字节之间。

V1 不使用 WebSocket、JSON 业务帧或运行时 Endpoint 发现文件。静态 `config.json` 和日志文件仍可存在，但不得承载命令、结果、事件、能力快照或所有权转移。

## 认证与所有权

1. Mod 配置与 MCP 配置共享至少 256 bit 的随机秘密，线路中不发送该秘密。
2. Mod 先发送随机 Challenge；MCP 使用 HMAC-SHA256 对双方 Nonce、实例 ID 和协商版本作域分离签名。
3. Mod 使用相同秘密返回 Server Proof，MCP 必须验证后才能发送命令。
4. Mod 每次启动生成新的 `mod_instance_id`，每次新 Owner Session 单调增加 `lease_epoch`。
5. 同一时刻只允许一个 Owner。连接中断进入短暂 Suspended 状态；相同 Session 可在 Grace Period 内恢复，其他 Owner 必须被拒绝。
6. Mod 在公布的有界保留期内缓存完整终态 Result，并把已使用 Command ID 的 Tombstone 与请求摘要保留到本次 Mod 进程结束，使断线后的 MCP 能收敛未知结果且不会在 Result 淘汰后重复执行。

## 一致性证据

可重复运行的 Harness 位于 [`../conformance/transport-spike/`](../conformance/transport-spike/)。它往返真实 `TransportFrame { message_id, ping }` Proto Wire Payload，并覆盖确定性短读、两帧粘包、零长度、超过 1 MiB、短 Header EOF 与短 Payload EOF。实现必须使用适用于 .NET 6 的分段读取循环，正确处理短读和 EOF。

## 后果

- 安装流程需要安全生成共享秘密，并分别写入 Mod 静态配置与 MCP 客户端环境；日志必须脱敏。
- 本协议只抵御不知道共享秘密的本地进程，不宣称隔离已经控制同一用户账户或能够读取配置文件的恶意代码。
- 未来若增加远程传输，必须新建独立安全 Profile，并提供 TLS 与身份授权设计；不得扩大本地 Profile 的绑定范围。
