# V1 契约 Fixture

本目录中的线路 Fixture 采用官方 Proto JSON Mapping，便于 C# 与 Python 测试读取；V1 实际线路只传输二进制 Proto，不传输 JSON。`actions/` 中的文件是 MCP Tool 输入、输出与生命周期检查点的聚合向量，由 JSON Schema 和一致性验证器读取，不在线路上传输。

## 目录

```text
fixtures/v1/
├── auth/
│   ├── hmac-sha256.json
│   └── hmac-minor-downgrade.synthetic.json
├── transport/
│   ├── server-hello.json
│   ├── client-hello.json
│   └── server-ready.json
├── commands/
    ├── query-runtime.request.json
    ├── query-runtime.accepted.json
    ├── query-runtime.succeeded.json
    ├── navigate.request.json
    ├── navigate.cancel-request.json
    ├── navigate.cancel-response.json
    ├── navigate.status-request.json
    └── navigate.status-response.json
└── bootstrap/
    ├── server-ready.json
    ├── hmac-sha256.json
    ├── query-runtime.request.json
    ├── query-runtime.accepted.json
    ├── query-runtime.succeeded.json
    └── query-runtime.not-ready.json
└── observation/
    ├── server-ready.json
    ├── hmac-sha256.json
    ├── <capability>.request.json / <capability>.accepted.json
    ├── <capability>.success*.json / <capability>.failure.json
    └── invalid-inputs.json
└── actions/
    └── <capability>.json
```

`hmac-sha256.json` 中的秘密只用于公开测试向量，不是示例生产凭据。Fixture 文件名与目标 Proto 消息的对应关系由 `fixtures/v1/index.json` 声明，验证器不得靠文件名猜测消息类型。

`hmac-minor-downgrade.synthetic.json` 只验证未来同 Major 下的 Minor 协商与 HMAC 字段选择，不表示仓库已经定义或发布 V1.1；当前真实握手 Fixture 始终使用 V1.0。

`v1/index.json` 的 `profiles.bootstrap` 是只包含 `query_runtime` 的最小能力子集，而不是把完整 V1 Manifest 摘要伪装成已实现能力。该 profile 用两个独立场景固定同一请求的成功终态与 `ERROR_CODE_NOT_READY` 失败终态；场景彼此独立，因此不会把一个 Command ID 串成两个终态。Bootstrap 复用完整 V1 的 ServerHello/ClientHello 身份和 Nonce，但使用自己的 singleton CapabilitySnapshot digest、ServerReady HMAC 与 Fence。

`profiles.observation` 固定六项观察能力集合（`query_runtime`、`query_players`、`query_world`、`query_inventory`、`query_ui`、`inspect`）及其独立 digest/HMAC/Fence。每项都有独立成功与失败生命周期；players 固定自己、在线队友与离线农场工的字段边界，world、inventory、inspect 分别保留最小和完整成功投影，UI 同时覆盖无菜单、顶层菜单、Inventory 页、Crafting 页、对话与箱子菜单 Snapshot。它是协议 Contract Fixture；实际 ServerReady 必须仅由已注册 Handler ID 从静态 Catalog 生成。

`actionFixtures` 为 V1 的十三项变更能力各保留一个聚合文件。每个文件固定最小输入、完整输入、非法输入、`ACCEPTED` 检查点、成功 Tool 结果和失败 Tool 结果；这样可以覆盖能力契约，又不会演变成每项能力六个平铺文件。`navigate`、`interact`、`use_tool`、`transfer_inventory_item`、`set_equipment_slot` 与 `move_inventory_item` 的文件还固定各自的长时执行门禁与失败语义。

## 为什么包含 HMAC Fixture

Loopback 只能限制网络入口，不能证明连接进程就是本项目的 MCP，因此 Mod 与 MCP 仍需用安装时生成的共享秘密互相认证。HMAC Fixture 的作用是固定“哪些字段、按什么字节顺序参与签名”，确保 C# 与 Python 对同一握手计算出完全相同的 Client Proof 和 Server Proof，并证明任一 Nonce、版本、Session、Lease、Capability Digest 或保留期被修改后原签名都会失效。

这些文件不是密码库，也不负责加密游戏数据：

- 固定 `secretBase64`（包括 bootstrap 向量）是公开测试密钥，只用于可复现向量，不能复制到生产配置；
- 真正安装时必须由 OS CSPRNG 生成至少 32 字节秘密；
- HMAC 提供认证和完整性，不提供线路内容保密；V1 的安全边界仍是同一台机器上的 loopback；
- 验证器同时用 Python 与 C# 独立计算签名，防止某一端“自洽但不互通”。

Fixture 用于验证机器可读契约，不能覆盖 Proto、Manifest 或规范性行为规则。非法帧、短读、重复 ID、状态机和安全负例由一致性测试代码构造，不能仅靠“无法解析的 JSON 文件”表达。
