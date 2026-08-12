---
name: stardew-harvest-crops
description: 通过一次可执行 Skill 调用，在明确矩形范围内按 CropFact 的交互或镰刀语义批量收获成熟作物，并返回背包变化与可续跑进度。用户要求收获一片作物时使用。
---

# 有界收获指定范围作物

## 可用工具

以下是脚本获得的最小原子 Tool 权限，不是要求 Agent 逐项编排的步骤：

- `stardew_query_runtime`：检查时间、位置、体力与阻塞 UI。
- `stardew_query_inventory`：记录背包容量、物品数量并寻找镰刀。
- `stardew_query_world`：枚举并复查成熟 Crop。
- `stardew_inspect`：在每次动作前后复查同一 Crop Ref。
- `stardew_navigate`：走到目标作物相邻位置。
- `stardew_equip`：对镰刀收获目标明确装备 Scythe。
- `stardew_interact`：收获 `harvestAction=interact` 的作物。
- `stardew_use_tool`：收获 `harvestAction=scythe` 的作物。

## 工作流程

1. Agent 先用只读查询确定当前地图内要处理的矩形 `area`；范围最大 32×32，不能把未查询区域称为已经处理。
2. Agent 只调用一次派生收获 Tool，并传入 `area`。可选参数用于限制目标数、动作数、期限、最低体力与停止时间；没有明确需求时使用 Schema 默认值。
3. 脚本在同一个 Owner Session 内完成成熟作物查询、Ref 复查、导航与动作。`harvestAction=interact` 使用原生交互，`harvestAction=scythe` 只使用 `toolKind=scythe` 的物品并提交零蓄力工具动作。
4. 每次动作后脚本重新查询同一范围；手摘或镰刀影响到的全部本轮目标都按坐标后置条件计数，不以动作 Tool 未报错代替成功事实。
5. Agent根据 `finalStatus`、`stopReason`、`inventoryChanges`、`remainingTargets` 与 `resumeHint` 汇报结果。只有返回 `resumable=true` 时，才可以在解决停止原因后由用户决定是否重新调用。

## 停止条件

- 缺少镰刀、查询结果无法完整拆分、达到目标／动作／期限上限、体力或时间门槛、导航失败、目标变化或无进展时有界停止并报告剩余目标。
- 日期变化后不再查询新一天的目标；取消发生在变更调用中或最后动作后置条件不可确认时返回 `unknown`。
- 未识别的 `harvestAction` 不猜测动作；任一 `unknown` 终态禁止自动重放最后动作。

## 输出要求

返回查询范围、初始与本轮计划目标数、确认收获／手摘／镰刀／跳过／失败数、动作次数、体力与背包变化、最后位置、停止原因以及剩余目标。不得仅凭动作 Tool 成功就声称收获，必须有动作后的作物事实变化。

## 安全边界

本 Skill 会改变作物与背包并消耗游戏时间，部分动作可能消耗体力，需要 `--allow-write`。它只在玩家当前地图和显式矩形范围内工作，不出售产物、不丢弃物品、不自动清理枯死作物，也不对查询范围外的作物做动作。
