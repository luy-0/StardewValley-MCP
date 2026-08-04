using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class TransferInventoryItemHandler : ILongRunningCapabilityHandler
{
    private readonly OpaqueRefStore _refs;
    private readonly IInventoryTransferAdapter _inventory;

    public TransferInventoryItemHandler(OpaqueRefStore refs)
        : this(refs, new LiveInventoryTransferAdapter(refs)) { }

    internal TransferInventoryItemHandler(OpaqueRefStore refs, IInventoryTransferAdapter inventory)
    {
        _refs = refs;
        _inventory = inventory;
    }

    public string Id => "transfer_inventory_item";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.TransferInventoryItem;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("transfer_inventory_item 请求类型无效");
        var transfer = request.TransferInventoryItem;
        if (!Enum.IsDefined(transfer.Direction)
            || transfer.Direction == InventoryTransferDirection.Unspecified)
            return Invalid("direction 必须是受支持的非空枚举");
        if (!PublicStringPolicy.IsNonEmptyValid(transfer.ItemRef?.Value))
            return Invalid("item_ref 格式无效");
        if (transfer.Quantity is 0 or > int.MaxValue)
            return Invalid("quantity 必须在 1..2147483647 之间");
        if (!IsRevision(transfer.UiRevision)
            || !IsRevision(transfer.PlayerInventoryRevision)
            || !IsRevision(transfer.ContainerInventoryRevision))
            return Invalid("UI 与双方 Inventory Revision 格式无效");
        return null;
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new InventoryTransferContinuation(_refs, _inventory, request.TransferInventoryItem);

    private static Error Invalid(string message) =>
        new() { Code = ErrorCode.InvalidArgument, Message = message };
    private static bool IsRevision(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal sealed class InventoryTransferContinuation : ICommandContinuation
{
    private readonly OpaqueRefStore _refs;
    private readonly IInventoryTransferAdapter _inventory;
    private readonly TransferInventoryItemRequest _request;
    private PreparedInventoryTransfer? _prepared;
    private bool _committing;

    public InventoryTransferContinuation(
        OpaqueRefStore refs,
        IInventoryTransferAdapter inventory,
        TransferInventoryItemRequest request
    )
    {
        _refs = refs;
        _inventory = inventory;
        _request = request.Clone();
    }

    public string Phase => _prepared is null ? "preflight" : "ready_to_commit";
    public uint? ProgressPercent => _prepared is null ? 0u : 50u;
    public bool CanCancel => !_committing;

    public ContinuationStep Tick(ContinuationStopSignal signal)
    {
        if (signal != ContinuationStopSignal.None)
            return new ContinuationStep.Stopped();
        if (_prepared is null)
        {
            var prepared = Prepare();
            if (prepared.Error is not null)
                return new ContinuationStep.Failed(prepared.Error);
            _prepared = prepared.Value!;
            return new ContinuationStep.Pending();
        }

        var current = Capture();
        if (current.Error is not null)
            return new ContinuationStep.Failed(current.Error);
        if (!ReferenceEquals(current.Value!.MenuIdentity, _prepared.MenuIdentity)
            || !ReferenceEquals(current.Value.ContainerIdentity, _prepared.ContainerIdentity))
            return Failed(ErrorCode.StaleRef, "箱子菜单或容器已变化");
        var validation = ValidateRequestAgainstCapture(current.Value);
        if (validation.Error is not null)
            return new ContinuationStep.Failed(validation.Error);
        var planned = Plan(current.Value, validation.Source!);
        if (planned.Error is not null)
            return new ContinuationStep.Failed(planned.Error);
        if (!InventoryTransferPlanner.SamePlan(_prepared.Plan, planned.Value!))
            return Failed(ErrorCode.StaleRef, "源物品或确定性转移计划已变化");

        var before = current.Value;
        IInventoryTransferCommit? commit = null;
        _committing = true;
        try
        {
            commit = _inventory.Commit(before, _request.Direction, planned.Value!);
            var after = Capture();
            if (after.Error is not null)
                throw new InventoryTransferPostconditionException("转移后库存事实不可确认");
            if (!PostconditionsHold(before, after.Value!, planned.Value!))
                throw new InventoryTransferPostconditionException("箱子物品转移后置条件未成立");
            commit.Complete();

            return new ContinuationStep.Succeeded(new CapabilityResult
            {
                TransferInventoryItem = new TransferInventoryItemResult
                {
                    TransferredQuantity = _request.Quantity,
                    SourceSlotIndex = checked((uint)planned.Value!.SourceSlot),
                    SourceRemainingQuantity = checked((uint)planned.Value.SourceRemaining),
                    PlayerInventoryRevision = after.Value!.PlayerSnapshot!.InventoryRevision,
                    ContainerInventoryRevision = after.Value.ContainerSnapshot!.InventoryRevision,
                },
            });
        }
        catch (Exception error)
        {
            var restored = commit is not null && RollbackAndVerify(commit, before);
            var message = error is InventoryTransferPostconditionException known
                ? known.Message
                : "箱子物品转移提交失败";
            return Failed(
                ErrorCode.ExecutionFailed,
                restored ? message : $"{message}，且回滚后状态无法确认"
            );
        }
    }

    private PreparedResult Prepare()
    {
        var capture = Capture();
        if (capture.Error is not null)
            return new PreparedResult(null, capture.Error);
        var validation = ValidateRequestAgainstCapture(capture.Value!);
        if (validation.Error is not null)
            return new PreparedResult(null, validation.Error);
        var planned = Plan(capture.Value!, validation.Source!);
        return planned.Error is not null
            ? new PreparedResult(null, planned.Error)
            : new PreparedResult(new PreparedInventoryTransfer(
                capture.Value!.MenuIdentity!,
                capture.Value.ContainerIdentity!,
                planned.Value!
            ), null);
    }

    private SourceResult ValidateRequestAgainstCapture(InventoryTransferCapture capture)
    {
        if (!string.Equals(_request.UiRevision, capture.UiRevision, StringComparison.Ordinal)
            || !string.Equals(
                _request.PlayerInventoryRevision,
                capture.PlayerSnapshot!.InventoryRevision,
                StringComparison.Ordinal
            )
            || !string.Equals(
                _request.ContainerInventoryRevision,
                capture.ContainerSnapshot!.InventoryRevision,
                StringComparison.Ordinal
            ))
            return new SourceResult(null, Error(ErrorCode.StaleRef, "UI 或 Inventory Revision 已失效"));

        var resolved = _refs.ResolveInventoryItem(_request.ItemRef);
        if (resolved.Status != InventoryItemResolveStatus.Resolved || resolved.Target is null)
        {
            var error = resolved.Status switch
            {
                InventoryItemResolveStatus.Stale => Error(ErrorCode.StaleRef, "item_ref 已失效"),
                InventoryItemResolveStatus.NotFound => Error(ErrorCode.NotFound, "item_ref 不存在"),
                InventoryItemResolveStatus.Unsupported => Error(ErrorCode.InvalidArgument, "item_ref 类型无效"),
                _ => Error(ErrorCode.Internal, "item_ref 无法解析"),
            };
            return new SourceResult(null, error);
        }
        var expected = _request.Direction == InventoryTransferDirection.PlayerToContainer
            ? InventoryItemProvenance.Player
            : InventoryItemProvenance.Container;
        if (resolved.Target.Provenance != expected)
            return new SourceResult(null, Error(ErrorCode.InvalidArgument, "direction 与 item_ref 来源不一致"));
        var items = expected == InventoryItemProvenance.Player
            ? capture.PlayerItems!
            : capture.ContainerItems!;
        var slot = resolved.Target.Slot;
        if (slot < 0 || slot >= items.Count || items[slot] is not { } item
            || !ReferenceEquals(item.Identity, resolved.Target.Target))
            return new SourceResult(null, Error(ErrorCode.StaleRef, "item_ref 已失效"));
        return new SourceResult(new ResolvedTransferSource(slot, item), null);
    }

    private PlanResult Plan(InventoryTransferCapture capture, ResolvedTransferSource source)
    {
        var sourceItems = _request.Direction == InventoryTransferDirection.PlayerToContainer
            ? capture.PlayerItems!
            : capture.ContainerItems!;
        var targetItems = _request.Direction == InventoryTransferDirection.PlayerToContainer
            ? capture.ContainerItems!
            : capture.PlayerItems!;
        var result = InventoryTransferPlanner.Plan(
            source.Slot,
            sourceItems,
            targetItems,
            checked((int)_request.Quantity)
        );
        var code = result.Status switch
        {
            InventoryTransferPlanStatus.Invalid => ErrorCode.InvalidArgument,
            InventoryTransferPlanStatus.Stale => ErrorCode.StaleRef,
            InventoryTransferPlanStatus.OutOfRange => ErrorCode.OutOfRange,
            InventoryTransferPlanStatus.NotReady => ErrorCode.NotReady,
            InventoryTransferPlanStatus.Unavailable => ErrorCode.Internal,
            _ => ErrorCode.Unspecified,
        };
        return result.Value is not null
            ? new PlanResult(result.Value, null)
            : new PlanResult(null, Error(code, result.Message));
    }

    private CaptureResult Capture()
    {
        var capture = _inventory.Capture();
        if (capture.Status != InventoryTransferCaptureStatus.Ready)
        {
            var error = capture.Status switch
            {
                InventoryTransferCaptureStatus.NotReady => Error(ErrorCode.NotReady, "当前箱子菜单尚未准备好"),
                InventoryTransferCaptureStatus.Unsupported => Error(ErrorCode.NotReady, "当前菜单不支持箱子物品转移"),
                _ => Error(ErrorCode.Internal, "当前箱子库存事实不可读"),
            };
            return new CaptureResult(null, error);
        }
        if (capture.MenuIdentity is null
            || capture.ContainerIdentity is null
            || capture.PlayerSnapshot is null
            || capture.ContainerSnapshot is null
            || capture.PlayerItems is null
            || capture.ContainerItems is null
            || capture.CommitState is null
            || capture.PlayerSnapshot.Slots.Count != capture.PlayerItems.Count
            || capture.ContainerSnapshot.Slots.Count != capture.ContainerItems.Count)
            return new CaptureResult(null, Error(ErrorCode.Internal, "箱子库存捕获无效"));
        return new CaptureResult(capture, null);
    }

    private bool PostconditionsHold(
        InventoryTransferCapture before,
        InventoryTransferCapture after,
        InventoryTransferPlan plan
    )
    {
        if (!ReferenceEquals(before.MenuIdentity, after.MenuIdentity)
            || !ReferenceEquals(before.ContainerIdentity, after.ContainerIdentity)
            || string.Equals(before.UiRevision, after.UiRevision, StringComparison.Ordinal)
            || string.Equals(before.PlayerSnapshot!.InventoryRevision, after.PlayerSnapshot!.InventoryRevision, StringComparison.Ordinal)
            || string.Equals(before.ContainerSnapshot!.InventoryRevision, after.ContainerSnapshot!.InventoryRevision, StringComparison.Ordinal))
            return false;
        var beforePlayer = Total(before.PlayerItems!);
        var beforeContainer = Total(before.ContainerItems!);
        var afterPlayer = Total(after.PlayerItems!);
        var afterContainer = Total(after.ContainerItems!);
        var quantity = plan.Quantity;
        if (beforePlayer + beforeContainer != afterPlayer + afterContainer)
            return false;
        if (_request.Direction == InventoryTransferDirection.PlayerToContainer
            ? beforePlayer - afterPlayer != quantity || afterContainer - beforeContainer != quantity
            : beforeContainer - afterContainer != quantity || afterPlayer - beforePlayer != quantity)
            return false;

        var targetBefore = _request.Direction == InventoryTransferDirection.PlayerToContainer
            ? before.ContainerItems!
            : before.PlayerItems!;
        var targetAfter = _request.Direction == InventoryTransferDirection.PlayerToContainer
            ? after.ContainerItems!
            : after.PlayerItems!;
        var sourceBefore = _request.Direction == InventoryTransferDirection.PlayerToContainer
            ? before.PlayerItems!
            : before.ContainerItems!;
        if (!TargetWritesHold(targetBefore, targetAfter, sourceBefore[plan.SourceSlot]!, plan))
            return false;

        var sourceAfter = _request.Direction == InventoryTransferDirection.PlayerToContainer
            ? after.PlayerItems!
            : after.ContainerItems!;
        if (plan.SourceRemaining == 0)
            return sourceAfter[plan.SourceSlot] is null;
        return sourceAfter[plan.SourceSlot] is { } remaining
            && ReferenceEquals(remaining.Identity, plan.SourceIdentity)
            && remaining.Stack == plan.SourceRemaining;
    }

    private static bool TargetWritesHold(
        IReadOnlyList<InventoryTransferItem?> before,
        IReadOnlyList<InventoryTransferItem?> after,
        InventoryTransferItem source,
        InventoryTransferPlan plan
    )
    {
        if (before.Count != after.Count)
            return false;
        var writes = plan.Writes.ToDictionary(write => write.Slot);
        for (var slot = 0; slot < before.Count; slot++)
        {
            var oldItem = before[slot];
            var newItem = after[slot];
            if (!writes.TryGetValue(slot, out var write))
            {
                if ((oldItem is null) != (newItem is null)
                    || oldItem is not null && (!ReferenceEquals(oldItem.Identity, newItem!.Identity)
                        || oldItem.Stack != newItem.Stack))
                    return false;
                continue;
            }
            if (write.ExistingIdentity is not null)
            {
                if (oldItem is null || newItem is null
                    || !ReferenceEquals(oldItem.Identity, write.ExistingIdentity)
                    || !ReferenceEquals(newItem.Identity, write.ExistingIdentity)
                    || newItem.Stack != checked(oldItem.Stack + write.Quantity))
                    return false;
                continue;
            }
            if (oldItem is not null || newItem is null
                || newItem.Stack != write.Quantity
                || !ReferenceEquals(newItem.Identity, write.NewIdentity)
                || ReferenceEquals(newItem.Identity, source.Identity)
                || !string.Equals(newItem.Guard, source.Guard, StringComparison.Ordinal)
                || source.MaximumStack > 1 && !newItem.CanStackWith(source))
                return false;
        }
        return true;
    }

    private static long Total(IReadOnlyList<InventoryTransferItem?> items) =>
        items.Where(item => item is not null).Sum(item => (long)item!.Stack);
    private bool RollbackAndVerify(
        IInventoryTransferCommit commit,
        InventoryTransferCapture before
    )
    {
        try
        {
            commit.Rollback();
            var restored = Capture();
            return restored.Error is null
                && ReferenceEquals(before.MenuIdentity, restored.Value!.MenuIdentity)
                && ReferenceEquals(before.ContainerIdentity, restored.Value.ContainerIdentity)
                && SameInventoryState(before.PlayerItems!, restored.Value.PlayerItems!)
                && SameInventoryState(before.ContainerItems!, restored.Value.ContainerItems!);
        }
        catch
        {
            return false;
        }
    }
    private static bool SameInventoryState(
        IReadOnlyList<InventoryTransferItem?> expected,
        IReadOnlyList<InventoryTransferItem?> actual
    ) => expected.Count == actual.Count && expected.Zip(actual).All(pair =>
        pair.First is null && pair.Second is null
        || pair.First is not null && pair.Second is not null
            && ReferenceEquals(pair.First.Identity, pair.Second.Identity)
            && pair.First.Stack == pair.Second.Stack);
    private static ContinuationStep Failed(ErrorCode code, string message) =>
        new ContinuationStep.Failed(Error(code, message));
    private static Error Error(ErrorCode code, string message) => new() { Code = code, Message = message };

    private sealed record PreparedInventoryTransfer(object MenuIdentity, object ContainerIdentity, InventoryTransferPlan Plan);
    private sealed record ResolvedTransferSource(int Slot, InventoryTransferItem Item);
    private sealed record CaptureResult(InventoryTransferCapture? Value, Error? Error);
    private sealed record SourceResult(ResolvedTransferSource? Source, Error? Error);
    private sealed record PlanResult(InventoryTransferPlan? Value, Error? Error);
    private sealed record PreparedResult(PreparedInventoryTransfer? Value, Error? Error);
    private sealed class InventoryTransferPostconditionException : Exception
    {
        public InventoryTransferPostconditionException(string message) : base(message) { }
    }
}
