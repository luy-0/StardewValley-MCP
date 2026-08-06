# Agent Skill 开发面

本目录提供基于 Stardew Valley MCP Tool 编写 Agent Skill 所需的最小材料，不包含完整的官方玩法 Skill 集合，也不建立独立运行时。

```text
skill/
├── templates/stardew-skill-template/  可复制的标准 SKILL.md 模板
├── examples/stardew-nearby-overview/  只读附近概览示例
├── examples/stardew-remove-tree/      受控移除普通树示例
├── examples/stardew-plant-seed/       单格种植并复查示例
├── examples/stardew-water-crops/      范围内逐格浇水示例
├── examples/stardew-harvest-crops/    区域或当前地图全部成熟作物收获示例
├── examples/stardew-sleep-until-next-day/  可执行的回家上床与换日示例
├── scripts/validate_skills.py         静态校验器
└── tests/                             校验器测试
```

普通示例仍由 Agent Runtime 读取 `SKILL.md` 后调用现有 `stardew_*` MCP Tool。睡眠示例额外提供确定性 `scripts/run.py`，由 MCP 的最小进程内宿主显式注册；脚本复用同一个 Mod Owner Session，只获得声明过的 Tool 子集，不建立新连接，也不会成为新的 Mod 公共能力。默认只读和 `--allow-write` 权限边界保持不变。

这些目录是公开原子能力如何组成任务闭环的参考实现，不构成覆盖全部玩法的官方 Skill 集合。低频任务可以由 Agent 按“查询 → 单次动作 → 复查”执行；睡眠等会被游戏时间持续改变的流程由脚本在一次 Skill 调用中连续完成。

## 可执行示例

以 `--allow-write` 启动 MCP 且 Mod 公告全部依赖 Tool 时，Tool 清单会额外出现 `stardew_skill_sleep_until_next_day`。它只编排公共原子 Tool，完成后返回日期、床位、睡眠确认、日结 UI 处理和最终可操作状态；只读模式或依赖缺失时不暴露。

## 创建 Skill

1. 复制 `templates/stardew-skill-template/` 并把目录改为小写连字符名称。
2. 同步修改 `SKILL.md` Frontmatter 中的 `name`、`description`，并更新或删除可选的 `agents/openai.yaml`。
3. 按本仓库五段式安全编写剖面写清真实 Tool、工作步骤、停止条件、输出和安全边界。
4. 删除模板说明，只保留 Agent 执行任务所需的信息。

## 校验

校验全部模板与示例：

```bash
uv run --project mcp python skill/scripts/validate_skills.py
uv run --project mcp python -m unittest discover -s skill/tests -v
```

校验指定 Skill：

```bash
uv run --project mcp python skill/scripts/validate_skills.py path/to/my-skill
```

校验器只检查标准 Frontmatter、本仓库安全编写剖面和 Tool 引用，不执行 Skill，不连接游戏，也不替代真实 MCP Tool 的参数与结果 Schema。

## 后续方向

当前只显式注册一个随仓库发布的可执行睡眠示例，不扫描第三方目录，也不提供插件依赖管理或第二套连接生命周期。未来可以在实际需求证明后，将同一 `SkillContext` 边界扩展为通用 SDK 与可安装 Skill Host。
