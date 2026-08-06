# V1 能力行为契约

状态：**公开 V1 候选契约**

Proto 是字段和编号权威，Manifest 是公开集合与策略权威；本文定义无法由普通 Proto 类型表达的参数约束、默认值、Ref/Revision 生命周期以及成功后置条件。只依赖请求载荷即可判定的结构型输入错误必须在接受前以 `INVALID_ARGUMENT` 拒绝，不能依赖具体 Handler 的偶然行为。Ref Kind、Ref 来源以及 Revision 与当前游戏对象的关系只能在游戏主线程安全点判定；这类上下文型非法输入允许按线路协议在接受后以 `FAILED/INVALID_ARGUMENT` 返回。

## 1. 公共值约束

- 所有字符串必须是有效 UTF-8 且不得包含 NUL。未单独声明上限的公开字符串最大 512 个 Unicode Scalar Value。
- `location_id` 使用 Stardew Valley `NameOrUniqueName`，长度为 `1..128`，不得使用本地化显示名称作为身份。
- `Ref.value` 长度为 `1..512`，对调用方完全不透明。调用方只能原样回传查询得到的 Ref。
- 所有普通请求枚举都不得使用 `*_UNSPECIFIED`；可选枚举只有在字段存在时才执行此检查。
- Slot Index 从 0 开始；Tile 坐标使用游戏 Tile 坐标，不是像素坐标。
- 所有请求的未知 MCP JSON 字段都在 MCP 边界拒绝；线路 Proto 的未知字段按版本规则安全忽略且不得产生语义效果。

## 2. Snapshot、Revision 与 Ref

每个 Snapshot 必须在一个游戏主线程安全点生成，内部字段必须来自同一逻辑 Tick，不能拼接多个 Tick 的可变状态。Revision 是 64 个小写十六进制字符组成的不透明 Token；同一 Mod 启动中，不同的动作相关状态不得复用同一 Revision。

### UI Revision

以下任一内容变化都必须生成新 `ui_revision`：菜单类型、Modal 状态、元素集合、元素 Ref、可见性、可用性、几何中心、标签、索引、商品物品、价格或库存。`activate_ui` 只能使用当前 Revision 中返回的 UI Element Ref；旧 Revision 必须返回 `STALE_REF`，不能尝试按坐标猜测新元素。

### Inventory Revision

以下任一内容变化都必须生成新 `inventory_revision`：Slot 数量、Slot 中的物品身份、堆叠、品质、工具等级、喷壶余水／容量／无限水状态或当前选中 Slot。所有 `QueryInventoryResult.snapshot.slots[].item.ref` 都是可用于 `inspect` 的 `INVENTORY_ITEM` Ref，包括玩家背包与可读容器中的非空 Slot。只有由 `player_inventory` 选择器（或其缺省等价形式）生成、且调用时仍匹配当前玩家背包与 `inventory_revision` 的 Item Ref 可以用于 `equip`；容器库存 Item Ref 不得用于 `equip`。当前受支持箱子菜单两侧的 Item Ref 可以按下述 `transfer_inventory_item` 规则用于单次转移。Machine、Loose Item 和 UI 中嵌套的 `ItemFact` 可以没有 Ref；即使存在，是否可用于 `inspect` 仍由服务端 Ref Binding 决定，且不得据此获得变更权限。

### World Revision 与普通 Ref

`world_revision` 在所查询区域的 Tile、Entity 或 Character 身份与动作相关事实变化时更新。World Entity 与 Character Ref 可以跨查询复用，但 Location 重建、对象销毁、身份 Guard 变化或 Mod 重启后必须变为 `STALE_REF`。

所有 Revision 与 Ref 都只在创建它们的 `mod_instance_id` 对应的游戏进程中有效。实现可以在不透明值内部编码实例和 Guard，但这些编码不属于公共契约。

实现必须为活动 Ref Binding Registry 设置硬容量上限，且容量至少覆盖一次最大合法公开查询可能签发的 Ref。容量不足时先回收已经 stale 的 Binding；仍不足时才回收最久未使用的 live Binding，并在移除前把它标记为 stale，使同一 owner 后续再次观察该对象时签发新 Ref。访问已注册 Ref 和再次观察同一 Binding 都必须更新最近使用顺序。回收不改变分类单调性：当前 Mod 实例曾签发的 Ref 被回收后仍必须稳定返回 `STALE/STALE_REF`，不得变成 `NOT_FOUND` 或重新分配给其他对象；当前实例从未签发的合法外形值返回 `NOT_FOUND`，其他 Mod 实例的 Ref 返回 `STALE/STALE_REF`。实现可以用经进程内密钥认证的单调签发序号等有界判据区分已回收与从未签发的值，不得为此保留无界 tombstone，也不得把内部编码变成调用方可依赖的格式。

使用 World Entity 或 Character Ref 启动 `navigate`、`interact` 或 `use_tool` 时，实现必须在命令开始的主线程安全点解析并锁定同一游戏对象、当时的 `NameOrUniqueName` 与动作 Tile。命令不会持续追踪移动目标；在提交动作或返回成功前必须重验同一对象仍存在且位置没有变化。命令开始前已经失效的绑定返回 `STALE_REF`，Kind 不适用于该能力返回 `INVALID_ARGUMENT`；命令开始后对象消失、换图或移动则返回 `EXECUTION_FAILED`，不得用旧坐标继续执行或成功收口。

### Inspect 不变量

`inspect` 按请求顺序返回一项结果；输入不得为空、不得重复，数量为 `1..64`。每个 `InspectedRef` 必须满足：

| Ref Kind | `InspectedRef.fact` 分支 | 来源与动作边界 |
|---|---|---|
| `WORLD_ENTITY` | `world_entity` | `query_world.entities[].ref`；容器对象也保持此 Kind，可用于导航、交互，并可作为 `query_inventory.container_ref` 输入 |
| `CHARACTER` | `character` | `query_world.characters[].ref`；可用于导航或交互 |
| `INVENTORY_ITEM` | `inventory_item` | 玩家背包或可读容器 `InventorySnapshot` 中的非空 Slot Item Ref；均可用于 `inspect`，只有玩家背包来源且匹配当前 Inventory Revision 的 Ref 可用于 `equip` |
| `CONTAINER` | `inventory` | `InventorySnapshot.container_ref`；表示库存视图，不是地图实体，不可用于导航或交互 |
| `UI_ELEMENT` | `ui_element` | `query_ui.elements[].ref`；只能在匹配 UI Revision 下交给该元素种类明确允许的动作能力 |

