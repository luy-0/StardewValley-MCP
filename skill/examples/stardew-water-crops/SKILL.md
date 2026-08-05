---
name: stardew-water-crops
description: 在明确查询范围内逐格给 Stardew Valley 尚未浇水的存活作物浇水，并按喷壶余水、体力与逐格复查结果有界停止。用户要求给一片或整片作物浇水时使用。
---

# 给整片作物浇水

## 可用工具

- `stardew_query_runtime`：检查天气、时间、位置和体力。
- `stardew_query_inventory`：寻找喷壶并读取余水、容量与 Item Ref。
- `stardew_query_world`：枚举查询范围内作物、水源地块并复查含水状态。
- `stardew_inspect`：每次动作前后复查目标 Crop Ref。
- `stardew_navigate`：逐格走到作物或水源旁边。
- `stardew_equip`：明确装备本轮喷壶。
- `stardew_use_tool`：对单个作物或水源执行一次喷壶动作。

## 工作流程

1. 仅在用户明确要求改变存档且 MCP 已启用写权限时继续。调用 `stardew_query_runtime`，记录天气、时间、体力和位置；当前地点下雨且该地点会被雨水浇灌时，直接复查并把已浇水作物作为无需动作处理。
2. 调用 `stardew_query_inventory`，选择唯一 Watering Can Item，记录 Item Ref、Inventory Revision、`waterRemaining`、`waterCapacity` 与 `bottomless`；找不到或候选不唯一时停止。
3. 明确本轮范围：优先使用用户给定区域；否则只使用玩家当前地图、以玩家为中心 `radius=15` 的查询窗口。调用 `stardew_query_world`，收集 `kind=crop`、`dead=false`、`watered=false` 的 Ref，并以地图、Y、X 排序。只把本次公开查询范围称为“整片”，不得声称扫描了范围外的整个农场。
4. 调用 `stardew_equip` 装备喷壶。对每个候选执行有界循环：先以 `stardew_inspect` 确认同一 Ref 仍是存活且未浇水的 Crop；调用 `stardew_navigate` 以 `arrival=adjacent` 抵达；再调用一次 `stardew_use_tool`，传入目标 Ref 与 `chargeLevel=0`；最后再次 `stardew_inspect`，只有 `crop.watered=true` 才计为完成。
5. 每处理一格后调用 `stardew_query_runtime` 复查体力与时间，并按需调用 `stardew_query_inventory` 复查喷壶余水。默认在体力低于 10、时间达到 2500、余水为 0 或连续一格无进展时停止。
6. 喷壶为空但查询范围内存在 `tile.water=true` 时，可以调用 `stardew_navigate` 到该水格相邻位置，再以该 WorldPosition 调用一次 `stardew_use_tool`，随后用 `stardew_query_inventory` 确认余水增加才继续。没有可确认水源时返回部分完成，不跨地图猜测水源。
7. 循环结束后再次调用 `stardew_query_world` 查询同一范围，汇总仍未浇水的目标；只在初始目标均已浇水或已不再是有效存活作物时报告完成。

## 停止条件

- 达到 `max_targets=128`、体力或时间门槛、喷壶无法补水、导航受阻、目标一次无进展、用户取消时停止并返回部分结果。
- 任一变更 Tool 返回 `failed` 时停止；返回 `unknown` 时停止，禁止自动重放该格动作。
- Ref stale 时重新查询同一坐标；若该格已浇水或作物已不存在，记录为外部变化，不重复用水。

## 输出要求

返回查询范围、初始目标数、确认浇水数、跳过数、剩余目标坐标、补水次数、喷壶最终余水、体力变化和停止原因。逐格未观察到 `watered=true` 的目标不得计入完成。

## 安全边界

本 Skill 会消耗游戏时间、体力和喷壶水量，需要 `--allow-write`。它不锄地、不种植、不吃食物、不传送，也不把范围外作物纳入“整片”结论；默认只使用零蓄力单格动作，避免意外影响邻近地块。
