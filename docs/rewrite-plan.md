# 公开版本重写计划

状态：**阶段 4 简单交互能力已完成，下一步进入阶段 5 长时运行能力**

本计划定义如何以旧仓库已经验证的游戏行为为参考，重新实现一套适合公开发布、独立安装和长期维护的 Mod、MCP 服务端与 Skill 开发套件。它不是旧代码搬迁清单；旧仓库只提供行为证据、失败经验和测试素材，不是新版本的架构模板。

## 一、任务定性

这次工作的正式名称是“公开版本重写”，而不是“仓库迁移”。二者的验收标准不同：迁移追求旧系统在新位置继续运行，重写则要求每项公开行为先通过新契约重新获得存在理由，再由不依赖历史兼容层的新实现提供。

因此，本项目遵循以下规则：

1. 不复制旧目录后逐步删除，而是在空目录中按新边界建立实现。
2. 不以旧类型依赖为保留理由；依赖只说明新实现尚未补齐。
3. 不承诺兼容尚未公开的 V2、Legacy 或私有平台协议。
4. 只复用经过审计、边界清晰且不携带历史类型的算法；其余行为依据 Spec 重写。
5. 每项能力必须以“Spec → Fixture → Mod → MCP → 端到端测试”的纵向切片交付。

## 二、旧系统提供什么

旧仓库中的内容按用途分为四类：

| 旧资产 | 新版本中的用途 | 处理方式 |
|---|---|---|
| 已验证的游戏行为与边界条件 | 需求证据 | 提炼为契约、fixture 和黑盒测试 |
| Protocol `2.4.5` Proto 与 18 项发布面 | 迁移基线 | 逐项复审，不自动成为公开 V1 |
| 纯游戏算法，如扫描、投影、寻路和输入协调 | 候选实现素材 | 完成依赖审计后重写或小范围移植 |
| AdapterV2、CommandProcessor、CompoundDispatcher、文件桥、Legacy Mapper、私有平台 Adapter | 历史债务证据 | 不迁入新仓，不提供兼容入口 |

“源码可复用”必须同时满足：不引用旧命名空间、不读取私有平台上下文、不承担多个架构职责，并且可以由新契约测试独立证明。仅仅已经在线上运行过，不满足复用条件。

## 三、目标产品边界

公开版本只包含以下五部分：

```text
spec    公共契约和代码生成输入
mod     SMAPI 内的游戏侧运行时
mcp     独立 MCP 服务端
skill   第三方 Skill SDK、模板、最小示例和测试工具
docs    安装、使用、架构和贡献文档
```

私有 Platform、Hosted Gateway、Agent Runtime、Persona、用户身份、计费和运营系统不进入公开仓库。未来私有平台如果接入公开 Mod/MCP，只能作为公开扩展端口的一个消费者，不能反向污染公共 Core。

## 四、版本策略

公开版本使用新的版本历史，不沿用内部迭代编号制造虚假的兼容承诺：

- 仓库和产品从 `0.x` 预览版开始，契约稳定后发布 `1.0.0`。
- 公共 Proto 使用新的 V1 package；Protocol `2.4.5` 只保留为行为对照基线。
- 新 Mod 与新 MCP 成对重写，不提供 V2 JSON、Legacy Handler 或旧文件协议回退。
- 首个公开稳定版之前允许删改草案字段，但每次变更仍须更新 fixture 与一致性测试。

在进入实现前，必须先把当前 `spec/` 从“2.4.5 迁移镜像”收敛为“公共 V1 候选契约”。未列入最终能力面的 Proto 分支应删除并保留编号，不能长期以“暂时内部”方式悬置。

## 五、目标架构

### 5.1 总体主链

```text
MCP 客户端
    │ stdio
    ▼
独立 MCP 服务端
    │ 经过认证的本地双向连接
    ▼
SMAPI Mod
    │ 主线程调度
    ▼
能力 Handler → Game Service → Stardew Valley
```

这条主链中只有一种命令模型、一套生命周期和一个能力注册表。不得为了过渡并行初始化旧 Processor、旧 Dispatcher 或第二套 Handler。

### 5.2 Mod 内部边界

Mod 建议划分为以下层次：

| 层次 | 职责 |
|---|---|
| Host | SMAPI 生命周期、配置、日志和依赖组装 |
| Transport | 本地连接、认证、帧收发，不解释游戏语义 |
| Protocol | Proto 编解码、Fence、命令状态与结果缓存 |
| Runtime | 能力注册、并发规则、Deadline、取消和主线程调度 |
| Capabilities | 每项公开能力唯一的 Handler |
| Game | 输入、导航、查询、投影等可测试游戏服务 |

新的 `ModEntry` 只负责组装上述组件和转发 SMAPI 事件，不得直接注册几十项业务分支，也不得持有兼容开关。一个公开能力在编译期只能对应一个 Handler。

### 5.3 MCP 内部边界

MCP 建议划分为以下层次：

| 层次 | 职责 |
|---|---|
| Server | MCP stdio 生命周期与 Tool/Resource 暴露 |
| Catalog | 从公共 Manifest 构造确定性的能力目录 |
| Projection | Proto 参数/结果与 MCP Schema/结果之间的转换 |
| Runtime | 命令身份、等待、取消、结果收敛与稳定错误 |
| Transport | 连接 Mod，不了解 MCP Tool 定义 |
| Skill Runtime | 校验和执行第三方 Skill，只依赖公共 SDK |

新 MCP 必须是可独立安装的软件包，不得导入旧仓库中的 `agent.protocol`、`runtime_manager`、私有观测模块或 Hosted Credential。平台接入能力留在仓库之外。

### 5.4 本地传输方向

必须彻底移除“文件作为消息总线”的设计，包括命令轮询、状态文件、事件文件、`adapter_info.json` 和一次性 Rendezvous 文件。静态 `config.json` 与普通日志文件不属于文件桥，但不能承载运行时命令或所有权转移。

