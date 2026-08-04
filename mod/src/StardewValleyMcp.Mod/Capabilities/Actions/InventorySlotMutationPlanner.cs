namespace StardewValleyMcp.Mod;

internal enum InventorySlotMutationKind
{
    Move,
    Swap,
    NoChange,
}

internal enum InventorySlotMutationPlanStatus
{
    Ready,
    Invalid,
    Stale,
    Unavailable,
}

internal sealed record InventorySlotMutationPlan(
    InventorySlotMutationKind Kind,
    int SourceSlot,
    object SourceIdentity,
    int DestinationSlot,
    object? DestinationIdentity
)
{
    public bool Changed => Kind != InventorySlotMutationKind.NoChange;
}

internal sealed record InventorySlotMutationPlanResult(
    InventorySlotMutationPlanStatus Status,
    InventorySlotMutationPlan? Plan,
    string Message
);

/// <summary>只规划同一玩家已解锁背包中的整件移动或交换。</summary>
internal static class InventorySlotMutationPlanner
{
    public static InventorySlotMutationPlanResult Plan(
        int sourceSlot,
        object? sourceIdentity,
        int destinationSlot,
        object? destinationIdentity,
        IReadOnlyList<object?> backpack
    )
    {
        if (sourceIdentity is null)
            return Invalid("源背包物品不能为空");
        if (sourceSlot < 0 || sourceSlot >= backpack.Count
            || destinationSlot < 0 || destinationSlot >= backpack.Count)
            return Invalid("源或目标 Slot 超出已解锁背包范围");
        if (!ReferenceEquals(backpack[sourceSlot], sourceIdentity))
            return Stale("源背包物品已变化");
        if (!ReferenceEquals(backpack[destinationSlot], destinationIdentity))
            return Stale("目标背包 Slot 已变化");
        if (sourceSlot == destinationSlot)
        {
            if (!ReferenceEquals(sourceIdentity, destinationIdentity))
                return Stale("同一背包 Slot 的对象身份不一致");
            return Ready(new InventorySlotMutationPlan(
                InventorySlotMutationKind.NoChange,
                sourceSlot,
                sourceIdentity,
                destinationSlot,
                destinationIdentity
            ));
        }
        if (destinationIdentity is not null
            && ReferenceEquals(sourceIdentity, destinationIdentity))
        {
            return new InventorySlotMutationPlanResult(
                InventorySlotMutationPlanStatus.Unavailable,
                null,
                "同一物品对象重复出现在多个背包 Slot"
            );
        }
        return Ready(new InventorySlotMutationPlan(
            destinationIdentity is null
                ? InventorySlotMutationKind.Move
                : InventorySlotMutationKind.Swap,
            sourceSlot,
            sourceIdentity,
            destinationSlot,
            destinationIdentity
        ));
    }

    public static bool SamePlan(
        InventorySlotMutationPlan left,
        InventorySlotMutationPlan right
    ) => left.Kind == right.Kind
        && left.SourceSlot == right.SourceSlot
        && ReferenceEquals(left.SourceIdentity, right.SourceIdentity)
        && left.DestinationSlot == right.DestinationSlot
        && ReferenceEquals(left.DestinationIdentity, right.DestinationIdentity);

    private static InventorySlotMutationPlanResult Ready(
        InventorySlotMutationPlan plan
    ) => new(InventorySlotMutationPlanStatus.Ready, plan, "");
    private static InventorySlotMutationPlanResult Invalid(string message) =>
        new(InventorySlotMutationPlanStatus.Invalid, null, message);
    private static InventorySlotMutationPlanResult Stale(string message) =>
        new(InventorySlotMutationPlanStatus.Stale, null, message);
}

internal enum InventorySlotMutationPoint
{
    SourceCleared,
    DestinationCleared,
    DestinationWrittenToSource,
    SourceWrittenToDestination,
}

internal interface IInventorySlotMutationBackend
{
    object? ReadSlot(int slot);
    void WriteSlot(int slot, object? item);
}

internal interface IInventorySlotMutationCommit
{
    void Complete();
    void Rollback();
}

