# 版本与兼容性

状态：**公开 V1 候选契约**

| 契约 | V1 候选 | 版本位置 |
|---|---:|---|
| Mod–MCP 线路协议 | `1.0` | `ProtocolVersion` 与 Proto package |
| 公开能力契约 | `1.0.0` | `capabilities/manifest.yaml` |

仓库产品版本独立于上述契约版本。实现完成前产品使用 `0.x` 预览版本；这不改变已经冻结的候选契约编号。

## Proto 兼容规则

- 字段编号和枚举编号不得复用。
- 删除的字段和枚举值必须标记为 `reserved`。
- 增加可选字段只有在旧接收方能够安全忽略，且新发送方不要求旧端提供时才属于 Minor 兼容。
- 增加必要语义约束、改变字段含义、改变 `oneof` 分支含义或改变终态语义属于 Major 破坏性变更。
- 线路 Major 不同必须拒绝，不得回退到其他格式。
- Patch 不参与线路协商，只能修正文档、Fixture 或不改变线路解释的问题。

## 能力兼容规则

- 一个能力 ID 只标识一种语义操作，不得挪作他用。
- 增加可选输入、增加可忽略结果字段或新增能力通常属于 Minor 兼容，但仍必须验证 MCP Tool Schema 对旧调用方的影响。
- 收窄输入、删除结果字段、改变副作用、弱化风险声明、改变取消语义或扩大默认权限属于破坏性变更。
- 能力的 `contract_version` 与线路版本独立；实现只暴露 Manifest、Mod Registry、MCP Projection 和本地授权的交集。

### 当前 V1 的兼容性增补

- `Error.navigation` 是可选的 `NavigationFailureContext`，用于失败导航的最后确认位置，以及正常超时时的路线段进度和续跑提示。它是旧接收方可安全忽略的附加线路字段，也是 MCP `error.details.navigation` 的可选附加字段，因此属于 V1 Minor 兼容增补；调用方不得要求所有错误或所有导航失败都存在该字段。
- `ENTITY_KIND_HOE_DIRT=14`、`WorldEntityFact.hoe_dirt=33` 与 `HoeDirtFact` 是 V1 的追加线路定义，旧接收方可以按 Proto 未知字段规则忽略，因此线路层属于 Minor 兼容增补。空 `HoeDirt` 不再作为 `ENTITY_KIND_GENERIC_OBJECT` 返回；曾只按 `generic_object` 过滤已耕地的调用方需要改为请求 `hoe_dirt`，而带作物的土地仍使用既有 `CropFact.watered`。

## Agent Skill 指引兼容规则

当前 Agent Skill 是开发指引，不参与 Mod–MCP 线路协商，也没有独立运行时版本。模板或正文约定发生变化时随仓库产品版本发布；Skill 引用的 MCP Tool 行为仍以对应公开能力契约版本为准。

## 变更流程

每次契约变更必须同时包含：

1. 修改后的机器可读权威定义；
2. 兼容性分类与理由；
3. 消费方需要修改时提供迁移说明；
4. 更新后的 Fixture 和一致性用例；
5. C# 与 Python 重新生成验证；
6. 至少一轮对抗审查。

内部 Protocol `2.4.5` 不是公开 V1 的前序版本，新仓库不得为它新增兼容代码。
