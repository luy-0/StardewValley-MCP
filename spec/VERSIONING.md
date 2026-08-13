# 版本与兼容性

状态：**公开 V1 候选契约**

| 契约 | V1 候选 | 版本位置 |
|---|---:|---|
| Mod–MCP 线路协议 | `1.0` | `ProtocolVersion` 与 Proto package |
| 公开能力契约 | 按能力独立，当前为 `1.0.0` 或 `1.1.0` | `capabilities/manifest.yaml` |
| 可执行 Skill 包契约 | `1` | `skill/runtime-manifest.schema.json` 的 `schemaVersion` |

仓库产品版本独立于上述契约版本。实现完成前产品使用 `0.x` 预览版本；这不改变已经冻结的候选契约编号。

## Proto 兼容规则

- 字段编号和枚举编号不得复用。
- 删除的字段和枚举值必须标记为 `reserved`。
- 增加可选字段只有在旧接收方能够安全忽略，且新发送方不要求旧端提供时才属于 Minor 兼容。
- 增加必要语义约束、改变字段含义、改变 `oneof` 分支含义或改变终态语义属于 Major 破坏性变更。
- 线路 Major 不同必须拒绝，不得回退到其他格式。
- Patch 不参与线路协商，只能修正文档、Fixture 或不改变线路解释的问题。

## 能力兼容规则

- 一个能力 ID 只标识一种语义操作，不得挪作他用。
- 增加可选输入、增加可忽略结果字段或新增能力通常属于 Minor 兼容，但仍必须验证 MCP Tool Schema 对旧调用方的影响。
- 收窄输入、删除结果字段、改变副作用、弱化风险声明、改变取消语义或扩大默认权限属于破坏性变更。
- 能力的 `contract_version` 与线路版本独立；实现只暴露 Manifest、Mod Registry、MCP Projection 和本地授权的交集。

### 当前 V1 的兼容性增补

