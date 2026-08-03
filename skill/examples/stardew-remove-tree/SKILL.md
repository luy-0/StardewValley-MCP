---
name: stardew-remove-tree
description: 使用当前已装备的斧头，对 Stardew Valley 玩家相邻的一棵普通树执行有界砍伐并复查结果。用户明确要求砍倒、移除或持续砍当前树木时使用。
---

# 移除相邻普通树

## 可用工具

- `stardew_query_runtime`：检查玩家位置与剩余体力。
- `stardew_query_world`：在没有明确 Ref 时寻找玩家相邻的普通树。
- `stardew_inspect`：确认目标类型并在每次动作后复查同一 Ref。
- `stardew_use_tool`：使用当前已经装备的工具执行一次普通动作。

## 工作流程

1. 只在用户明确要求产生游戏变更后继续，并确认 MCP 已显式启用写权限。默认 `max_attempts=8`、`min_energy=10`。
2. 调用 `stardew_query_runtime`。体力低于 `min_energy` 时立即停止；本 Skill 不自动吃东西、导航或装备工具。
3. 优先使用用户或前序查询提供的 Tree Ref。没有 Ref 时，以玩家位置为中心、`radius=1` 调用 `stardew_query_world`，只查询 `entityKinds=["tree"]`；只接受同一地图且 cardinal-adjacent 的普通树。存在多个候选时停止并请求用户选择。
4. 以 `refs=[targetRef]` 首次调用 `stardew_inspect`。目标必须是已解析的 World Entity，且 `kind=tree`；首次已经 stale、找不到、事实不可用或不是普通树时失败，不得视为已完成。
5. 调用一次 `stardew_use_tool`，传入 `targetRef` 和 `chargeLevel=0`，并把尝试次数加一。记录返回的实际 Tool ID 与 Energy 变化。
6. 再次以 `refs=[targetRef]` 调用 `stardew_inspect`。至少一次工具动作成功后，同一 Ref 变为 stale 表示目标已经移除；仍可解析且 Tree Health 或 stump 状态发生变化时，调用 `stardew_query_runtime` 复查体力和位置，确认目标仍在同图相邻且尚未达到上限后返回步骤 5。事实没有变化时立即停止，不再浪费体力。

## 停止条件

- 目标在至少一次成功动作后变为 stale：成功，停止。
- 达到 `max_attempts`、体力不足、目标不再相邻、状态一次未变化或当前工具不产生有效进展：停止并报告未完成。
- `stardew_use_tool` 返回 `failed`：停止并报告错误。返回 `unknown` 时同样停止，禁止自动重放该次变更。
- 用户取消、游戏未就绪或 Ref 指向其他对象时立即停止。

## 输出要求

返回目标位置与 Ref、尝试次数、是否移除、停止原因、最后一次可确认的实际 Tool ID、累计可确认的 Energy 变化，以及最后一次可用的 Tree Health/stump 状态。`unknown` 终态下把无法确认的 Tool ID 与 Energy 变化明确标为“未知”。不要声称“已移除”，除非动作后同一 Ref 已明确变为 stale。

## 安全边界

本 Skill 会改变存档并消耗体力，需要 `--allow-write`。它只处理当前地图相邻的普通树，不处理 Fruit Tree、Resource Clump、移动角色或 Mod 自定义实体，也不自动导航和装备。当前查询面不能在第一次动作前零成本确认已装备工具；若工具错误，最多执行一次无进展动作后停止并如实报告。
