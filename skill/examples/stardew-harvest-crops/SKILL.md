---
name: stardew-harvest-crops
description: 在指定区域或当前整张地图内批量收获 Stardew Valley 已成熟作物，根据 CropFact 的交互或镰刀方式选择原子动作，并分块扫描、逐格复查和报告剩余目标。用户要求收获一片作物、收完温室、收完整个当前地图或收割全部成熟作物时使用。
---

# 收获区域或当前地图全部成熟作物

## 可用工具

- `stardew_query_runtime`：检查时间、位置、体力与阻塞 UI。
- `stardew_query_inventory`：记录背包容量、物品数量并寻找镰刀。
- `stardew_query_world`：枚举并复查成熟 Crop。
- `stardew_inspect`：在每次动作前后复查同一 Crop Ref。
- `stardew_navigate`：走到目标作物相邻位置。
- `stardew_equip`：对镰刀收获目标明确装备 Scythe。
- `stardew_interact`：收获 `harvestAction=interact` 的作物。
- `stardew_use_tool`：收获 `harvestAction=scythe` 的作物。

## 工作流程

1. 仅在用户明确要求改变存档且 MCP 已启用写权限时继续。调用 `stardew_query_runtime`；存在阻塞菜单、玩家不可控制或时间达到 2500 时停止。
2. 明确本轮模式。用户给出矩形或中心半径时只处理该范围；用户明确说“整张地图”“温室全部”或“当前地图全部”时，使用 `stardew_query_runtime` 返回的当前 `locationId` 进入全地图模式。没有明确全地图意图时，默认只使用玩家周围 `radius=15`，不得擅自扩大存档改动范围。
3. 范围模式只调用一次 `stardew_query_world`。全地图模式从 `(0,0)` 开始按 32×32、不重叠的矩形调用 `stardew_query_world`，固定 `includeTiles=false`、`includeCharacters=false`、`entityKinds=[crop]` 和 `maxEntities=512`。成功结果的 `snapshot.area` 是地图裁剪后的真实范围：宽度小于 32 表示到达右边界，高度小于 32 表示到达下边界；边界恰为 32 的整数倍时，下一块返回 `OUT_OF_RANGE` 才作为该方向结束。若 `entitiesTruncated=true`，把该块拆成 16×16，仍截断时继续减半；不能取得不截断结果时停止。最多扫描 `max_chunks=64`。
4. 合并全部查询结果，按 `locationId + x + y` 去重，只保留 `kind=crop`、`readyForHarvest=true`、`dead=false` 的目标，并按 Y、X 排序。记录每项 Ref、坐标与 `harvestAction`；完成全量扫描前不得开始收获，也不得把某一个查询窗口称为整张地图。
5. 调用 `stardew_query_inventory` 保存动作前背包 Snapshot。存在 `harvestAction=scythe` 时，必须找到唯一 Scythe Item Ref；找不到时可以继续手摘目标，但把镰刀目标列为未完成。
6. 对每个目标先调用 `stardew_inspect`。目标仍成熟时，调用 `stardew_navigate` 以 `arrival=adjacent` 抵达。`harvestAction=interact` 时调用一次 `stardew_interact`；`harvestAction=scythe` 时先以当前 Inventory Revision 调用 `stardew_equip` 装备镰刀，再调用一次 `stardew_use_tool`，传入目标 Ref 与 `chargeLevel=0`。
7. 动作后再次以同一 Ref 调用 `stardew_inspect`，并用 `stardew_query_world` 重新查询目标坐标。目标 Ref stale、该格 Crop 消失，或再生作物变为 `readyForHarvest=false`，才能计为已收获；若仍成熟则立即停止该目标，不连续盲试。
8. 每个目标后调用 `stardew_query_inventory`。若背包没有接收手摘产物的空间、动作返回容量相关错误，或新 Snapshot 与世界事实均无进展，则停止并返回部分完成。单轮最多处理 `max_targets=256`；达到上限时保留剩余坐标，后续从全地图复查结果续跑。
9. 结束时以与初始阶段相同的范围或分块算法重新调用 `stardew_query_world`。只有复查范围内不存在 `readyForHarvest=true` 的存活 Crop 时，才报告全部完成；否则报告部分完成与剩余目标。

## 停止条件

- 背包容量不足、缺少镰刀、查询块截断且无法继续拆分、达到扫描块或目标上限、导航失败、单个目标无进展、时间门槛或用户取消时停止并报告剩余目标。
- 任一变更 Tool 返回 `failed` 时停止当前流程；返回 `unknown` 时立即停止全部流程，禁止重放。
- 未识别的 `harvestAction` 不猜测动作，保留为未完成。

## 输出要求

返回执行模式、地图或查询范围、扫描块数、初始成熟数、确认收获数、手摘数、镰刀收获数、剩余目标坐标、背包主要增量以及停止原因。全地图模式必须明确报告最终复查后的成熟作物数；不得仅凭动作 Tool 成功就声称收获，必须同时有动作后的作物事实变化。

## 安全边界

本 Skill 会改变作物与背包并消耗游戏时间，部分动作可能消耗体力，需要 `--allow-write`。全地图只表示执行开始时玩家所在的一个 `locationId`，不包含室内外相连地图、其他玩家设施或尚未加载的位置。它不出售产物、不丢弃物品、不自动清理枯死作物，也不对查询范围外的作物做动作。
