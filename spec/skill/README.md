# Agent Skill 依赖边界

状态：**阶段 6 依赖边界说明**

当前阶段的 Skill 是供 Agent 读取的操作指引，不是由 MCP 动态加载的可执行插件。Agent 按 `SKILL.md` 描述的步骤直接调用现有 Stardew Valley MCP Tool；MCP 与 Mod 不感知 Skill，也不增加新的运行时、协议分支或能力注册。

## 最小目录

```text
<skill-name>/
├── SKILL.md
└── agents/
    └── openai.yaml    # 可选的产品界面元数据
```

`SKILL.md` 的 YAML Frontmatter 至少包含 `name` 与 `description`，并可使用 Agent Skill 标准允许的其他字段。`name` 使用小写字母、数字和连字符，且与目录名一致；`description` 同时说明 Skill 做什么以及哪些用户请求应触发它。

本仓库提供的模板与示例使用中文优先，并采用以下二级标题，以便审查工作流边界：

```text
## 可用工具
## 工作流程
## 停止条件
## 输出要求
## 安全边界
```

这五个标题是本仓库的安全编写剖面，不是新的通用 Agent Skill 格式。`可用工具` 只列出公共 Catalog 中真实存在的 `stardew_*` MCP Tool。正文不得要求 Agent 调用 Mod 内部类型、MCP 内部 Python 模块、私有平台接口、旧协议别名或文件桥。

## 执行边界

- Agent Runtime 负责发现和加载 `SKILL.md`；本仓库不规定各 Agent 产品的安装目录或触发机制。
- Agent 直接调用 MCP Tool，并遵守对应 Tool 的输入 Schema、结构化结果、错误、Ref/Revision、权限和风险规则。
- 默认 MCP 仍只读。Skill 即使写了变更步骤，也不能绕过 `--allow-write`、公共 Manifest、MCP 支持集与 Mod 公告的现有交集门禁。
- Skill 必须显式写出查询、动作、复查和停止条件。`unknown_outcome` 不得被当成成功，也不得自动重放变更动作。
- Skill 是指导 Agent 的可复用知识，不是新的公共原子能力，不进入 Proto、Capability Manifest 或 Mod Registry。

## 当前交付与未来演进

阶段 6 当前只交付约定、模板、两个最小示例和静态校验工具，不交付 Python SDK、Skill Host、动态挂载、插件依赖管理或独立生命周期。

未来如果 Agent 指引无法满足确定性执行、独立分发或复用需求，可以在新的版本化阶段演进为可执行 SDK 与 MCP Skill Host。该方向必须复用现有 Catalog、Command Runtime 和 Transport，且不能反向改变当前 Mod–MCP 原子能力契约。
