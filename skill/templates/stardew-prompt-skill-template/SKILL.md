---
name: stardew-prompt-skill-template
description: 创建或改写一个只通过公开 Stardew Valley MCP Tool 工作的 Prompt 型 Agent Skill。需要为新的查询、动作或有界编排编写标准 SKILL.md 时使用。
---

# 创建 Prompt 型 Stardew Valley MCP Skill

复制本目录后，把目录名、Frontmatter 和正文替换成具体任务，并更新或删除可选的 `agents/openai.yaml`。保持 Skill 聚焦于一种可复用工作流，不要把多个无关玩法堆入同一文件。

## 可用工具

在复制后的 Skill 中列出实际需要的 `stardew_*` Tool，并说明每个 Tool 在工作流中的用途。只使用公共 Catalog 中存在的 Tool；本模板示例可从只读的 `stardew_query_runtime` 开始。

## 工作流程

1. 先查询执行任务所需的最小游戏事实。
2. 明确每一步调用哪个 Tool、使用上一步的哪些结构化字段，以及如何验证返回结果。
3. 对变更流程采用“查询 → 单次动作 → 复查”的循环，并设置明确上限；不要把复合编排下沉到 Mod。
4. 删除全部模板说明，只保留另一个 Agent 实际执行时需要的指令。

## 停止条件

列出成功、失败、`unknown`、stale Ref、无状态变化、达到上限和用户取消时的处理。变更 Tool 返回 `unknown` 时停止，不得自动重放。

## 输出要求

规定 Agent 必须向用户返回的结构化事实、完成状态、停止原因和仍需人工决定的内容。结论必须能够由 Tool 结果复核，不得补写游戏未返回的事实。

## 安全边界

说明 Skill 是只读还是会改变存档，并列出可能消耗的体力、物品、金钱、时间或关系。变更 Skill 必须提醒使用者显式启用 `--allow-write`；所有步骤只能调用公共 Catalog 中声明的 MCP Tool。