阶段 0 已决定由 Mod 持有固定配置的 loopback TCP Listener，MCP 作为 Client 主动连接，并通过高熵共享秘密完成挑战认证。线路采用 4 字节大端长度前缀加二进制 Proto，不使用 WebSocket、JSON 业务帧或运行时 Rendezvous。该方向避免 MCP 每次启动都通过文件通知 Mod 新 Endpoint，也使 Mod 生命周期与游戏生命周期一致；可复现 Spike 与决策理由见 `spec/decisions/0002-local-transport.md`。

本地模式仍必须具备单 Owner、Lease Epoch、能力摘要、Deadline、取消、幂等命令和 `UNKNOWN` 结果收敛，不能把 loopback 当作认证。

## 六、能力裁决结果

阶段 0 已把 18 项历史候选收敛为 15 项公开 V1 原语。机器可读裁决见 `spec/capabilities/adjudication.yaml`，理由见 `spec/capabilities/decisions.md`，最终能力面只以 `spec/capabilities/manifest.yaml` 为权威。

```text
say, emote, face, navigate, interact, use_tool, equip,
open_menu, activate_ui, close_menu,
query_runtime, query_world, query_inventory, query_ui, inspect
```

其中 `move_to` 与 `go_to` 合并为 `navigate`，`query_menu` 合并到 `query_ui`，`menu_click` 被带 UI Revision 的 `activate_ui` 取代。`go_to_bed` 不进入 V1 Mod/MCP 原语，也不在本轮提供官方 Skill；`tp`、Compound Capability 与旧查询别名均不进入公共 Proto。

## 七、交付阶段

### 阶段 0：冻结公开 V1 候选契约

任务（已完成）：

1. [x] 完成 18 项能力裁决表，明确保留、删除或降级原因。
2. [x] 设计最小 Envelope、Handshake、Command、Result、Cancel 和 Status。
3. [x] 完成本地传输 Spike，决定 Listener 归属和线路承载方式。
4. [x] 去除 Hosted 身份字段和重复身份字段，只保留本地安全真正需要的 Fence。
5. [x] 把 Proto package、Manifest、错误码、状态机和 Fixture 更新为公共 V1 候选。

退出条件：Spec 中不存在未裁决能力；新协议不提供 Legacy/V2/File Bridge 模式；C# 与 Python 均可从同一份 Proto 生成并通过 Schema 测试。

完成记录：阶段 0 已通过多轮独立审查与最终验收；审查过程已经归档到非产品目录 `agent_workspace/reviews/phase0/`，正式结论均已落实到 Spec 与一致性测试。后续实现若要求改变候选契约，必须按 `spec/VERSIONING.md` 重新作出版本判断并更新 Fixture，不能在代码中建立隐式例外。

### 阶段 1：建立可独立构建的新骨架

任务（已完成）：

1. [x] 建立干净的 Mod Solution、测试项目和构建脚本。
2. [x] 建立独立 Python Package、CLI、锁定依赖和测试入口。
3. [x] 建立统一 Proto 代码生成、格式检查和跨系统 CI。
4. [x] 加入禁止依赖、机器路径泄露与 Spec 一致性检查。
5. [x] 固化 MCP Wheel、Mod ZIP 内容审计与统一回归入口。

退出条件：干净检出后无需旧仓库即可构建、测试和打包；生产源码中禁止词扫描为零。

完成记录：已在全新的临时目录执行 `./scripts/verify.sh --with-mod`，证明代码生成、Spec 一致性、Transport Spike、C# 测试、Python 测试、Mod 构建、MCP 打包和发布包审计均不依赖旧仓库。当前门禁包含 4 项 C# 契约测试、7 项 Python 协议与 MCP 测试，以及公开源码历史依赖和机器专属路径扫描。Mod ZIP 只允许 Manifest、Mod、Protocol 与 Protobuf 四个运行文件，MCP Wheel 与源码包必须包含服务端、Transport、生成协议与 `query_runtime` Tool Schema。

### 阶段 2：贯通第一个真实纵向切片

以 `query_runtime` 作为第一条端到端能力，任务（已完成）：

1. [x] 完成基于共享秘密的本地认证、能力摘要与单 Owner 会话。
2. [x] 通过新 Proto 发送命令，并由 Mod 在 SMAPI 主线程读取游戏状态。
3. [x] 把结构化结果投影为唯一的 MCP Tool `stardew_query_runtime`。
4. [x] 覆盖成功、`not_ready`、HMAC、Frame 与 MCP 标准会话测试。
5. [x] 在真实游戏存档中执行一次只读调用并通过 Output Schema 校验。

退出条件：一个全新安装的 MCP 客户端能通过新链路读取实际游戏状态；过程中没有文件消息桥、旧 Processor 或私有平台依赖。

完成记录：2026-07-26，SMAPI 成功加载 `0.1.0-alpha.1` Mod 并在 loopback 端口启动新协议 Listener。标准 MCP `ClientSession` 只发现 `stardew_query_runtime`，一次真实调用返回 `succeeded`，读取到当前存档的日期、时间、玩家位置、资源、天气与 UI 摘要，完整结果通过公开 Output Schema。共享秘密只在本地进程内读取，未输出、未写入测试文件或发布包。

### 阶段 3：完成观察能力

阶段 3 交付 `query_world`、`query_inventory`、`query_ui` 与 `inspect`，并把 `query_runtime` 一并纳入当前构建的五项只读能力目录。旧 StarCoplay 只提供游戏行为证据：保留其“从游戏内存投影事实、真实 Slot、定位符与 Guard 校验、单 Ref 失败不拖垮整批”的有效经验；删除旧 `observe` 聚合、自然语言摘要、类别别名、Agent 内存分页、字符串容器 ID、动态 Ref Dispatcher、V2 DTO 与 Legacy/Bridge 宿主依赖。

