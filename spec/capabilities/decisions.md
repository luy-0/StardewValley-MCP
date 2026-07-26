# 公开 V1 能力裁决

状态：**已冻结为 V1 候选**

裁决只回答能力是否属于公共 Mod/MCP 原语，不评价旧实现是否已经完成。最终公开集合以 `manifest.yaml` 为机器可读权威，本文件记录从 18 项旧候选到 V1 能力面的理由。

| 旧能力 | 裁决 | V1 能力 | 理由 |
|---|---|---|---|
| `say` | 重写后保留 | `say` | 明确、即时、无法由其他原语组合 |
| `emote` | 重写后保留 | `emote` | 明确、即时、无法由其他原语组合 |
| `face` | 重写后保留 | `face` | 导航和交互之外仍需要独立朝向控制 |
| `move_to` | 合并 | `navigate` | 与跨地图移动共享目标、取消和终态语义，旧 L1/L2 分层不应暴露给调用者 |
| `go_to` | 合并 | `navigate` | 由一个导航能力根据目标 Location 自动选择同图或跨图路径 |
| `go_to_bed` | 从 V1 删除 | — | 属于跨导航、交互、确认与跨日 UI 的工作流；不应固化为 Mod 原语，可在 Skill 能力成熟后另行设计 |
| `interact` | 重写后保留 | `interact` | 游戏基础原语；目标改为明确的 Tile 或 Ref，禁止隐式远程导航 |
| `use_tool` | 重写后保留 | `use_tool` | 游戏基础原语；必须确认实际工具、目标和能量变化 |
| `equip` | 重写后保留 | `equip` | 游戏基础原语；只接受 Slot Index 或查询得到的 Item Ref，删除模糊名称/类别选择 |
| `open_menu` | 重写后保留 | `open_menu` | 打开顶层游戏菜单无法由 UI 元素激活替代；菜单类型使用枚举 |
| `query_menu` | 合并 | `query_ui` | 与 UI Snapshot 重复；统一返回菜单、Revision 和可交互元素 |
| `menu_click` | 替换 | `activate_ui` | 原组件 ID 与坐标不稳定；V1 使用 UI Ref 加 Snapshot Revision 防止点击过期界面 |
| `menu_close` | 重写后保留 | `close_menu` | 明确的 UI 原语；关闭前后状态必须可验证 |
| `query_runtime_snapshot` | 重命名并重写 | `query_runtime` | 保留最小运行时、玩家、时间与天气事实，删除无效 include 开关 |
| `query_world_region` | 重命名并重写 | `query_world` | 保留有界区域查询；增加严格面积和结果数量限制 |
| `query_inventory_snapshot` | 重命名并重写 | `query_inventory` | 统一玩家与容器库存语义；Ref 与 Slot Index 明确 |
| `query_ui_snapshot` | 重命名并重写 | `query_ui` | 成为唯一 UI 观察能力，提供 UI Revision |
| `query_inspect_refs` | 重命名并重写 | `inspect` | 保留批量延迟解析，但 Ref 对调用方保持不透明 |

## V1 最终候选集合

```text
say, emote, face, navigate, interact, use_tool, equip,
open_menu, activate_ui, close_menu,
query_runtime, query_world, query_inventory, query_ui, inspect
```

## 明确不公开

- `tp`：绕过游戏移动规则，只能作为实现内部恢复策略；不得出现在公共 Proto 或 Manifest。
- `go_to_bed`：本轮不提供官方 Skill，也不留隐藏 Handler。
- `query_location`、`query_map_exits`、旧 `query_inventory`：其有效信息分别由 `query_runtime`、`query_world` 和新 `query_inventory` 覆盖。
- `do`、批处理 Compound Capability 和 Developer Command：不属于公共 V1。
