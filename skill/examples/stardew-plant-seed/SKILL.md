---
name: stardew-plant-seed
description: 在 Stardew Valley 一格空的已耕土地上种下一颗指定种子，并通过作物事实与背包变化确认种植结果。用户要求在明确地块播种或种下一颗种子时使用。
---

# 在一格土地种下一颗种子

## 可用工具

- `stardew_query_runtime`：确认世界与玩家状态。
- `stardew_query_inventory`：取得种子 Item Ref、槽位与 Inventory Revision。
- `stardew_query_world`：查找目标空 HoeDirt，并复查是否生成 Crop。
- `stardew_inspect`：在动作前确认目标仍是空 HoeDirt。
- `stardew_navigate`：走到目标地块相邻位置并面向目标。
- `stardew_equip`：按 Item Ref 明确手持指定种子。
- `stardew_interact`：对目标地块提交一次原生动作键语义。

## 工作流程

1. 仅在用户明确要求改变存档且 MCP 已启用写权限时继续。调用 `stardew_query_runtime`；世界未就绪、存在阻塞菜单或玩家不可控制时停止。
2. 调用 `stardew_query_inventory` 查询玩家背包。按用户给出的名称或 Qualified Item ID 选择唯一种子堆；存在多个含义不同的候选时请求用户选择。记录 Item Ref、Stack、槽位和 Inventory Revision。
3. 使用用户提供的地块 Ref；没有 Ref 时，以用户给定位置或玩家附近调用 `stardew_query_world`，只接受 `kind=hoe_dirt`、没有 Crop 且可操作性不是明确 false 的地块。候选不唯一时停止并请求选择。
4. 以该 Ref 调用 `stardew_inspect`，确认仍是空 HoeDirt。随后调用 `stardew_navigate`，以目标 Ref、`arrival=adjacent` 抵达并面向目标。
5. 调用 `stardew_equip`，传入种子 Item Ref 和本轮 Inventory Revision；成功后只调用一次 `stardew_interact`，目标为 HoeDirt Ref。`unknown` 终态不得重放。
6. 重新调用 `stardew_query_world` 查询目标位置，并调用 `stardew_query_inventory`。只有目标格出现 Crop，且种子堆减少一颗或原堆因用尽而消失，才判定成功。

## 停止条件

- 目标不是空耕地、种子不在玩家背包、季节或地图规则拒绝种植、导航失败、动作后没有 Crop 或种子数量变化不符合预期：停止并报告未完成。
- 任一变更 Tool 返回 `failed` 时停止；返回 `unknown` 时停止并要求先重新查询，禁止自动重试。
- Ref 或 Revision 失效时只允许重新执行查询与选择，不得沿用旧 Ref 直接动作。

## 输出要求

返回种子名称与 Qualified Item ID、目标地图和坐标、动作前后种子数量、动作后 Crop ID、是否成功及停止原因。没有同时观察到 Crop 与背包后置条件时，不得声称已经种下。

## 安全边界

本 Skill 会消耗一颗种子并改变存档，需要 `--allow-write`。一次只处理一格，不自动锄地、浇水、购买种子或改换候选地块；用户没有明确指定目标且存在多个候选时不得自行挑选。
