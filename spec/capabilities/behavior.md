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

以下任一内容变化都必须生成新 `inventory_revision`：Slot 数量、Slot 中的物品身份、堆叠、品质、工具等级或当前选中 Slot。所有 `QueryInventoryResult.snapshot.slots[].item.ref` 都是可用于 `inspect` 的 `INVENTORY_ITEM` Ref，包括玩家背包与可读容器中的非空 Slot。只有由 `player_inventory` 选择器（或其缺省等价形式）生成、且调用时仍匹配当前玩家背包与 `inventory_revision` 的 Item Ref 可以用于 `equip`；容器库存 Item Ref 不得用于 `equip`。Machine、Loose Item 和 UI 中嵌套的 `ItemFact` 可以没有 Ref；即使存在，是否可用于 `inspect` 仍由服务端 Ref Binding 决定，且不得据此获得 `equip` 权限。

### World Revision 与普通 Ref

`world_revision` 在所查询区域的 Tile、Entity 或 Character 身份与动作相关事实变化时更新。World Entity 与 Character Ref 可以跨查询复用，但 Location 重建、对象销毁、身份 Guard 变化或 Mod 重启后必须变为 `STALE_REF`。

所有 Revision 与 Ref 都只在创建它们的 `mod_instance_id` 对应的游戏进程中有效。实现可以在不透明值内部编码实例和 Guard，但这些编码不属于公共契约。

使用 World Entity 或 Character Ref 启动 `navigate`、`interact` 或 `use_tool` 时，实现必须在命令开始的主线程安全点解析并锁定同一游戏对象、当时的 `NameOrUniqueName` 与动作 Tile。命令不会持续追踪移动目标；在提交动作或返回成功前必须重验同一对象仍存在且位置没有变化。命令开始前已经失效的绑定返回 `STALE_REF`，Kind 不适用于该能力返回 `INVALID_ARGUMENT`；命令开始后对象消失、换图或移动则返回 `EXECUTION_FAILED`，不得用旧坐标继续执行或成功收口。

### Inspect 不变量

`inspect` 按请求顺序返回一项结果；输入不得为空、不得重复，数量为 `1..64`。每个 `InspectedRef` 必须满足：

| Ref Kind | `InspectedRef.fact` 分支 | 来源与动作边界 |
|---|---|---|
| `WORLD_ENTITY` | `world_entity` | `query_world.entities[].ref`；容器对象也保持此 Kind，可用于导航、交互，并可作为 `query_inventory.container_ref` 输入 |
| `CHARACTER` | `character` | `query_world.characters[].ref`；可用于导航或交互 |
| `INVENTORY_ITEM` | `inventory_item` | 玩家背包或可读容器 `InventorySnapshot` 中的非空 Slot Item Ref；均可用于 `inspect`，只有玩家背包来源且匹配当前 Inventory Revision 的 Ref 可用于 `equip` |
| `CONTAINER` | `inventory` | `InventorySnapshot.container_ref`；表示库存视图，不是地图实体，不可用于导航或交互 |
| `UI_ELEMENT` | `ui_element` | `query_ui.elements[].ref`；只能在匹配 UI Revision 下用于 `activate_ui` |

`query_inventory.container_ref` 接受两类输入：带 `ContainerFact` 的 `WORLD_ENTITY` Ref，或先前 `InventorySnapshot.container_ref` 返回的 `CONTAINER` Ref。前者标识地图上的容器实体，后者标识该容器的库存视图；实现不得仅凭 Ref 字符串格式猜测 Kind。

| Resolution 状态 | Kind | Fact | Error |
|---|---|---|---|
| `RESOLVED` | 非 `UNSPECIFIED` | 必须存在且与 Kind 匹配 | 不得存在 |
| `STALE` | 已知时提供 | 不得存在 | `STALE_REF` |
| `NOT_FOUND` | 已知时提供 | 不得存在 | `NOT_FOUND` |
| `UNSUPPORTED` | 已知时提供 | 不得存在 | `INVALID_ARGUMENT` |
| `FACT_UNAVAILABLE` | 非 `UNSPECIFIED` | 不得存在 | `INTERNAL`，消息固定为“当前 Ref 事实不可用” |