阶段 2 的 `query_runtime` 代码是首链 Bootstrap，不是第二项能力可以直接复制的模板。阶段 3 正式写 Handler 前必须先完成一次结构收敛：

- Tool Schema 只保留一个生成产物 `mcp/src/stardew_valley_mcp/generated/tool_catalog.json`，Catalog 包含完整公共 Tool 定义；运行时按公共 Manifest、MCP 支持、Mod 握手公告和权限策略的交集决定 `list_tools`。
- Transport 只负责 loopback TCP、HMAC、Frame、Session 与 Fence，不保存具体能力 digest/timeout，不构造具体请求，也不判断具体结果分支。
- Command Runtime 统一处理 Command ID、ACCEPTED/终态、Deadline、断线未知结果和错误映射；Catalog 管能力元数据；Projection 使用 Proto Descriptor 与生成 Enum 映射完成默认 Proto→JSON 转换，只有契约确实不同的字段才使用小型覆盖。
- Mod 的 Local Server 只完成认证、帧收发、队列与主线程交接；编译期显式 Capability Registry 把每个 Proto operation 映射到唯一 Handler，不使用反射扫描、字符串动态注册、Legacy fallback 或复合编排。

开发计划：

1. [x] 调查旧 Mod 的世界扫描、库存、UI、Inspect/Ref 实现，以及旧调用方的组合方式和卡顿记录。
2. [x] 冻结阶段 3 边界：V1 不增加 `observe` 聚合能力、不增加查询别名、不增加分页 Cursor；无法识别的 Ref 使用现有 `UNSUPPORTED/INVALID_ARGUMENT`，不扩展新的 `malformed` 状态。
3. [x] 冻结性能预算：成功帧小于 768 KiB；默认世界查询目标不超过 16 ms，最大合法区域不超过 50 ms；其余观察 Handler 目标不超过 16 ms。
4. [x] 完成上述阶段 2.5 结构收敛，并通过架构边界测试；在此之前不得新增第二个游戏 Handler。
5. [x] 实现进程内不透明 Ref Store，以及 World、Inventory、UI 三类 Revision；调用方不得解析 Ref，Mod 不根据外部字符串猜测 Kind。
6. [x] 按 `query_world → query_inventory → query_ui → inspect` 完成四条纵向切片；每条同步交付 Spec Fixture、唯一 Mod Handler、默认 Descriptor Projection 和测试，不预建通用游戏查询框架。
7. [x] 为四项能力补齐最小、完整、非法、成功与失败覆盖；固定五项观察能力的握手 Snapshot、HMAC、Fence 和生命周期场景。
8. [x] 通过跨语言协议测试、C#/Python 单元与标准 MCP 会话测试、边界扫描和 Mod 构建。
9. [x] 部署新 Mod 后通过标准 MCP Session 逐项执行真实调用；验证世界、玩家与容器库存、无菜单 UI、真实与不存在 Ref 的混合 Inspect，并记录 Handler 耗时与结果字节数。有菜单 UI 由离线 Fixture 覆盖，不作为本阶段实机阻塞项。

阶段 2.5 完成记录：MCP 已拆分为唯一生成 Catalog、Descriptor Projection、通用 Command Runtime 与纯 Transport，并以公共 Manifest、MCP 支持集、Mod 公告集和权限策略四方交集决定 Tool；Mod 已用编译期显式 Registry 取代 `LocalServer` 的单能力分支，并校验 Handler ID、Proto operation 与 Request Type 一致。结构门禁同时覆盖握手 Deadline、typed request 错配、未知 Enum、活动命令重放和已完成命令的缓存终态收敛；第二轮独立审查未发现 P0/P1 阻塞。

实现顺序以依赖而不是旧目录划分。`query_world` 先提供 World Entity/Character Ref，`query_inventory` 再基于容器 World Ref 提供库存视图与 Item Ref，`query_ui` 提供 Revision 绑定的 Element Ref，最后由 `inspect` 统一验证所有 Ref Kind。MCP 只投影原始结构化事实；任何面向模型的摘要、搜索、聚合或玩法工作流留给未来 Skill/客户端层。

实机验证可以调用项目级 `launch-stardew-game` Skill：先通过统一构建入口生成 Mod，再以独立 SMAPI 进程和精确测试存档进入游戏，避免复用或干扰其他游戏进程。该 Skill 只负责隔离启动与进入存档；`query_world`、`query_inventory`、`query_ui` 和 `inspect` 仍需逐项执行真实 MCP 调用，并以专用日志、Handler 耗时、结果字节数和协议结果作为阶段 3 验收证据。

阶段 3 完成记录：2026-07-27，隔离 SMAPI 实例自动加载测试存档后，标准 MCP `ClientSession` 发现且仅发现五项观察 Tool。`query_runtime`、`query_world`、`query_inventory`、`query_ui` 与 `inspect` 均返回 `succeeded`；默认世界查询返回 10 个实体，玩家背包返回 5 个非空 Slot，容器查询返回 36 个 Slot 和 1 个非空 Slot，来自世界与背包的真实 Ref 均被 `inspect` 解析，不存在的 Ref 以单项 `not_found` 返回且未中断批次。专用 SMAPI 日志同时记录了每项 Handler 的耗时和序列化字节数；预热后的默认调用为 `query_world` 11–16 ms、`query_inventory` 2–4 ms、`query_ui` 4 ms、`inspect` 2 ms，结果均远小于 768 KiB。首次世界查询包含运行时冷启动开销，记录为 171 ms，不为这一罕见样本扩展架构；有菜单 UI、异常对象投影和 `FACT_UNAVAILABLE` 继续由 Fixture 与单元测试覆盖。

