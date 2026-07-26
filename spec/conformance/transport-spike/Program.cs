using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

const int HeaderSize = 4;
const int MaxFrameLength = 1_048_576;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
Console.WriteLine($"READY {port}");
Console.Out.Flush();

var passed = new List<string>();

try
{
    await RunNormalCaseAsync(await listener.AcceptTcpClientAsync());
    passed.Add("短读与粘包");

    await ExpectFailureAsync(
        await listener.AcceptTcpClientAsync(),
        FrameFailure.InvalidLength,
        "零长度");
    passed.Add("零长度");

    await ExpectFailureAsync(
        await listener.AcceptTcpClientAsync(),
        FrameFailure.InvalidLength,
        "超长帧");
    passed.Add("超长帧");

    await ExpectFailureAsync(
        await listener.AcceptTcpClientAsync(),
        FrameFailure.ShortHeader,
        "短 Header EOF");
    passed.Add("短 Header EOF");

    await ExpectFailureAsync(
        await listener.AcceptTcpClientAsync(),
        FrameFailure.ShortPayload,
        "短 Payload EOF");
    passed.Add("短 Payload EOF");

    Console.WriteLine($"SPIKE_OK cases={passed.Count} [{string.Join(", ", passed)}]");
}
finally
{
    listener.Stop();
}

static async Task RunNormalCaseAsync(TcpClient client)
{
    using (client)
    using (var stream = client.GetStream())
    {
        // 将单次底层 ReadAsync 限制为最多 2 字节，稳定触发短读路径；
        // 这是对真实 Socket Stream 的分段读取，不依赖 TCP 分包时机。
        var first = await ReadFrameAsync(stream, maxReadChunk: 2);
        Ensure(first.Failure == FrameFailure.None, $"短读帧失败：{first.Failure}");
        Ensure(first.ReadCalls > 2, "短读用例没有经过多次底层读取");
        ValidatePingTransportFrame(first.Payload!);
        await WriteFrameAsync(stream, first.Payload!);

        // Python 端用一次 sendall 发送这两帧。读取器必须严格按长度边界
        // 消费第一帧，并保留 Socket 中已经到达的第二帧。
        var second = await ReadFrameAsync(stream);
        var third = await ReadFrameAsync(stream);
        Ensure(second.Failure == FrameFailure.None, $"粘包第一帧失败：{second.Failure}");
        Ensure(third.Failure == FrameFailure.None, $"粘包第二帧失败：{third.Failure}");
        ValidatePingTransportFrame(second.Payload!);
        ValidatePingTransportFrame(third.Payload!);
        await WriteFrameAsync(stream, second.Payload!);
        await WriteFrameAsync(stream, third.Payload!);

        var eof = await ReadFrameAsync(stream);
        Ensure(eof.Failure == FrameFailure.CleanEof, $"完整帧后的 EOF 应正常关闭，实际为 {eof.Failure}");
    }
}

static async Task ExpectFailureAsync(TcpClient client, FrameFailure expected, string label)
{
    using (client)
    using (var stream = client.GetStream())
    {
        var frame = await ReadFrameAsync(stream);
        Ensure(frame.Failure == expected, $"{label}：期望 {expected}，实际 {frame.Failure}");
        // 失败后不再读取下一帧；using 退出即关闭连接，符合失败关闭要求。
    }
}

static async Task<FrameReadResult> ReadFrameAsync(NetworkStream stream, int maxReadChunk = int.MaxValue)
{
    var header = new byte[HeaderSize];
    var headerRead = await ReadExactOrEofAsync(stream, header, maxReadChunk);
    if (headerRead.BytesRead == 0)
    {
        return new FrameReadResult(FrameFailure.CleanEof, null, headerRead.ReadCalls);
    }
    if (!headerRead.Complete)
    {
        return new FrameReadResult(FrameFailure.ShortHeader, null, headerRead.ReadCalls);
    }

    var length = BinaryPrimitives.ReadUInt32BigEndian(header);
    if (length is 0 or > MaxFrameLength)
    {
        return new FrameReadResult(FrameFailure.InvalidLength, null, headerRead.ReadCalls);
    }

    var payload = new byte[(int)length];
    var payloadRead = await ReadExactOrEofAsync(stream, payload, maxReadChunk);
    if (!payloadRead.Complete)
    {
        return new FrameReadResult(
            FrameFailure.ShortPayload,
            null,
            headerRead.ReadCalls + payloadRead.ReadCalls);
    }

    return new FrameReadResult(
        FrameFailure.None,
        payload,
        headerRead.ReadCalls + payloadRead.ReadCalls);
}