`query_inventory.container_ref` 接受两类输入：带 `ContainerFact` 的 `WORLD_ENTITY` Ref，或先前 `InventorySnapshot.container_ref` 返回的 `CONTAINER` Ref。前者标识地图上的容器实体，后者标识该容器的库存视图；实现不得仅凭 Ref 字符串格式猜测 Kind。

| Resolution 状态 | Kind | Fact | Error |
|---|---|---|---|
| `RESOLVED` | 非 `UNSPECIFIED` | 必须存在且与 Kind 匹配 | 不得存在 |
| `STALE` | 已知时提供 | 不得存在 | `STALE_REF` |
| `NOT_FOUND` | 已知时提供 | 不得存在 | `NOT_FOUND` |
| `UNSUPPORTED` | 已知时提供 | 不得存在 | `INVALID_ARGUMENT` |
| `FACT_UNAVAILABLE` | 非 `UNSPECIFIED` | 不得存在 | `INTERNAL`，消息固定为“当前 Ref 事实不可用” |

`STALE` 由对象或 owner 生命周期已由肯定证据终止，或上述有界 Registry 明确执行容量回收而产生，并保持单调不复活。getter、关键字段、typed projector 或完整 UI capture 本次不可读，但 Binding 身份仍可验证时，必须逐项返回 `FACT_UNAVAILABLE`；该项不改变 Binding，不得被视为 stale 或获得 stale 优先回收资格，不产生 warning，也不得拖垮同批其他 Ref。对于 UI Ref，只有菜单关闭或被替换、完整且可信的当前公开元素集合明确不含该元素、同一身份槽位已由不同组件或语义对象替换，或 Registry 明确执行容量回收，才构成 stale 证据；不完整捕获不得淘汰本轮未观察到的 Binding。既有安全 fallback 若已形成完整公开 Fact，仍返回 `RESOLVED` 及对应 warning。

## 3. 成功终态的统一含义

`SUCCEEDED` 表示能力声明的后置条件已经通过游戏侧观测确认，而不只是输入已经发送。结果消息中不再提供 `reached=false`、`activated=false` 等可以和成功终态冲突的布尔值。

如果操作输入合法但后置条件未在 Deadline 前成立，必须返回 `FAILED/EXECUTION_FAILED` 或 `TIMED_OUT/DEADLINE_EXCEEDED`。已经满足目标状态的幂等 No-op 可以成功，但结果必须明确说明，例如 `FaceResult.changed=false`、`EquipResult.changed=false`、`CloseMenuResult.already_closed=true`。

## 4. 操作能力

Mod 在游戏世界就绪且本地控制服务运行期间，必须保证单机游戏在窗口失焦时仍继续推进游戏 Update；窗口失焦本身不是操作能力返回 `NOT_READY` 的理由。实现可以在该运行期临时关闭游戏的 `pauseWhenOutOfFocus`，但必须在返回标题界面时恢复原值。这个保证只负责游戏时钟和动作生命周期继续推进，不授权实现伪造 `IsActive`、安装全局输入队列、反射 SMAPI 输入状态或绕过各能力自己的后置条件。

### `say`

- `content` 为 `1..500` 个 Unicode Scalar Value。
- 成功要求游戏聊天系统确认接受完整文本；空白文本是否合法遵循游戏，但只包含 NUL 或长度越界必须拒绝。
- `content_length` 使用 Unicode Scalar Value 数量，不使用 UTF-16 Code Unit 数量。

### `emote`

- `emote` 必须是 Manifest 当前实现支持的非 `UNSPECIFIED` 值。
- 成功要求玩家进入对应 Emote 状态；仅发送按键不能视为成功。

### `face`

- 玩家必须处于允许改变朝向的状态；阻塞菜单或不可控制状态返回 `NOT_READY`。
- 成功要求 `final_direction` 等于请求方向。已经面向目标时返回 `changed=false`。

### `navigate`

- 必须且只能提供 `position` 或 `target_ref`。Ref 必须解析为 World Entity 或 Character。
- `arrival` 必须为 `EXACT` 或 `ADJACENT`；Ref 目标只允许 `ADJACENT`。
- `stand_side` 只允许和 `ADJACENT` 同时出现；`face_on_arrival` 出现时不得为 `UNSPECIFIED`。
- 导航可以经过正常游戏 Warp，但不得传送、修改碰撞或绕过游戏访问条件。
- `EXACT` 只有最终 Location 与 Tile 都完全相等才能成功。`ADJACENT` 指定 `stand_side` 后只能站在该侧，不得静默换边；未指定时实现可以从四个 cardinal-adjacent Tile 中选择一个可达位置。
- 成功要求 Final Position 满足 Arrival，且最终朝向满足可选要求。路径不存在、目标消失或抵达条件不成立不能返回成功。
- `resolved_destination` 是本次锁定并实际用于抵达判断的玩家落脚 Tile；它不是 Ref 指向对象自身的 Tile。`route_location_ids` 只记录玩家实际到达过的 Location，首项为起点、末项为终点，不得返回尚未执行的规划路线。
- 导航在已确认过玩家位置后以 `FAILED` 结束时，Error 必须在 `navigation.last_confirmed_position` 返回最后一次主线程确认的 `location_id` 与 Tile；该上下文只描述停止位置，不把失败伪装为 `NavigateResult`，也不承诺未确认的移动已经完成。
- 导航因 Coordinator Deadline 以 `TIMED_OUT/DEADLINE_EXCEEDED` 结束，且已经取得路线计划或确认过位置时，Error 的 `navigation` 必须包含 `route_segments_total`、`route_segments_completed`、`interruption_reason="deadline_exceeded"` 与 `resume_hint`；有最后确认位置时也必须包含 `last_confirmed_position`。路线段等于计划中一条正常 Warp 边，只有进入该边 Target Location 并通过稳定门禁后才计入已完成段数；Location 数量不得替代路线段数。调用方可使用原最终目标重新调用 `navigate`，不得把续跑提示解释为自动重放授权。
- 提交 PFC 或门动作前允许取消；取消或 Deadline 必须清除当前 PFC、移动方向与尚未提交的门输入。已经触发地图切换时继续收敛到稳定 Location，再按 Coordinator 的停止信号结束，不能传送回滚。

### `interact`

