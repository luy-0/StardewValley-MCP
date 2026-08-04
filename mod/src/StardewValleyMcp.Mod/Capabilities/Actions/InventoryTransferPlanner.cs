namespace StardewValleyMcp.Mod;

/// <summary>箱子物品单次转移的无副作用确定性计划器。</summary>
internal static class InventoryTransferPlanner
{
    public static InventoryTransferPlanResult Plan(
        int sourceSlot,
        IReadOnlyList<InventoryTransferItem?> source,
        IReadOnlyList<InventoryTransferItem?> target,
        int quantity
    )
    {
        if (sourceSlot < 0 || sourceSlot >= source.Count || source[sourceSlot] is not { } item)
            return InventoryTransferPlanResult.Stale("源物品已变化");
        if (item.Special)
            return InventoryTransferPlanResult.Invalid("当前物品具有特殊领取语义，不支持直接转移");
        if (quantity <= 0)
            return InventoryTransferPlanResult.Invalid("quantity 必须大于 0");
        if (quantity > item.Stack)
            return InventoryTransferPlanResult.OutOfRange("quantity 超过源物品数量");

        var remaining = quantity;
        var writes = new List<InventoryTransferWrite>();
        for (var slot = 0; slot < target.Count && remaining > 0; slot++)
        {
            var existing = target[slot];
            if (existing is null || !existing.CanStackWith(item))
                continue;
            var available = Math.Max(0, existing.MaximumStack - existing.Stack);
            var added = Math.Min(available, remaining);
            if (added <= 0)
                continue;
            writes.Add(new InventoryTransferWrite(slot, added, existing.Identity, null));
            remaining -= added;
        }
        for (var slot = 0; slot < target.Count && remaining > 0; slot++)
        {
            if (target[slot] is not null)
                continue;
            var added = Math.Min(item.MaximumStack, remaining);
            if (added <= 0)
                return InventoryTransferPlanResult.Invalid("源物品堆叠上限无效");
            object clone;
            try
            {
                clone = item.CloneForTransfer(added);
            }
            catch
            {
                return InventoryTransferPlanResult.Unavailable("源物品无法安全复制");
            }
            writes.Add(new InventoryTransferWrite(slot, added, null, clone));
            remaining -= added;
        }
        if (remaining != 0)
            return InventoryTransferPlanResult.NotReady("目标库存容量不足；请减少数量或整理目标库存后重新查询");

        return InventoryTransferPlanResult.Success(new InventoryTransferPlan(
            sourceSlot,
            quantity,
            item.Stack - quantity,
            item.Identity,
            writes
        ));
    }

    public static bool SamePlan(InventoryTransferPlan left, InventoryTransferPlan right) =>
        left.SourceSlot == right.SourceSlot
        && left.Quantity == right.Quantity
        && left.SourceRemaining == right.SourceRemaining
        && ReferenceEquals(left.SourceIdentity, right.SourceIdentity)
        && left.Writes.Count == right.Writes.Count
        && left.Writes.Zip(right.Writes).All(pair =>
            pair.First.Slot == pair.Second.Slot
            && pair.First.Quantity == pair.Second.Quantity
            && ReferenceEquals(pair.First.ExistingIdentity, pair.Second.ExistingIdentity));
}

internal sealed record InventoryTransferItem(
    object Identity,
    int Stack,
    int MaximumStack,
    bool Special,
    string Guard,
    Func<InventoryTransferItem, bool> StackCompatibility,
    Func<int, object> CloneFactory
)
{
    public bool CanStackWith(InventoryTransferItem other) => StackCompatibility(other);
    public object CloneForTransfer(int quantity) => CloneFactory(quantity);
}

internal sealed record InventoryTransferWrite(
    int Slot,
    int Quantity,
    object? ExistingIdentity,
    object? NewIdentity
);

internal sealed record InventoryTransferPlan(
    int SourceSlot,
    int Quantity,
    int SourceRemaining,
    object SourceIdentity,
    IReadOnlyList<InventoryTransferWrite> Writes
);

internal enum InventoryTransferPlanStatus
{
    Success,
    Invalid,
    Stale,
    OutOfRange,
    NotReady,
    Unavailable,
}

internal sealed record InventoryTransferPlanResult(
    InventoryTransferPlanStatus Status,
    InventoryTransferPlan? Value,
    string Message
)
{
    public static InventoryTransferPlanResult Success(InventoryTransferPlan value) =>
        new(InventoryTransferPlanStatus.Success, value, "");
    public static InventoryTransferPlanResult Invalid(string message) =>
        new(InventoryTransferPlanStatus.Invalid, null, message);
    public static InventoryTransferPlanResult Stale(string message) =>
        new(InventoryTransferPlanStatus.Stale, null, message);
    public static InventoryTransferPlanResult OutOfRange(string message) =>
        new(InventoryTransferPlanStatus.OutOfRange, null, message);
    public static InventoryTransferPlanResult NotReady(string message) =>
        new(InventoryTransferPlanStatus.NotReady, null, message);
    public static InventoryTransferPlanResult Unavailable(string message) =>
        new(InventoryTransferPlanStatus.Unavailable, null, message);
}
