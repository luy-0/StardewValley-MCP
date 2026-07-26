using System.Buffers.Binary;
using Google.Protobuf;

namespace StardewValleyMcp.Protocol.V1;

public static class FrameCodec
{
    public const int MaxPayloadLength = 1_048_576;

    public static async Task<TransportFrame> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > MaxPayloadLength)
            throw new InvalidDataException($"非法帧长度: {length}");

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return TransportFrame.Parser.ParseFrom(payload);
    }

    public static async Task WriteAsync(
        Stream stream,
        TransportFrame frame,
        CancellationToken cancellationToken
    )
    {
        var payload = frame.ToByteArray();
        if (payload.Length is 0 or > MaxPayloadLength)
            throw new InvalidDataException($"非法帧长度: {payload.Length}");

        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("帧在完整读取前结束");
            offset += read;
        }
    }
}