- 目标必须在玩家当前 `location_id`，并位于游戏交互允许的相邻 Tile；能力不会隐式导航。
- Ref 必须解析为当前可交互的 World Entity 或 Character。
- 窗口失焦时仍按相同契约执行；实现必须使用游戏语义动作并观察真实后置条件，不得回退到全局输入注入。若游戏世界本身尚未进入可推进状态，仍返回 `NOT_READY`。
- `interact` 执行游戏完整的原生动作键语义：空手或手持物品时，都可能按照游戏规则触发检查、对话、赠礼、种植、放置或食用确认。调用方必须先通过 `equip` 明确选择预期手持物，并根据任务复查目标、背包、关系或 UI 后置条件；能力不会替调用方猜测任务意图。
- 命令开始时锁存当前手持物实例、Qualified Item ID 与背包槽位；提交前手持物发生变化时返回 `EXECUTION_FAILED`，不得静默改用另一件物品。
- 提交前必须重验目标、面朝目标，并使 `GetGrabTile()` 与目标 Tile 对齐；为对齐进行的 Tile 内微移不得让玩家离开起始 Tile。
- 成功要求观察到与本次交互关联的游戏后置条件，例如 Dialogue/Menu 打开、对象状态变化、物品变化或 Relationship 变化。种植、食用与赠礼等 Skill 仍必须复查自己的任务级后置条件；没有任何可关联效果时返回 `EXECUTION_FAILED`。
- 调用游戏动作 API 前允许取消；游戏已经消费本次动作后不得再报告取消成功，此时 `CanCancel=false` 并继续观察真实后置条件。输入 API 的返回值、玩家暂时 Busy 或动画回到 Idle 都不能单独证明交互成功。

### `use_tool`

- 目标必须位于当前 Location 和当前工具的合法作用范围；能力不会隐式导航或装备工具。
- 窗口失焦时仍按相同契约执行；实现必须让工具动作在持续 Update 中完成 accepted、release 与 settle，不得回退到全局输入注入。若游戏世界本身尚未进入可推进状态，仍返回 `NOT_READY`。
- 首版只支持 Axe、Pickaxe、Hoe、Watering Can 与 Scythe。Fishing Rod、Slingshot、Pan、Milk Pail、Shears、普通武器和无法识别的 Mod Tool 返回 `INVALID_ARGUMENT`；这些工具需要独立的持续会话或目标语义，不进入通用单次工具原语。
- 当前没有装备 Tool 时返回 `NOT_READY`；能力不会代替调用方选择或装备工具。
- Axe、Pickaxe 与 Scythe 只允许 `charge_level=0`。Hoe 与 Watering Can 允许 `0..min(5, 当前工具实际支持等级)`；超出范围返回 `INVALID_ARGUMENT`。
- 命令开始时锁存当前 Tool 实例和 Qualified Item ID；提交前工具被替换时返回 `EXECUTION_FAILED`，不得自动重新装备。
- 状态机至少区分 `resolve → face → press/charge → accepted → release → settle`。只有观察到本次工具动作被游戏接受、需要的释放已经完成且动画/工具状态收敛才能成功；输入调用返回、玩家短暂 `UsingTool` 或最终 Idle 不能单独作为完整证据。
- 击中空 Tile 也可以成功，不要求一定改变世界状态，Energy 变化也可以为零。
- Result 必须回显实际工具 Qualified Item ID、实际 Charge Level 和 Energy 变化。
- 调用 `BeginUsingTool()` 前允许取消并幂等清理；该公开 API 会立即排队不可逆的游戏动作，因此调用前必须先设置 `CanCancel=false`。调用之后收到取消返回 `CONFLICT`；若此后触发 Deadline，实现仍必须安全释放并等待本次工具动作收敛，但不得直接改写游戏内部动画或工具状态伪造回滚。

### `equip`

- 必须且只能提供 `slot_index` 或 `item_ref`。
- `slot_index` 必须小于当前玩家背包 Slot 数；该 Slot 为空时返回 `NOT_FOUND`。
- 使用 `item_ref` 时必须同时提供产生该 Ref 的当前 `inventory_revision`；Ref 必须来自玩家背包 Snapshot，容器库存 Item Ref 即使可被 `inspect` 解析也必须以 `INVALID_ARGUMENT` 拒绝。
- 使用 Slot 时如果提供 Revision，也必须匹配当前背包。
- 成功要求当前选中 Slot 和 Item 与 Result 一致；已经装备目标时返回 `changed=false`。

### `transfer_inventory_item`

- 请求必须提供非 `UNSPECIFIED` 的方向、源 `item_ref`、`1..2147483647` 的数量、当前 `ui_revision`，以及玩家与容器两侧的当前 Inventory Revision。方向必须与 Item Ref 的玩家或容器来源一致。
- 只允许当前仍打开且由 `query_ui` 完整支持的精确原版普通 Chest、Big Chest 或内置 Fridge `ItemGrabMenu`；菜单对象、当前玩家、Location、父 Chest attachment、Container Ref、双方 backing/capacity、Chest Mutex 持有权和空 `heldItem` 必须在预检与提交前都成立。其他来源或特殊物品以明确错误拒绝。
- 实现必须分两个 Tick 完成预检与提交；提交前重新验证 UI Revision、双方 Inventory Revision、源对象身份、Slot 与 Stack。取消只能在同步内存提交开始前接受。
- 调用方不指定目标 Slot。实现必须按目标 Slot 从低到高先填满所有兼容的非满堆叠，再依次使用空 Slot；只有目标能够完整容纳请求数量时才允许提交。容量不足返回 `NOT_READY` 并提示减少数量或整理目标库存后重新查询，双方保持不变，不允许部分成功、地面掉落或把余量留在 `heldItem`。
- 提交不得模拟坐标、按键或左右键，也不得调用菜单 click/callback。成功必须返回实际转移数量、原源 Slot、源剩余数量和双方新 Revision；实际数量必须等于请求数量，双方完整库存的源减少、目标增加与总数量守恒必须通过后置验证。成功后旧 Item Ref 不代表新位置，调用方应重新查询取得新 Ref。
- Recipe、Stardrop、矮人语翻译指南及其他会被原生 ItemGrabMenu 消费或触发特殊效果的物品必须拒绝。数量超过源 Stack 返回 `OUT_OF_RANGE`；任一 Revision、Ref 或对象身份变化返回 `STALE_REF`；无菜单、不支持菜单、`heldItem` 非空、Mutex 未就绪或容量不足返回 `NOT_READY`；不可读事实返回 `INTERNAL`；同步提交或后置条件失败必须先回滚本 Tick 的库存内容变更，再返回 `EXECUTION_FAILED`。罕见的“提交后投影失败”已经观察过空源 Slot 时，旧源 Item Ref 可以按单调生命周期变为 stale，即使原物品对象与堆叠已回滚；调用方必须重新查询，不得要求旧 Ref 复活。

### `set_equipment_slot`