`STALE` 只在对象或 owner 生命周期已由肯定证据终止时产生，并保持单调不复活。getter、关键字段、typed projector 或完整 UI capture 本次不可读，但 Binding 身份仍可验证时，必须逐项返回 `FACT_UNAVAILABLE`；该项不改变 Binding、不产生 warning，也不得拖垮同批其他 Ref。既有安全 fallback 若已形成完整公开 Fact，仍返回 `RESOLVED` 及对应 warning。

## 3. 成功终态的统一含义

`SUCCEEDED` 表示能力声明的后置条件已经通过游戏侧观测确认，而不只是输入已经发送。结果消息中不再提供 `reached=false`、`activated=false` 等可以和成功终态冲突的布尔值。

如果操作输入合法但后置条件未在 Deadline 前成立，必须返回 `FAILED/EXECUTION_FAILED` 或 `TIMED_OUT/DEADLINE_EXCEEDED`。已经满足目标状态的幂等 No-op 可以成功，但结果必须明确说明，例如 `FaceResult.changed=false`、`EquipResult.changed=false`、`CloseMenuResult.already_closed=true`。

## 4. 操作能力

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
- 提交 PFC 或门动作前允许取消；取消或 Deadline 必须清除当前 PFC、移动方向与尚未提交的门输入。已经触发地图切换时继续收敛到稳定 Location，再按 Coordinator 的停止信号结束，不能传送回滚。

### `interact`

- 目标必须在玩家当前 `location_id`，并位于游戏交互允许的相邻 Tile；能力不会隐式导航。
- Ref 必须解析为当前可交互的 World Entity 或 Character。
- 首版要求游戏窗口处于前台；失焦时返回 `NOT_READY`，不得提交一个无法可靠观察后置条件的动作，也不得回退到全局输入注入。
- 玩家提交动作时必须空手或手持 `Tool`。手持食物、礼物、可放置物或其他非工具 Item 时返回 `NOT_READY`，不得把通用交互隐式扩张为赠礼、食用或放置。
- 提交前必须重验目标、面朝目标，并使 `GetGrabTile()` 与目标 Tile 对齐；为对齐进行的 Tile 内微移不得让玩家离开起始 Tile。
- 成功要求观察到与本次交互关联的游戏后置条件，例如 Dialogue/Menu 打开、对象状态变化、物品变化或 Relationship 变化。没有任何可关联效果时返回 `EXECUTION_FAILED`。
- 调用游戏动作 API 前允许取消；游戏已经消费本次动作后不得再报告取消成功，此时 `CanCancel=false` 并继续观察真实后置条件。输入 API 的返回值、玩家暂时 Busy 或动画回到 Idle 都不能单独证明交互成功。

### `use_tool`

- 目标必须位于当前 Location 和当前工具的合法作用范围；能力不会隐式导航或装备工具。
- 首版要求游戏窗口处于前台；失焦时返回 `NOT_READY`，不得提交一个无法正常结算的工具动作，也不得回退到全局输入注入。
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

### `open_menu`

- `menu` 必须是受支持的非 `UNSPECIFIED` 顶层菜单。
- 存在不能安全关闭或切换的 Modal 时返回 `NOT_READY`，不得通过坐标点击绕过。
- 成功要求 `menu_type_after` 对应请求 Menu；目标已经打开时允许幂等成功。

### `activate_ui`

- `element_ref` 必须来自当前 `query_ui`，`ui_revision` 必须完全匹配。
- 元素必须 `visible=true` 且 `enabled=true`。一次调用只执行一次 Primary Activation，不提供 Click Count。
- 成功要求游戏接受激活，并观察到新的 UI Revision 或与该元素对应的游戏事实变化；否则返回 `EXECUTION_FAILED`。

### `close_menu`

- 没有菜单时以 `already_closed=true` 幂等成功。
- 有菜单时，成功要求 `menu_type_after` 为空且新的 UI Snapshot 表示没有菜单。
- 游戏拒绝关闭的强制 Modal 返回 `NOT_READY`。

## 5. 查询能力

### `query_runtime`

