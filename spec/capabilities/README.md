# 公开能力契约

`manifest.yaml` 是 Mod 可以宣告、MCP 可以暴露、第三方 Skill 可以依赖的能力面权威来源，并且必须通过 `manifest.schema.json` 校验。公开 V1 包含 16 项能力。

参数约束、默认值、Ref/Revision 生命周期和成功后置条件见 [`behavior.md`](behavior.md)。

## 暴露规则

```text
公共 Manifest
  ∩ Mod 编译期 Registry
  ∩ 握手能力快照
  ∩ MCP 已编译 Projection
  ∩ 本地授权 Scope 与风险策略
```

未知运行时条目只记录脱敏审计并忽略，不得动态创建 Tool、加载代码或扩大权限。Tool 列表中出现的能力必须能在同一 Capability Digest 下调用；Digest 改变需要重新握手和重新投影。

## 字段语义

- `id`：稳定能力 ID，也必须等于 `CommandRequest.operation` 分支名。
- `title`、`description`：生成 MCP Tool 人类说明的中文来源。
- `contract_version`：能力参数、结果和行为语义版本。
- `request`、`result`：Proto 消息名称。
- `side_effect`：`read_only` 或 `mutating`。
- `execution`：`immediate` 或 `long_running`。
- `cancellable`：Handler 是否支持业务取消。
- `default_timeout_ms`、`max_timeout_ms`：Mod 使用单调时钟执行的超时边界。
- `required_scope`：最低本地授权 Scope。
- `risk`：供 Skill 解析与 MCP Annotation 使用的固定风险标签。
- `destructive`：MCP `destructiveHint` 的显式策略，不从风险数组机械推断。

`risk` 表示一次合法调用**可能**产生的物质影响，不要求每次调用都必然发生。风险为空不代表没有游戏内副作用，只表示当前固定策略标签中没有需要额外声明的影响。

Manifest 不包含 C# 或 Python 实现路径。Registry 与 Projection 关系由一致性测试对照清单验证，不能通过运行时扫描源码推断。
