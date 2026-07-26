# 公开版本重写计划

状态：**阶段 2 已完成，阶段 3 观察能力正在实现**

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
5. [ ] 实现进程内不透明 Ref Store，以及 World、Inventory、UI 三类 Revision；调用方不得解析 Ref，Mod 不根据外部字符串猜测 Kind。
6. [ ] 按 `query_world → query_inventory → query_ui → inspect` 完成四条纵向切片；每条同步交付 Spec Fixture、唯一 Mod Handler、默认 Descriptor Projection 和测试，不预建通用游戏查询框架。
7. [ ] 为四项能力补齐最小、完整、非法、成功与失败覆盖；固定五项观察能力的握手 Snapshot、HMAC、Fence 和生命周期场景。
8. [ ] 通过跨语言协议测试、C#/Python 单元与标准 MCP 会话测试、边界扫描、干净构建和发布包审计。
9. [ ] 部署新 Mod 后逐项执行真实 MCP 调用；验证世界、玩家与容器库存、无菜单和有菜单 UI、混合成功/失败 Ref Inspect，并记录 Handler 耗时与结果字节数。

阶段 2.5 完成记录：MCP 已拆分为唯一生成 Catalog、Descriptor Projection、通用 Command Runtime 与纯 Transport，并以公共 Manifest、MCP 支持集、Mod 公告集和权限策略四方交集决定 Tool；Mod 已用编译期显式 Registry 取代 `LocalServer` 的单能力分支，并校验 Handler ID、Proto operation 与 Request Type 一致。结构门禁同时覆盖握手 Deadline、typed request 错配、未知 Enum、活动命令重放和已完成命令的缓存终态收敛；第二轮独立审查未发现 P0/P1 阻塞。

实现顺序以依赖而不是旧目录划分。`query_world` 先提供 World Entity/Character Ref，`query_inventory` 再基于容器 World Ref 提供库存视图与 Item Ref，`query_ui` 提供 Revision 绑定的 Element Ref，最后由 `inspect` 统一验证所有 Ref Kind。MCP 只投影原始结构化事实；任何面向模型的摘要、搜索、聚合或玩法工作流留给未来 Skill/客户端层。

实机验证可以调用项目级 `launch-stardew-game` Skill：先通过统一构建入口生成 Mod，再以独立 SMAPI 进程和精确测试存档进入游戏，避免复用或干扰其他游戏进程。该 Skill 只负责隔离启动与进入存档；`query_world`、`query_inventory`、`query_ui` 和 `inspect` 仍需逐项执行真实 MCP 调用，并以专用日志、Handler 耗时、结果字节数和协议结果作为阶段 3 验收证据。

退出条件：所有保留的观察能力拥有最小、完整、非法参数、成功和失败 fixture，并通过 C#/Python 交叉测试与实机性能门禁。

### 阶段 4：完成简单交互能力

重写社交、朝向、装备和菜单能力，建立统一的结果确认机制。每项能力单独完成纵向切片，不先批量复制 Handler 再补测试。

退出条件：每项能力都能证明最终游戏效果；不同 UI Scale 和关键菜单状态拥有回归用例。

### 阶段 5：完成长时运行能力

重写导航、交互与工具使用能力，集中验证并发、取消、超时、断线和未知结果收敛。工作流型能力不进入这一层。

退出条件：任意时刻最多一个变更命令；取消与 Deadline 可复现；断线不会自动重复执行变更；卡住恢复不会绕过安全边界。

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

阶段 1 与 `query_runtime` 首个纵向切片完成后，下一步不回到旧仓库复制运行时：

1. 复审首个切片暴露的 Transport、线程和错误边界，只修复有证据的问题。
2. 按纵向切片依次实现 `query_world`、`query_inventory`、`query_ui` 与 `inspect`，不预建通用框架。
3. 每增加一项能力，同步补齐 Spec、Fixture、Mod、MCP、自动化测试和一次真实游戏验证。
4. 发布准备持续补充许可证、第三方许可证和安全政策，但不与能力实现耦合。