- 请求必须提供当前原版 Inventory 页签发的 Equipment Slot Ref、UI Revision、玩家 Inventory Revision，以及 `item_ref` 或 `clear=true` 两者之一。`clear=false` 无效；游标持有物品、菜单或页签不受支持时返回 `NOT_READY`。
- `item_ref` 必须来自当前玩家背包。能力只接受精确原版 Hat、Ring、Boots、Shirt、Pants、Trinket，以及内部成员均为精确原版 Ring 的 Combined Ring；类型错槽、派生类型、堆叠异常和原版特殊转换物品返回 `INVALID_ARGUMENT`。
- 穿戴时新装备必须先离开背包再进入装备槽，防止同一网络对象同时拥有两个 Parent。替换后的旧装备必须回到新装备的原源 Slot，即使其他 Slot 已满；取下时只能使用玩家已解锁容量内最低序号的空 Slot。背包已满时取下返回 `NOT_READY`；清空已经为空的槽幂等成功且 `changed=false`。
- 实现必须分两个 Tick 预检与提交，并在提交前重新验证菜单、页面、组件、两项 Revision、源物品、旧装备和确定性目的 Slot。同步提交开始后不可取消；`CurrentToolIndex` 数值始终不得改变。正常成功路径中，当前手持 Slot 的停止与开始持有生命周期各至多触发一次；失败回滚必须按反向路径恢复最终可观察的持有状态，因此不承诺把正向与回滚两段合计为一次。
- 失败时必须按原版装备路径尽力恢复背包 Slot、装备槽、Trinket 列表长度、对象身份和可观察装备效果；不承诺撤销已经由任意第三方 Hook 发出的历史事件。回滚仍无法确认时返回 `EXECUTION_FAILED` 并要求重新查询。
- 成功后返回目标槽类型、序号、新玩家 Inventory Revision 和是否实际变化。清空结果必须省略 `item`；非空装备结果中的 `ItemFact` 不签发物品 Ref。成功后旧 UI Revision 和源 Item Ref 失效，调用方必须重新查询新状态。

### `move_inventory_item`

- 请求必须提供当前原版 Inventory 页中的玩家背包 Item Ref、目标 `ITEM_SLOT` Ref、UI Revision 和玩家 Inventory Revision。目标 Ref 必须完整绑定当前 `GameMenu`、玩家侧、非装备槽、Slot 序号、组件与语义目标；容器槽、装备槽、旧页面或旧组件 Ref 必须拒绝。
- 能力只搬运同一玩家已解锁背包中的完整 Item 对象。目标为空时执行 move；目标非空时执行 swap，即使两项可以堆叠也不合并；源槽与目标槽相同时幂等成功并返回 `changed=false, swapped=false`。能力不拆分数量、不跨容器、不经游标、不丢弃或消费物品，也不自动寻找空槽。
- 实现必须分两个 Tick 完成预检与提交。提交前必须重新验证页面、玩家背包 backing、目标组件、两项 Revision、源与目标对象身份和 Stack，以及 `CurrentToolIndex`；同步双槽提交开始后不可取消。
- 真实写入必须先清空参与槽，再把目标旧对象写回源槽并把源对象写入目标槽，防止同一对象短暂属于两个 Slot。`CurrentToolIndex` 数值始终不得改变；若当前槽参与，整个双槽事务前后只对提交前、提交后的当前 Item 各执行至多一次停止与开始持有生命周期，同槽 no-op 不触发回调。
- 成功必须确认两槽结果、其他槽不变、所有 Stack 不变、游标仍为空、对象守恒、两项 Revision 在真实写入后变化，并返回源槽、目标槽、`changed`、`swapped` 与新玩家 Inventory Revision。真实写入成功后源与目标旧 Item Ref 失效，Slot Ref 仍绑定同一组件；调用方必须重新查询取得新 Revision 和 Item Ref。同槽 no-op 不改变 Ref 或 Revision。
- 写入或后置校验失败时，必须以两槽局部 journal 尽力停止提交后的当前 Item、清空计划对象、恢复原对象与原持有状态。回滚不得覆盖第三方在失败窗口写入的未知对象；无法安全恢复或确认时返回 `EXECUTION_FAILED` 并要求重新查询。能力不承诺撤销第三方 Item callback 已产生的历史副作用。

### `craft_item`

- 请求必须提供当前精确原版非烹饪 Crafting 页签发的 Recipe Ref、当前 UI Revision 与 `1..25` 的制作轮数。能力不接受配方名称，也不重新构造配方；Recipe Ref 必须仍绑定同一菜单、页面、组件和原版 `CraftingRecipe` 对象。
- 实现必须分两个 Tick 完成预检与提交，并在提交前重验世界、玩家、菜单、页面、空 `heldItem`、Recipe Ref 与 UI Revision。同步制作开始后不可取消。O15 已签发但不在当前可见分页的配方仍可制作；能力不得为此切换分页或模拟点击。
- 每轮必须先按原版规则同时检查玩家背包和当前页面材料容器，再对全部可能产出做保守的完整入包检查。任一可能产出无法由当前背包完整容纳时，本轮不得开始；首版可以拒绝只有先消耗材料腾出 Slot 后才能容纳的边界场景。
- 每轮只允许调用一次 `CraftingRecipe.createItem()`，随后使用同一实际 Item 完成扣料与入包，并按原版非烹饪路径更新任务、配方制作计数和制作成就。不得调用私有 Crafting 点击路径、坐标点击、键盘修饰键，也不得把放不下的产物丢到地面。
- 第一轮即材料不足或背包无容量时返回 `NOT_READY`，不得改变材料、背包或统计。完成至少一轮后遇到材料或容量边界，命令成功并以 `completed_craft_count < requested_craft_count` 及稳定 `stop_reason` 表达部分完成；调用方应基于新的查询决定是否续跑。
- 成功结果必须按实际 Qualified Item ID 与显示名聚合产出，按 ingredient key 聚合实际材料消耗，并返回请求／完成轮数、新玩家 Inventory Revision 和新 UI Revision。Recipe Fact、材料事实或组件变化会使 UI Revision 失效；无关背包布局变化不使请求失效，提交阶段改用当前材料与容量安全决定是否继续。成功后受影响的 Item Ref 失效，结果中的 Revision 只描述新状态，不是后续写入授权。
- 首版不支持烹饪、调味料或派生 CraftingPage／CraftingRecipe。提交阶段的罕见原版或第三方异常返回 `EXECUTION_FAILED`，即使此前已有轮次完成也不伪报全部成功；调用方必须先重新查询，不能直接重试。实现应在不覆盖未知物品的前提下把已经创建但未能完整入包的实际产物保留在 Crafting 游标，避免产物只残留于异常栈中的局部变量。

### `purchase_shop_item`