static async Task<ExactReadResult> ReadExactOrEofAsync(
    NetworkStream stream,
    byte[] buffer,
    int maxReadChunk)
{
    var offset = 0;
    var readCalls = 0;

    while (offset < buffer.Length)
    {
        var requested = Math.Min(buffer.Length - offset, maxReadChunk);
        var count = await stream.ReadAsync(buffer.AsMemory(offset, requested));
        readCalls++;
        if (count == 0)
        {
            return new ExactReadResult(false, offset, readCalls);
        }
        offset += count;
    }

    return new ExactReadResult(true, offset, readCalls);
}

static async Task WriteFrameAsync(NetworkStream stream, byte[] payload)
{
    Ensure(payload.Length is > 0 and <= MaxFrameLength, "输出帧长度越界");
    var header = new byte[HeaderSize];
    BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
    await stream.WriteAsync(header);
    await stream.WriteAsync(payload);
    await stream.FlushAsync();
}

static void ValidatePingTransportFrame(byte[] payload)
{
    // 本 Spike 的测试消息是 transport.proto 中的真实 wire shape：
    // TransportFrame.message_id(field 1, string) + TransportFrame.ping(field 30, message)，
    // 其中 Ping.sequence 是 field 1 uint64。最小解析器只验证该 Golden Shape；
    // 正式实现仍由生成的 Google.Protobuf 类型负责完整反序列化。
    var offset = 0;
    var outerKey1 = ReadVarint(payload, ref offset);
    Ensure(outerKey1 == ((1u << 3) | 2u), "TransportFrame 缺少 message_id");
    var messageId = ReadLengthDelimited(payload, ref offset);
    Ensure(messageId.Length is >= 1 and <= 64, "message_id 长度非法");
    Ensure(messageId.All(value => value is >= 0x20 and <= 0x7e), "message_id 不是可打印 ASCII");

    var outerKey2 = ReadVarint(payload, ref offset);
    Ensure(outerKey2 == ((30u << 3) | 2u), "TransportFrame Body 不是 ping");
    var ping = ReadLengthDelimited(payload, ref offset);
    Ensure(offset == payload.Length, "TransportFrame 含未预期字段");

    var pingOffset = 0;
    Ensure(ReadVarint(ping, ref pingOffset) == (1u << 3), "Ping 缺少 sequence");
    _ = ReadVarint(ping, ref pingOffset);
    Ensure(pingOffset == ping.Length, "Ping 含未预期字段");
}

static byte[] ReadLengthDelimited(byte[] data, ref int offset)
{
    var length = checked((int)ReadVarint(data, ref offset));
    Ensure(length >= 0 && offset <= data.Length - length, "Proto 长度越界");
    var value = data.AsSpan(offset, length).ToArray();
    offset += length;
    return value;
}

static ulong ReadVarint(byte[] data, ref int offset)
{
    ulong value = 0;
    for (var shift = 0; shift < 64; shift += 7)
    {
        Ensure(offset < data.Length, "Proto varint 被截断");
        var current = data[offset++];
        value |= (ulong)(current & 0x7f) << shift;
        if ((current & 0x80) == 0)
        {
            return value;
        }
    }
    throw new InvalidDataException("Proto varint 超过 10 字节");
}

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidDataException(message);
    }
}

enum FrameFailure
{
    None,
    CleanEof,
    ShortHeader,
    ShortPayload,
    InvalidLength,
}

sealed record ExactReadResult(bool Complete, int BytesRead, int ReadCalls);

sealed record FrameReadResult(FrameFailure Failure, byte[]? Payload, int ReadCalls);
