---
name: stardew-executable-skill-template
description: 创建能够由 Stardew Valley MCP Skill Host 动态加载、复用当前 Owner Session 并通过受限 SkillContext 编排原子 Tool 的可执行 Skill。公共开发者需要编写确定性、多步骤或时序敏感的游戏工作流时使用。
---

# 创建可执行 Stardew Valley Skill

## 可用工具

- `stardew_query_runtime`：示例只读取当前运行状态；复制模板后改成工作流实际依赖的最小 Tool 集合。

## 工作流程

1. 修改目录名与本文件 Frontmatter `name`，并同步把 `runtime.yaml` 中的 Tool 名改成 `stardew_skill_<目录名去掉 stardew- 后以底线连接>`。
2. 在 `runtime.yaml` 声明入口、输入／输出 Schema、风险 Annotation、原子 Tool 依赖、宿主超时与并发策略。
3. 在 `scripts/run.py` 实现 `async run(ctx, arguments)`；只通过 `ctx.call_tool` 串行调用声明过的 Tool，不创建新的 MCP 或 Mod 连接。
4. 为查询、变更、后置复查、失败、未知终态、取消和超时分别编写测试；变更结果未知时禁止自动重放。
5. 使用公共校验器验证整个目录，再在真实存档中核对任务级后置条件。

## 停止条件

Manifest、Schema、依赖、权限或任何原子 Tool 结果不满足预期时停止。不能确认变更终态时返回 `unknown`，不得把不确定结果改写为可重试失败。

## 输出要求

所有返回值必须通过 `schemas/output.json`。成功必须包含任务级后置条件；失败和未知终态必须包含稳定错误码、中文消息与 `retryable`。

## 安全边界

可执行脚本与 MCP 处于同一进程，只有用户明确信任的目录才可以通过 `--skill-dir` 加载。`SkillContext` 是原子 Tool 的最小授权面，不是针对恶意 Python 的系统沙箱。
