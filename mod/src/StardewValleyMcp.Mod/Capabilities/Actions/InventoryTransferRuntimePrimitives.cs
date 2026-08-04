using StardewValley;

namespace StardewValleyMcp.Mod;

/// <summary>把游戏物品列表包装为确定性转移计划使用的只读事实。</summary>
internal static class InventoryTransferRuntimeItemFactory
{
    public static IReadOnlyList<InventoryTransferItem?> Wrap(
        IReadOnlyList<Item?> source
    ) => source.Select(item => item is null ? null : new InventoryTransferItem(
        item,
        item.Stack,
        item.maximumStackSize(),
        item.IsRecipe || item.QualifiedItemId is "(O)326" or "(O)434",
        InventoryItemGuard.Create(item),
        other => other.Identity is Item candidate && item.canStackWith(candidate),
        quantity =>
        {
            var copy = item.getOne();
            copy.Stack = quantity;
            if (copy.Stack != quantity)
                throw new InvalidOperationException("物品副本数量无效");
            return copy;
        }
    )).ToArray();
}

/// <summary>提交确定性转移计划，并保留源与目标库存的局部回滚日志。</summary>
internal static class InventoryTransferRuntimeCommitter
{
    public static IInventoryTransferCommit Commit(
        IList<Item> source,
        IList<Item> target,
        InventoryTransferPlan plan
    )
    {
        if (plan.SourceSlot >= source.Count
            || source[plan.SourceSlot] is not { } sourceItem
            || !ReferenceEquals(sourceItem, plan.SourceIdentity))
            throw new InvalidOperationException("源物品已变化");

        var targetBackups = plan.Writes.Select(write => new TargetBackup(
            write.Slot,
            write.Slot < target.Count ? target[write.Slot] : null,
            write.Slot < target.Count ? target[write.Slot]?.Stack : null
        )).ToArray();
        var journal = new TransferCommit(
            source,
            target,
            plan.SourceSlot,
            sourceItem,
            sourceItem.Stack,
            target.Count,
            targetBackups
        );
        try
        {
            foreach (var write in plan.Writes)
            {
                if (write.ExistingIdentity is Item existing)
                {
                    if (write.Slot >= target.Count
                        || !ReferenceEquals(target[write.Slot], existing))
                        throw new InvalidOperationException("目标堆叠已变化");
                    existing.Stack = checked(existing.Stack + write.Quantity);
                    continue;
                }
                while (target.Count <= write.Slot)
                    target.Add(null!);
                if (target[write.Slot] is not null)
                    throw new InvalidOperationException("目标空槽已变化");
                if (write.NewIdentity is not Item copy
                    || ReferenceEquals(copy, sourceItem)
                    || copy.Stack != write.Quantity
                    || !string.Equals(
                        InventoryItemGuard.Create(copy),
                        InventoryItemGuard.Create(sourceItem),
                        StringComparison.Ordinal
                    ))
                    throw new InvalidOperationException("目标空槽副本无效");
                target[write.Slot] = copy;
            }

            if (plan.SourceRemaining == 0)
                source[plan.SourceSlot] = null!;
            else
                sourceItem.Stack = plan.SourceRemaining;
        }
        catch
        {
            journal.Rollback();
            throw;
        }
        return journal;
    }

    private sealed record TargetBackup(int Slot, Item? Item, int? Stack);

    private sealed class TransferCommit : IInventoryTransferCommit
    {
        private readonly IList<Item> _source;
        private readonly IList<Item> _target;
        private readonly int _sourceSlot;
        private readonly Item _sourceItem;
        private readonly int _sourceStack;
        private readonly int _targetCount;
        private readonly IReadOnlyList<TargetBackup> _targetBackups;
        private bool _finished;

        public TransferCommit(
            IList<Item> source,
            IList<Item> target,
            int sourceSlot,
            Item sourceItem,
            int sourceStack,
            int targetCount,
            IReadOnlyList<TargetBackup> targetBackups
        )
        {
            _source = source;
            _target = target;
            _sourceSlot = sourceSlot;
            _sourceItem = sourceItem;
            _sourceStack = sourceStack;
            _targetCount = targetCount;
            _targetBackups = targetBackups;
        }

        public void Complete() => _finished = true;

        public void Rollback()
        {
            if (_finished)
                return;
            while (_source.Count <= _sourceSlot)
                _source.Add(null!);
            _source[_sourceSlot] = _sourceItem;
            _sourceItem.Stack = _sourceStack;
            foreach (var backup in _targetBackups)
            {
                while (_target.Count <= backup.Slot)
                    _target.Add(null!);
                _target[backup.Slot] = backup.Item!;
                if (backup.Item is not null && backup.Stack.HasValue)
                    backup.Item.Stack = backup.Stack.Value;
            }
            while (_target.Count > _targetCount)
                _target.RemoveAt(_target.Count - 1);
            _finished = true;
        }
    }
}
