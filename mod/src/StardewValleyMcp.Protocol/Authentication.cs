using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace StardewValleyMcp.Protocol.V1;

public static class Authentication
{
    public static byte[] ComputeClientTag(
        byte[] secret,
        string modInstanceId,
        string clientInstanceId,
        byte[] serverNonce,
        byte[] clientNonce,
        ProtocolVersion requestedVersion,
        string resumeSessionId
    )
    {
        using var input = new MemoryStream();
        WriteLengthPrefixed(input, "stardew-valley-mcp/v1/client-auth");
        WriteLengthPrefixed(input, modInstanceId);
        WriteLengthPrefixed(input, clientInstanceId);
        WriteLengthPrefixed(input, serverNonce);
        WriteLengthPrefixed(input, clientNonce);
        WriteUInt32(input, requestedVersion.Major);
        WriteUInt32(input, requestedVersion.Minor);
        WriteLengthPrefixed(input, resumeSessionId);
        return HMACSHA256.HashData(secret, input.ToArray());
    }

    public static byte[] ComputeServerTag(
        byte[] secret,
        string modInstanceId,
        string clientInstanceId,
        byte[] serverNonce,
        byte[] clientNonce,
        ProtocolVersion selectedVersion,
        string sessionId,
        ulong leaseEpoch,
        string capabilityDigest,
        uint resultRetentionMs,
        uint reconnectGraceMs
    )
    {
        using var input = new MemoryStream();
        WriteLengthPrefixed(input, "stardew-valley-mcp/v1/server-auth");
        WriteLengthPrefixed(input, modInstanceId);
        WriteLengthPrefixed(input, clientInstanceId);
        WriteLengthPrefixed(input, serverNonce);
        WriteLengthPrefixed(input, clientNonce);
        WriteUInt32(input, selectedVersion.Major);
        WriteUInt32(input, selectedVersion.Minor);
        WriteLengthPrefixed(input, sessionId);
        WriteUInt64(input, leaseEpoch);
        WriteLengthPrefixed(input, capabilityDigest);
        WriteUInt32(input, resultRetentionMs);
        WriteUInt32(input, reconnectGraceMs);
        return HMACSHA256.HashData(secret, input.ToArray());
    }

    public static bool FixedTimeEquals(byte[] expected, Google.Protobuf.ByteString actual)
    {
        return CryptographicOperations.FixedTimeEquals(expected, actual.Span);
    }

    private static void WriteLengthPrefixed(Stream stream, string value)
    {
        WriteLengthPrefixed(stream, Encoding.UTF8.GetBytes(value));
    }

    private static void WriteLengthPrefixed(Stream stream, byte[] value)
    {
        WriteUInt32(stream, checked((uint)value.Length));
        stream.Write(value, 0, value.Length);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
