# ADR-0003：统一命令生命周期与分层执行

- 状态：已接受
- 日期：2026-07-27

## 背景

阶段 3 只实现了观察能力。线路 Proto 与 [`mod-mcp-protocol.md`](../mod-mcp-protocol.md) 已定义 `ACCEPTED`、`RUNNING`、取消、Deadline、状态查询、终态保留和断线收敛，但当前 Mod Handler 都在一个主线程调用中直接返回终态，MCP 也只消费 `ACCEPTED → 终态`。若直接继续添加动作 Handler，取消帧无法在动作运行时被读取，合法的 `RUNNING` 会被 MCP 当作协议错误，Transport/LocalServer 也会再次承担业务状态机。

旧 Star CoPlay 证明了 `say`、`emote`、`face`、`equip` 和菜单动作所需的游戏 API 与逐 Tick 完成判断，但旧 Scheduler、AdapterV2、CommandProcessor、文件桥、Legacy Mapper 和 CompoundDispatcher 混合了协议、兼容和编排职责，不能成为新实现的结构模板。

## 备选方案

### A. 所有命令同步执行

实现最少，但 Socket 读取循环会被终态等待占用，无法可靠处理取消、状态查询、断线恢复和晚到结果。

### B. 所有命令都建立公开异步 Job

生命周期一致，但会迫使查询和简单动作产生无意义的进度与额外 MCP Tool，并引入公开 Job API。

### C. 单一外部状态机，内部分快慢执行路径

所有已接受命令共享同一命令账本、幂等、Deadline、取消、状态查询和结果保留；内部仅由 `CapabilityDescriptor.execution` 选择一次性主线程执行或逐 Tick 推进。

## 决策

采用方案 C。

1. Mod 与 MCP 之间始终只有 [`mod-mcp-protocol.md`](../mod-mcp-protocol.md) 定义的一套命令生命周期，不增加第二套长任务协议、公开 Job 或兼容消息。
2. `execution=immediate` 的命令在一个游戏主线程安全点执行并观察自身最小后置条件，通常不发送 `RUNNING`。`execution=long_running` 的命令在取得游戏执行权后进入 `RUNNING`，由每 Tick 推进的窄 execution continuation 完成。
3. Mod 使用独立 Command Coordinator 维护唯一接受点、命令账本、状态转换、单调 Deadline、取消意图、一个活动变更命令、完整 Result 保留和进程期 Tombstone。Transport 只负责认证 Frame、Session、Fence 和收发。
4. Handler 仍按 Proto operation 编译期显式注册，一个 operation 只有一个 Handler。Handler 可以立即返回终态，或返回只负责游戏推进、完成判断和安全清理的 continuation；它不得读取网络 Session、实现重连、缓存结果或调度其他能力。
5. MCP Tool 调用继续等待终态。Command Runtime 使用单一 Frame 读取与事件分发机制并在内部使用 Cancel/Status 控制消息；V1 不新增 `cancel_command` 或 `get_command_status` MCP Tool。
6. `SUCCEEDED` 只表示该能力在 [`capabilities/behavior.md`](../capabilities/behavior.md) 声明的游戏后置条件已被观察确认，不增加通用 `confirmed` 字段，也不要求每项动作执行一次完整查询。
7. `cancellable=true` 表示命令至少存在一个可取消阶段，不表示已经发生的游戏效果可以回滚。越过能力定义的不可逆提交点后，取消必须以 `accepted=false/CONFLICT` 拒绝；完成、失败或 Deadline 按主线程裁决继续收敛。
8. 断线不自动重复或取消已接受命令。MCP 只用原 `command_id` 恢复 Session、查询状态或重放相同请求；结果未知时不得换新 ID 自动执行变更。
9. Manifest 是 Descriptor 权威源。C# Descriptor Catalog、Python Tool Catalog 与请求类型映射必须由公共 Manifest/Proto 确定性生成；Mod Snapshot 只公告当前 Registry 实际实现的交集，不能为未实现能力建立占位 Handler。

## 阶段 4 能力分类

| 能力 | 执行模式 | 可取消 | 最小成功证据 |
|---|---|---:|---|
| `say` | immediate | 否 | 游戏聊天系统接受完整文本 |
| `emote` | immediate | 否 | 玩家进入请求的 Emote 状态，不等待整个动画结束 |
| `face` | long-running | 是 | 最终朝向与请求一致 |
| `equip` | long-running | 是 | 当前 Slot 与 Item 和请求一致 |
| `open_menu` | long-running | 是 | 目标菜单与新 UI Revision 已观察到 |
| `activate_ui` | long-running | 提交前 | 当前 Revision 上的元素被激活，并观察到新 UI Revision 或关联游戏事实 |
| `close_menu` | long-running | 是 | 菜单为空；原本无菜单允许幂等成功 |

阶段 4 保留 Manifest 现有的 `execution` 与 `cancellable` 值。实机证据若证明某项分类错误，必须按 `VERSIONING.md` 显式修改 Manifest、Fixture 与行为契约，不能在 Handler 中暗中采用不同语义。

## 后果

- 阶段 4 在首个动作能力前必须先完成 Command Coordinator、MCP 事件分发、取消/状态/retention 和生成 Descriptor 收敛。
- 阶段 5 的 `navigate`、`interact` 与 `use_tool` 可以复用同一生命周期，但路径、输入和工具动画仍由各自 Handler 独立实现。
- 进度百分比估算、多变更并发、复合工作流、持久任务队列和跨 Mod 重启恢复不属于阶段 4。
- 旧仓只能提供窄游戏算法和黑盒行为证据；旧类层级与兼容入口不进入新仓。
