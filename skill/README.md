# Agent Skill 开发面

本目录提供基于 Stardew Valley MCP Tool 编写 Agent Skill 所需的最小材料，不包含官方玩法 Skill 集合，也不提供新的运行时。

```text
skill/
├── templates/stardew-skill-template/  可复制的标准 SKILL.md 模板
├── examples/stardew-nearby-overview/  只读附近概览示例
├── examples/stardew-remove-tree/      受控移除普通树示例
├── scripts/validate_skills.py         静态校验器
└── tests/                             校验器测试
```

Agent Runtime 读取 `SKILL.md` 后直接调用现有 `stardew_*` MCP Tool。MCP 和 Mod 不加载这些目录，也不会把示例注册成新的公共能力；默认只读和 `--allow-write` 权限边界保持不变。

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

## 未来方向

如果未来需要确定性代码执行或独立安装，可以在新的版本化阶段增加可执行 SDK 与 Skill Host。当前目录刻意不包含 `skill.yaml`、`implementation.py`、动态加载器、插件依赖管理或第二套命令生命周期。
