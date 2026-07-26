# V1 契约 Fixture

本目录中的 JSON 文件采用官方 Proto JSON Mapping，便于 C# 与 Python 测试读取；V1 实际线路只传输二进制 Proto，不传输 JSON。每个 Fixture 必须由 `../conformance/verify.py` 根据目标消息严格解析并完成二进制往返。

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
└── commands/
    ├── query-runtime.request.json
    ├── query-runtime.accepted.json
    ├── query-runtime.succeeded.json
    ├── navigate.request.json
    ├── navigate.cancel-request.json
    ├── navigate.cancel-response.json
    ├── navigate.status-request.json
    └── navigate.status-response.json
```

`hmac-sha256.json` 中的秘密只用于公开测试向量，不是示例生产凭据。Fixture 文件名与目标 Proto 消息的对应关系由 `fixtures/v1/index.json` 声明，验证器不得靠文件名猜测消息类型。

`hmac-minor-downgrade.synthetic.json` 只验证未来同 Major 下的 Minor 协商与 HMAC 字段选择，不表示仓库已经定义或发布 V1.1；当前真实握手 Fixture 始终使用 V1.0。

## 为什么包含 HMAC Fixture

Loopback 只能限制网络入口，不能证明连接进程就是本项目的 MCP，因此 Mod 与 MCP 仍需用安装时生成的共享秘密互相认证。HMAC Fixture 的作用是固定“哪些字段、按什么字节顺序参与签名”，确保 C# 与 Python 对同一握手计算出完全相同的 Client Proof 和 Server Proof，并证明任一 Nonce、版本、Session、Lease、Capability Digest 或保留期被修改后原签名都会失效。

这些文件不是密码库，也不负责加密游戏数据：

- 固定 `secretBase64` 是公开测试密钥，只用于可复现向量，不能复制到生产配置；
- 真正安装时必须由 OS CSPRNG 生成至少 32 字节秘密；
- HMAC 提供认证和完整性，不提供线路内容保密；V1 的安全边界仍是同一台机器上的 loopback；
- 验证器同时用 Python 与 C# 独立计算签名，防止某一端“自洽但不互通”。

Fixture 用于验证机器可读契约，不能覆盖 Proto、Manifest 或规范性行为规则。非法帧、短读、重复 ID、状态机和安全负例由一致性测试代码构造，不能仅靠“无法解析的 JSON 文件”表达。