返回日期、时间、天气、玩家位置、资源和 UI 摘要。游戏尚未加载 Save 时返回 `NOT_READY`，不能返回由零值拼成的假 Snapshot。

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
- `container_ref` 必须解析为带 `ContainerFact` 的 `WORLD_ENTITY` 或当前可读取的 `CONTAINER` 库存视图，否则返回 `STALE_REF`、`NOT_FOUND` 或 `INVALID_ARGUMENT`。
- V1 的可读取世界容器是当前已加载 Location 中由 `query_world` 返回的 Chest/Fridge 类实体；不通过显示名、坐标字符串或短地图名旁路 Ref 校验。
- `container_kind` 的稳定值固定为 `player`、`fridge`、`junimo_chest`、`mini_shipping_bin`、`auto_loader`、`big_chest`、`chest` 或 `container`；`query_world` 与 `query_inventory` 必须使用同一分类规则。
- 容器库存必须只读取已经存在的 Local、Global、Separate Wallet 或 Junimo backing；缺失的共享 backing 解释为空逻辑视图，不得通过查询创建 backing 或写入游戏状态。容量使用父 Chest 的实际容量；容量为负、backing 数量超过容量或关键 getter 异常时，以脱敏 `INTERNAL` 失败。
- Slot 按 Index 升序。`include_empty_slots=false` 时可以省略空 Slot，但保留原始 Index。
- `inventory_revision` 必须先基于全容量 Slot、完整 `ItemFact`、Item Ref 与 owner 内部事实计算，再过滤空 Slot；因此同一状态下 `include_empty_slots` 不改变 Revision。玩家当前选中 Slot 属于 Revision 材料，容器 Revision 不受玩家切换工具影响。
- 同一父容器经 `WORLD_ENTITY` 或其 `CONTAINER` Ref 查询时，必须返回相同的 Container Ref、Slot、Item Ref、`container_kind`、`slot_count` 与 Revision。Container Ref 绑定父 Chest 与 Location，不得只绑定可能由多个容器共享的库存 backing。
- 玩家背包与可读容器的每个非空 Slot Item 都必须携带 `INVENTORY_ITEM` Ref，供后续 `inspect` 解析；是否可用于 `equip` 仍按“操作能力”中的玩家背包来源与 Revision 规则判定。

### `query_ui`

始终返回当前 `ui_revision`。没有菜单时 `menu_open=false`、Menu 缺省且 Elements 为空；有菜单时 Menu 必须存在，Elements 按 `(kind,index,ref.value)` 稳定排序。V1 只对精确原版 `GameMenu` 的顶层 Tab、精确原版 `DialogueBox` 已经出现在 `responseCC` 中的响应，以及精确原版 `ShopMenu` 当前 viewport 的出售行签发 `UI_ELEMENT` Ref；派生类和其他菜单只返回公共 Menu shell、空 Elements 与 `UI_MENU_UNSUPPORTED` warning，不使用通用 clickable fallback。

`modal` 是 V1 的窄 allowlist 分类值：仅精确原版 `DialogueBox` 或 `LetterViewerMenu` 为 `true`，其他类型（包括其派生类）均为 `false`；它不表示菜单一定可关闭或不阻塞游戏。UI 查询不得调用点击、按键、hover、组件填充、菜单更新、切换、购买或第三方 callback。GameMenu、DialogueBox、ShopMenu 的完整 extractor 分别以 32、64、16 个元素为上限；超过上限时整体降级为 shell-only 并返回 `UI_ELEMENTS_LIMIT_UNSUPPORTED`，不得静默截断。

UI warning 使用以下稳定 code：`UI_MENU_UNSUPPORTED` 表示只有 shell；`UI_MENU_FACT_UNAVAILABLE` 表示非关键 Menu 字段不可读；`UI_ELEMENTS_NOT_PRESENTED` 表示对话响应尚未生成 clickable component；`UI_ELEMENTS_LIMIT_UNSUPPORTED` 表示超出完整投影上限；`UI_ELEMENT_PROJECTION_FAILED` 表示元素无法安全投影；`UI_ELEMENT_ACTIVATION_UNCERTAIN` 表示无法无副作用证明可激活；`UI_ITEM_FACT_UNAVAILABLE`、`UI_PRICE_CURRENCY_UNREPRESENTED`、`UI_PRICE_PARTIAL` 分别表示 Shop Item、货币或交换物事实不完整。Warnings 按 `(code,ref.value-or-empty,message)` Ordinal 排序且不进入 `ui_revision`。

### `inspect`

批量规则和状态组合见“Snapshot、Revision 与 Ref”。一个 Ref 失败不使其他 Ref 失败；只有整个请求结构无效时才返回命令级失败。

