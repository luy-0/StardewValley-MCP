using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace StardewValleyMcp.Protocol.V1;

public static class CapabilityCatalog
{
    private static readonly IReadOnlyDictionary<string, CapabilityDescriptor> ObservationDescriptors =
        new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal)
        {
            ["inspect"] = ReadOnlyImmediate("inspect", nameof(InspectRequest), nameof(InspectResult), 5_000, 15_000),
            ["query_inventory"] = ReadOnlyImmediate("query_inventory", nameof(QueryInventoryRequest), nameof(QueryInventoryResult), 5_000, 15_000),
            ["query_runtime"] = ReadOnlyImmediate("query_runtime", nameof(QueryRuntimeRequest), nameof(QueryRuntimeResult), 5_000, 15_000),
            ["query_ui"] = ReadOnlyImmediate("query_ui", nameof(QueryUiRequest), nameof(QueryUiResult), 5_000, 15_000),
            ["query_world"] = ReadOnlyImmediate("query_world", nameof(QueryWorldRequest), nameof(QueryWorldResult), 10_000, 30_000),
        };

    public static CapabilitySnapshot CreateObservationSnapshot()
    {
        return CreateSnapshotFor(ObservationDescriptors.Keys);
    }

    public static CapabilityDescriptor GetObservationDescriptor(string id)
    {
        if (!ObservationDescriptors.TryGetValue(id, out var descriptor))
            throw new ArgumentOutOfRangeException(nameof(id), id, "未定义的观察能力");
        return descriptor.Clone();
    }

    public static CapabilitySnapshot CreateSnapshotFor(IEnumerable<string> registeredIds)
    {
        var descriptors = registeredIds.Select(id =>
        {
            return GetObservationDescriptor(id);
        });
        return CreateSnapshot(descriptors);
    }

    private static CapabilityDescriptor ReadOnlyImmediate(string id, string requestType, string resultType, uint defaultTimeoutMs, uint maxTimeoutMs)
    {
        return new CapabilityDescriptor
        {
            Id = id,
            ContractVersion = "1.0.0",
            SideEffect = SideEffect.ReadOnly,
            Execution = ExecutionMode.Immediate,
            Cancellable = false,
            DefaultTimeoutMs = defaultTimeoutMs,
            MaxTimeoutMs = maxTimeoutMs,
            RequestType = requestType,
            ResultType = resultType,
            RequiredScope = "game:read",
            Destructive = false,
        };
    }

    public static CapabilitySnapshot CreateSnapshot(IEnumerable<CapabilityDescriptor> descriptors)
    {
        var snapshot = new CapabilitySnapshot();
        snapshot.Capabilities.Add(descriptors.OrderBy(item => item.Id, StringComparer.Ordinal));
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