退出条件：所有保留的观察能力拥有最小、完整、非法参数、成功和失败 fixture，并通过 C#/Python 交叉测试与实机性能门禁。

### 阶段 4：完成简单交互能力

阶段 4 重写 `say`、`emote`、`face`、`equip`、`open_menu`、`activate_ui` 与 `close_menu`。旧仓只提供游戏 API、完成判断和失败场景证据：旧 `menu_click` 被 `activate_ui` 的 `UI Revision + Element Ref` 取代，旧 `menu_close` 重写为 `close_menu`；不保留旧别名、坐标猜测、Input Handler 注册表或 Agent 侧组合参数。

阶段 3 的运行时只能处理一次主线程调用直接到终态。阶段 4 不在这套模型上逐项打补丁，而是先按 [ADR-0003](../spec/decisions/0003-unified-command-runtime.md) 落地“单一外部状态机、内部分层快慢路径”：

- 所有已接受命令共享同一 `command_id`、账本、Deadline、取消、状态查询、终态保留和 Tombstone；`execution` 只选择 immediate 或逐 Tick runner，不建立 Capability 类层级。
- Mod 将命令生命周期从 `LocalServer` 抽到独立 Command Coordinator。`LocalServer` 只负责 TCP、认证、Frame、Session、Fence 和单写出队列；Coordinator 不解释具体 operation，Handler 不解释网络与重连。
- MCP Command Runtime 使用唯一 Frame 读取与事件分发机制，同时消费 `RUNNING`、终态、Cancel 与 Status 响应。MCP Tool 调用仍等待终态，不增加公开的 cancel/status Tool。
- C# Descriptor Catalog、Python Tool Catalog 与请求类型映射由 Manifest/Proto 确定性生成；Mod Snapshot 只广告实际注册 Handler。不得继续维护 `ObservationDescriptors`、观察专用 `_operation_for` 或在 Server/Transport 中加入能力特判。
- `SUCCEEDED` 表示该能力自己的游戏后置条件已经观察成立，而不是“输入已发送”。每个 Handler 只实现窄的 completion policy，不新增通用确认服务或结果布尔值。
- V1 同时最多一个变更命令；只读查询可在安全 Tick 穿插，但不能看到半个 Tick 的中间写入。断线后只使用原 Command ID 恢复/查询，未知结果不得换新 ID 自动重做。

#### 能力与验收重点

| 能力 | 执行/取消 | 关键成功证据 |
|---|---|---|
| `say` | immediate，不可取消 | 游戏聊天系统接受完整文本；Unicode Scalar 长度正确 |
| `emote` | immediate，不可取消 | 玩家进入请求 Emote 状态；不等待整个动画结束 |
| `face` | long-running，可取消 | 最终方向匹配；已匹配返回 `changed=false` |
| `equip` | long-running，可取消 | 主线程重验玩家背包 Ref/Revision，最终 Slot 与 Item 匹配 |
| `open_menu` | long-running，可取消 | 目标菜单类型与新 UI Revision 已观察到 |
| `close_menu` | long-running，可取消 | 菜单为空；无菜单幂等成功，强制 Modal 返回 `NOT_READY` |
| `activate_ui` | long-running，仅提交前可取消 | 重验 Element Ref/Revision/visible/enabled，一次激活后观察到新 Revision 或关联游戏事实 |

#### 开发顺序

1. [x] **阶段 4.0：运行时基础。** 生成全量 C# Descriptor 和通用 Python Request 映射；拆出 Mod Command Coordinator 与 MCP 事件分发；实现 `RUNNING`、Cancel、Status、Deadline、Result retention/Tombstone。用 fake immediate 与 fake staged execution 覆盖状态竞争，不提前实现游戏动作。
2. [x] **阶段 4.1：`face`。** 作为第一个低风险 staged slice，验证 `RUNNING`、取消、Deadline、输入清理和最终朝向。
3. [x] **阶段 4.2：`say → emote`。** 验证 immediate mutation、`game:write`、外部沟通风险，以及“效果开始即成功”而非等待动画结束。
4. [x] **阶段 4.3：`equip`。** 验证 Slot/Item Ref 二选一、Inventory Revision、玩家背包来源、空 Slot、stale Ref 与 no-op。
5. [x] **阶段 4.4：`open_menu → close_menu → activate_ui`。** 先建立可恢复的菜单开闭，再处理带 Revision 的元素激活；覆盖 UI Scale、Modal、旧 Revision、不可见/禁用元素和潜在破坏性操作。
6. [x] 每个 slice 均按 `Spec/Fixture → 唯一 Mod Handler → MCP 调用 → 自动化 → 单条实机验收` 完成后再进入下一项；失败立即停在当前 slice 取证，不批量执行七项动作。

阶段 4.0 完成记录：2026-07-27，Mod 已将唯一接受点、幂等账本、单变更并发、主线程 immediate/staged 推进、Deadline、Cancel、Status、300 秒结果保留与进程期 Tombstone 收敛到独立 `CommandCoordinator`；`LocalServer` 只保留认证、Fence、控制帧路由和单一有界写出队列，并通过接受响应门闩保证 `ACCEPTED` 一定先于后续主动事件入队。MCP 已由唯一 Reader 按 `command_id` 与 `reply_to` 分发重复 `RUNNING`、终态和控制响应；客户端取消已接受的可取消调用时发送内部 Cancel，断线后只用原 Command ID 查询 Status，`found=false` 与结果过期均收敛为 `unknown`，不会重放命令。Manifest/Proto 现在确定性生成 15 项 C# Descriptor、完整 Tool Catalog，并通过 `CommandRequest` Descriptor 动态取得 Python Request 类型；默认仍只授权 `game:read`，显式加入 `game:write` 且 Mod 公告动作 Handler 后才会暴露动作 Tool。本阶段未实现任何真实动作 Handler，也未修改 V1 Proto。