- 请求必须提供当前精确原版商店视口签发的商品行 Ref、当前 UI Revision 与 `1..25` 的购买轮数。能力不接受商品名称或屏幕坐标；商品 Ref 必须仍绑定同一菜单、组件、商品对象、价格与库存事实。
- 首版只支持能生成精确原版普通 Object 的金币实物商品。配方、回购物、特殊物品、非金币货币、交换物价格、Storage Shop、派生菜单或商品，以及带购买检查或购买回调的商品必须拒绝，不得把说明文字中的价格猜测为金币价格。
- 实现必须分两个 Tick 完成预检与提交，并在提交前重验世界、玩家、菜单、空 `heldItem`、安全计时、商品 Ref、UI Revision、金币、库存与背包容量。同步购买开始后不可取消；商品滚出当前视口、菜单或商品事实变化时必须以过期 Ref 拒绝。
- 一次请求必须全有或全无。金币、有限库存或背包容量不足时返回 `NOT_READY`，不得自动减少购买轮数、留下部分购买结果或把余量丢到地面；零价商品合法。购买轮数乘商品模板 Stack 不得超过该物品的单堆叠上限。
- 成功必须使用原版商店事务取得同一个实际 Item，并把它完整加入玩家背包。售罄商品必须从当前商店可售列表移除；成功后 `heldItem` 必须为空，金币差额必须等于 `total_price`，实际入包数量必须与请求轮数和商品模板 Stack 一致。
- 成功结果必须返回购买轮数、不带 Ref 的实际 ItemFact、总价、购买前后金币、新玩家 Inventory Revision 与新 UI Revision；有限库存还必须返回余量，无限库存必须省略 `stock_remaining`。所有金币字段使用非负整数，调用方不得从字段缺省推断负数或透支。
- 商品购买入口只能是本能力；商店商品行即使带稳定 Ref，也不得由 `activate_ui` 通过坐标点击购买。原版事务开始后的罕见异常返回 `EXECUTION_FAILED`，调用方必须重新查询后再决定是否重试；尚未完整入包的实际商品应保留在商店游标，不得丢弃或落地。

### `open_menu`

- `menu` 必须是受支持的非 `UNSPECIFIED` 顶层菜单。
- 存在不能安全关闭或切换的 Modal 时返回 `NOT_READY`，不得通过坐标点击绕过。
- 成功要求 `menu_type_after` 对应请求 Menu；目标已经打开时允许幂等成功。

### `activate_ui`

- `element_ref` 必须来自当前 `query_ui`，`ui_revision` 必须完全匹配。
- 元素必须 `visible=true` 且 `enabled=true`。一次调用只执行一次 Primary Activation，不提供 Click Count。
- `DIALOGUE_ADVANCE` 是精确原版普通对话的语义推进元素；它没有屏幕几何目标，`center=(0,0)` 只是线路必填的非屏幕占位值。激活必须忽略 Center 并调用游戏原生对话推进流程，不能把它解释为坐标点击或 `close_menu`。
- 成功要求游戏接受激活，并观察到新的 UI Revision 或与该元素对应的游戏事实变化；否则返回 `EXECUTION_FAILED`。

### `close_menu`

- 没有菜单时以 `already_closed=true` 幂等成功。
- 有菜单时，成功要求 `menu_type_after` 为空且新的 UI Snapshot 表示没有菜单。
- 精确原版、非选择型 `DialogueBox` 仅在末页正文已经完整呈现、非过渡、等待计时结束、非事件对话且没有后续对象对话时允许关闭；角色对话还必须处于最终 Dialogue entry 且没有 continued 或 broken-up 后续页。实现必须调用游戏原生对话推进流程并观察菜单真正关闭，不得用 `exitThisMenu`、Escape 或通用坐标点击强退。
- 多页、问题、事件或状态不可安全判定的 `DialogueBox`（包括派生类），以及游戏拒绝关闭的其他强制 Modal，都返回 `NOT_READY`。

## 5. 查询能力

### `query_runtime`

返回日期、时间、天气、明日天气预报、今日运势、今日美食节目可学习菜谱、玩家位置、资源、UI 摘要及当前玩家保存的 `home_location_id`。天气必须同时保留兼容布尔字段和明确 `WeatherKind` 枚举；调用方需要判断整体天气时应优先读取 `weather.kind`，需要判断次日计划时读取 `weather.tomorrow`，且该预报必须与当前 Location 的天气上下文一致。今日运势必须返回原始 `daily_luck.value` 与按电视运势阈值归类的 `daily_luck.tier`。今日美食节目必须是只读投影：可计算 Sunday 首播与 Wednesday 重播对应菜谱，但不得因为查询而写入玩家已学菜谱或队伍重播周状态。`home_location_id` 沿用游戏自身的住宅 Location 标识；多人小屋必须保持其唯一室内 ID，不得退化为可能重名的短建筑名。游戏尚未加载 Save 时返回 `NOT_READY`，不能返回由零值拼成的假 Snapshot。

### `query_players`

- 返回当前存档的主机、在线农场工与离线农场工；单人存档必须返回唯一的当前玩家。请求不提供在线过滤或分页，结果把 `relation=MYSELF` 的当前玩家放在第一位，其余玩家按有符号 `player_id` 升序排列，且 `player_id` 不得重复。
- `player_id` 是 `Farmer.UniqueMultiplayerID` 的 invariant-culture 有符号十进制字符串，调用方必须把它视为不透明身份；它不是整个 Save 的 ID。`display_name` 使用存档内角色名，不得改用平台账号名、`userID`、`platformID` 或本地化的“自己”。
- `relation` 只能通过玩家 ID 是否等于当前 `Game1.player` 的 ID 判定，不得使用事件期间可能指向其他 Farmer 实例的宽松 Local Player 语义。`is_host` 独立表示存档主机身份，因此客户端自己的结果可以同时为 `relation=MYSELF`、`is_host=false`。
- `online=false` 时不得返回 `position`、`facing`、`energy`、`max_energy` 或 `is_in_bed`，也不得把 `disconnectLocation`、`disconnectPosition` 或保存对象中的旧资源值冒充实时事实。在线字段临时不可确认时保持缺省；`energy` 与 `max_energy` 必须同时出现或同时缺省。
- `position.location_id` 与 `home_location_id` 必须使用 `NameOrUniqueName`；多人 Cabin 必须保留唯一室内 ID。住宅无法安全解析时缺省 `home_location_id`，不得退化为显示名称或扫描猜测。
- V1 不返回其他玩家的生命值、`can_move`、金钱、背包、技能、装备或平台身份；这些字段不属于玩家发现能力，或无法作为可靠的跨客户端实时事实。游戏尚未加载 Save 时返回 `NOT_READY`。

