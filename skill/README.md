# Skill 开发套件

本目录用于支持第三方开发 Skill。

只包含：

- 公共接口定义；
- 编写模板；
- 最小示例；
- 校验与一致性测试工具。

这里不提供完整的官方玩法 Skill 集合。最小示例只演示一种契约或编写模式，不应逐渐扩张为需要长期维护的产品功能。

## 阶段 6 计划目录

```text
skill/
├── sdk/python/               独立 Python SDK、校验器与开发者命令
├── templates/python/         可复制的最小 Skill 包骨架
├── examples/read-only/       只读查询编排示例
├── examples/mutating/        有界变更编排示例
└── tests/                    SDK、模板与示例的一致性测试
```

Skill 接口依赖 `../spec/`，不得导入 MCP 或 Mod 的私有实现。

生产环境中的 Skill 加载与执行由 MCP 内部独立的 Skill Host 负责，不放在本目录。Host 只能向 Skill 提供 SDK 定义的 `SkillContext`，并在加载期与运行期共同执行能力、版本、权限和风险门禁。

Python SDK 将作为独立的 `stardew-valley-skill-sdk` 分发，公共 import 为 `stardew_valley_skill`。第三方 Skill V1 只允许使用 Python 标准库和该 SDK；开发者通过同一 CLI 完成创建模板、校验和无游戏测试，不需要安装完整 MCP Server。

阶段 6 只提供两个最小示例：一个只读查询示例，以及一个对相邻普通树重复调用 `use_tool`、复查并有界停止的变更示例。它们用于证明开发方式和测试方式，不作为完整官方玩法技能长期扩张。
