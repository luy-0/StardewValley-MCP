---
name: stardew-harvest-crops
description: 在明确查询范围内逐格收获 Stardew Valley 已成熟作物，根据 CropFact 的交互或镰刀方式选择原子动作并复查作物与背包。用户要求收获一片或整片成熟作物时使用。
---

# 收获整片作物

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
2. 明确本轮范围：优先使用用户给定区域；否则只使用玩家当前地图、以玩家为中心 `radius=15` 的查询窗口。调用 `stardew_query_world`，收集 `kind=crop`、`readyForHarvest=true`、`dead=false` 的 Ref，并以地图、Y、X 排序。记录每项 `harvestAction`。
3. 调用 `stardew_query_inventory` 保存动作前背包 Snapshot。存在 `harvestAction=scythe` 时，必须找到唯一 Scythe Item Ref；找不到时可以继续手摘目标，但把镰刀目标列为未完成。
4. 对每个目标先调用 `stardew_inspect`。目标仍成熟时，调用 `stardew_navigate` 以 `arrival=adjacent` 抵达。`harvestAction=interact` 时调用一次 `stardew_interact`；`harvestAction=scythe` 时先以当前 Inventory Revision 调用 `stardew_equip` 装备镰刀，再调用一次 `stardew_use_tool`，传入目标 Ref 与 `chargeLevel=0`。
5. 动作后再次以同一 Ref 调用 `stardew_inspect`，并重新查询目标坐标。目标 Ref stale、该格 Crop 消失，或再生作物变为 `readyForHarvest=false`，才能计为已收获；若仍成熟则立即停止该目标，不连续盲试。
6. 每个目标后调用 `stardew_query_inventory`。若背包没有接收手摘产物的空间、动作返回容量相关错误，或新 Snapshot 与世界事实均无进展，则停止并返回部分完成。最多处理 `max_targets=128`。
7. 结束时调用 `stardew_query_world` 复查同一范围，只在初始成熟目标均已收获或已因外部变化不再成熟时报告完成。

## 停止条件

- 背包容量不足、缺少镰刀、导航失败、单个目标无进展、达到目标上限、时间门槛或用户取消时停止并报告剩余目标。
- 任一变更 Tool 返回 `failed` 时停止当前流程；返回 `unknown` 时立即停止全部流程，禁止重放。
- 未识别的 `harvestAction` 不猜测动作，保留为未完成。

## 输出要求

返回查询范围、初始成熟数、确认收获数、手摘数、镰刀收获数、剩余目标坐标、背包主要增量以及停止原因。不得仅凭动作 Tool 成功就声称收获，必须同时有动作后的作物事实变化。

## 安全边界

本 Skill 会改变作物与背包并消耗游戏时间，部分动作可能消耗体力，需要 `--allow-write`。它不出售产物、不丢弃物品、不自动清理枯死作物，也不对查询范围外的作物做动作。