### `query_world`

- 未提供 Region 时使用玩家位置为中心、半径 8。
- `TileArea.width/height` 分别为 `1..32`，总面积不超过 1024。
- `RadiusArea.radius` 为 `0..15`；Location 为空或未加载返回 `NOT_FOUND`。
- 显式 Region 的 `location_id` 必须完全匹配一个当前已加载 Location 的 `NameOrUniqueName`，比较时大小写不敏感；不得回退到本地化名称、短建筑名或旧 Map Token。
- 请求范围与地图边界相交时，`snapshot.area` 返回裁剪后的实际矩形；完全不相交时返回 `OUT_OF_RANGE`。`around.center.location_id` 与被查询 Location 必须一致。
- 三个 `include_*` 缺省时解释为 `true`；可以显式关闭任意集合。
- `entity_kinds` 仅在 `include_entities` 未显式设为 `false` 时允许。
- `max_entities`、`max_characters` 为 `1..512`；0 分别使用默认值 256。
- Tile 按 `(y,x)` 排序且在区域合法时不得截断；Entity 与 Character 分别按 Ref Value UTF-8 升序排序后截断，并设置各自的 `*_truncated`。
- Tile 的布尔字段没有 Unknown presence；任一 Tile 读取因 Location 或第三方 override 异常而无法完成时，整个命令必须以 `EXECUTION_FAILED` 失败，不得把不可读字段伪造为 `false`，也不得依赖传输层将异常改写为通用 `INTERNAL`。
- 需要完整集合时，调用方必须缩小区域重新查询；V1 不提供跨 Revision Cursor。

### `query_inventory`

- 未提供 Container 时默认玩家背包；`player_inventory` 空消息与缺省语义相同。
- Watering Can 的 `ItemFact` 必须存在 `water_remaining`、`water_capacity` 与 `bottomless`；其他物品必须缺省这三个可选字段，不能用零值冒充喷壶事实。
- `container_ref` 必须解析为带 `ContainerFact` 的 `WORLD_ENTITY` 或当前可读取的 `CONTAINER` 库存视图，否则返回 `STALE_REF`、`NOT_FOUND` 或 `INVALID_ARGUMENT`。
- V1 的可读取世界容器是当前已加载 Location 中由 `query_world` 返回的 Chest/Fridge 类实体；不通过显示名、坐标字符串或短地图名旁路 Ref 校验。
- `container_kind` 的稳定值固定为 `player`、`fridge`、`junimo_chest`、`mini_shipping_bin`、`auto_loader`、`big_chest`、`chest` 或 `container`；`query_world` 与 `query_inventory` 必须使用同一分类规则。
- 容器库存必须只读取已经存在的 Local、Global、Separate Wallet 或 Junimo backing；缺失的共享 backing 解释为空逻辑视图，不得通过查询创建 backing 或写入游戏状态。容量使用父 Chest 的实际容量；容量为负、backing 数量超过容量或关键 getter 异常时，以脱敏 `INTERNAL` 失败。
- Slot 按 Index 升序。`include_empty_slots=false` 时可以省略空 Slot，但保留原始 Index。
- `inventory_revision` 必须先基于全容量 Slot、完整 `ItemFact`、Item Ref 与 owner 内部事实计算，再过滤空 Slot；因此同一状态下 `include_empty_slots` 不改变 Revision。玩家当前选中 Slot 属于 Revision 材料，容器 Revision 不受玩家切换工具影响。
- 同一父容器经 `WORLD_ENTITY` 或其 `CONTAINER` Ref 查询时，必须返回相同的 Container Ref、Slot、Item Ref、`container_kind`、`slot_count` 与 Revision。Container Ref 绑定父 Chest 与 Location，不得只绑定可能由多个容器共享的库存 backing。
- 玩家背包与可读容器的每个非空 Slot Item 都必须携带 `INVENTORY_ITEM` Ref，供后续 `inspect` 解析；是否可用于 `equip` 仍按“操作能力”中的玩家背包来源与 Revision 规则判定。

### `query_ui`

始终返回当前 `ui_revision`。没有菜单时 `menu_open=false`、Menu 缺省且 Elements 为空；有菜单时 Menu 必须存在，Elements 按 `(kind,inventory_side-or-unspecified,equipment-slot-kind-or-unspecified,index,ref.value)` 稳定排序。V1 只对精确原版 `GameMenu` 的顶层 Tab 与下述 Inventory／Crafting 页元素、精确原版 `DialogueBox` 已经出现在 `responseCC` 中的响应、精确原版非选择型 `DialogueBox` 的唯一语义推进元素、精确原版 `ShopMenu` 当前 viewport 的出售行，以及下述受支持 `ItemGrabMenu` 的两侧槽位签发 `UI_ELEMENT` Ref；派生类和其他菜单只返回公共 Menu shell、空 Elements 与 `UI_MENU_UNSUPPORTED` warning，不使用通用 clickable fallback。

非选择型 `DialogueBox` 必须且只能投影一个 `DIALOGUE_ADVANCE`。当前页后面仍有页面时标签为“继续”，否则为“结束”；判断必须与游戏自身的下一页/关闭图标语义一致。只有正文完整呈现、菜单不在过渡且 `safetyTimer <= 0` 时 `enabled=true`。问题对话只投影 `DIALOGUE_RESPONSE`，不得同时投影推进元素。页面变化、对话关闭或菜单替换后旧推进 Ref 必须 stale；同一稳定页面的重复查询与 `inspect` 必须复用同一 Ref。

精确原版 `ShopMenu` 只投影当前 viewport 的出售行，每行包含稳定 Ref、可读时的 ItemFact、原版单轮价格与有限库存；非金币货币和额外交换物分别使用 `UI_PRICE_CURRENCY_UNREPRESENTED` 与 `UI_PRICE_PARTIAL` warning。商品行始终 `enabled=false`，不得通过 `activate_ui` 点击；普通金币实物只能将该 Ref 与同一 UI Revision 交给 `purchase_shop_item`。滚出 viewport、售罄移除、菜单／组件／商品对象替换后旧 Ref stale；价格或库存变化必须推进 UI Revision。

