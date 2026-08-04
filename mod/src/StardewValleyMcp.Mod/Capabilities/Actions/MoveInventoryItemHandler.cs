using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class MoveInventoryItemHandler : ILongRunningCapabilityHandler
{
    private readonly OpaqueRefStore _refs;
    private readonly IInventorySlotMoveRuntimeAdapter _runtime;

    public MoveInventoryItemHandler(OpaqueRefStore refs)
        : this(refs, new LiveInventorySlotMoveRuntimeAdapter(refs)) { }

    internal MoveInventoryItemHandler(
        OpaqueRefStore refs,
        IInventorySlotMoveRuntimeAdapter runtime
    )
    {
        _refs = refs;
        _runtime = runtime;
    }

    public string Id => "move_inventory_item";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.MoveInventoryItem;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("move_inventory_item 请求类型无效");
        var value = request.MoveInventoryItem;
        if (!PublicStringPolicy.IsNonEmptyValid(value.ItemRef?.Value))
            return Invalid("item_ref 格式无效");
        if (!PublicStringPolicy.IsNonEmptyValid(value.DestinationSlotRef?.Value))
            return Invalid("destination_slot_ref 格式无效");
        if (!IsRevision(value.UiRevision)
            || !IsRevision(value.PlayerInventoryRevision))
            return Invalid("UI 与玩家 Inventory Revision 格式无效");
        return null;
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new MoveInventoryItemContinuation(_refs, _runtime, request.MoveInventoryItem);

    private static bool IsRevision(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };
}

internal sealed class MoveInventoryItemContinuation : ICommandContinuation
{
    private readonly OpaqueRefStore _refs;
    private readonly IInventorySlotMoveRuntimeAdapter _runtime;
    private readonly MoveInventoryItemRequest _request;
    private PreparedInventorySlotMove? _prepared;
    private bool _committing;

    public MoveInventoryItemContinuation(
        OpaqueRefStore refs,
        IInventorySlotMoveRuntimeAdapter runtime,
        MoveInventoryItemRequest request
    )
    {
        _refs = refs;
        _runtime = runtime;
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
            var preparation = Prepare();
            if (preparation.Error is not null)
                return new ContinuationStep.Failed(preparation.Error);
            _prepared = preparation.Value!;
            return new ContinuationStep.Pending();
        }

        var capture = Capture();
        if (capture.Error is not null)
            return new ContinuationStep.Failed(capture.Error);
        var current = capture.Value!;
        if (!SameContext(current, _prepared))
            return Failed(ErrorCode.StaleRef, "背包页面、玩家背包或目标 Slot 组件已变化");
        var planning = Plan(current);
        if (planning.Error is not null)
            return new ContinuationStep.Failed(planning.Error);
        if (!InventorySlotMutationPlanner.SamePlan(_prepared.Plan, planning.Value!.Plan)
            || _prepared.SourceStack != planning.Value.Source.Stack
            || _prepared.DestinationStack != planning.Value.Destination?.Stack)
            return Failed(ErrorCode.StaleRef, "源物品、目标 Slot 或物品堆叠已变化");

