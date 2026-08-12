# Agent Skill 开发面

本目录提供基于 Stardew Valley MCP Tool 编写 Prompt 型与可执行 Skill 所需的模板、最小示例和测试工具，不提供完整的官方玩法 Skill 集合。公共执行契约以 [`../spec/skill/README.md`](../spec/skill/README.md) 和其中的 Manifest Schema 为准。

```text
skill/
├── templates/stardew-prompt-skill-template/      Prompt 型模板
├── templates/stardew-executable-skill-template/  可执行模板
├── examples/stardew-nearby-overview/             只读附近概览示例
├── examples/stardew-remove-tree/                  受控移除普通树示例
├── examples/stardew-plant-seed/                   单格种植指引
├── examples/stardew-water-crops/                  可执行范围浇水示例
├── examples/stardew-harvest-crops/                可执行批量收获示例
├── examples/stardew-sleep-until-next-day/         可执行换日示例
├── examples/stardew-refill-watering-can/           可执行喷壶补水示例
├── scripts/validate_skills.py
└── tests/
```

## 选择 Skill 形态

Prompt 型 Skill 适合短步骤、高自由度、需要 Agent 根据上下文决策的任务，只需 `SKILL.md`。可执行 Skill 适合重复、多目标、时序敏感或游戏时间持续推进的任务，需要额外提供 `runtime.yaml`、输入／输出 Schema 和 `scripts/run.py`。

浇水、收获、睡眠与喷壶补水是当前随 MCP 发行包交付的四个可执行 Skill。它们由通用 Loader 从 `runtime.yaml` 发现，复用当前 Owner Session，只获得各自声明的原子 Tool 子集，不创建第二连接，也不进入 Mod Capability Manifest。

## 创建 Prompt 型 Skill

1. 复制 `templates/stardew-prompt-skill-template/`。
2. 同步修改目录名、`SKILL.md` Frontmatter 和可选的 `agents/openai.yaml`。
3. 写清可用 Tool、流程、停止条件、输出和安全边界。

## 创建可执行 Skill

1. 复制 `templates/stardew-executable-skill-template/`。
2. 同步修改目录名、Frontmatter，以及 `runtime.yaml` 的 Tool 名、说明、Schema、依赖、风险和期限。
3. 在 `scripts/run.py` 实现 `async run(ctx, arguments)`；只通过 `ctx.call_tool` 串行使用声明过的原子 Tool。
4. 为成功、失败、未知终态、超时、取消和任务级后置条件增加测试。
5. 运行校验器；通过 `--skill-dir` 加载到 MCP 后进行真实存档验收。

新增可执行 Skill 不应修改 MCP Server、构建钩子或 SkillHost。若必须为某个 Skill 增加能力专用 Python 分支，说明包契约或原子 Tool 仍不完整，应先修复公共边界。

## 动态加载

随发行包提供的内置可执行 Skill 会自动加载。额外可信目录使用：

```bash
uv run --project mcp stardew-valley-mcp serve \
  --allow-write \
  --skill-dir ./my-stardew-skills
```

搜索目录可以是单个 Skill，也可以是多个 Skill 的直接父目录。当前安装、删除或更新后需要重启 MCP，但不需要重启游戏；Host 不递归扫描更深目录，也不自动信任系统中发现的 Python 文件。

进程内 Python Skill 与 MCP 拥有相同的系统访问能力。`SkillContext` 限制正式 Tool 调用，但不是恶意代码沙箱，因此只加载明确审查并信任的目录。

## 校验

```bash
uv run --project mcp python skill/scripts/validate_skills.py
uv run --project mcp python -m unittest discover -s skill/tests -v
```

校验器会检查 `SKILL.md`、可执行 Manifest、资源路径、JSON Schema、入口脚本，以及文档声明 Tool 与运行依赖是否一致。它不连接游戏，真实任务仍需验证任务级后置条件。
