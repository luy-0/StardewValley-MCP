# Skill 包契约

状态：**格式版本 1 候选契约**

本规范定义可由 MCP Skill Runtime 校验并挂载的软件包。仓库顶层 `skill/` 只提供 SDK、模板、最小示例和测试工具，不是官方 Skill 集合。

## 包结构

```text
<skill-id>/
├── skill.yaml
├── implementation.py
└── README.md          # 第三方包必须提供
```

`skill.yaml` 必须通过 `skill.schema.json` 校验。当前 Entry Point 固定为 `implementation:run`。实现只能使用 Skill SDK 和所提供的执行上下文，不得导入 MCP 内部模块、Mod 代码、私有平台模块或任意主机路径。

## Manifest 语义

- `id` 是至少包含三段的小写点分标识，包目录名必须与它一致。
- `version` 严格采用 `MAJOR.MINOR.PATCH`。
- 本仓库中的 `game` 固定为 `stardew-valley`。
- `input_schema` 与 `output_schema` 使用文档规定的封闭 JSON Schema 子集；输入对象始终封闭。
- `requires.capabilities` 声明稳定能力 ID 和兼容版本范围，不得依赖未列入公共清单的能力。
- `risk` 是声明性字段，必须包含全部潜在副作用。运行时策略可以拒绝或要求确认，但不得弱化已声明风险。
- `timeout_seconds` 范围为 1 到 300。
- 格式版本 1 中，`retry` 固定为 `never`。Skill 如需安全重试，必须通过稳定的能力命令身份显式实现。
- `failure_codes` 是 Skill 声明的公开失败类型。

## 风险标签

格式版本 1 固定使用以下标签：

```text
changes_save, advances_time, consumes_item, consumes_energy,
spends_money, changes_relationship, external_communication,
changes_position
```

未知标签会导致校验失败。只有全部依赖能力都存在、版本范围匹配且策略允许所有声明风险时，Skill 才能变为可用。

## 运行时隔离边界

当前可信本地模型在同一 OS 用户下执行通过校验的 Python 包，它不能隔离恶意代码。在未来规范定义真正的隔离边界前，公共 SDK 和示例必须被视为可信本地扩展。