精确原版 `GameMenu` 当前位于精确原版 Inventory 页时，必须在顶部 Tab 之外投影所有已解锁玩家背包格与实际存在的装备槽。背包视觉组件可以多于玩家 `MaxItems`，但只公开 `0..MaxItems-1` 的真实 Slot；Snapshot 必须恰好有一条 PLAYER `UiInventoryLink`，其 Item Ref 与 Inventory Revision 必须复用同一时刻 `query_inventory(include_empty_slots=true)` 的权威结果。装备槽使用 `EQUIPMENT_SLOT + equipment_slot_kind + 同种类内 index`，固定 Hat、Left Ring、Right Ring、Boots、Shirt、Pants 的 index 为 0，Trinket 使用从 0 开始的 ordinal。装备物品不属于玩家背包，非空槽只嵌入不带 Ref 的完整 `ItemFact`，空槽缺省 `item`；背包空槽缺省 `item_ref`。背包格与装备槽都必须存在、`enabled=false`，不得通过 `activate_ui` 点击。

Inventory 页槽位 UI Ref 表示逻辑槽位而非当前 Item：同一菜单、页面和组件内的内容变化应复用槽位 Ref并更新 UI Revision；页面切走、菜单或组件替换后旧 Ref stale。装备槽事实只受当前 UI Revision 保护，不新增 Equipment Revision。现有 `equip` 仍只选择玩家工具栏/当前手持背包物品，不表示穿戴装备。

精确原版 `GameMenu` 当前位于精确原版、非烹饪 `CraftingPage` 时，必须投影该页已构建的全部配方组件，不得从全局数据重新构造或按名称搜索。当前配方页元素 `visible=true`，其他页为 `false`；所有 `CRAFTING_RECIPE` 元素均 `enabled=false`，`activate_ui` 不得执行它们。每个事实必须包含配方键、显示名、已知状态、只表示材料充足的 `craftable`、所有材料需求及当前可用数量，以及全部可能产出与单次数量。材料可用量同时计入玩家背包和当前页指定的材料容器，并保留原版类别与特殊野种匹配。

配方以原版 component `myID - 201` 作为全局 `index`并稳定排序，上限为 256。翻动配方页、材料变化或 held-item 状态变化必须推进 UI Revision，但已构建配方的 Ref 保持稳定；切换 Tab、关闭菜单、页面或 component／recipe 绑定被替换后旧 Ref stale。任一配方、材料容器或序号无法完整确认，或数量超过上限时，整批配方按 `UI_CRAFTING_CAPTURE_INCOMPLETE` 处理，不得部分签发 Ref。查询不得创建产出 Item、推进 RNG、消耗材料、调用点击或更新制作计数。

受支持的 `ItemGrabMenu` 仅限精确原版菜单，来源为当前 Location 中仍附着的精确原版普通 Chest、Big Chest 或内置 Fridge，并且菜单两侧 backing、容量和完整槽位组件都与权威库存一致。Global、Junimo、Mini Shipping、Separate Wallet、AutoLoader、Enricher、派生 Chest、非 Chest 来源与其他特殊菜单保持 shell-only。玩家侧与容器侧各在 `inventories` 中提供一条 `side + inventory_revision + slot_count` 轻量关联，容器侧另带 `container_ref`；每个槽位元素保留该侧真实 0-based `index`，以 `inventory_side` 区分两侧，非空时只附 `item_ref` 与显示名称，不重复完整 `ItemFact`。两侧关联、Item Ref、Container Ref 与 Revision 必须复用 `query_inventory(include_empty_slots=true)` 的 resolver 与 projector；空槽也必须公开且没有 `item_ref`。当前版本的槽位一律 `enabled=false`，不得通过 `activate_ui` 点击。

受支持 ItemGrabMenu 存在 `heldItem`，或 backing、组件拓扑、关键库存事实暂时不可完整读取时，返回空元素与 `UI_INVENTORY_CAPTURE_INCOMPLETE`，本轮按 incomplete 处理，不得淘汰旧槽位 Ref。精确 Inventory 页存在 `CursorSlotItem` 时使用 `UI_INVENTORY_CURSOR_ITEM_UNSUPPORTED`；其当前 page、backing、组件映射、装备拓扑或关键 getter 暂时不可信时使用 `UI_INVENTORY_CAPTURE_INCOMPLETE`。GameMenu 当前页或 `readyToClose` 状态不可读时使用 `UI_GAME_MENU_CAPTURE_INCOMPLETE`。上述 GameMenu incomplete 轮次必须移除全部页内元素与 PLAYER link，并把所有顶部 Tab 标为 disabled；旧页内 Ref为 `FACT_UNAVAILABLE`。只有当前 page 非空且 runtime type 明确不是原版 InventoryPage 时，才使用 `UI_GAME_MENU_PAGE_UNSUPPORTED` 返回完整 Tab-only 集合并使旧页内 Ref stale。

`modal` 是 V1 的窄 allowlist 分类值：仅精确原版 `DialogueBox` 或 `LetterViewerMenu` 为 `true`，其他类型（包括其派生类）均为 `false`；它不表示菜单一定可关闭或不阻塞游戏。UI 查询不得调用点击、按键、hover、组件填充、菜单更新、切换、购买或第三方 callback。GameMenu 顶层 Tab、Crafting 配方、DialogueBox、ShopMenu、ItemGrabMenu 的完整 extractor 分别以 64、256、64、16、128 个元素为上限；超过上限时整体降级，不得静默截断。

UI warning 使用以下稳定 code：`UI_MENU_UNSUPPORTED` 表示只有 shell；`UI_MENU_FACT_UNAVAILABLE` 表示非关键 Menu 字段不可读；`UI_GAME_MENU_PAGE_UNSUPPORTED` 表示当前 GameMenu page 是稳定不支持的派生/替换页；`UI_GAME_MENU_CAPTURE_INCOMPLETE` 表示 GameMenu 当前页或切换状态暂时不可读；`UI_ELEMENTS_NOT_PRESENTED` 表示对话响应尚未生成 clickable component；`UI_ELEMENTS_LIMIT_UNSUPPORTED` 表示超出完整投影上限；`UI_ELEMENT_PROJECTION_FAILED` 表示元素无法安全投影；`UI_INVENTORY_CAPTURE_INCOMPLETE` 表示当前库存或装备槽关联不可完整确认；`UI_INVENTORY_CURSOR_ITEM_UNSUPPORTED` 表示 Inventory 页游标持有的瞬态物品未纳入公开事实；`UI_ITEM_FACT_UNAVAILABLE`、`UI_PRICE_CURRENCY_UNREPRESENTED`、`UI_PRICE_PARTIAL` 分别表示 Shop Item、货币或交换物事实不完整。Warnings 按 `(code,ref.value-or-empty,message)` Ordinal 排序且不进入 `ui_revision`。

