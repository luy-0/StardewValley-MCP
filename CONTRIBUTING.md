# 参与贡献

感谢你帮助改进 StardewValley MCP。

## 选择正确的修改边界

- 游戏集成和 Mod 运行时行为应修改 `mod/`。
- MCP 运行时行为和协议投影应修改 `mcp/`。
- Skill 接口、模板、最小示例或测试工具应修改 `skill/`。
- 跨组件的公共契约应修改 `spec/`。
- 安装、使用和开发说明应修改 `docs/`。

不得在多个目录中分别定义同一份公共契约。除非互操作确有需要，否则实现细节不得升级为公共契约要求。

所有说明性文档优先使用中文；协议标识、字段名、代码符号、文件名和必要的标准术语可以保留英文。

## 契约变更

修改 `spec/` 时应同时包含：

1. 规范性契约变更；
2. 兼容性判断和升级说明；
3. 对应的 fixture 或一致性测试；
4. 必要的 Mod、MCP 或 Agent Skill 指引、模板与示例修改。

破坏性变更必须在合并前作出明确的版本决策。具体规则见 [spec/VERSIONING.md](spec/VERSIONING.md)。

## Pull Request

每个 Pull Request 应只解决一个问题，并说明修改内容、所属边界、兼容性影响以及验证方式。

提交前至少运行：

```bash
./scripts/verify.sh
```

涉及真实 Mod 构建或发行包时，在已安装 Stardew Valley 与 SMAPI 的机器上运行：

```bash
./scripts/verify.sh --with-mod
```

新增或升级依赖时，必须按实际发行内容同步检查 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 与包审计规则。贡献默认依照仓库的 [Apache License 2.0](LICENSE) 提交。