验证结果：生成检查与 Spec conformance 通过，跨语言生命周期 Fixture 覆盖 `RUNNING`、`CANCELLED`、`TIMED_OUT` 和过期 Tombstone；Python 59 项、Protocol 10 项、Mod 64 项测试通过，Transport Spike、公共边界扫描、MCP Wheel/源码包与 Mod ZIP 审计、`git diff --check` 全部通过。最终独立交叉审查未发现 P0/P1/P2，阶段 4.1 可以直接以 `face` 验证首个真实 staged action。

阶段 4 完成记录：2026-07-27，七项简单交互能力均通过公共 Manifest、唯一 Mod Handler 与通用 MCP Command Runtime 交付；默认 `serve` 仍只暴露五项只读能力，显式 `serve --allow-write` 后按 Manifest、MCP 支持集、Mod 公告和权限策略交集暴露十二项已实现能力。Mod 在收口时进一步按 `Bootstrap`、`Transport`、`Runtime`、`Capabilities/Queries`、`Capabilities/Actions`、`Projection` 与 `References` 建立职责目录，具体 Handler 只由 `Bootstrap/DefaultCapabilitySet` 组装，Registry 不再构造业务实现。

实机验收使用隔离 SMAPI 进程加载 `TestAgent_434178162` 存档，玩家 `Sea` 位于 `FarmHouse`。标准 MCP Session 已验证中英文 `say`、心形与音乐 `emote`、`face` 朝向变化、按真实 Item Ref 与 Inventory Revision 装备 Scythe、打开背包、按 UI Ref 与 Revision 激活 Skills 页签、关闭菜单，以及 `equip`、`activate_ui` 的 stale Revision 失败路径；后续查询确认朝向、当前工具、菜单类型和 Revision 均发生预期变化。自动化最终结果为 MCP 63 项、Protocol 10 项、Mod 104 项测试通过，生成检查、Spec conformance、Transport Spike、公共边界扫描、Wheel/sdist、Mod ZIP、包审计与 `git diff --check` 全部通过。

阶段 4 明确不实现 `navigate`、`interact`、`use_tool`、复合农务、持久队列、多变更并发、公开命令历史 Tool 或进度百分比估算。这些要么属于阶段 5，要么不进入 Mod/MCP 原语层。

退出条件：七项能力均只通过新 Registry/Handler 暴露；`RUNNING`、取消、Deadline、断线恢复、状态查询和结果淘汰拥有跨语言 Fixture 与 Mod/MCP 测试；每项能力都能证明最终游戏效果；`equip` 与 `activate_ui` 的 Ref/Revision 过期路径可复现；菜单能力在不同 UI Scale 和关键菜单状态下有回归用例；至少一次标准 MCP Session 真实调用覆盖 immediate、可取消 staged 和 UI Ref 变更三类路径。

### 阶段 5：完成长时运行能力

阶段 5 交付 `navigate`、`interact` 与 `use_tool`，并完成公开 V1 的十五项原语能力面。旧 StarCoplay 已经为这三项能力提供了足够多的实机行为、修复历史和可提炼算法，因此本阶段不是从零探索游戏机制；但旧实现仍与 Protocol 2.4.5、共享可变 Handler、旧 Scheduler、全局输入桥和并行 Legacy/V2 注册链绑定，不能整文件搬入新仓。

#### 旧仓审计结论

| 旧资产 | 当前进度与价值 | 阶段 5 处理 |
|---|---|---|
| 正式 `MoveToProtoHandler` / `GoToProtoHandler` | 已实现同图精确/相邻移动、跨图 BFS、正常 Warp、门交互、地图稳定等待、取消与失败清理，并经历多轮实机修复 | 复用行为和失败用例；重写为唯一 `NavigateHandler`，不迁移旧 Handler 或子 Handler 调用结构 |
| `MapData` / `GoToRoutePlanner` | 已能从运行时 Warp、Door 和建筑内部构造 Location 图 | 提炼为命令开始时的只读拓扑 Snapshot 与纯 Route Planner；删除静态全局状态、显示名表、兼容字段和配置型别名 |
| `ActionExecutor.NavigateTo` | 已验证 `PathFindController` 可完成正常游戏寻路 | 保留 PFC 机制；重写严格到达判断。旧代码把距离目标两格内也视为成功，这一行为不得复用 |
| `InteractProtoHandler` / `TileActionService` | 已验证“面朝 → Grab Tile 对齐/微移 → 提交一次动作”的时序 | 提炼对齐算法和提交阶段；删除 `no_observed_effect`、通用 `player_busy` 也算成功的错误准出 |
| `UseToolProtoHandler` / 蓄力逻辑 | 已验证普通工具、Hoe/Watering Can 蓄力和输入释放的基本路径 | 提炼蓄力达到实际 `toolPower` 后释放的机制；重写实际工具锁存、动作接受、完成观察、取消和结果采集 |
| `InputBridge` / `InputCombo` | 解决过失焦、按住、释放和 sticky key，但同时承担全局队列、SMAPI 私有反射和 simulator 重装 | 不迁移。优先调用游戏公开语义 API；确需按住时只建立命令私有的窄输入端口，不反射 SMAPI 内部状态，不建立第二套队列 |
| 旧测试与实机脚本 | 提供了大量故障案例，但正式三个 Proto Handler 几乎没有可隔离自动化测试 | 把故障历史改写为新 Fixture、Fake Game Port 测试和逐条实机用例；旧测试结果不作为新仓完成证明 |

审计后的直接复用边界很明确：新仓的 `CommandCoordinator`、`ICommandContinuation`、`OpaqueRefStore`、Descriptor Catalog、MCP Command Runtime 和 `DefaultCapabilitySet` 可以原样继续使用；旧仓没有一份阶段 5 生产文件适合整文件复制。旧代码仍然显著降低了重写风险，因为 PFC、出口图、两类 Warp、Grab Tile 对齐、蓄力与清理顺序都已经有真实失败记录可供实现和测试使用。

