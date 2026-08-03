# ADR-0001：公开 V1 的能力边界与版本策略

- 状态：已接受
- 日期：2026-07-26

## 背景

公开协议需要一套能够独立版本化、由机器可读定义完整描述的能力边界。Proto 中可表达的消息、实现内部辅助行为和面向 Agent 的公共 Tool 必须明确区分，避免某个消息因为存在于代码中就被误认为公共承诺。

## 决策

1. 公开协议建立独立的 V1 版本历史，protobuf package 使用 `stardew_valley.mcp.v1`。
2. V1 的公开能力集合只以 `../capabilities/manifest.yaml` 为权威，共 15 项。
3. Proto 消息存在不自动表示对应能力可以被 Mod 公告或投影为 MCP Tool。
4. V1 使用一套二进制 Proto 业务协议，不提供平行的线路格式或隐藏能力入口。
5. 仓库在实现完成前保持 `0.x` 产品版本；公开契约的 `contract_version` 从 `1.0.0` 开始。

## 后果

- Mod 与 MCP 必须协商兼容的线路版本和 Capability Digest。
- 未进入公共 Manifest 的实现细节不构成兼容承诺。
- 公共能力发生变化时必须遵守 `../VERSIONING.md`，同步更新 Proto、Manifest、Fixture 与实现。
