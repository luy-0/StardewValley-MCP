using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class InventoryProjector
{
    public static InventorySnapshot Project(
        ReadableInventoryView view,
        OpaqueRefStore refs,
        bool includeEmptySlots
    )
    {
        var captured = new CapturedInventorySlot[view.Capacity];
        for (var index = 0; index < view.Capacity; index++)
        {
            var item = view.Slots[index];
            captured[index] = new CapturedInventorySlot(
                item,
                item is null ? "" : InventoryItemGuard.Create(item)
            );
        }
        return ProjectCapturedSlots(
            view.RefOwner,
            view.ContainerKind,
            view.ContainerRef,
            captured,
            view.RevisionSelectedSlot,
            includeEmptySlots,
            refs,
            (target, reference) => ItemFactProjector.Project((StardewValley.Item)target, reference),
            view.RefObservationCapacity
        );
    }

    internal static InventorySnapshot ProjectCapturedSlots(
        IInventoryRefOwner owner,
        string containerKind,
        Ref? containerRef,
        IReadOnlyList<CapturedInventorySlot> captured,
        int selectedSlot,
        bool includeEmptySlots,
        OpaqueRefStore refs,
        Func<object, Ref, ItemFact> projectItem,
        int? refObservationCapacity = null
    )
    {
        var completeSlots = new List<InventorySlot>(captured.Count);

        for (var index = 0; index < captured.Count; index++)
        {
            var slot = new InventorySlot { Index = checked((uint)index) };
            var item = captured[index];
            if (item.Target is null)
            {
                refs.ObserveEmptyInventorySlot(owner, index);
            }
            else
            {
                var itemRef = refs.ObserveInventoryItem(
                    owner,
                    index,
                    item.Target,
                    item.Guard
                );
                slot.Item = projectItem(item.Target, itemRef);
            }
            completeSlots.Add(slot);
        }
        refs.CompleteInventoryObservation(owner, refObservationCapacity ?? captured.Count);
        return AssembleCompleteSnapshot(
            containerKind,
            containerRef,
            captured.Count,
            completeSlots,
            selectedSlot,
            includeEmptySlots
        );
    }

    internal static InventorySnapshot AssembleCompleteSnapshot(
        string containerKind,
        Ref? containerRef,
        int capacity,
        IReadOnlyList<InventorySlot> completeSlots,
        int selectedSlot,
        bool includeEmptySlots
    )
    {
        if (capacity < 0 || completeSlots.Count != capacity)
            throw new InvalidOperationException("完整库存 Slot 数与容量不一致");
        var snapshot = new InventorySnapshot
        {
            ContainerKind = containerKind,
            SlotCount = checked((uint)capacity),
        };
        if (containerRef is not null)
            snapshot.ContainerRef = containerRef.Clone();
        for (var index = 0; index < completeSlots.Count; index++)
        {
            if (completeSlots[index].Index != checked((uint)index))
                throw new InvalidOperationException("完整库存 Slot Index 不连续");
            snapshot.Slots.Add(completeSlots[index].Clone());
        }
        return InventorySnapshotContract.Finalize(snapshot, selectedSlot, includeEmptySlots);
    }
}

internal readonly record struct CapturedInventorySlot(object? Target, string Guard);

internal static class InventorySnapshotContract
{
    private static readonly byte[] RevisionDomain = Encoding.UTF8.GetBytes("stardew.inventory.v1\0");

    public static InventorySnapshot Finalize(
        InventorySnapshot completeSnapshot,
        int selectedSlot,
        bool includeEmptySlots
    )
    {
        var result = completeSnapshot.Clone();
        result.InventoryRevision = ComputeRevision(completeSnapshot, selectedSlot);
        if (!includeEmptySlots)
        {
            var populated = result.Slots.Where(slot => slot.Item is not null).ToArray();
            result.Slots.Clear();
            result.Slots.AddRange(populated);
        }
        return result;
    }

    public static string ComputeRevision(InventorySnapshot completeSnapshot, int selectedSlot)
    {
        var material = completeSnapshot.Clone();
        material.InventoryRevision = "";
        var factBytes = material.ToByteArray();
        var payload = new byte[RevisionDomain.Length + sizeof(int) + factBytes.Length];
        RevisionDomain.CopyTo(payload, 0);
        BinaryPrimitives.WriteInt32BigEndian(
            payload.AsSpan(RevisionDomain.Length, sizeof(int)),
            selectedSlot
        );
        factBytes.CopyTo(payload, RevisionDomain.Length + sizeof(int));
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