- `CropFact`、`MachineFact`、`FarmAnimalFact` 与 `FurnitureFact` 追加的生长、加工、照料、归属和交互事实全部带 presence（`interaction_kinds` 依 repeated 规则除外），`query_world` 与复用同一公共投影的 `inspect` 能力契约版本同步升至 `1.1.0`。旧接收方可以忽略这些结果字段；新调用方只有在字段明确出现时才能使用，不能把零值、空字符串、空枚举集合、`UNSPECIFIED` 或机器 `UNKNOWN` 解释成已确认的业务状态。`FurnitureFact.interaction_profile_complete=true` 时，空的 `interaction_kinds` 才表示已完整判断为普通装饰家具。
- `EmoteKind` 在保留既有 `1..8` 编号与语义的前提下追加原版玩家表情，`emote` 能力契约版本升至 `1.1.0`。这是输入枚举的 Minor 扩展：旧调用方仍可继续发送原八项；新调用方必须以 Mod 公告的精确能力版本为准，不得向旧实现发送新增枚举，也不得发送任意原始整数。
- `TileFact.watering_can_refillable=7` 与 `TileFact.pathfinding_blocked=8` 是带 presence 的 `query_world` V1 Minor 可选结果增补。旧接收方可以忽略；新调用方只能使用实现显式返回的游戏原生补水判定与当前玩家原生寻路碰撞判定，任一字段缺失时必须安全停止，不得把既有 `water`、`passable`、`occupied` 或零值解释为等价事实。
- `query_players=35` 及其 Request／Result、`PlayersSnapshot`、`PlayerPresenceFact` 与 `PlayerRelation` 是新增的独立只读能力分支，属于 V1 Minor 兼容增补。旧 Mod 不会公告该能力，旧 MCP 也不会暴露对应 Tool；新调用方必须把 `player_id` 视为当前存档内的不透明身份，并允许离线玩家缺少全部实时字段。
- `RuntimeSnapshot.daily_luck/queen_of_sauce`、`PlayerFact.home_location_id`、`WeatherFact.kind/tomorrow` 以及相关追加枚举／消息是 `query_runtime` 的 V1 Minor 结果增补。旧接收方可以忽略这些字段；新接收方面对未升级发送方的字段零值时不得伪造运势、菜谱、住宅或天气事实，应依赖握手后的同版本能力目录。
- `CropFact.harvest_action`、`ItemFact.tool_kind`、喷壶专属的 `ItemFact.water_remaining/water_capacity/bottomless` 与 `BedFact.sleep_position` 是追加结果字段，属于 V1 Minor 兼容增补。旧接收方可以忽略；使用新字段的 Skill 必须只在字段存在且枚举可识别时执行，不得把缺省零值推断为交互收获、工具种类、空喷壶或床位坐标。
- `UiDialogueKind` 与可选的 `UiMenuFact.dialogue_kind` 是 V1 Minor 结果增补。旧接收方可以忽略；新调用方只能使用实现明确提供的语义值，字段缺省时必须停止需要特定问题身份的自动选择，不能回退到文案或按钮顺序猜测。
- `interact` 在首个稳定公开版本前补全为游戏原生动作键语义，并把风险声明从单一 `changes_save` 扩大为 `changes_save, changes_relationship, consumes_item`。这是 V1 候选契约的发布前风险纠错：调用方升级后必须重新展示并确认写授权，且必须在调用前显式装备预期物品、调用后复查任务级后置条件；稳定版本发布后再扩大既有能力副作用必须按 Major 变更处理。
- `Error.navigation` 是可选的 `NavigationFailureContext`，用于失败导航的最后确认位置，以及正常超时时的路线段进度和续跑提示。它是旧接收方可安全忽略的附加线路字段，也是 MCP `error.details.navigation` 的可选附加字段，因此属于 V1 Minor 兼容增补；调用方不得要求所有错误或所有导航失败都存在该字段。
- `ENTITY_KIND_HOE_DIRT=14`、`WorldEntityFact.hoe_dirt=33` 与 `HoeDirtFact` 是 V1 的追加线路定义，旧接收方可以按 Proto 未知字段规则忽略，因此线路层属于 Minor 兼容增补。空 `HoeDirt` 不再作为 `ENTITY_KIND_GENERIC_OBJECT` 返回；曾只按 `generic_object` 过滤已耕地的调用方需要改为请求 `hoe_dirt`，而带作物的土地仍使用既有 `CropFact.watered`。
- `UI_ELEMENT_KIND_DIALOGUE_ADVANCE=6` 以及普通 `DialogueBox` 新增的语义推进元素，是追加枚举值与结果元素的 V1 Minor 兼容增补。旧调用方必须忽略无法识别的 UI Kind，不能尝试激活未知元素；依赖 MCP Tool Schema 的调用方需要更新 Tool Catalog 后才能识别并使用 `dialogue_advance`。
- `UiInventorySide`、`UiInventoryLink`、`UiSnapshot.inventories`、`UiElementFact.inventory_side/item_ref` 以及受支持 `ItemGrabMenu` 的只读槽位元素，是可忽略字段与结果元素的 V1 Minor 兼容增补。旧调用方可以忽略库存关联；槽位始终 `enabled=false`，不得因既有 `ITEM_SLOT` 枚举而推断其可由 `activate_ui` 执行。依赖 MCP Tool Schema 的调用方需要更新 Tool Catalog 后才能读取两侧 Revision、Container Ref 与 Item Ref。
- `transfer_inventory_item`、`InventoryTransferDirection` 及其 Request/Result 是新增的独立能力分支，属于 V1 Minor 兼容增补。旧实现不会公告该能力，旧 MCP 也不会暴露对应 Tool；新调用方必须从当前 `query_ui` 取得 UI Revision、双方 Inventory Revision 和源 Item Ref，不能把已有槽位元素解释为可点击。
- `set_equipment_slot` 及其 Request/Result 是新增的独立能力分支，属于 V1 Minor 兼容增补。调用方必须使用当前原版背包页面签发的 Equipment Slot Ref、UI Revision 和玩家 Inventory Revision；穿戴时还必须使用当前玩家背包 Item Ref。
- `move_inventory_item` 及其 Request/Result 是新增的独立能力分支，属于 V1 Minor 兼容增补。调用方必须使用当前原版背包页面签发的玩家 Item Ref、目标 Item Slot Ref、UI Revision 和玩家 Inventory Revision；能力只执行整件移动、整件交换或同槽幂等成功。
- `craft_item`、`CraftItemStopReason` 及其 Request/Result 是新增的独立能力分支，属于 V1 Minor 兼容增补。调用方必须使用当前 Crafting 页签发的 Recipe Ref 与 UI Revision；批量请求可以在至少完成一轮后以结构化停止原因返回部分成功。
- `purchase_shop_item` 及其 Request/Result 是新增的独立能力分支，属于 V1 Minor 兼容增补。调用方必须使用当前精确原版商店视口签发的 Sale Ref 与 UI Revision；首版只支持全有或全无的普通金币实物购买。
- 首个稳定公开版本发布前，Shop 商品行从通用 `activate_ui` 坐标激活中移除并固定为 `enabled=false`，作为 V1 候选契约的发布前纠错；商品行 Ref 继续存在，只能交给 `purchase_shop_item`。稳定版本发布后若再次收窄既有能力输入集合，必须按 Major 变更处理。
- `UI_ELEMENT_KIND_EQUIPMENT_SLOT`、`UiEquipmentSlotKind` 与 `UiElementFact.equipment_slot_kind` 是 Inventory 页只读投影的 V1 Minor 兼容增补。旧调用方可以忽略新增元素；装备槽始终 `enabled=false`，装备物品不携带 `INVENTORY_ITEM` Ref，也不得据此推断已经提供穿戴或取下动作。
- `UI_ELEMENT_KIND_CRAFTING_RECIPE`、`CraftingRecipeFact`、材料／产出事实与 `UiElementFact.crafting_recipe` 是 Crafting 页只读投影的 V1 Minor 兼容增补。旧调用方可忽略新增元素；配方元素始终 `enabled=false`，`craftable` 仅表示材料足够，不表示已经提供制作动作。

## Agent Skill 与可执行 Skill 兼容规则

Prompt 型 Agent Skill 是开发指引，不参与 Mod–MCP 线路协商；模板或正文变化随仓库产品版本发布。可执行 Skill 的包结构、入口、Schema、授权、超时和结果语义由 `schemaVersion` 管理，但同样不参与 Mod 线路协商。

增加可选 Manifest 字段、可忽略结果字段或新的独立 Skill 通常属于兼容增补。删除必需字段、改变入口调用形式、扩大默认信任目录、弱化未知终态保护、改变副作用 Annotation 或允许绕过声明 Tool 集合属于破坏性变更。Skill 引用的原子 Tool 行为仍以对应公开能力契约版本为准。

## 变更流程

每次契约变更必须同时包含：

1. 修改后的机器可读权威定义；
2. 兼容性分类与理由；
3. 消费方需要修改时提供迁移说明；
4. 更新后的 Fixture 和一致性用例；
5. C# 与 Python 重新生成验证；
6. 至少一轮对抗审查。
