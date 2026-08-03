---
name: stardew-nearby-overview
description: 查询并整理 Stardew Valley 当前日期、时间、玩家位置以及附近可见对象和角色。用户询问“我在哪里”“附近有什么”“看看周围环境”或需要执行动作前先了解现场时使用。
---

# 查询附近环境

## 可用工具

- `stardew_query_runtime`：取得当前日期、时间和玩家位置。
- `stardew_query_world`：查询玩家周围的地图事实、对象和角色。

## 工作流程

1. 调用 `stardew_query_runtime`，记录日期、时间和玩家 `locationId`、`x`、`y`。
2. 以该位置为 `around.center` 调用 `stardew_query_world`。显式传入 `radius=8`、`includeTiles=false`、`includeEntities=true`、`includeCharacters=true`；用户要求地形时才把 `includeTiles` 改为 `true`。
3. 按与查询中心的曼哈顿距离整理对象和角色；距离相同时保持 Tool 返回顺序。保留原始 `kind`、名称、位置和 Ref，不要把未返回的名称、状态或用途补写成事实。
4. 原样保留查询中的 Warning、截断标记和范围信息；如果结果为空，明确报告本次查询范围内没有可见目标。

## 停止条件

- 任一查询返回 `failed` 或 `unknown` 时停止，并报告结构化错误；不要把未知结果改写为成功。
- 游戏世界尚未就绪时停止，不重复轮询。
- 默认每个查询只调用一次；只有用户明确要求扩大范围时，才使用不超过 15 的新半径再次查询。

## 输出要求

使用简短中文列出：日期与时间、地图与玩家坐标、附近对象、附近角色、查询半径以及 Warning。对象较多时按种类分组并给出数量，保留用户后续操作所需的 Ref，但不要输出与请求无关的完整原始响应。

## 安全边界

只调用上述两个只读 Tool。不得导航、交互、装备或使用工具，也不得根据附近事实自行执行后续动作。