## 6. 事实覆盖边界

V1 为常见树木、作物、资源、机器、容器、床、家具、掉落物、门、Warp 和角色提供类型化 Fact。其他原版或第三方 Mod 地图对象使用 `ENTITY_KIND_GENERIC_OBJECT` 与 `GenericObjectFact` 提供最小 Runtime Type、Qualified Item ID、位置、显示名和可交互性；实现不得静默丢弃无法类型化但位于查询区域的可见对象。

单个 Character 或 FarmAnimal 的第三方 getter 抛出异常时，实现必须保留只由已知枚举位置、opaque Ref，以及可由安全 CLR 类型判断的现有 `CharacterKind` 构成的最小 `CharacterFact`，并附 `CHARACTER_PROJECTION_FALLBACK` warning；不得继续猜测名称、朝向或类型详情，也不得输出 Proto 中不存在的 runtime type 字段。如果连枚举位置也不可读，则跳过该角色并附不带 Ref 的 `CHARACTER_PROJECTION_SKIPPED` warning，不得编造坐标。Location 的 `GetFridgePosition`/`GetFridge` 抛出异常时，只跳过该冰箱并附不带 Ref 的 `FRIDGE_DISCOVERY_FAILED` warning，不得中止其他实体投影或生成悬空 Ref。

`ItemFact.category` 是 `Item.Category` 使用 invariant culture 格式化得到的十进制整数字符串；它不是本地化分类名称。

`WorldEntityFact.actionable` 只在实现能以无副作用方式可靠判断“创建该 Snapshot 的当前玩家在同一逻辑 Tick 是否可操作该实体”时出现：`true` 表示可操作，`false` 表示已知不可操作。字段缺省表示可操作性未知；包含该 World Entity 的 `QueryWorldResult` 或 `InspectResult` 必须附带 `code=ENTITY_ACTIONABLE_UNKNOWN` 的 `QueryWarning`，且 `ref` 指向对应的 World Entity。第三方实现的可操作性 getter 抛出异常或无法安全调用时，实现必须保留该实体的其他可读取事实，以缺省字段与 warning 表达未知，不得静默映射为 `false`。该字段不是执行权限授予，也不表示玩家已经相邻、路径可达、当前没有 Modal，亦不保证后续 Tick 执行交互时仍可操作。

`DoorFact.locked` 只在实现能以无副作用方式可靠判断“创建该 Snapshot 的当前玩家在同一逻辑 Tick 是否可通过此门”时出现：`true` 表示当前不可通过，`false` 表示当前可以通过。字段缺省表示准入状态未知；包含该 Door Fact 的 `QueryWorldResult` 或 `InspectResult` 必须附带 `code=DOOR_ACCESS_UNKNOWN` 的 `QueryWarning`，且 `ref` 指向对应的 World Entity。实现不得通过调用会传送、播放声音、弹出对话或改变游戏状态的入口来填充该字段，也不得用 `false` 代替未知。该字段不表示玩家已经相邻、路径可达、当前没有 Modal，亦不保证后续 Tick 执行交互时状态不变。

## 7. 观察能力性能预算

观察 Handler 在 SMAPI 主线程同步生成 Snapshot，因此必须同时限制输入规模、结果大小与单次主线程占用。以下预算是公开 V1 的参考验收门槛，而不是向调用方承诺所有硬件上的实时 SLA：

- 任一成功 `CommandEvent` 序列化后必须小于 `786432` 字节，为线路的 1 MiB 帧上限保留至少 25% 余量；实现不得先生成超大结果再依赖 Transport 拒绝。
- 真实存档中的默认 `query_world`（玩家半径 8）单次 Handler 目标为不超过 16 ms；最大合法 1024 Tile 区域单次不超过 50 ms。
- `query_inventory`、`query_ui` 与最多 64 个 Ref 的 `inspect` 单次 Handler 目标为不超过 16 ms。
- 实机门禁必须记录纯 Handler 耗时和序列化字节数；MCP 往返时间包含排队到下一 Tick、线路和客户端投影，不得冒充主线程耗时。
- 超过预算时必须缩小扫描、减少重复投影或优化查找；不得把游戏对象读取移动到后台线程，也不得通过遗漏 Generic Object、关闭默认集合或降低契约上限来伪造通过。