/// <summary>按无双 Parent 顺序提交两个背包 Slot，并保留两槽局部回滚日志。</summary>
internal static class InventorySlotMutationExecutor
{
    public static IInventorySlotMutationCommit Commit(
        IInventorySlotMutationBackend backend,
        InventorySlotMutationPlan plan,
        Action<InventorySlotMutationPoint>? afterMutation = null
    )
    {
        var journal = new InventorySlotMutationJournal(backend, plan);
        if (!plan.Changed)
            return journal;
        try
        {
            if (!ReferenceEquals(backend.ReadSlot(plan.SourceSlot), plan.SourceIdentity)
                || !ReferenceEquals(
                    backend.ReadSlot(plan.DestinationSlot),
                    plan.DestinationIdentity
                ))
                throw new InvalidOperationException("背包源或目标 Slot 已变化");

            backend.WriteSlot(plan.SourceSlot, null);
            afterMutation?.Invoke(InventorySlotMutationPoint.SourceCleared);

            if (plan.DestinationIdentity is not null)
            {
                backend.WriteSlot(plan.DestinationSlot, null);
                afterMutation?.Invoke(InventorySlotMutationPoint.DestinationCleared);
                backend.WriteSlot(plan.SourceSlot, plan.DestinationIdentity);
                afterMutation?.Invoke(InventorySlotMutationPoint.DestinationWrittenToSource);
            }

            backend.WriteSlot(plan.DestinationSlot, plan.SourceIdentity);
            afterMutation?.Invoke(InventorySlotMutationPoint.SourceWrittenToDestination);
            return journal;
        }
        catch (Exception mutationError)
        {
            try
            {
                journal.Rollback();
            }
            catch (Exception rollbackError)
            {
                throw new InventorySlotMutationRollbackException(
                    mutationError,
                    rollbackError
                );
            }
            throw;
        }
    }

    private sealed class InventorySlotMutationJournal : IInventorySlotMutationCommit
    {
        private readonly IInventorySlotMutationBackend _backend;
        private readonly InventorySlotMutationPlan _plan;
        private bool _finished;

        public InventorySlotMutationJournal(
            IInventorySlotMutationBackend backend,
            InventorySlotMutationPlan plan
        )
        {
            _backend = backend;
            _plan = plan;
        }

        public void Complete() => _finished = true;

        public void Rollback()
        {
            if (_finished)
                return;
            if (!_plan.Changed)
            {
                _finished = true;
                return;
            }
            Exception? firstError = null;
            var sourceSafe = DetachIdentity(_plan.SourceIdentity, ref firstError);
            var destinationSafe = _plan.DestinationIdentity is null
                || DetachIdentity(_plan.DestinationIdentity, ref firstError);

            if (sourceSafe)
                TryRestore(_plan.SourceSlot, _plan.SourceIdentity, ref firstError);
            if (destinationSafe)
                TryRestore(
                    _plan.DestinationSlot,
                    _plan.DestinationIdentity,
                    ref firstError
                );

            _finished = true;
            if (firstError is not null)
                throw firstError;
        }

        private bool DetachIdentity(object identity, ref Exception? firstError)
        {
            var safe = true;
            foreach (var slot in new[] { _plan.SourceSlot, _plan.DestinationSlot }.Distinct())
            {
                try
                {
                    if (ReferenceEquals(_backend.ReadSlot(slot), identity))
                        _backend.WriteSlot(slot, null);
                }
                catch (Exception error)
                {
                    firstError ??= error;
                    safe = false;
                }
            }
            return safe;
        }

        private void TryRestore(int slot, object? value, ref Exception? firstError)
        {
            try
            {
                var current = _backend.ReadSlot(slot);
                if (ReferenceEquals(current, value))
                    return;
                if (current is not null)
                {
                    firstError ??= new InvalidOperationException(
                        $"回滚目标 Slot {slot} 已被未知对象占用"
                    );
                    return;
                }
                _backend.WriteSlot(slot, value);
            }
            catch (Exception error)
            {
                firstError ??= error;
            }
        }
    }
}

internal sealed class InventorySlotMutationRollbackException : Exception
{
    public InventorySlotMutationRollbackException(
        Exception mutationError,
        Exception rollbackError
    ) : base(
        "背包 Slot 写入失败，且局部回滚存在错误",
        new AggregateException(mutationError, rollbackError)
    ) { }
}
