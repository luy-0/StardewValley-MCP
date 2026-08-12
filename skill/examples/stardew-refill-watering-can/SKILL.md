---
name: stardew-refill-watering-can
description: 通过一次可执行 Skill 调用，在当前地图查找实际最近的可达补水点并将唯一喷壶装满。用户要求寻找水源、补充喷壶或在批量浇水中断后补水时使用。
---

# 查找最近水源并装满喷壶

## 可用工具

- `stardew_query_runtime`：确认当前地图、玩家位置与可操作状态。
- `stardew_query_inventory`：确认唯一喷壶并复查补水前后水量。
- `stardew_query_world`：分块扫描当前地图的可通行、占用与原生补水事实。
- `stardew_navigate`：依次尝试脚本按路径成本选出的岸边站位。
- `stardew_equip`：使用背包 Ref 明确装备唯一喷壶。
- `stardew_use_tool`：对选定补水地块执行一次零蓄力喷壶动作。

## 工作流程

1. Agent 只调用一次派生补水 Tool；通常无需参数。
2. 脚本在当前 Owner Session 内确认喷壶、扫描当前地图、使用游戏原生 `pathfindingBlocked` 事实构造 Tile 图并以 BFS 计算可达路径成本，在导航受阻时顺延下一候选。
3. 脚本装备喷壶、执行一次补水，并重新查询背包；只有水量等于容量才报告完成。
4. Agent 根据 `finalStatus`、`stopReason`、`lastConfirmedState` 和 `resumeHint` 汇报结果。

## 停止条件

- 喷壶已满时不导航、不装备、不使用工具，直接返回 `already_full`。
- 当前地图没有原生补水地块、岸边站位全部不可达或所有导航尝试失败时有界停止。
- Deadline、取消或动作副作用无法确认时停止；未知副作用不会自动重放。

## 输出要求

返回补水前后水量与容量、扫描 Tile 数、候选与不可达计数、导航与动作次数、选定水源和站位、最终位置、停止原因、续跑提示以及最后确认状态。只有动作后重新查询到水量等于容量，才返回已完成。

## 安全边界

本 Skill 需要 `--allow-write`，只在玩家当前地图内工作。水源资格只信任 `wateringCanRefillable`，路径只信任当前玩家的原生 `pathfindingBlocked`；不根据水面字段、地图名、坐标、显示名称或旧的 `passable/occupied` 组合猜测。当前地图没有水源时不会跨地图搜索。
