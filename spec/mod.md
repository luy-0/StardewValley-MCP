# Mod 公开实现契约

状态：**公开 V1 候选契约**

本文规定任何兼容公开 V1 的 SMAPI Mod 实现必须遵守的运行边界。它不重新定义 Proto 字段或能力语义：线路结构以 [`proto/*.proto`](proto/README.md) 为权威，跨进程行为以 [`mod-mcp-protocol.md`](mod-mcp-protocol.md) 为权威，单项能力的参数与成功条件以 [`capabilities/behavior.md`](capabilities/behavior.md) 为权威。

## 1. Host 与生命周期

- Mod 是本地连接的 Listener Owner，必须只绑定配置指定的 loopback 地址。
- Listener、认证和能力 Registry 必须随当前游戏进程启动与销毁；`mod_instance_id` 不能跨进程复用。
- 游戏尚未加载可操作存档时，Mod 必须返回 `NOT_READY`，不得以 Proto 零值伪造有效 Snapshot。
- 静态配置和普通日志可以使用文件；文件不得承担命令、结果、事件、能力快照、Endpoint 发现或 Owner 转移。

## 2. 认证、Owner 与状态所有权

- 在任何认证后消息被处理前，Mod 必须完成共享秘密 HMAC、版本、Nonce 和 Client 身份验证。
- 同一 Mod 实例同一时刻只能存在一个 Owner Session。Lease Epoch、Resume、第二 Owner 拒绝和旧 Fence 处理必须符合行为协议。
- Session、Command 状态、Result Cache 和 Tombstone 的权威所有者是 Mod；MCP 不得覆盖或伪造 Mod 终态。
- 认证失败、协议错误和日志必须脱敏，不得输出共享秘密、HMAC、完整配置或主机绝对路径。

## 3. 能力 Registry

- Registry 必须在编译期显式注册，不得通过扫描程序集、配置字符串或网络返回动态加载 Handler。
- 每个公开能力 ID 在一个构建中必须且只能对应一个 Handler。
- Mod 宣告的 Capability Snapshot 只能是公共 Manifest 与本构建 Registry 的交集；Descriptor 的 Request、Result、Scope、风险和超时必须与 Manifest 完全一致。
- 未注册能力必须拒绝，不能转交第二套 Processor、Dispatcher、Legacy Handler 或通用反射执行入口。
- Capability Digest 改变时必须建立新 Session，不能在活动 Session 中静默扩大能力面。

## 4. 命令接受与调度

- Frame、Fence、能力、权限、参数和 Timeout 必须在唯一接受点之前验证。
- 接受点必须原子写入 Command ID、规范请求摘要和 `ACCEPTED`，之后才能排入游戏主线程。
- 所有 Stardew Valley 对象的读取与修改只能发生在游戏主线程安全点；Socket 线程只能完成收发、认证、校验和入队。
- 同一时刻最多执行一个变更命令。不得存在绕过该限制的第二 Scheduler 或兼容命令通道。
- 即时能力可以从 `ACCEPTED` 直接终态；长时能力必须按公开状态机推进，并遵守取消、Deadline 和终态不可变规则。

## 5. 结果、幂等与恢复

- `SUCCEEDED` 只能在对应能力的游戏侧后置条件已经确认后产生，不能把“输入已发送”当作成功。
- 相同 Command ID 与相同请求必须复用活动状态或缓存结果；相同 ID 与不同请求必须返回 `CONFLICT`。
- 完整 Result 可以在公布的期限后淘汰，但 Tombstone 与请求摘要必须保留到 Mod 进程结束；命中 Tombstone 不得重新执行。
- 连接断开不得自动取消或重放已接受命令。Mod 应继续收敛终态，恢复连接后允许授权 Owner 查询。
- Ref、Revision 和 Snapshot 必须由同一 Mod 实例在游戏主线程生成，并遵守各自的失效条件与原子性。

## 6. 禁止兼容面

公开 V1 Mod 不得初始化或提供以下运行路径：

```text
AdapterV2, CommandProcessor, CompoundDispatcher, Legacy Handler,
FallbackToLegacyMapper, v2-json, WebSocket fallback, File Bridge,
runtime Rendezvous, Hosted Identity Adapter
```

旧仓库可以作为行为证据，但上述类型和通道不能因为新实现尚未补齐依赖而重新进入启动链。

## 7. 可替换的内部实现

本契约不规定 C# 类名、依赖注入框架、具体寻路算法、日志库或目录布局。实现可以自由组织 Host、Transport、Runtime、Capabilities 与 Game Service，只要所有可观测行为、安全边界和一致性门禁仍满足本 Spec。

实现期必须通过 [`conformance/README.md`](conformance/README.md) 中的 Mod、Transport 与命令生命周期门禁，并证明 Registry、Manifest、Proto 和 MCP Tool Catalog 的能力集合一致。