        IInventorySlotMutationCommit? commit = null;
        _committing = true;
        try
        {
            commit = _runtime.Commit(current, planning.Value.Plan);
            var afterResult = Capture();
            if (afterResult.Error is not null
                || !PostconditionsHold(current, afterResult.Value!, _prepared, planning.Value.Plan))
                throw new InventorySlotMovePostconditionException();
            var after = afterResult.Value!;
            commit.Complete();
            return new ContinuationStep.Succeeded(new CapabilityResult
            {
                MoveInventoryItem = new MoveInventoryItemResult
                {
                    SourceSlotIndex = checked((uint)planning.Value.Plan.SourceSlot),
                    DestinationSlotIndex = checked((uint)planning.Value.Plan.DestinationSlot),
                    Changed = planning.Value.Plan.Changed,
                    Swapped = planning.Value.Plan.Kind == InventorySlotMutationKind.Swap,
                    PlayerInventoryRevision = after.PlayerSnapshot!.InventoryRevision,
                },
            });
        }
        catch (Exception error)
        {
            var restored = RollbackAndVerify(commit, current);
            var message = error is InventorySlotMovePostconditionException
                ? "背包 Slot 写入后置条件未成立"
                : "背包 Slot 写入提交失败";
            return Failed(
                ErrorCode.ExecutionFailed,
                restored ? message : $"{message}，且回滚后状态无法确认；请重新查询"
            );
        }
    }

    private PreparedResult Prepare()
    {
        var capture = Capture();
        if (capture.Error is not null)
            return new PreparedResult(null, capture.Error);
        var planning = Plan(capture.Value!);
        if (planning.Error is not null)
            return new PreparedResult(null, planning.Error);
        var value = planning.Value!;
        return new PreparedResult(new PreparedInventorySlotMove(
            capture.Value!.MenuIdentity!,
            capture.Value.PageIdentity!,
            capture.Value.PlayerIdentity!,
            capture.Value.BackingIdentity!,
            capture.Value.Components[value.Plan.DestinationSlot],
            capture.Value.CurrentToolIndex,
            value.Source.Stack,
            value.Destination?.Stack,
            value.Plan
        ), null);
    }

    private PlanningResult Plan(InventorySlotMoveCapture capture)
    {
        if (!string.Equals(_request.UiRevision, capture.UiRevision, StringComparison.Ordinal)
            || !string.Equals(
                _request.PlayerInventoryRevision,
                capture.PlayerSnapshot!.InventoryRevision,
                StringComparison.Ordinal
            ))
            return PlanningResult.ErrorResult(Error(ErrorCode.StaleRef, "UI 或玩家 Inventory Revision 已失效"));

        var source = ResolveSource(capture);
        if (source.Error is not null)
            return PlanningResult.ErrorResult(source.Error);
        var destination = ResolveDestination(capture);
        if (destination.Error is not null)
            return PlanningResult.ErrorResult(destination.Error);
        var destinationItem = capture.Backpack[destination.Index];
        var planned = InventorySlotMutationPlanner.Plan(
            source.Slot,
            source.Item!.Identity,
            destination.Index,
            destinationItem?.Identity,
            capture.Backpack.Select(item => item?.Identity).ToArray()
        );
        if (planned.Plan is not null)
            return new PlanningResult(new PlannedInventorySlotMove(
                planned.Plan,
                source.Item,
                destinationItem
            ), null);
        var code = planned.Status switch
        {
            InventorySlotMutationPlanStatus.Invalid => ErrorCode.InvalidArgument,
            InventorySlotMutationPlanStatus.Stale => ErrorCode.StaleRef,
            InventorySlotMutationPlanStatus.Unavailable => ErrorCode.Internal,
            _ => ErrorCode.Internal,
        };
        return PlanningResult.ErrorResult(Error(code, planned.Message));
    }

    private SourceResult ResolveSource(InventorySlotMoveCapture capture)
    {
        var resolved = _refs.ResolveInventoryItem(_request.ItemRef);
        if (resolved.Status != InventoryItemResolveStatus.Resolved || resolved.Target is null)
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
        if (slot < 0 || slot >= capture.Backpack.Count
            || capture.Backpack[slot] is not { } item
            || !ReferenceEquals(item.Identity, resolved.Target.Target))
            return new SourceResult(-1, null, Error(ErrorCode.StaleRef, "item_ref 已失效"));
        return new SourceResult(slot, item, null);
    }

    private DestinationResult ResolveDestination(InventorySlotMoveCapture capture)
    {
        var resolved = _refs.ResolveUiElement(_request.DestinationSlotRef);
        if (resolved.Status != UiElementResolveStatus.Resolved || resolved.Target is null)
        {
            var error = resolved.Status switch
            {
                UiElementResolveStatus.Stale => Error(ErrorCode.StaleRef, "destination_slot_ref 已失效"),
                UiElementResolveStatus.NotFound => Error(ErrorCode.NotFound, "destination_slot_ref 不存在"),
                UiElementResolveStatus.Unsupported => Error(ErrorCode.InvalidArgument, "destination_slot_ref 类型无效"),
                UiElementResolveStatus.Unavailable => Error(ErrorCode.Internal, "destination_slot_ref 当前不可解析"),
                _ => Error(ErrorCode.Internal, "destination_slot_ref 无法解析"),
            };
            return new DestinationResult(-1, null, error);
        }
        var value = resolved.Target;
        if (value.PublicKind != UiElementKind.ItemSlot
            || value.Extractor != UiExtractorKind.GameMenu
            || value.InventorySide != UiInventorySide.Player
            || value.EquipmentSlotKind != UiEquipmentSlotKind.Unspecified)
            return new DestinationResult(-1, null, Error(ErrorCode.InvalidArgument, "Ref 不是当前玩家背包 Slot"));
        if (value.Index < 0 || value.Index >= capture.Components.Count
            || value.Component is null
            || !ReferenceEquals(capture.Components[value.Index], value.Component)
            || !ReferenceEquals(value.Component, value.Target))
            return new DestinationResult(-1, null, Error(ErrorCode.StaleRef, "目标背包 Slot 组件已变化"));
        return new DestinationResult(value.Index, value.Component, null);
    }

    private CaptureResult Capture()
    {
        var capture = _runtime.Capture();
        if (capture.Status != InventorySlotMoveCaptureStatus.Ready)
        {
            var error = capture.Status switch
            {
                InventorySlotMoveCaptureStatus.NotReady => Error(ErrorCode.NotReady, "当前原版背包页面尚未准备好、游标持有物品或当前手持状态不一致"),
                InventorySlotMoveCaptureStatus.Unsupported => Error(ErrorCode.NotReady, "当前菜单不支持玩家背包 Slot 移动"),
                _ => Error(ErrorCode.Internal, "当前玩家背包事实不可读"),
            };
            return new CaptureResult(null, error);
        }
        if (capture.MenuIdentity is null
            || capture.PageIdentity is null
            || capture.PlayerIdentity is null
            || capture.BackingIdentity is null
            || capture.PlayerSnapshot is null
            || capture.CommitState is null
            || capture.PlayerSnapshot.Slots.Count != capture.Backpack.Count
            || capture.PlayerSnapshot.SlotCount != capture.Backpack.Count
            || capture.Components.Count != capture.Backpack.Count
            || capture.CurrentToolIndex < 0
            || capture.CurrentToolIndex >= capture.Backpack.Count)
            return new CaptureResult(null, Error(ErrorCode.Internal, "玩家背包 Slot 捕获无效"));
        return new CaptureResult(capture, null);
    }

    private static bool SameContext(
        InventorySlotMoveCapture capture,
        PreparedInventorySlotMove prepared
    ) => ReferenceEquals(capture.MenuIdentity, prepared.MenuIdentity)
        && ReferenceEquals(capture.PageIdentity, prepared.PageIdentity)
        && ReferenceEquals(capture.PlayerIdentity, prepared.PlayerIdentity)
        && ReferenceEquals(capture.BackingIdentity, prepared.BackingIdentity)
        && capture.CurrentToolIndex == prepared.CurrentToolIndex
        && prepared.Plan.DestinationSlot >= 0
        && prepared.Plan.DestinationSlot < capture.Components.Count
        && ReferenceEquals(
            capture.Components[prepared.Plan.DestinationSlot],
            prepared.DestinationComponent
        );

    private static bool PostconditionsHold(
        InventorySlotMoveCapture before,
        InventorySlotMoveCapture after,
        PreparedInventorySlotMove prepared,
        InventorySlotMutationPlan plan
    )
    {
        if (!SameContext(after, prepared)
            || before.Backpack.Count != after.Backpack.Count
            || after.PlayerSnapshot is null
            || (plan.Changed && (
                string.Equals(before.UiRevision, after.UiRevision, StringComparison.Ordinal)
                || string.Equals(
                    before.PlayerSnapshot!.InventoryRevision,
                    after.PlayerSnapshot.InventoryRevision,
                    StringComparison.Ordinal
                ))))
            return false;
        for (var slot = 0; slot < before.Backpack.Count; slot++)
        {
            var expected = slot == plan.SourceSlot
                ? plan.Changed ? before.Backpack[plan.DestinationSlot] : before.Backpack[slot]
                : slot == plan.DestinationSlot
                    ? before.Backpack[plan.SourceSlot]
                    : before.Backpack[slot];
            if (!SameItem(expected, after.Backpack[slot]))
                return false;
        }
        return true;
    }

    private bool RollbackAndVerify(
        IInventorySlotMutationCommit? commit,
        InventorySlotMoveCapture before
    )
    {
        try
        {
            commit?.Rollback();
            var after = Capture();
            return after.Error is null && SameContent(before, after.Value!);
        }
        catch
        {
            return false;
        }
    }

    private static bool SameContent(
        InventorySlotMoveCapture left,
        InventorySlotMoveCapture right
    )
    {
        if (!ReferenceEquals(left.MenuIdentity, right.MenuIdentity)
            || !ReferenceEquals(left.PageIdentity, right.PageIdentity)
            || !ReferenceEquals(left.PlayerIdentity, right.PlayerIdentity)
            || !ReferenceEquals(left.BackingIdentity, right.BackingIdentity)
            || left.CurrentToolIndex != right.CurrentToolIndex
            || left.Backpack.Count != right.Backpack.Count)
            return false;
        for (var slot = 0; slot < left.Backpack.Count; slot++)
        {
            if (!SameItem(left.Backpack[slot], right.Backpack[slot]))
                return false;
        }
        return true;
    }

    private static bool SameItem(InventorySlotMoveItem? left, InventorySlotMoveItem? right) =>
        left is null || right is null
            ? left is null && right is null
            : ReferenceEquals(left.Identity, right.Identity)
                && left.Stack == right.Stack;

    private static ContinuationStep Failed(ErrorCode code, string message) =>
        new ContinuationStep.Failed(Error(code, message));
    private static Error Error(ErrorCode code, string message) => new()
    {
        Code = code,
        Message = message,
    };

    private sealed record PreparedInventorySlotMove(
        object MenuIdentity,
        object PageIdentity,
        object PlayerIdentity,
        object BackingIdentity,
        object DestinationComponent,
        int CurrentToolIndex,
        int SourceStack,
        int? DestinationStack,
        InventorySlotMutationPlan Plan
    );
    private sealed record PlannedInventorySlotMove(
        InventorySlotMutationPlan Plan,
        InventorySlotMoveItem Source,
        InventorySlotMoveItem? Destination
    );
    private sealed record PreparedResult(PreparedInventorySlotMove? Value, Error? Error);
    private sealed record PlanningResult(PlannedInventorySlotMove? Value, Error? Error)
    {
        public static PlanningResult ErrorResult(Error error) => new(null, error);
    }
    private sealed record CaptureResult(InventorySlotMoveCapture? Value, Error? Error);
    private sealed record SourceResult(int Slot, InventorySlotMoveItem? Item, Error? Error);
    private sealed record DestinationResult(int Index, object? Component, Error? Error);
}

internal sealed class InventorySlotMovePostconditionException : Exception { }