元素集合只有在对应 extractor 已完整枚举其公开范围时才可作为负向生命周期证据。`UI_ELEMENTS_NOT_PRESENTED`、`UI_ELEMENTS_LIMIT_UNSUPPORTED`、`UI_ELEMENT_PROJECTION_FAILED`、`UI_INVENTORY_CAPTURE_INCOMPLETE`、`UI_INVENTORY_CURSOR_ITEM_UNSUPPORTED` 或 `UI_GAME_MENU_CAPTURE_INCOMPLETE` 表示本轮元素集合不完整；实现不得据此把未观察到的旧 UI Ref 标记 stale。`UI_MENU_UNSUPPORTED` 与 `UI_GAME_MENU_PAGE_UNSUPPORTED` 的公开元素集合按其受支持范围完整；`UI_MENU_FACT_UNAVAILABLE` 只涉及非关键 Menu shell 字段，`UI_ITEM_FACT_UNAVAILABLE`、`UI_PRICE_CURRENCY_UNREPRESENTED` 和 `UI_PRICE_PARTIAL` 只影响已观察元素的附属事实，这些 warning 本身不得阻止元素集合完成。`inspect` 在不完整捕获中找不到目标 UI Ref 时返回可重试的 `FACT_UNAVAILABLE`，后续完整捕获恢复同一元素时必须继续使用原 Ref；完整捕获明确缺少目标时返回 `STALE`，且该 Ref 不得复活。

### `inspect`

批量规则和状态组合见“Snapshot、Revision 与 Ref”。一个 Ref 失败不使其他 Ref 失败；只有整个请求结构无效时才返回命令级失败。

## 6. 事实覆盖边界

V1 为常见树木、作物、空的已耕地、资源、机器、容器、床、家具、掉落物、门、Warp 和角色提供类型化 Fact。没有作物的 `HoeDirt` 必须投影为 `ENTITY_KIND_HOE_DIRT` 与 `HoeDirtFact`，其中 `watered` 表示该格当前是否已浇水；同一 Ref 经 `query_world` 与 `inspect` 读取时必须得到一致的 `HoeDirtFact`。带作物的 `HoeDirt` 继续投影为 `ENTITY_KIND_CROP` 与 `CropFact`，其含水状态仍以 `CropFact.watered` 表达，不得同时附加 `HoeDirtFact`；`harvest_action` 明确区分原生交互收获与镰刀收获。可睡床的 `BedFact.sleep_position` 必须使用床的实际睡眠触发格，不能拿家具锚点或占用格集合代替。其他原版或第三方 Mod 地图对象使用 `ENTITY_KIND_GENERIC_OBJECT` 与 `GenericObjectFact` 提供最小 Runtime Type、Qualified Item ID、位置、显示名和可交互性；实现不得静默丢弃无法类型化但位于查询区域的可见对象。

单个 Character 或 FarmAnimal 的第三方 getter 抛出异常时，实现必须保留只由已知枚举位置、opaque Ref，以及可由安全 CLR 类型判断的现有 `CharacterKind` 构成的最小 `CharacterFact`，并附 `CHARACTER_PROJECTION_FALLBACK` warning；不得继续猜测名称、朝向或类型详情，也不得输出 Proto 中不存在的 runtime type 字段。同一 World Entity 或 Character 已由 `query_world` 生成安全 fallback 后，`inspect` 必须保留调用方传入的 Ref，并返回相同语义的最小 Fact 与 fallback warning；不得重试已知失败 getter、重新签发 Ref 或降级为 `FACT_UNAVAILABLE`。如果连枚举位置也不可读，则跳过该角色并附不带 Ref 的 `CHARACTER_PROJECTION_SKIPPED` warning，不得编造坐标。Location 的 `GetFridgePosition`/`GetFridge` 抛出异常时，只跳过该冰箱并附不带 Ref 的 `FRIDGE_DISCOVERY_FAILED` warning，不得中止其他实体投影或生成悬空 Ref。

`ItemFact.category` 是 `Item.Category` 使用 invariant culture 格式化得到的十进制整数字符串；它不是本地化分类名称。

`WorldEntityFact.actionable` 只在实现能以无副作用方式可靠判断“创建该 Snapshot 的当前玩家在同一逻辑 Tick 是否可操作该实体”时出现：`true` 表示可操作，`false` 表示已知不可操作。字段缺省表示可操作性未知；包含该 World Entity 的 `QueryWorldResult` 或 `InspectResult` 必须附带 `code=ENTITY_ACTIONABLE_UNKNOWN` 的 `QueryWarning`，且 `ref` 指向对应的 World Entity。第三方实现的可操作性 getter 抛出异常或无法安全调用时，实现必须保留该实体的其他可读取事实，以缺省字段与 warning 表达未知，不得静默映射为 `false`。该字段不是执行权限授予，也不表示玩家已经相邻、路径可达、当前没有 Modal，亦不保证后续 Tick 执行交互时仍可操作。

`DoorFact.locked` 只在实现能以无副作用方式可靠判断“创建该 Snapshot 的当前玩家在同一逻辑 Tick 是否可通过此门”时出现：`true` 表示当前不可通过，`false` 表示当前可以通过。字段缺省表示准入状态未知；包含该 Door Fact 的 `QueryWorldResult` 或 `InspectResult` 必须附带 `code=DOOR_ACCESS_UNKNOWN` 的 `QueryWarning`，且 `ref` 指向对应的 World Entity。实现不得通过调用会传送、播放声音、弹出对话或改变游戏状态的入口来填充该字段，也不得用 `false` 代替未知。该字段不表示玩家已经相邻、路径可达、当前没有 Modal，亦不保证后续 Tick 执行交互时状态不变。

## 7. 观察能力性能预算

观察 Handler 在 SMAPI 主线程同步生成 Snapshot，因此必须同时限制输入规模、结果大小与单次主线程占用。以下预算是公开 V1 的参考验收门槛，而不是向调用方承诺所有硬件上的实时 SLA：

- 任一成功 `CommandEvent` 序列化后必须小于 `786432` 字节，为线路的 1 MiB 帧上限保留至少 25% 余量；实现不得先生成超大结果再依赖 Transport 拒绝。
- 真实存档中的默认 `query_world`（玩家半径 8）单次 Handler 目标为不超过 16 ms；最大合法 1024 Tile 区域单次不超过 50 ms。
- `query_players`、`query_inventory`、`query_ui` 与最多 64 个 Ref 的 `inspect` 单次 Handler 目标为不超过 16 ms。
- 实机门禁必须记录纯 Handler 耗时和序列化字节数；MCP 往返时间包含排队到下一 Tick、线路和客户端投影，不得冒充主线程耗时。
- 超过预算时必须缩小扫描、减少重复投影或优化查找；不得把游戏对象读取移动到后台线程，也不得通过遗漏 Generic Object、关闭默认集合或降低契约上限来伪造通过。
