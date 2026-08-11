---
name: stardew-sleep-until-next-day
description: 让 Stardew Valley 当前玩家回到自己的唯一住宅，找到可睡床位、确认睡觉并以日期变化验证进入第二天。用户要求回家睡觉、上床或睡到第二天时使用。
---

# 回家并睡到第二天

本示例带有 `scripts/run.py` 确定性入口。支持该入口的 MCP 会在一次调用内连续执行以下阶段；Agent 不应在各步骤之间重新思考和手工接力。

## 可用工具

- `stardew_query_runtime`：读取当前日期、住宅唯一地图 ID、玩家位置与 UI 状态。
- `stardew_query_players`：确认当前玩家确实位于目标床位且游戏已标记为在床上，避免误选其他二选一对话。
- `stardew_query_world`：在住宅地图内寻找可睡 Bed 与睡眠触发格。
- `stardew_navigate`：跨地图回家并走到床的睡眠触发格。
- `stardew_interact`：自动触发未出现时，从床旁执行一次原生交互。
- `stardew_query_ui`：确认睡眠问答与换日过程中的菜单状态。
- `stardew_activate_ui`：激活刚刚由上床动作产生的肯定回答。
- `stardew_close_menu`：仅关闭可安全关闭、且明确不是选择题的换日信息页。

## 工作流程

1. 仅在用户明确要求结束当天且 MCP 已启用写权限时继续。调用 `stardew_query_runtime`，记录完整日期、位置和 `player.homeLocationId`；开始时已有菜单则停止，不擅自关闭用户上下文。
2. 使用 `homeLocationId` 调用 `stardew_query_world`，先查询 `area=(0,0,32,32)` 且只包含 Bed；未找到时依次查询 `(32,0,32,32)`、`(0,32,32,32)`、`(32,32,32,32)`。接受 `bed.canSleep=true` 且带 `sleepPosition` 的床；若玩家开始时正站在该床占用格内，允许选择因当前玩家自身占用而暂时返回 `canSleep=false` 的床。多个候选时优先可睡且距离玩家最近者。
3. 调用 `stardew_navigate`，目标为 Bed 的 `sleepPosition`，`arrival=exact`。抵达后调用 `stardew_query_ui`；允许最多三次只读复查，等待床的 TouchAction 产生问题对话。
4. 若仍无对话，调用 `stardew_navigate` 到 Bed Ref 的相邻位置并面向床，再只调用一次 `stardew_interact`，随后调用 `stardew_query_ui`。除此之外不重复交互。
5. 只有本轮上床动作后新出现精确原版 `DialogueBox`、恰好存在 index 0/1 两个启用的 `dialogue_response`，并且 `stardew_query_players` 同时确认当前玩家位于所选 Bed 的 `sleepPosition` 且 `isInBed=true` 时才继续。睡眠问题的肯定响应是 index 0；把该元素 Ref 和当前 UI Revision 交给 `stardew_activate_ui`。若对话结构或床位事实不符，停止且不选择任何响应。
6. 确认后在总期限内持续调用 `stardew_query_runtime`，并穿插 `stardew_query_ui` 观察换日。`SaveGameMenu` 只等待；`ShippingMenu`、无选择的普通升级页与普通信息对话只在公共 Tool 判定可安全推进时处理。未知菜单、升级选择、节日决定或其他需要选择的页面必须停止为“需要决定”。
7. 日期变化后继续复查，只有菜单已清空且玩家恢复 `canMove=true` 时才报告新日期和完成。日期未变化不得把“已点击 Yes”视为完成；未确认睡眠问题却发生跨日必须报告为昏倒换日。

## 停止条件

- 找不到唯一住宅、找不到可睡床、床位不可达、睡眠问题未出现或结构不符、日期在轮询上限内未变化：停止并报告未完成。
- 睡眠确认前任一变更 Tool 返回 `unknown` 时停止且不得重放；确认后出现 `unknown` 时只做只读查询，因为过日不可逆。
- 出现任何需要玩家选择的换日页面时停止并返回“需要决定”，不得擅自选择奖励或分支。

## 输出要求

返回旧日期与新日期、住宅 `locationId`、床 Ref 与 `sleepPosition`、是否看到睡眠问题、是否确认、换日菜单处理次数以及最终状态：完成、部分完成、需要决定或失败。只有日期事实改变时才能报告“已睡到第二天”。

## 安全边界

本 Skill 会结束当前游戏日、推进日期并触发保存，需要 `--allow-write`，属于不可逆高影响流程。它不直接修改玩家位置、睡眠状态或日期，不使用传送和强制过日 API；只通过正常导航、床交互和结构化 UI 操作推进。四个 32×32 查询窗口之外的超大型 Mod 住宅不在当前保证范围内。
