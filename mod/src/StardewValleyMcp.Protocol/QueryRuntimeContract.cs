using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace StardewValleyMcp.Protocol.V1;

public static class QueryRuntimeContract
{
    public const uint DefaultTimeoutMs = 5_000;
    public const uint MaxTimeoutMs = 15_000;

    public static CapabilitySnapshot CreateSnapshot()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "query_runtime",
            ContractVersion = "1.0.0",
            SideEffect = SideEffect.ReadOnly,
            Execution = ExecutionMode.Immediate,
            Cancellable = false,
            DefaultTimeoutMs = DefaultTimeoutMs,
            MaxTimeoutMs = MaxTimeoutMs,
            RequestType = nameof(QueryRuntimeRequest),
            ResultType = nameof(QueryRuntimeResult),
            RequiredScope = "game:read",
            Destructive = false,
        };
        var snapshot = new CapabilitySnapshot();
        snapshot.Capabilities.Add(descriptor);
        snapshot.Digest = ComputeDigest(snapshot.Capabilities);
        return snapshot;
    }

    public static string ComputeDigest(IEnumerable<CapabilityDescriptor> descriptors)
    {
        using var input = new MemoryStream();
        foreach (var descriptor in descriptors.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            WriteLengthPrefixed(input, descriptor.Id);
            WriteLengthPrefixed(input, descriptor.ContractVersion);
            input.WriteByte(checked((byte)descriptor.SideEffect));
            input.WriteByte(checked((byte)descriptor.Execution));
            input.WriteByte(descriptor.Cancellable ? (byte)1 : (byte)0);
            WriteUInt32(input, descriptor.DefaultTimeoutMs);
            WriteUInt32(input, descriptor.MaxTimeoutMs);
            WriteLengthPrefixed(input, descriptor.RequestType);
            WriteLengthPrefixed(input, descriptor.ResultType);
            WriteLengthPrefixed(input, descriptor.RequiredScope);
            var risks = descriptor.Risks.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            WriteUInt32(input, checked((uint)risks.Length));
            foreach (var risk in risks)
                WriteLengthPrefixed(input, risk);
            input.WriteByte(descriptor.Destructive ? (byte)1 : (byte)0);
        }

        return Convert.ToHexString(SHA256.HashData(input.ToArray())).ToLowerInvariant();
    }

    private static void WriteLengthPrefixed(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