#### 设计边界

1. 三项能力各自只有一个公开 Handler，并继续由 `DefaultCapabilitySet` 编译期显式组装。`navigate` 内部可以使用同图移动、路由和门触发服务，但不能调用 `InteractHandler`；`interact` 与 `use_tool` 也不能隐式调用 `navigate` 或 `equip`。
2. 每条命令只有现有 Coordinator 管理的一份生命周期、一个 Deadline 和一个 Command ID。Handler 只返回 continuation phase，不建立子命令、内部 Scheduler、第二套超时总钟或自动重放。
3. 游戏机制与协议 Handler 分层。计划新增 `Game/Navigation` 保存拓扑、纯路由、PFC 与 Warp 驱动，新增 `Game/Actions` 保存动作提交和工具生命周期探针；这些服务不依赖 Transport、MCP 或 Runtime，也不持有跨命令的可变动作队列。
4. `Capabilities/Actions` 负责请求校验、Ref 解析、命令私有状态机和结果组装。`Game` 服务只接收已经解析的 Location、Tile、方向和工具，不解析 Proto Ref，也不猜测显示名。
5. 目标身份只接受公开 `WorldPosition` 或进程内不透明 Ref。Location 一律使用 `NameOrUniqueName` 并按 `StringComparison.OrdinalIgnoreCase` 比较；不引入字符串 TargetRef、中文地图名、模糊搜索或坐标 fallback。
6. `SUCCEEDED` 只表示 Spec 中该能力自己的后置条件已经观察成立。输入已排队、PFC 已停止、玩家暂时 busy 或动画回到 idle，都不能单独作为成功。
7. 首版主动接受少量清晰边界，不为覆盖所有罕见状态重建旧复杂度。卡住最多在同一合法路径上有限重算一次；失败后清理并返回稳定错误，不传送、不改碰撞、不追逐移动角色、不自动装备或自动接近目标。

建议的内部依赖如下：

```text
DefaultCapabilitySet
  ├─ NavigateHandler
  │    └─ NavigateContinuation
  │         ├─ ActionTargetResolver
  │         ├─ WorldRouteSnapshot + RoutePlanner
  │         ├─ LocalPathDriver
  │         └─ WarpDriver
  ├─ InteractHandler
  │    └─ InteractContinuation
  │         ├─ ActionTargetResolver
  │         └─ PlayerActionDriver
  └─ UseToolHandler
       └─ UseToolContinuation
            ├─ ActionTargetResolver
            └─ PlayerActionDriver + ToolUseLifecycleProbe
```

这里的 `ActionTargetResolver` 是动作层对现有 `OpaqueRefStore` 的窄适配，不是新的 Ref 注册表。`PlayerActionDriver` 也只是调用游戏语义动作并暴露“是否提交、是否释放、是否收敛”的端口，不拥有全局队列；每个 continuation 保存自己的阶段与清理状态。

#### 目标与 Ref 的首版语义

- `WorldPosition` 在命令主线程启动时校验 Location 与 Tile；`navigate` 可以指向其他已知 Location，`interact` 与 `use_tool` 必须指向玩家当前 Location。
- Ref 只允许 `WORLD_ENTITY` 或 `CHARACTER`。Resolver 必须从绑定的当前对象重新取得 Location 和动作 Tile，不能使用 Token 字符串、显示名或签发时缓存坐标猜测目标。
- Ref 导航只允许 `ADJACENT`。首版在命令启动时固定一次目标 Location/Tile，并在提交动作或返回成功前重验同一对象仍存在且位置未变化；Character 在执行中移动时返回 `EXECUTION_FAILED`，不复制 Legacy 的定时追踪、次数上限或跨图追逐。后续若需要持续跟随，应作为新的版本化行为单独设计。
- 启动前已经失效的 Ref 返回 `STALE_REF`，合法但找不到目标返回 `NOT_FOUND`，Kind 不适用返回 `INVALID_ARGUMENT`；启动后目标消失、移动或不再满足到达/作用条件返回 `EXECUTION_FAILED`，不得用旧位置成功收口。
- `NavigateResult.resolved_destination` 表示本次实际锁定的玩家落脚 Tile；`route_location_ids` 记录实际到达的 Location 顺序，而不是尚未执行的规划路线。

#### 三项能力的明确实现

**`navigate`**

- 同图 `EXACT` 使用 PFC 正常寻路，只有最终 Location 与 Tile 完全相等才能成功；PFC 提前结束但未到达必须失败。
- `ADJACENT` 未指定 `stand_side` 时，从四个合法相邻 Tile 中选择可达项；指定后只允许该侧，不得悄悄换边。到达后再独立验证 `face_on_arrival`。
- 跨图在命令开始时从当前已加载 Location、Warp、Door 和建筑内部构造拓扑 Snapshot，以纯 BFS 得到出口序列。每条边保留具体触发 Tile、目标 Location 和 `WalkThrough`/`InteractDoor` 类型，不把同一地图对的多个出口合并丢失。
- `WalkThrough` 复用旧仓“候选 Warp Tile + 有界方向尝试”的经验；`InteractDoor` 通过内部 `PlayerActionDriver` 触发图中已确定的门 Tile，不调用公开 `interact`。只有观察到预期目标 Location 才算该边完成，进入错误地图立即失败。
- 每次 Warp 后等待目标 Location、玩家可控、无阻塞 Menu/工具状态和连续稳定帧，再继续下一段。取消或 Deadline 必须清除 PFC、移动方向和尚未提交的输入；任何恢复都不得调用 `warpFarmer`。

**`interact`**

