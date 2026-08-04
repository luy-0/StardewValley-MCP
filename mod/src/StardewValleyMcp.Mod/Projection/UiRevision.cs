using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class UiRevision
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("stardew.ui.v1\0");

    public static string Finalize(
        UiSnapshot snapshot,
        string menuMarker,
        UiExtractorKind extractor,
        string actionState
    )
    {
        CanonicalizeElements(snapshot);
        var material = CanonicalMaterial(snapshot, menuMarker, extractor, actionState);
        snapshot.UiRevision = Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant();
        return snapshot.UiRevision;
    }

    internal static byte[] CanonicalMaterial(
        UiSnapshot snapshot,
        string menuMarker,
        UiExtractorKind extractor,
        string actionState
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrEmpty(menuMarker))
            throw new InvalidOperationException("UI revision menu marker 不能为空");
        var canonical = snapshot.Clone();
        canonical.UiRevision = "";
        CanonicalizeElements(canonical);

        using var stream = new MemoryStream();
        stream.Write(Domain);
        WriteText(stream, menuMarker);
        WriteText(stream, extractor.ToString());
        WriteText(stream, actionState ?? "");
        var proto = canonical.ToByteArray();
        WriteLength(stream, proto.Length);
        stream.Write(proto);
        return stream.ToArray();
    }

    internal static void CanonicalizeElements(UiSnapshot snapshot)
    {
        var ordered = snapshot.Elements
            .OrderBy(element => element.Kind)
            .ThenBy(element => element.HasInventorySide
                ? element.InventorySide
                : UiInventorySide.Unspecified)
            .ThenBy(element => element.Index)
            .ThenBy(element => element.Ref?.Value ?? "", StringComparer.Ordinal)
            .Select(element => element.Clone())
            .ToArray();
        snapshot.Elements.Clear();
        snapshot.Elements.AddRange(ordered);
        var inventories = snapshot.Inventories
            .OrderBy(link => link.Side)
            .Select(link => link.Clone())
            .ToArray();
        snapshot.Inventories.Clear();
        snapshot.Inventories.AddRange(inventories);
    }

    private static void WriteText(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteLength(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteLength(Stream stream, int length)
    {
        Span<byte> encoded = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(encoded, length);
        stream.Write(encoded);
    }
}
