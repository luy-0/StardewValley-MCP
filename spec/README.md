# StardewValley MCP 公开规范

状态：**公开 V1 契约**

本目录是 Mod、MCP 服务端与 Skill 开发套件共享的公共契约权威来源，包含数据定义、能力边界、生命周期规则、安全与兼容策略，以及不依赖具体实现的一致性测试。它不是运行时组件，`spec/` 下没有任何需要作为服务启动的内容。

## 规范编写原则

### 1. 描述互操作，不描述实现

只有两个独立实现必须共同遵守的规则才能进入 `spec/`。文件路径、Python 类、C# 类、线程实现、数据库表和内部日志方案都不是公共契约，除非它们确实跨越公共边界。

### 2. 一个事实只有一个权威来源

| 契约事实 | 权威来源 |
|---|---|
| 线路消息结构、字段编号、枚举 | `proto/*.proto` |
| 公开能力清单及策略 | `capabilities/manifest.yaml` 及其 Schema |
| Mod–MCP 传输、认证与命令生命周期 | `mod-mcp-protocol.md` |
| Mod 公开运行边界 | `mod.md` |
| MCP 投影与稳定错误 | `mcp/README.md` |
| Agent Skill 依赖边界 | `skill/README.md` |
| 兼容性与版本变更 | `VERSIONING.md` |
| 可观测的一致性要求 | `conformance/` 与 `fixtures/` |

Markdown 用于解释机器可读定义，但不得重复维护 protobuf、YAML 或 JSON Schema 中的完整字段表。

`proto/` 与 `mod-mcp-protocol.md` 不是两套协议：前者回答“线路上有哪些字段和编号”，后者回答“这些消息何时可以发送、状态如何变化、认证和幂等如何成立”。行为文档不得重新列一份字段定义，Proto 注释也不得偷偷定义完整状态机。

### 3. 区分线路可表达集合与公开能力面

Proto 定义实现之间可以交换的消息；Manifest 定义公开、受支持且允许投影为 MCP Tool 或 Skill 依赖的能力集合。某个消息存在不代表它自动成为公开能力。

### 4. 规范行为必须可以测试

规范性要求使用 **MUST**、**MUST NOT**、**SHOULD** 和 **MAY**。每条 MUST 级互操作规则都必须能够通过 fixture、一致性用例或跨实现测试验证。

### 5. 在信任边界默认拒绝

不支持的版本、无效 Schema、过期 Lease、能力摘要不匹配、未授权 Tool 和不支持的能力都必须被拒绝。实现不得静默降级或自动扩大能力面。

### 6. 有意识地维护线路兼容性

已经发布的 Proto 字段编号和枚举值永不复用。破坏性变更必须作出明确版本决策并附迁移说明；从某份实现代码偶然推断出的行为不构成契约。

### 7. 示例不具备规范性

Fixture 只展示有效或无效交互。如果示例与机器可读契约或规范性规则冲突，以机器可读契约和规范性规则为准。

### 8. 保持公共边界最小化

公共规范只定义 Mod、MCP 与 Agent Skill 互操作所需的数据和行为，不纳入与该互操作无关的部署、账号或运营字段。

### 9. 说明性文档优先使用中文

规范说明、教程和贡献文档默认使用中文。协议标识、字段名、代码符号、文件名及 MCP、Proto、Skill 等标准术语可以保留英文，避免改变契约含义。

## V1 设计结论

- 协议 package 为 `stardew_valley.mcp.v1`。
- 公共能力由 Manifest 定义为 16 项正交能力。
- 本地链路使用 Mod Listener、MCP Client 和长度前缀二进制 Proto。
- 认证使用本地连接所需的 Session、Lease 与 Capability Digest。

## 索引

- [版本与兼容性](VERSIONING.md)
- [公开 V1 范围决策](decisions/0001-public-v1-scope.md)
- [本地传输决策](decisions/0002-local-transport.md)
- [统一命令运行时决策](decisions/0003-unified-command-runtime.md)
- [Mod–MCP 行为协议](mod-mcp-protocol.md)
- [Proto 线路数据模型](proto/README.md)
- [Mod 公开实现契约](mod.md)
- [公开能力模型](capabilities/README.md)
- [能力行为契约](capabilities/behavior.md)
- [MCP 投影](mcp/README.md)
- [Agent Skill 依赖边界](skill/README.md)
- [契约 Fixture](fixtures/README.md)
- [一致性要求](conformance/README.md)

## V1 契约维护

实现必须以这里的契约为输入。任何公共行为变更都必须同步修改权威定义、Fixture、兼容判断和一致性测试，不能只在某一端实现中建立隐式例外。
