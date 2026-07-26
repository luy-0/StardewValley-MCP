# V1 能力行为契约

状态：**公开 V1 候选契约**

Proto 是字段和编号权威，Manifest 是公开集合与策略权威；本文定义无法由普通 Proto 类型表达的参数约束、默认值、Ref/Revision 生命周期以及成功后置条件。违反输入约束的命令必须在接受前以 `INVALID_ARGUMENT` 拒绝，不能依赖具体 Handler 的偶然行为。

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

以下任一内容变化都必须生成新 `inventory_revision`：Slot 数量、Slot 中的物品身份、堆叠、品质、工具等级或当前选中 Slot。只有 `InventorySnapshot.slots[].item.ref` 保证是可以用于 `equip` 的 Inventory Item Ref；Machine、Loose Item 和 UI 中嵌套的 `ItemFact` 可以没有 Ref，即使存在也不能作为 Equip 权限。

### World Revision 与普通 Ref

`world_revision` 在所查询区域的 Tile、Entity 或 Character 身份与动作相关事实变化时更新。World Entity 与 Character Ref 可以跨查询复用，但 Location 重建、对象销毁、身份 Guard 变化或 Mod 重启后必须变为 `STALE_REF`。

所有 Revision 与 Ref 都只在创建它们的 `mod_instance_id` 对应的游戏进程中有效。实现可以在不透明值内部编码实例和 Guard，但这些编码不属于公共契约。

### Inspect 不变量

`inspect` 按请求顺序返回一项结果；输入不得为空、不得重复，数量为 `1..64`。每个 `InspectedRef` 必须满足：

| Ref Kind | `InspectedRef.fact` 分支 | 来源与动作边界 |
|---|---|---|
| `WORLD_ENTITY` | `world_entity` | `query_world.entities[].ref`；容器对象也保持此 Kind，可用于导航、交互，并可作为 `query_inventory.container_ref` 输入 |
| `CHARACTER` | `character` | `query_world.characters[].ref`；可用于导航或交互 |
| `INVENTORY_ITEM` | `inventory_item` | 玩家背包 Slot 中的 Item Ref；只能在匹配 Inventory Revision 下用于 `equip` |
| `CONTAINER` | `inventory` | `InventorySnapshot.container_ref`；表示库存视图，不是地图实体，不可用于导航或交互 |
| `UI_ELEMENT` | `ui_element` | `query_ui.elements[].ref`；只能在匹配 UI Revision 下用于 `activate_ui` |

`query_inventory.container_ref` 接受两类输入：带 `ContainerFact` 的 `WORLD_ENTITY` Ref，或先前 `InventorySnapshot.container_ref` 返回的 `CONTAINER` Ref。前者标识地图上的容器实体，后者标识该容器的库存视图；实现不得仅凭 Ref 字符串格式猜测 Kind。

| Resolution 状态 | Kind | Fact | Error |
|---|---|---|---|
| `RESOLVED` | 非 `UNSPECIFIED` | 必须存在且与 Kind 匹配 | 不得存在 |
| `STALE` | 已知时提供 | 不得存在 | `STALE_REF` |
| `NOT_FOUND` | 已知时提供 | 不得存在 | `NOT_FOUND` |
| `UNSUPPORTED` | 已知时提供 | 不得存在 | `INVALID_ARGUMENT` |

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
- 成功要求 Final Position 满足 Arrival，且最终朝向满足可选要求。路径不存在、目标消失或抵达条件不成立不能返回成功。

### `interact`

- 目标必须在玩家当前 `location_id`，并位于游戏交互允许的相邻 Tile；能力不会隐式导航。
- Ref 必须解析为当前可交互的 World Entity 或 Character。
- 成功要求观察到与本次交互关联的游戏后置条件，例如 Dialogue/Menu 打开、对象状态变化、物品变化或 Relationship 变化。没有任何可关联效果时返回 `EXECUTION_FAILED`。

### `use_tool`

- 目标必须位于当前 Location 和当前工具的合法作用范围；能力不会隐式导航或装备工具。
- `charge_level` 最大为 5，并且不得超过当前工具支持的等级。
- 成功要求工具动作由游戏接受且相关动画完成；击中空 Tile 也可以成功，不要求一定改变世界状态。
- Result 必须回显实际工具 Qualified Item ID、实际 Charge Level 和 Energy 变化。

### `equip`

- 必须且只能提供 `slot_index` 或 `item_ref`。
- `slot_index` 必须小于当前玩家背包 Slot 数；该 Slot 为空时返回 `NOT_FOUND`。
- 使用 `item_ref` 时必须同时提供产生该 Ref 的当前 `inventory_revision`；Ref 必须来自玩家背包 Snapshot。
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
- 三个 `include_*` 缺省时解释为 `true`；可以显式关闭任意集合。
- `entity_kinds` 仅在 `include_entities` 未显式设为 `false` 时允许。
- `max_entities`、`max_characters` 为 `1..512`；0 分别使用默认值 256。
- Tile 按 `(y,x)` 排序且在区域合法时不得截断；Entity 与 Character 分别按 Ref Value UTF-8 升序排序后截断，并设置各自的 `*_truncated`。
- 需要完整集合时，调用方必须缩小区域重新查询；V1 不提供跨 Revision Cursor。

### `query_inventory`

- 未提供 Container 时默认玩家背包；`player_inventory` 空消息与缺省语义相同。
- `container_ref` 必须解析为带 `ContainerFact` 的 `WORLD_ENTITY` 或当前可读取的 `CONTAINER` 库存视图，否则返回 `STALE_REF`、`NOT_FOUND` 或 `INVALID_ARGUMENT`。
- Slot 按 Index 升序。`include_empty_slots=false` 时可以省略空 Slot，但保留原始 Index。

### `query_ui`

始终返回当前 `ui_revision`。没有菜单时 `menu_open=false`、Menu 缺省且 Elements 为空；有菜单时 Menu 必须存在，Elements 按 `(kind,index,ref.value)` 稳定排序。

### `inspect`

批量规则和状态组合见“Snapshot、Revision 与 Ref”。一个 Ref 失败不使其他 Ref 失败；只有整个请求结构无效时才返回命令级失败。

## 6. 事实覆盖边界

V1 为常见树木、作物、资源、机器、容器、床、家具、掉落物、门、Warp 和角色提供类型化 Fact。其他原版或第三方 Mod 地图对象使用 `ENTITY_KIND_GENERIC_OBJECT` 与 `GenericObjectFact` 提供最小 Runtime Type、Qualified Item ID、位置、显示名和可交互性；实现不得静默丢弃无法类型化但位于查询区域的可见对象。