- 只作用于当前 Location 中游戏允许的 cardinal-adjacent Tile，不隐式导航。提交前依次完成目标重验、玩家状态检查、面朝、`GetGrabTile()` 对齐和必要的 Tile 内微移；微移不得让玩家离开起始 Tile。
- 首版要求玩家空手或手持 Tool。手持食物、可放置物、礼物等非工具 Item 时返回 `NOT_READY`，避免通用 `interact` 暗中变成赠礼、食用或放置能力。
- 优先使用游戏公开的动作语义提交一次交互，不复制固定 X 键、全局 InputBridge 或失焦反射。若实测证明只能通过输入路径保持原生语义，首版只实现命令私有、可确认提交与释放的窄 Adapter；失焦不可用时明确返回 `NOT_READY`，而不是恢复旧反射链。
- 提交前捕获目标绑定、Location、UI Revision、Inventory Revision 和目标可读状态；提交后只有出现可关联的 Dialogue/Menu、Location、Inventory、Relationship 或目标状态变化时才成功。通用 `player_busy` 只能是中间证据，观察窗口结束仍无关联变化返回 `EXECUTION_FAILED`。
- 提交动作前 `CanCancel=true`；游戏已经消费动作后 `CanCancel=false`，继续观察真实结果，不能把已经发生的副作用伪装为取消成功。

**`use_tool`**

- 不隐式导航或装备，提交前重新锁存当前 Tool 实例、Qualified Item ID、目标、实际可达蓄力和 Energy。工具在执行中被替换时失败，不得回显启动前的显示名冒充实际工具。
- 首版白名单为 Axe、Pickaxe、Hoe、Watering Can 与 Scythe。Fishing Rod、Slingshot、Pan、Milk Pail、Shears、普通武器和未知 Mod Tool 暂不进入通用单次工具原语，以稳定 `INVALID_ARGUMENT` 拒绝；未来按各自目标、持续会话与取消语义单独扩展。
- Axe、Pickaxe 与 Scythe 首版只允许 `charge_level=0`；Hoe 与 Watering Can 允许 `0..min(5, 当前工具实际支持等级)`。达到请求的实际 `toolPower` 后必须完成释放，不能复制旧仓互相漂移的 0..4、0..5 与多套帧数魔数。
- 状态机至少区分 `resolve → face → press/charge → accepted → release → settle`。实现前先用真实游戏 Spike 找到不会漏掉瞬时工具动作的 acceptance latch；仅看到输入队列为空或玩家 idle 不得成功。
- 成功要求游戏接受了本次工具动作且相关动画/工具状态已经收敛。空 Tile 可以成功，Energy 变化可以为零；结果仍必须回显实际目标、实际工具 Qualified Item ID、实际 charge 和 double Energy 差值。
- 提交或蓄力释放前允许取消并幂等清理；动作已经接受并释放后 `CanCancel=false`。Deadline 仍需安全释放未完成输入，但不得强行改写 `UsingTool`、动画或工具内部状态。

#### 开发顺序

1. [ ] **阶段 5.0：契约收口与游戏 API Spike。** 在 `behavior.md`、Fixture 和测试中固化 Character Ref “启动时锁定、结束前重验、不持续追踪”、`resolved_destination`、Interact 手持物门禁、首版工具白名单、提交点与稳定错误；分别验证公开动作 API 在交互、普通工具、蓄力工具、聚焦和失焦状态下能否提供可靠的提交/释放证据。Spike 不通过时先缩小支持边界，不引入旧 InputBridge。
2. [ ] **阶段 5.1：同图 `navigate` 纵向切片。** 实现共享 Target Resolver、严格 EXACT、ADJACENT、`stand_side`、`face_on_arrival`、PFC 清理与命令私有 continuation；同步交付 Fixture、Fake Game Port 测试、通用 MCP 调用和一条实机验证。
3. [ ] **阶段 5.2：跨图 `navigate` 纵向切片。** 实现运行时拓扑 Snapshot、纯 BFS、WalkThrough、InteractDoor、预期 Location 校验和 Warp 后稳定门禁；先覆盖单 Warp/单 Door，再覆盖多跳，不加入传送 fallback。
4. [ ] **阶段 5.3：`interact` 纵向切片。** 实现相邻目标、Grab Tile 对齐、手持物门禁、一次提交、关联后置条件和提交点取消语义；先验证 Dialogue/Menu/门，再验证一个对象状态或 Inventory 变化，不为未支持类型建立通用猜测器。
5. [ ] **阶段 5.4：`use_tool` 纵向切片。** 先实现 uncharged Axe/Pickaxe/Scythe，再实现 Hoe/Watering Can 的普通与蓄力路径；由生命周期探针证明 accepted/released/settled，最后组装实际工具、charge 和 Energy 结果。
6. [ ] **阶段 5.5：可靠性与实机收口。** 统一验证单变更并发、各阶段 Cancel、Deadline、断线后 Status 恢复、结果保留、stale Ref、目标移动、错误 Warp、无路径、输入释放和卡住失败；逐项调用真实 MCP Tool，并由后续查询证明实际位置、UI/目标状态或工具效果。

每个子阶段继续遵循 `Spec/Fixture → 唯一 Mod Handler → 自动生成的 MCP Tool → 自动化测试 → 单条实机验收`。MCP 侧不新增能力特判、单 Tool Schema 或投影函数；只要公共 Manifest、生成 Catalog、Mod 握手公告和 `game:write` 权限形成交集，三项 Tool 就应自动出现。

#### 最低测试矩阵

