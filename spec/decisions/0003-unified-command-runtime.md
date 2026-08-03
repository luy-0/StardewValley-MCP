# ADR-0003：统一命令生命周期与分层执行

- 状态：已接受
- 日期：2026-07-27

## 背景

线路 Proto 与 [`mod-mcp-protocol.md`](../mod-mcp-protocol.md) 定义了 `ACCEPTED`、`RUNNING`、取消、Deadline、状态查询、终态保留和断线收敛。观察查询可以在一个主线程调用中直接返回终态，但导航、交互和工具使用必须跨多个 Tick 推进；两类能力需要共享同一外部生命周期，同时保持 Transport、命令运行时和游戏能力职责分离。

## 备选方案

### A. 所有命令同步执行

实现最少，但 Socket 读取循环会被终态等待占用，无法可靠处理取消、状态查询、断线恢复和晚到结果。

### B. 所有命令都建立公开异步 Job

生命周期一致，但会迫使查询和简单动作产生无意义的进度与额外 MCP Tool，并引入公开 Job API。

### C. 单一外部状态机，内部分快慢执行路径

所有已接受命令共享同一命令账本、幂等、Deadline、取消、状态查询和结果保留；内部仅由 `CapabilityDescriptor.execution` 选择一次性主线程执行或逐 Tick 推进。

## 决策

采用方案 C。

1. Mod 与 MCP 之间始终只有 [`mod-mcp-protocol.md`](../mod-mcp-protocol.md) 定义的一套命令生命周期，不增加第二套长任务协议或公开 Job。
2. `execution=immediate` 的命令在一个游戏主线程安全点执行并观察自身最小后置条件，通常不发送 `RUNNING`。`execution=long_running` 的命令在取得游戏执行权后进入 `RUNNING`，由每 Tick 推进的窄 execution continuation 完成。
3. Mod 使用独立 Command Coordinator 维护唯一接受点、命令账本、状态转换、单调 Deadline、取消意图、一个活动变更命令、完整 Result 保留和进程期 Tombstone。Transport 只负责认证 Frame、Session、Fence 和收发。
4. Handler 仍按 Proto operation 编译期显式注册，一个 operation 只有一个 Handler。Handler 可以立即返回终态，或返回只负责游戏推进、完成判断和安全清理的 continuation；它不得读取网络 Session、实现重连、缓存结果或调度其他能力。
5. MCP Tool 调用继续等待终态。Command Runtime 使用单一 Frame 读取与事件分发机制并在内部使用 Cancel/Status 控制消息；V1 不新增 `cancel_command` 或 `get_command_status` MCP Tool。
6. `SUCCEEDED` 只表示该能力在 [`capabilities/behavior.md`](../capabilities/behavior.md) 声明的游戏后置条件已被观察确认，不增加通用 `confirmed` 字段，也不要求每项动作执行一次完整查询。
7. `cancellable=true` 表示命令至少存在一个可取消阶段，不表示已经发生的游戏效果可以回滚。越过能力定义的不可逆提交点后，取消必须以 `accepted=false/CONFLICT` 拒绝；完成、失败或 Deadline 按主线程裁决继续收敛。
8. 断线不自动重复或取消已接受命令。MCP 只用原 `command_id` 恢复 Session、查询状态或重放相同请求；结果未知时不得换新 ID 自动执行变更。
9. Manifest 是 Descriptor 权威源。C# Descriptor Catalog、Python Tool Catalog 与请求类型映射必须由公共 Manifest/Proto 确定性生成；Mod Snapshot 只公告当前 Registry 实际实现的交集，不能为未实现能力建立占位 Handler。

## 能力分类

| 能力 | 执行模式 | 可取消 | 最小成功证据 |
|---|---|---:|---|
| `say` | immediate | 否 | 游戏聊天系统接受完整文本 |
| `emote` | immediate | 否 | 玩家进入请求的 Emote 状态，不等待整个动画结束 |
| `face` | long-running | 是 | 最终朝向与请求一致 |
| `equip` | long-running | 是 | 当前 Slot 与 Item 和请求一致 |
| `open_menu` | long-running | 是 | 目标菜单与新 UI Revision 已观察到 |
| `activate_ui` | long-running | 提交前 | 当前 Revision 上的元素被激活，并观察到新 UI Revision 或关联游戏事实 |
| `close_menu` | long-running | 是 | 菜单为空；原本无菜单允许幂等成功 |

Manifest 是 `execution` 与 `cancellable` 的权威来源。实机证据若证明某项分类错误，必须按 `VERSIONING.md` 显式修改 Manifest、Fixture 与行为契约，不能在 Handler 中暗中采用不同语义。

## 后果

- `navigate`、`interact` 与 `use_tool` 复用同一命令生命周期，但路径、输入和工具动画仍由各自 Handler 独立实现。
- 进度百分比估算、多变更并发、复合工作流、持久任务队列和跨 Mod 重启恢复不属于 V1。
- Transport、Command Runtime 和具体能力 Handler 必须保持单向依赖，不能互相承担对方职责。
