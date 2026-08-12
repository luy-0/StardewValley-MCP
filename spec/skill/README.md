# Agent Skill 与可执行 Skill Host 契约

状态：**公开 V1 可执行 Skill 契约**

Skill 是基于公共原子 Tool 的二次组合，不属于 Mod Proto、Mod Capability Manifest 或 Mod Registry。Prompt 型 Skill 由 Agent Runtime 阅读 `SKILL.md` 后自行调用 Tool；可执行 Skill 由 MCP 进程内的 Skill Host 加载，并作为派生 MCP Tool 暴露给 Agent。两者可以使用同一个 Skill 目录，但执行权和兼容边界不同。

## 1. 两种 Skill 形态

### Prompt 型 Skill

只包含 `SKILL.md` 与可选的 `agents/openai.yaml`。Agent 负责理解步骤、选择参数和逐次调用原子 Tool，MCP 不加载该目录，也不为它增加 Tool。

### 可执行 Skill

在 Prompt 型材料之外增加 `runtime.yaml`、JSON Schema 与确定性脚本：

```text
<skill-name>/
├── SKILL.md
├── runtime.yaml
├── schemas/
│   ├── input.json
│   └── output.json
├── scripts/
│   └── run.py
└── agents/
    └── openai.yaml
```

`runtime.yaml` 的唯一机器可读定义是 [`runtime-manifest.schema.json`](runtime-manifest.schema.json)。实现必须拒绝未知字段、无效 Schema、绝对资源路径、越出 Skill 包目录的资源以及无法解析的入口脚本。

## 2. Skill Host 的位置

Skill Host 是 MCP 内部的受限编排执行层。它复用 MCP 当前持有的 Mod Owner Session、Catalog、Command Runtime 与 Transport，不建立第二条 Mod 连接，也不直接构造 Proto 命令。Mod 只观察到脚本顺序提交的公共原子命令，不感知 Skill 名称和复合任务。

MCP 对 Agent 暴露的 Tool 集合为：

```text
已授权原子 Tool
+ 依赖 Tool 全部可用的已加载可执行 Skill Tool
```

可执行 Skill 不参与 Mod Capability Digest。其输入、输出、Annotation、依赖与宿主执行参数由自身 `runtime.yaml` 和 JSON Schema 决定；说明文档不得成为第二套契约。

## 3. 发现与加载

- MCP 必须加载发行包随附的内置可执行 Skill。
- 额外 Skill 目录只能由用户通过启动配置显式加入信任范围；当前标准入口是可以重复使用的 `serve --skill-dir <path>`。
- 搜索根可以直接指向一个带 `runtime.yaml` 的 Skill，也可以指向其直接父目录。Host 不递归扫描更深层目录，避免意外扩大信任范围。
- 第一版动态加载发生在 MCP 启动时。安装或删除 Skill 后只需重启 MCP，不需要重启游戏或修改 Mod。
- 同名 Tool、无效包或缺失资源必须使 MCP 启动失败，不能静默跳过后继续暴露不完整能力。
- 后续实现可以增加文件监听与 MCP Tool List Changed 通知，但不得在旧脚本仍运行时原地替换其代码或契约。

## 4. 入口与权柄

入口格式固定为 `<相对 Python 文件>:<异步函数>`。入口函数接收 `(SkillContext, arguments)`，并返回满足 `outputSchema` 的 JSON 对象：

```python
async def run(ctx, arguments):
    result = await ctx.call_tool("stardew_query_runtime", {})
    ...
```

脚本只能把 `requires.tools` 中声明的 Tool 交给 `ctx.call_tool`。SkillContext 必须在调用结束、取消或超时后撤销；撤销后的调用必须失败。脚本不得读取共享秘密、创建 `StardewClient`、连接 Mod 端口、发送 Proto Frame 或绕过公共 Catalog。

`requires.tools` 只能声明 Mod 公告并由 MCP Catalog 投影的原子 `stardew_*` Tool，不得声明 `stardew_skill_*` 派生 Tool。V1 的输入／输出 Schema 只允许当前 JSON 文档内能够静态解析的本地 `$ref`，不加载网络、文件或相邻包中的 Schema。

原子调用必须串行等待终态后再提交下一项，不能并行占用 Mod 主线程。当前 `execution.concurrency` 只支持 `exclusive`：同一 MCP Session 内一次只运行一个可执行 Skill，避免多个复合流程交错修改游戏。

## 5. Tool Schema 与风险

可执行 Skill Tool 必须同时声明 `inputSchema`、`outputSchema` 和完整 Annotation：

- `readOnlyHint`：整个流程是否只读；
- `destructiveHint`：是否可能造成不便自动撤销的存档变化；
- `idempotentHint`：相同调用重复执行是否保证等价；
- `openWorldHint`：是否操作本地游戏以外的外部实体。

Host 必须在调用前验证输入，并在返回前验证输出。脚本已经提交变更但输出不符合 Schema 时，Host 必须返回 `unknown` 且禁止自动重放；只读脚本可以返回确定性内部失败。

## 6. 失败、未知终态与超时

可执行 Skill 使用 `succeeded`、`failed`、`unknown` 三种顶层状态。`failed` 表示可以确认任务没有成功完成；`unknown` 表示已经可能产生存档变化，但当前证据不足以确认任务终态。

- 变更 Tool 返回 `unknown` 后禁止自动重放，也禁止继续提交其他变更。只有后续独立只读事实能够唯一证明该次变更的后置条件已经成立时，受信任脚本才可以调用 `ctx.resolve_unknown_mutation(tool_name)` 清除最近一次未知变更；工具名称必须与最近一次 `unknown` 变更 Tool 完全一致。脚本随后仍需验证任务级后置条件，证据不足时必须停止并保持 `unknown`。
- 变更 Tool 正在执行或已经成功后发生宿主超时，Host 必须返回 `unknown`、`retryable=false`，并包含最后 Tool、变更 Tool、完成调用数和阶段。
- 变更 Tool 提交期间连接中断或脚本异常时，只要结果可能已经生效，Host 同样必须返回 `unknown`，不能降级成可重试的内部失败。
- 纯只读流程在宿主超时时可以返回 `failed`、`retryable=true`。
- MCP 取消必须传播到当前脚本和可取消的原子命令；不能确认变更终态时仍按 `unknown` 处理。
- 成功不能只依赖原子 Tool 的返回值；脚本必须验证自身任务级后置条件。

## 7. 信任与隔离

进程内 Python Skill 是受信任扩展。`SkillContext` 只约束脚本通过正式接口取得的原子 Tool 权限，不是针对恶意 Python 的操作系统沙箱；任意 Python 仍可能读取当前进程可访问的文件、环境与网络。

因此，实现不得自动加载系统中发现的任意目录，也不得把 `allowed_tools` 宣称为第三方代码隔离。需要运行不可信 Skill 时，应在后续版本中使用独立受限进程或 WASM，并通过 Host RPC 借用原子 Tool；该隔离执行器仍不得建立自己的 Mod Owner Session。

## 8. 维护规则

- 高自由度、短步骤任务可以保持 Prompt 型 Skill。
- 重复、多目标、时序敏感或游戏时间持续推进的流程应使用确定性脚本。
- 必须逐帧读取或修改游戏状态的逻辑仍属于 Mod 原子 Handler，不应搬入 Python Skill。
- 新增可执行 Skill 不得要求修改 MCP Server、构建脚本或 SkillHost 的能力专用分支；只应新增一个符合本契约的 Skill 包和对应测试。
- 公共仓库只提供 SDK、模板、最小示例与测试工具，不承诺随主仓发布完整的官方玩法 Skill 集合。
