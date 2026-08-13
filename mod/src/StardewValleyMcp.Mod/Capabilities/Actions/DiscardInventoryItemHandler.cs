using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class DiscardInventoryItemHandler : ILongRunningCapabilityHandler
{
    private readonly OpaqueRefStore _refs;
    private readonly IDiscardInventoryItemRuntimeAdapter _runtime;

    public DiscardInventoryItemHandler(OpaqueRefStore refs)
        : this(refs, new LiveDiscardInventoryItemRuntimeAdapter(refs)) { }

    internal DiscardInventoryItemHandler(
        OpaqueRefStore refs,
        IDiscardInventoryItemRuntimeAdapter runtime
    )
    {
        _refs = refs;
        _runtime = runtime;
    }

    public string Id => "discard_inventory_item";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.DiscardInventoryItem;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("discard_inventory_item 请求类型无效");
        var value = request.DiscardInventoryItem;
        if (!PublicStringPolicy.IsNonEmptyValid(value.ItemRef?.Value))
            return Invalid("item_ref 格式无效");
        if (value.Quantity == 0 || value.Quantity > int.MaxValue)
            return Invalid("quantity 必须在 1..2147483647 范围内");
        if (!IsRevision(value.PlayerInventoryRevision))
            return Invalid("玩家 Inventory Revision 格式无效");
        return null;
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new Continuation(_refs, _runtime, request.DiscardInventoryItem.Clone());

    private static Error Invalid(string message) =>
        new() { Code = ErrorCode.InvalidArgument, Message = message };

    private static bool IsRevision(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed class Continuation : ICommandContinuation
    {
        private readonly OpaqueRefStore _refs;
        private readonly IDiscardInventoryItemRuntimeAdapter _runtime;
        private readonly DiscardInventoryItemRequest _request;
        private PreparedDiscard? _prepared;
        private bool _committing;

        public Continuation(
            OpaqueRefStore refs,
            IDiscardInventoryItemRuntimeAdapter runtime,
            DiscardInventoryItemRequest request
        )
        {
            _refs = refs;
            _runtime = runtime;
            _request = request;
        }

        public string Phase => _prepared is null
            ? "validating_inventory"
            : "committing_discard";

        public uint? ProgressPercent => _prepared is null ? 0u : 50u;
        public bool CanCancel => !_committing;

        public ContinuationStep Tick(ContinuationStopSignal signal)
        {
            if (signal != ContinuationStopSignal.None)
                return new ContinuationStep.Stopped();
            if (_prepared is null)
            {
                var preparation = PrepareFirstTick();
                if (preparation.Error is not null)
                    return new ContinuationStep.Failed(preparation.Error);
                _prepared = preparation.Value!;
                return new ContinuationStep.Pending();
            }

            var captured = Capture();
            if (captured.Error is not null)
                return new ContinuationStep.Failed(captured.Error);
            var current = captured.Value!;
            var source = ResolveSource(current);
            if (source.Error is not null)
                return new ContinuationStep.Failed(source.Error);
            if (!SamePreparedContext(current, source, _prepared))
                return Failed(ErrorCode.StaleRef, "玩家背包、物品、选中槽或垃圾桶状态已变化");

            DiscardInventoryPlan plan;
            try
            {
                if (!_runtime.CanBeTrashed(current, source.Slot))
                    return Failed(ErrorCode.ItemNotDiscardable, "游戏当前不允许丢弃该物品");
                plan = _runtime.PrepareCommit(
                    current,
                    source.Slot,
                    checked((int)_request.Quantity)
                );
            }
            catch (ItemNotDiscardableException)
            {
                return Failed(ErrorCode.ItemNotDiscardable, "游戏当前不允许丢弃该物品");
            }
            catch
            {
                return Failed(ErrorCode.ExecutionFailed, "丢弃提交准备失败");
            }

            _committing = true;
            try
            {
                _runtime.Commit(current, plan);
            }
            catch (DiscardInventoryBeforeTrashException error)
            {
                if (!error.RollbackConfirmed)
                    return Unknown("物品拆出后背包恢复无法确认；请重新查询背包与金钱，且不要自动重试");
                return Failed(
                    ErrorCode.ExecutionFailed,
                    "物品在进入游戏垃圾桶前拆出失败，背包已恢复"
                );
            }
            catch (DiscardInventoryOutcomeUnknownException)
            {
                return Unknown("游戏垃圾桶调用已经开始，但结果无法确认；请重新查询背包与金钱，且不要自动重试");
            }
            catch
            {
                return Unknown("提交阶段发生未分类异常，无法证明是否进入原生垃圾桶；请重新查询背包与金钱，且不要自动重试");
            }

            var afterResult = Capture();
            if (afterResult.Error is not null
                || !PostconditionsHold(current, afterResult.Value!, plan))
                return Unknown("原生丢弃已执行，但后置事实无法确认；请重新查询背包与金钱，且不要自动重试");
            var after = afterResult.Value!;
            return new ContinuationStep.Succeeded(new CapabilityResult
            {
                DiscardInventoryItem = new DiscardInventoryItemResult
                {
                    RequestedQuantity = _request.Quantity,
                    DiscardedQuantity = _request.Quantity,
                    SourceSlotIndex = checked((uint)plan.SourceSlot),
                    SourceRemainingQuantity = checked((uint)plan.ExpectedRemaining),
                    PlayerInventoryRevision = after.PlayerSnapshot!.InventoryRevision,
                    MoneyBefore = checked((uint)plan.MoneyBefore),
                    MoneyAfter = checked((uint)after.Money),
                    MoneyRefunded = checked((uint)(after.Money - plan.MoneyBefore)),
                },
            });
        }

        private PreparationResult PrepareFirstTick()
        {
            var capture = Capture();
            if (capture.Error is not null)
                return new PreparationResult(null, capture.Error);
            var current = capture.Value!;
            if (!string.Equals(
                current.PlayerSnapshot!.InventoryRevision,
                _request.PlayerInventoryRevision,
                StringComparison.Ordinal
            ))
                return PreparationResult.Failure(Error(ErrorCode.StaleRef, "玩家 Inventory Revision 已失效"));
            var source = ResolveSource(current);
            if (source.Error is not null)
                return PreparationResult.Failure(source.Error);
            if (_request.Quantity > int.MaxValue
                || _request.Quantity > source.Item!.Stack)
                return PreparationResult.Failure(Error(ErrorCode.OutOfRange, "quantity 超过物品当前堆叠"));
            try
            {
                if (!_runtime.CanBeTrashed(current, source.Slot))
                    return PreparationResult.Failure(Error(ErrorCode.ItemNotDiscardable, "游戏当前不允许丢弃该物品"));
            }
            catch
            {
                return PreparationResult.Failure(Error(ErrorCode.Internal, "游戏原生可丢弃性当前不可读"));
            }
            return new PreparationResult(new PreparedDiscard(
                current.MenuIdentity,
                current.PageIdentity,
                current.PlayerIdentity!,
                current.BackingIdentity!,
                source.Slot,
                source.Item.Identity,
                source.Item.Stack,
                current.CurrentToolIndex,
                current.CurrentItemIdentity,
                current.Money,
                current.TrashCanLevel,
                current.SpecialItems.ToArray(),
                current.PlayerSnapshot.InventoryRevision
            ), null);
        }

        private SourceResult ResolveSource(DiscardInventoryCapture capture)
        {
            var resolved = _refs.ResolveInventoryItem(_request.ItemRef);
            if (resolved.Status != InventoryItemResolveStatus.Resolved
                || resolved.Target is null)
            {
                var error = resolved.Status switch
                {
                    InventoryItemResolveStatus.Stale => Error(ErrorCode.StaleRef, "item_ref 已失效"),
                    InventoryItemResolveStatus.NotFound => Error(ErrorCode.NotFound, "item_ref 不存在"),
                    InventoryItemResolveStatus.Unsupported => Error(ErrorCode.InvalidArgument, "item_ref 类型无效"),
                    InventoryItemResolveStatus.Unavailable => Error(ErrorCode.Internal, "item_ref 当前不可解析"),
                    _ => Error(ErrorCode.Internal, "item_ref 无法解析"),
                };
                return new SourceResult(-1, null, error);
            }
            if (resolved.Target.Provenance != InventoryItemProvenance.Player)
                return new SourceResult(-1, null, Error(ErrorCode.InvalidArgument, "item_ref 不属于玩家背包"));
            var slot = resolved.Target.Slot;
            if (slot < 0
                || slot >= capture.Backpack.Count
                || capture.Backpack[slot] is not { } item
                || !ReferenceEquals(item.Identity, resolved.Target.Target))
                return new SourceResult(-1, null, Error(ErrorCode.StaleRef, "item_ref 已失效"));
            return new SourceResult(slot, item, null);
        }

        private CaptureResult Capture()
        {
            var capture = _runtime.Capture();
            if (capture.Status != DiscardInventoryCaptureStatus.Ready)
            {
                var error = capture.Status == DiscardInventoryCaptureStatus.NotReady
                    ? Error(ErrorCode.NotReady, "当前玩家状态不允许修改背包")
                    : Error(ErrorCode.Internal, "当前玩家背包事实不可读");
                return new CaptureResult(null, error);
            }
            if (capture.PlayerIdentity is null
                || capture.BackingIdentity is null
                || capture.PlayerSnapshot is null
                || capture.CommitState is null
                || capture.PlayerSnapshot.SlotCount != capture.Backpack.Count
                || capture.PlayerSnapshot.Slots.Count != capture.Backpack.Count
                || capture.CurrentToolIndex < 0
                || capture.CurrentToolIndex >= capture.Backpack.Count
                || capture.Money < 0
                || capture.TrashCanLevel < 0)
                return new CaptureResult(null, Error(ErrorCode.Internal, "玩家背包捕获无效"));
            return new CaptureResult(capture, null);
        }

        private static bool SamePreparedContext(
            DiscardInventoryCapture capture,
            SourceResult source,
            PreparedDiscard prepared
        ) => ReferenceEquals(capture.MenuIdentity, prepared.MenuIdentity)
            && ReferenceEquals(capture.PageIdentity, prepared.PageIdentity)
            && ReferenceEquals(capture.PlayerIdentity, prepared.PlayerIdentity)
            && ReferenceEquals(capture.BackingIdentity, prepared.BackingIdentity)
            && source.Slot == prepared.SourceSlot
            && ReferenceEquals(source.Item!.Identity, prepared.SourceIdentity)
            && source.Item.Stack == prepared.SourceStack
            && capture.CurrentToolIndex == prepared.CurrentToolIndex
            && ReferenceEquals(capture.CurrentItemIdentity, prepared.CurrentItemIdentity)
            && capture.Money == prepared.Money
            && capture.TrashCanLevel == prepared.TrashCanLevel
            && capture.SpecialItems.SequenceEqual(prepared.SpecialItems, StringComparer.Ordinal)
            && string.Equals(
                capture.PlayerSnapshot!.InventoryRevision,
                prepared.InventoryRevision,
                StringComparison.Ordinal
            );

        private static bool PostconditionsHold(
            DiscardInventoryCapture before,
            DiscardInventoryCapture after,
            DiscardInventoryPlan plan
        )
        {
            if (!ReferenceEquals(after.MenuIdentity, before.MenuIdentity)
                || !ReferenceEquals(after.PageIdentity, before.PageIdentity)
                || !ReferenceEquals(after.PlayerIdentity, before.PlayerIdentity)
                || !ReferenceEquals(after.BackingIdentity, before.BackingIdentity)
                || after.Backpack.Count != before.Backpack.Count
                || after.CurrentToolIndex != plan.CurrentToolIndex
                || after.Money != plan.ExpectedMoneyAfter
                || !after.SpecialItems.SequenceEqual(
                    plan.ExpectedSpecialItemsAfter,
                    StringComparer.Ordinal
                ))
                return false;

            var expectedCurrent = plan.CurrentToolIndex == plan.SourceSlot
                && plan.ExpectedRemaining == 0
                    ? null
                    : plan.CurrentItemIdentity;
            if (!ReferenceEquals(after.CurrentItemIdentity, expectedCurrent))
                return false;
            for (var index = 0; index < before.Backpack.Count; index++)
            {
                var previous = before.Backpack[index];
                var current = after.Backpack[index];
                var previousSlot = before.PlayerSnapshot!.Slots[index];
                var currentSlot = after.PlayerSnapshot!.Slots[index];
                if (index == plan.SourceSlot)
                {
                    if (plan.ExpectedRemaining == 0)
                    {
                        if (current is not null || currentSlot.Item is not null)
                            return false;
                    }
                    else if (current is null
                        || !ReferenceEquals(current.Identity, plan.SourceIdentity)
                        || current.Stack != plan.ExpectedRemaining
                        || !string.Equals(current.Guard, previous!.Guard, StringComparison.Ordinal)
                        || previousSlot.Item is null
                        || currentSlot.Item is null
                        || !SourceFactMatches(
                            previousSlot.Item,
                            currentSlot.Item,
                            plan.ExpectedRemaining
                        ))
                    {
                        return false;
                    }
                    continue;
                }
                if (!SameItem(previous, current)
                    || !Equals(previousSlot.Item, currentSlot.Item))
                    return false;
            }
            return !string.Equals(
                after.PlayerSnapshot!.InventoryRevision,
                before.PlayerSnapshot!.InventoryRevision,
                StringComparison.Ordinal
            );
        }

        private static bool SameItem(
            DiscardInventoryItemRuntimeFact? left,
            DiscardInventoryItemRuntimeFact? right
        ) => left is null
            ? right is null
            : right is not null
                && ReferenceEquals(left.Identity, right.Identity)
                && left.Stack == right.Stack
                && string.Equals(left.Guard, right.Guard, StringComparison.Ordinal);

        private static bool SourceFactMatches(
            ItemFact before,
            ItemFact after,
            int remaining
        )
        {
            var expected = before.Clone();
            expected.Stack = checked((uint)remaining);
            // Ref 仍绑定同一 Slot 和对象，投影应保持稳定；完整 protobuf 相等同时
            // 保护 Quality、Tool 状态及未来新增字段不被原生回调意外改变。
            return Equals(expected, after);
        }

        private static ContinuationStep.Failed Failed(ErrorCode code, string message) =>
            new(Error(code, message));

        private static ContinuationStep.Failed Unknown(string message) =>
            Failed(ErrorCode.CommitOutcomeUnknown, message);

        private static Error Error(ErrorCode code, string message) =>
            new() { Code = code, Message = message };

        private sealed record PreparedDiscard(
            object? MenuIdentity,
            object? PageIdentity,
            object PlayerIdentity,
            object BackingIdentity,
            int SourceSlot,
            object SourceIdentity,
            int SourceStack,
            int CurrentToolIndex,
            object? CurrentItemIdentity,
            int Money,
            int TrashCanLevel,
            IReadOnlyList<string> SpecialItems,
            string InventoryRevision
        );

        private sealed record PreparationResult(PreparedDiscard? Value, Error? Error)
        {
            public static PreparationResult Failure(Error error) => new(null, error);
        }

        private sealed record CaptureResult(DiscardInventoryCapture? Value, Error? Error);
        private sealed record SourceResult(
            int Slot,
            DiscardInventoryItemRuntimeFact? Item,
            Error? Error
        );
    }
}
