# 本地 TCP 传输 Spike

这个目录把 ADR-0002 的长度前缀传输结论固化为可复现资产。它不实现认证、Session 或命令状态机，只验证正式实现依赖的最底层事实：.NET 6 `TcpListener` 与 Python 3 标准库 Socket 能按「4 字节大端长度 + Proto Payload」可靠通信。

## 运行

前置条件是能构建 `net6.0` 的 .NET SDK、Microsoft.NETCore.App 6.x Runtime 与 Python 3，不需要 NuGet 包或 Python 第三方包。CI 的 SDK／Runtime 分层原因见 [`../../../docs/runtime-compatibility.md`](../../../docs/runtime-compatibility.md)。在仓库根目录执行：

```bash
./spec/conformance/transport-spike/run.sh
```

成功时会同时出现：

```text
SPIKE_OK cases=5 [短读与粘包, 零长度, 超长帧, 短 Header EOF, 短 Payload EOF]
PYTHON_OK protobuf_wire=true framed_roundtrip=3 negative_cases=4
```

## 覆盖边界

Harness 由 Python 启动 .NET 6 Listener，并依次建立五条真实 loopback TCP 连接：

1. 第一条连接往返三个合法 `TransportFrame { message_id, ping }` Proto Payload。服务端把底层 `NetworkStream.ReadAsync` 单次读取限制为 2 字节，确定性经过显式短读循环；随后 Python 用一次 `sendall` 写入两个完整帧，验证粘包不会破坏帧边界。
2. 第二、三条连接分别发送长度 `0` 和 `1,048,577`，服务端必须立即失败关闭。
3. 第四条连接只发送 2 字节 Header 后 EOF，必须识别为短 Header。
4. 第五条连接声明 8 字节 Payload、实际只发送 3 字节后 EOF，必须识别为短 Payload。

测试 Payload 按 `spec/proto/transport.proto` 的字段号直接编码，并由 C# 最小解析器验证 `message_id`、`ping` 与 `sequence`。最小解析器仅用于让这个 Spike 零依赖；正式 Mod 和 MCP 必须使用同一 Proto 源生成的类型，不得复制该解析器。

## 明确限制

- 该 Spike 只证明当前环境中的 framing 和跨语言 wire 互通，不替代完整一致性测试。
- 它没有覆盖非法 Proto、HMAC、握手顺序、Lease、Capability Digest 或命令生命周期；这些属于 `spec/conformance/README.md` 定义的其他门禁。
- C# 读取循环没有使用 `Stream.ReadExactlyAsync`，因为该 API 不属于本仓库选定的 .NET 6 目标。
