# V1 一致性要求

一个实现只能针对它所声明的运行配置和能力证明一致性。某个实现通过自己的单元测试，不能证明另一种语言或另一端实现也符合规范。

## Spec 级门禁

1. 所有 Proto 文件使用 `stardew_valley.mcp.v1`，并能从同一输入生成 C# 与 Python 产物。
2. Manifest 通过 JSON Schema，能力 ID 唯一，并与 `CommandRequest.operation` 和 `CapabilityResult.result` 分支一一对应。
3. 每项 Manifest Request/Result 消息存在，默认超时不大于最大超时，只读/变更 Scope 匹配。
4. C# 与 Python 生成产物各自导出的 Descriptor 字节一致，并由两种语言独立计算出同一 Capability Digest 与 HMAC。
5. 15 项 MCP Input/Output Schema 可由 Proto Descriptor、Manifest、显式 Override 与 Error Map 确定重建，生成物逐字节一致。
6. Fixture 能由生成的 C# 与 Python Proto 严格解析，序列化后再次解析保持语义相等，并通过跨帧身份、Fence、Command、Result 与状态关联检查。
7. 18 项历史能力均有机器可读裁决，公共 Proto 不包含未列入 Manifest 的能力分支。

当前机器可读门禁执行方式：

```bash
python3 -m venv /tmp/sdvmcp-spec-venv
/tmp/sdvmcp-spec-venv/bin/pip install -r spec/conformance/requirements.txt
/tmp/sdvmcp-spec-venv/bin/python spec/conformance/verify.py
./spec/conformance/transport-spike/run.sh
```

只有缺少 .NET SDK 的环境才允许临时使用 `--skip-csharp`；CI 与发布验收不得跳过 C# Fixture、Descriptor、Digest 与 HMAC 检查。`verify.py` 是阶段 0 的静态契约验证器，不宣称能够替代阶段 1 之后针对真实 Mod/MCP 实现的运行时 Conformance Harness。

## 阶段 0 Transport 门禁

阶段 0 由 `verify.py` 和 `transport-spike/` 可执行验证：长度前缀、短读、粘包、非法长度、短 Header/Payload EOF、真实 Proto Wire 往返、HMAC 正反向量、Descriptor 摘要、跨握手 Fixture 关联和 Fence 一致性。当前开发机结果记录在传输 ADR；跨 OS 自动矩阵在阶段 1 建立 CI 后执行。

## 实现期 Transport 门禁

1. 非 loopback 绑定或 Peer 必须拒绝。
2. 帧长度 `0`、超过 1 MiB、短 Header、短 Payload 和非法 Proto 都必须失败关闭。
3. HMAC 的正向向量通过；Nonce、实例 ID、版本、Session、Digest、Result Retention 或 Reconnect Grace 任一输入变化都必须验证失败。
4. 握手超时、乱序、错误 Fence、旧 Lease、第二 Owner 和无效 Capability Digest 必须拒绝。
5. 短读和多帧粘包必须正确处理；不得假设一次 Socket Read 等于一个 Frame。

## 实现期命令生命周期门禁

1. 相同 Command ID 与相同请求只执行一次并返回已有状态；不同请求返回 `CONFLICT`。
2. 一个时刻最多一个变更命令，且所有游戏访问发生在主线程。
3. 状态转换只能沿公开状态机前进，终态不可变。
4. 成功必须带匹配 Result；失败、取消和超时必须带 Error。
5. 断线不重放命令，重连后能在 Result Retention 内查询收敛。
6. 不可取消能力、终态命令和未知命令不得伪造取消成功。

## 实现期 MCP 门禁

1. Tool 列表等于 Manifest、Mod Snapshot、Projection 与策略交集。
2. 受保护字段不能通过 initialize metadata 或 Tool 参数注入。
3. Input/Output Schema、Enum 映射和结构化结果符合 `../mcp/README.md`。
4. `failed` 与 `unknown` 不得包装为成功 Tool Result；未知变更结果不得自动重放。
5. 软件包无需私有平台仓库即可安装和测试。

## 实现期 Mod 门禁

1. 每项宣告能力只有一个编译期 Handler。
2. Scheduler 不查询或初始化第二套命令处理器。
3. 变更执行前最后检查 Session、Lease 与 Capability Digest。
4. 每个已接受命令到达唯一终态，或者在 MCP 侧保持可观测 `UNKNOWN`。
5. Socket 线程不得访问 Stardew Valley 对象。

## 禁止依赖门禁

生产源码与机器可读协议不得依赖或提供：

```text
AdapterV2, V2Command, V2Result, CommandProcessor, CompoundDispatcher,
FallbackToLegacyMapper, v2-json, Legacy namespace, File Bridge,
runtime Rendezvous, agent.protocol, runtime_manager, Hosted Credential
```

ADR、迁移计划和能力裁决可以提及被删除系统，禁止词扫描不得把历史说明误判为运行时依赖。
