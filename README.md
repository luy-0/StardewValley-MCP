# StardewValley MCP

StardewValley MCP 是一个开源集成项目，让兼容 MCP 的智能体通过 SMAPI Mod 观察并操作《星露谷物语》。

本仓库包含两类资产：

- **实现**：真正运行的程序，包括 Mod 和 MCP 服务端。
- **契约**：规定 Mod、MCP 服务端与 Skill 如何通信和互操作。

## 仓库结构

```text
StardewValley-MCP/
├── mod/       SMAPI Mod 实现
├── mcp/       MCP 服务端实现
├── skill/     Agent 指引、模板、最小示例和测试工具
├── spec/      Mod、MCP 与 Skill 共享的公共契约
└── docs/      安装、使用和开发教程
```

### `mod/`

可运行的 SMAPI Mod，负责观察游戏状态、在主线程执行操作，以及处理游戏内命令。

### `mcp/`

可运行的 MCP 服务端，负责向 MCP 客户端暴露游戏能力、校验调用，并将获准执行的命令路由到 Mod。

### `skill/`

Skill 开发入口，只包含接口定义、编写模板、最小示例和一致性测试工具，不提供完整的官方玩法 Skill 集合。

### `spec/`

公共规范的权威来源，定义 Mod 与 MCP 服务端的消息、Schema、能力、错误和生命周期，也说明 Agent Skill 依赖这些公共 Tool 时必须遵守的边界。它不是运行时组件，也不是需要启动的服务。

公开 V1 候选契约的索引位于 [`spec/README.md`](spec/README.md)。它以旧实现的可验证行为为证据重新设计，只定义新的 V1 Proto 主链，不提供任何历史协议回退。

### `docs/`

面向使用者和贡献者的安装、配置、使用、排障与开发文档。

当前开发按[公开版本重写计划](docs/rewrite-plan.md)推进。

## 边界规则

1. 运行时代码只能进入 `mod/` 或 `mcp/`。
2. 跨组件契约只在 `spec/` 定义一次。
3. Skill 只能依赖公共契约和接口，不得依赖实现内部结构。
4. 教程用于解释契约，不得重新定义契约。
5. 修改公共契约时，必须同时提供兼容说明与一致性验证。
6. 所有说明性文档优先使用中文；协议标识、字段名、代码符号和必要标准术语保留英文。

## 项目状态

当前预览版已经具备独立的 Mod、MCP Python 包，以及公共 V1 契约定义的十五项原语能力：五项只读能力和十项需要明确授权的变更能力。十项变更能力包括七项简单交互，以及 `navigate`、`interact`、`use_tool` 三项长时能力。默认 MCP 启动方式仍只读；只有显式启用写权限，并且公共 Manifest、MCP 支持集与 Mod 握手公告同时包含对应能力时，才会暴露操作 Tool。阶段 6 已以 Agent `SKILL.md` 指引层交付模板、两个最小示例和静态校验工具；阶段 7 正在完成公开发布准备，因此本仓库仍是预览版。

普通用户的源码安装与调用方法见[快速开始](docs/getting-started.md)；Agent 从干净检出执行安装与验收时使用 [AGENT-GUIDE.md](AGENT-GUIDE.md)。本地完整回归入口为：

```bash
./scripts/verify.sh
```

本项目采用 [Apache License 2.0](LICENSE)；第三方组件与发行边界见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。Stardew Valley 与 SMAPI 是用户自行取得的外部前置条件，不随本项目分发。

## 参与贡献

初始贡献流程和契约变更流程参见 [CONTRIBUTING.md](CONTRIBUTING.md)。