| 能力 | 自动化必须覆盖 | 实机必须覆盖 |
|---|---|---|
| `navigate` | 同图 already-there/可达/无路/严格终点；Adjacent 自动侧/指定侧/显式朝向；单 Warp/门/多跳/错误地图；Ref stale/移动；walking、door、stable 阶段取消与 Deadline | 同图 EXACT、同图 ADJACENT、一次 WalkThrough、一次门、多跳、途中取消 |
| `interact` | 异地图/非相邻/错误 Ref Kind/手持物拦截；Grab Tile 对齐；Dialogue/Menu/Location/Inventory/目标状态成功；无效果失败；提交前取消与提交后拒绝取消 | NPC 或对象对话、容器或门、一个对象/Inventory 变化、无效果失败 |
| `use_tool` | 未装备/不支持工具/超 charge/工具替换；普通动作、蓄力达到与释放、瞬时动作、空 Tile、零 Energy；各阶段取消、Deadline 与幂等释放 | Axe、Pickaxe、Scythe、Hoe 普通/蓄力、Watering Can 普通/蓄力，以及空 Tile、错误工具和蓄力中取消 |
| 共通运行时 | 单变更并发、读查询安全穿插、断线用原 Command ID 查询、终态保留/Tombstone、无自动重放 | 标准 MCP Session 中断并恢复一次长命令，确认没有重复副作用 |

阶段 5 明确不实现持续追踪移动角色、自动装备、自动导航后交互、钓鱼/弹弓/畜牧专用动作、批量农务、工作流编排、进度百分比估算、隐藏传送恢复或兼容旧能力名。这些边界不阻止三项 V1 原语完成；它们应在真实需求出现后由 Skill 或新的版本化能力处理。

退出条件：十五项公开能力在 `--allow-write` 下由同一 Catalog/握手交集完整暴露；三项新能力只通过唯一 Handler 和现有 Coordinator 执行；任意时刻最多一个变更命令；取消与 Deadline 在各关键阶段可复现；断线不会自动重复执行；严格到达、关联交互效果和工具接受/收敛均有真实游戏证据；所有失败都清理 PFC/输入且不绕过正常游戏访问边界；公共源码不包含 Legacy/V2、旧 Dispatcher、文件桥、机器绝对路径或私有平台依赖。

### 阶段 6：交付 Skill 开发面

实现 Skill SDK、Manifest 校验器、模板、一个只读最小示例、一个变更型最小示例和测试 Harness。示例只证明接口使用方法，不扩张为官方玩法库。

退出条件：第三方无需引用 MCP 内部模块即可创建、校验和测试 Skill；未授权能力与风险声明不完整时默认拒绝。

### 阶段 7：公开发布准备

完成安装器或明确的安装步骤、MCP 客户端配置、故障诊断、日志脱敏、许可证、第三方许可证清单、安全政策、版本发布流程和从干净机器开始的验收。

退出条件：新用户不接触私有仓库即可完成安装、第一次只读调用和第一次受控操作；发布包不包含私有路径、凭据、构建垃圾或历史实现。

## 八、单项能力的 Definition of Done

每项能力只有同时满足以下条件才算迁移完成：

1. 能力存在于公共 Manifest，参数、结果、风险和生命周期已经裁决。
2. Proto 与 MCP JSON Schema 由同一权威定义生成或交叉验证。
3. Mod 中只有一个 Handler，且不引用任何旧运行时类型。
4. MCP Tool 由 Manifest 确定性投影，不依靠源码扫描。
5. 最小、完整、非法、成功与失败 fixture 齐全。
6. 单元测试、契约测试和真实游戏 E2E 均通过。
7. 文档说明限制、风险、错误和可取消性。
8. 禁止依赖扫描和干净构建门禁通过。

“代码已复制”“能够编译”或“现有测试仍通过”都不能单独表示能力完成。

## 九、永久质量门禁

CI 至少包含以下门禁：

- `spec` 中 Manifest、Proto、Schema 和 fixture 的一致性校验；
- C# 与 Python 生成 Descriptor 对比；
- Manifest、Mod Registry 与 MCP Tool Catalog 的能力集合完全一致；
- 生产源码禁止出现 `AdapterV2`、`CommandProcessor`、`CompoundDispatcher`、`FallbackToLegacyMapper`、`v2-json` 和 Legacy/V2 命名空间；
- MCP 禁止导入旧仓库及私有平台包；
- 公开仓库文本禁止包含用户目录、工作区目录等机器专属绝对路径；
- Mod 与 MCP 分别从干净环境构建和测试；
- 安装包内容、秘密扫描、许可证和依赖清单检查；
- 至少一条只读能力和一条变更能力的真实游戏 Smoke Test。

禁止词只扫描生产源码，迁移说明和历史决策文档可以提到已删除组件，否则文档本身会造成误报。

## 十、执行组织

阶段 0 是所有实现工作的前置依赖；阶段 1 可以与契约收尾部分交叉进行。阶段 2 完成前不批量实现其他能力，因为第一条纵切会暴露传输、线程、错误和代码生成边界的问题。阶段 3 完成后，阶段 4 与阶段 6 可以并行；阶段 5 依赖统一 Runtime 已经通过简单交互验证；阶段 7 从阶段 1 开始持续积累，但只在所有能力门禁通过后结束。

每个阶段单独建立 Issue/Epic，每个能力建立可独立审查的纵向任务。Pull Request 不按“复制 Mod 目录”“复制 MCP 目录”划分，而按“一个基础设施边界”或“一项能力端到端完成”划分。

## 十一、近期下一步

阶段 4 已完成，下一步进入阶段 5：

1. 先执行阶段 5.0，只修改契约、Fixture 和最小 Spike，裁决动作提交与工具 lifecycle 的可靠公共 API；在这个结论完成前不写三个正式 Handler。
2. 按“同图导航 → 跨图导航 → 交互 → 工具”的顺序逐条完成纵向切片，不并行铺开三个半成品状态机。
3. 每条切片继续使用现有 Coordinator、Ref Store、Catalog 和 Descriptor Projection；任何需要第二套 Scheduler、Handler 互调或全局输入队列的设计都应退回重画边界。
4. 阶段 5.5 完成十五项能力的真实 MCP Session 验收后，再把主要精力转入阶段 6 的 Skill 开发面。
