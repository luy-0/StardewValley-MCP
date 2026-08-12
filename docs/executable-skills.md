# 可执行 Skill 开发与维护

可执行 Skill 是 MCP 本地的复合工作流，不是新的 Mod Proto 能力。它把多个公共原子 Tool 组织成一次确定性调用，由 Skill Host 复用当前 Mod Owner Session 执行，并作为派生 MCP Tool 提供给 Agent。

## 开发入口

复制 [`../skill/templates/stardew-executable-skill-template/`](../skill/templates/stardew-executable-skill-template/) 后，主要维护四部分：

1. `SKILL.md`：告诉 Agent 何时使用以及任务边界；
2. `runtime.yaml`：声明 Tool 元数据、依赖、风险、入口与执行策略；
3. `schemas/`：定义 Agent 输入和整个 Skill 的结构化结果；
4. `scripts/run.py`：通过受限 `SkillContext` 串行编排原子 Tool。

权威字段与执行规则见 [`../spec/skill/README.md`](../spec/skill/README.md)。当前发行包内置四个示例：

- [`stardew-water-crops`](../skill/examples/stardew-water-crops/)：有界浇灌显式矩形范围，支持安全蓄力与结构化部分进度；
- [`stardew-harvest-crops`](../skill/examples/stardew-harvest-crops/)：按公开收获语义执行手摘或镰刀收获，并汇总背包变化；
- [`stardew-sleep-until-next-day`](../skill/examples/stardew-sleep-until-next-day/)：完成回家、上床、确认睡眠和换日收敛。
- [`stardew-refill-watering-can`](../skill/examples/stardew-refill-watering-can/)：扫描当前地图的原生补水地块，基于当前玩家的原生寻路碰撞事实按 BFS 可达成本选择岸边站位，并复查喷壶是否装满。

## 最小维护原则

- 只声明真正需要的原子 Tool；写权限仍由 MCP `--allow-write` 控制。
- 每个原子调用等待终态后再进行下一步，避免多个变更在 Mod 主线程交错。
- 成功必须由查询或状态变化证明，不能仅以动作 Tool 未报错作为依据。
- 变更结果 `unknown`、变更提交期间的异常、变更后的宿主超时或输出 Schema 失配都禁止自动重放。
- Skill 脚本不得创建新的 Mod/MCP 连接，也不得读取共享秘密或直接发送 Proto。
- 新增 Skill 只新增目录与测试，不修改 MCP 中央注册代码。
- 批量任务必须接受显式范围、目标数、动作数和期限；正常边界停止返回部分进度，不能伪装成异常失败。
- 日期变化、取消和后置观察失败不得混入下一天事实；最后变更无法确认时返回 `unknown`。

## 加载与验证

```bash
uv run --project mcp python skill/scripts/validate_skills.py ./my-stardew-skills
uv run --project mcp stardew-valley-mcp serve \
  --allow-write \
  --skill-dir ./my-stardew-skills
```

MCP 启动时加载目录并生成派生 Tool；更新后重启 MCP 即可，不需要重启游戏。`--skill-dir` 表示用户明确授信目录中的 Python 代码，当前进程内 Host 不提供操作系统级沙箱。

## 何时不要使用可执行 Skill

单次、上下文自由度较高的任务可以继续使用 Prompt 型 Skill。必须逐帧处理输入、动画、地图加载或游戏主线程对象的逻辑应保留在 Mod 原子 Handler。只有重复、多目标或时序敏感的稳定工作流，才值得固化为脚本。
