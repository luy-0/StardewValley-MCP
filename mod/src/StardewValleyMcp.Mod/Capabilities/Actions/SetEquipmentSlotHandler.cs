using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class SetEquipmentSlotHandler : ILongRunningCapabilityHandler
{
    private readonly OpaqueRefStore _refs;
    private readonly IEquipmentSlotRuntimeAdapter _runtime;

    public SetEquipmentSlotHandler(OpaqueRefStore refs)
        : this(refs, new LiveEquipmentSlotRuntimeAdapter(refs)) { }

    internal SetEquipmentSlotHandler(
        OpaqueRefStore refs,
        IEquipmentSlotRuntimeAdapter runtime
    )
    {
        _refs = refs;
        _runtime = runtime;
    }

    public string Id => "set_equipment_slot";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.SetEquipmentSlot;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("set_equipment_slot 请求类型无效");
        var value = request.SetEquipmentSlot;
        if (!PublicStringPolicy.IsNonEmptyValid(value.EquipmentSlotRef?.Value))
            return Invalid("equipment_slot_ref 格式无效");
        if (value.ValueCase == SetEquipmentSlotRequest.ValueOneofCase.None)
            return Invalid("必须提供 item_ref 或 clear=true");
        if (value.ValueCase == SetEquipmentSlotRequest.ValueOneofCase.ItemRef
            && !PublicStringPolicy.IsNonEmptyValid(value.ItemRef?.Value))
            return Invalid("item_ref 格式无效");
        if (value.ValueCase == SetEquipmentSlotRequest.ValueOneofCase.Clear
            && !value.Clear)
            return Invalid("clear 只能显式为 true");
        if (!IsRevision(value.UiRevision)
            || !IsRevision(value.PlayerInventoryRevision))
            return Invalid("UI 与玩家 Inventory Revision 格式无效");
        return null;
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new SetEquipmentSlotContinuation(_refs, _runtime, request.SetEquipmentSlot);

    private static bool IsRevision(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };
}

internal sealed class SetEquipmentSlotContinuation : ICommandContinuation
{
    private readonly OpaqueRefStore _refs;
    private readonly IEquipmentSlotRuntimeAdapter _runtime;
    private readonly SetEquipmentSlotRequest _request;
    private PreparedEquipmentSlotMutation? _prepared;
    private bool _committing;

    public SetEquipmentSlotContinuation(
        OpaqueRefStore refs,
        IEquipmentSlotRuntimeAdapter runtime,
        SetEquipmentSlotRequest request
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

        var captured = Capture();
        if (captured.Error is not null)
            return new ContinuationStep.Failed(captured.Error);
        var current = captured.Value!;
        if (!SameContext(current, _prepared))
            return Failed(ErrorCode.StaleRef, "背包页面或装备槽组件已变化");
        var planning = Plan(current, _prepared.Kind, _prepared.Index);
        if (planning.Error is not null)
            return new ContinuationStep.Failed(planning.Error);
        if (!EquipmentSlotMutationPlanner.SamePlan(_prepared.Plan, planning.Value!))
            return Failed(ErrorCode.StaleRef, "源物品、旧装备或目的 Slot 已变化");

        IEquipmentSlotMutationCommit? commit = null;
        _committing = true;
        try
        {
            commit = _runtime.Commit(current, _prepared.Kind, _prepared.Index, planning.Value!);
            var afterResult = Capture();
            if (afterResult.Error is not null
                || !PostconditionsHold(current, afterResult.Value!, _prepared, planning.Value!))
                throw new EquipmentSlotPostconditionException();
            var after = afterResult.Value!;
            commit.Complete();
            var target = FindSlot(after, _prepared.Kind, _prepared.Index)!;
            var result = new SetEquipmentSlotResult
            {
                EquipmentSlotKind = _prepared.Kind,
                EquipmentSlotIndex = checked((uint)_prepared.Index),
                PlayerInventoryRevision = after.PlayerSnapshot!.InventoryRevision,
                Changed = planning.Value!.Changed,
            };
            if (target.Item is not null)
                result.Item = target.Item.PublicFact.Clone();
            return new ContinuationStep.Succeeded(new CapabilityResult
            {
                SetEquipmentSlot = result,
            });
        }
        catch (Exception error)
        {
            var restored = RollbackAndVerify(commit, current);
            var message = error is EquipmentSlotPostconditionException
                ? "装备槽写入后置条件未成立"
                : "装备槽写入提交失败";
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
        var equipment = ResolveEquipment(capture.Value!);
        if (equipment.Error is not null)
            return new PreparedResult(null, equipment.Error);
        var plan = Plan(capture.Value!, equipment.Kind, equipment.Index);
        return plan.Error is not null
            ? new PreparedResult(null, plan.Error)
            : new PreparedResult(new PreparedEquipmentSlotMutation(
                capture.Value!.MenuIdentity!,
                capture.Value.PageIdentity!,
                capture.Value.PlayerIdentity!,
                equipment.Component!,
                equipment.Kind,
                equipment.Index,
                plan.Value!
            ), null);
    }

    private PlanResult Plan(
        EquipmentSlotCapture capture,
        UiEquipmentSlotKind kind,
        int index
    )
    {
        if (!RevisionsMatch(capture))
            return PlanResult.ErrorResult(Error(ErrorCode.StaleRef, "UI 或玩家 Inventory Revision 已失效"));
        var target = FindSlot(capture, kind, index);
        if (target is null)
            return PlanResult.ErrorResult(Error(ErrorCode.StaleRef, "装备槽已失效"));
        if (target.Item is not null && !Supported(target.Item, kind))
            return PlanResult.ErrorResult(Error(ErrorCode.InvalidArgument, "当前旧装备不支持安全替换或取下"));

        int? sourceSlot = null;
        object? sourceIdentity = null;
        if (_request.ValueCase == SetEquipmentSlotRequest.ValueOneofCase.ItemRef)
        {
            var source = ResolveSource(capture, kind);
            if (source.Error is not null)
                return PlanResult.ErrorResult(source.Error);
            sourceSlot = source.Slot;
            sourceIdentity = source.Item!.Identity;
            if (ReferenceEquals(sourceIdentity, target.Item?.Identity))
                return PlanResult.ErrorResult(Error(ErrorCode.Internal, "源物品与旧装备对象重复"));
        }

        var planned = EquipmentSlotMutationPlanner.Plan(
            _request.ValueCase == SetEquipmentSlotRequest.ValueOneofCase.Clear,
            sourceSlot,
            sourceIdentity,
            capture.Backpack.Select(item => item?.Identity).ToArray(),
            target.Item?.Identity
        );
        if (planned.Plan is not null)
            return new PlanResult(planned.Plan, null);
        var code = planned.Status switch
        {
            EquipmentSlotMutationPlanStatus.Invalid => ErrorCode.InvalidArgument,
            EquipmentSlotMutationPlanStatus.Stale => ErrorCode.StaleRef,
            EquipmentSlotMutationPlanStatus.NotReady => ErrorCode.NotReady,
            _ => ErrorCode.Internal,
        };
        return PlanResult.ErrorResult(Error(code, planned.Message));
    }

    private SourceResult ResolveSource(EquipmentSlotCapture capture, UiEquipmentSlotKind kind)
    {
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
            return new SourceResult(-1, null, error);
        }
        if (resolved.Target.Provenance != InventoryItemProvenance.Player)
            return new SourceResult(-1, null, Error(ErrorCode.InvalidArgument, "item_ref 不属于玩家背包"));
        var slot = resolved.Target.Slot;
        if (slot < 0 || slot >= capture.Backpack.Count
            || capture.Backpack[slot] is not { } item
            || !ReferenceEquals(item.Identity, resolved.Target.Target))
            return new SourceResult(-1, null, Error(ErrorCode.StaleRef, "item_ref 已失效"));
        if (!Supported(item, kind))
            return new SourceResult(-1, null, Error(ErrorCode.InvalidArgument, "物品类型、堆叠或特殊语义不支持该装备槽"));
        return new SourceResult(slot, item, null);
    }

    private EquipmentResolution ResolveEquipment(EquipmentSlotCapture capture)
    {
        var resolved = _refs.ResolveUiElement(_request.EquipmentSlotRef);
        if (resolved.Status != UiElementResolveStatus.Resolved || resolved.Target is null)
        {
            var error = resolved.Status switch
            {
                UiElementResolveStatus.Stale => Error(ErrorCode.StaleRef, "equipment_slot_ref 已失效"),
                UiElementResolveStatus.NotFound => Error(ErrorCode.NotFound, "equipment_slot_ref 不存在"),
                UiElementResolveStatus.Unsupported => Error(ErrorCode.InvalidArgument, "equipment_slot_ref 类型无效"),
                _ => Error(ErrorCode.Internal, "equipment_slot_ref 无法解析"),
            };
            return new EquipmentResolution(UiEquipmentSlotKind.Unspecified, -1, null, error);
        }
        var value = resolved.Target;
        if (value.PublicKind != UiElementKind.EquipmentSlot
            || value.Extractor != UiExtractorKind.GameMenu
            || value.EquipmentSlotKind == UiEquipmentSlotKind.Unspecified)
            return new EquipmentResolution(UiEquipmentSlotKind.Unspecified, -1, null, Error(ErrorCode.InvalidArgument, "Ref 不是受支持的装备槽"));
        var slot = FindSlot(capture, value.EquipmentSlotKind, value.Index);
        if (slot is null
            || value.Component is null
            || !ReferenceEquals(slot.Component, value.Component)
            || !ReferenceEquals(slot.Component, value.Target))
            return new EquipmentResolution(UiEquipmentSlotKind.Unspecified, -1, null, Error(ErrorCode.StaleRef, "装备槽组件已变化"));
        return new EquipmentResolution(value.EquipmentSlotKind, value.Index, value.Component, null);
    }

    private CaptureResult Capture()
    {
        var capture = _runtime.Capture();
        if (capture.Status != EquipmentSlotCaptureStatus.Ready)
        {
            var error = capture.Status switch
            {
                EquipmentSlotCaptureStatus.NotReady => Error(ErrorCode.NotReady, "当前原版背包页面尚未准备好，或游标仍持有物品"),
                EquipmentSlotCaptureStatus.Unsupported => Error(ErrorCode.NotReady, "当前菜单不支持装备槽写入"),
                _ => Error(ErrorCode.Internal, "当前装备槽事实不可读"),
            };
            return new CaptureResult(null, error);
        }
        if (capture.MenuIdentity is null
            || capture.PageIdentity is null
            || capture.PlayerIdentity is null
            || capture.PlayerSnapshot is null
            || capture.CommitState is null
            || capture.PlayerSnapshot.Slots.Count != capture.Backpack.Count
            || capture.PlayerSnapshot.SlotCount != capture.Backpack.Count
            || capture.CurrentToolIndex < 0
            || capture.CurrentToolIndex >= capture.Backpack.Count)
            return new CaptureResult(null, Error(ErrorCode.Internal, "装备槽捕获无效"));
        return new CaptureResult(capture, null);
    }

    private bool Supported(EquipmentRuntimeItem item, UiEquipmentSlotKind kind)
    {
        if (EquipmentSlotCompatibility.IsSpecial(item.QualifiedItemId)
            || item.Stack != 1
            || item.MaximumStack != 1)
            return false;
        try
        {
            return _runtime.IsSupported(item, kind);
        }
        catch
        {
            return false;
        }
    }

    private bool RevisionsMatch(EquipmentSlotCapture capture) =>
        string.Equals(_request.UiRevision, capture.UiRevision, StringComparison.Ordinal)
        && string.Equals(
            _request.PlayerInventoryRevision,
            capture.PlayerSnapshot!.InventoryRevision,
            StringComparison.Ordinal
        );

    private static bool SameContext(
        EquipmentSlotCapture capture,
        PreparedEquipmentSlotMutation prepared
    )
    {
        var slot = FindSlot(capture, prepared.Kind, prepared.Index);
        return ReferenceEquals(capture.MenuIdentity, prepared.MenuIdentity)
            && ReferenceEquals(capture.PageIdentity, prepared.PageIdentity)
            && ReferenceEquals(capture.PlayerIdentity, prepared.PlayerIdentity)
            && slot is not null
            && ReferenceEquals(slot.Component, prepared.Component);
    }

    private bool PostconditionsHold(
        EquipmentSlotCapture before,
        EquipmentSlotCapture after,
        PreparedEquipmentSlotMutation prepared,
        EquipmentSlotMutationPlan plan
    )
    {
        if (!SameContext(after, prepared)
            || before.CurrentToolIndex != after.CurrentToolIndex
            || after.PlayerSnapshot is null
            || (plan.Changed && (
                string.Equals(before.UiRevision, after.UiRevision, StringComparison.Ordinal)
                || string.Equals(
                    before.PlayerSnapshot!.InventoryRevision,
                    after.PlayerSnapshot.InventoryRevision,
                    StringComparison.Ordinal
                )))
            || before.Backpack.Count != after.Backpack.Count
            || before.Equipment.Count != after.Equipment.Count)
            return false;

        for (var slot = 0; slot < before.Backpack.Count; slot++)
        {
            var expected = ExpectedBackpack(before, plan, slot);
            if (!SameItem(expected, after.Backpack[slot]))
                return false;
        }
        foreach (var previous in before.Equipment)
        {
            var current = FindSlot(after, previous.Kind, previous.Index);
            var expected = previous.Kind == prepared.Kind && previous.Index == prepared.Index
                ? plan.Kind switch
                {
                    EquipmentSlotMutationKind.Wear or EquipmentSlotMutationKind.Replace =>
                        before.Backpack[plan.SourceSlot!.Value],
                    EquipmentSlotMutationKind.Clear or EquipmentSlotMutationKind.NoChange => null,
                    _ => null,
                }
                : previous.Item;
            if (current is null || !SameItem(expected, current.Item))
                return false;
        }
        return true;
    }

    private static EquipmentRuntimeItem? ExpectedBackpack(
        EquipmentSlotCapture before,
        EquipmentSlotMutationPlan plan,
        int slot
    )
    {
        if (!plan.Changed)
            return before.Backpack[slot];
        if (plan.SourceSlot == slot)
            return plan.EquipmentBefore is null
                ? null
                : FindByIdentity(before, plan.EquipmentBefore);
        if (plan.BackpackDestinationSlot == slot)
            return FindByIdentity(before, plan.EquipmentBefore);
        return before.Backpack[slot];
    }

    private static EquipmentRuntimeItem? FindByIdentity(
        EquipmentSlotCapture capture,
        object? identity
    ) => identity is null
        ? null
        : capture.Backpack.Concat(capture.Equipment.Select(slot => slot.Item))
            .FirstOrDefault(item => item is not null && ReferenceEquals(item.Identity, identity));

    private bool RollbackAndVerify(
        IEquipmentSlotMutationCommit? commit,
        EquipmentSlotCapture before
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

    private static bool SameContent(EquipmentSlotCapture left, EquipmentSlotCapture right)
    {
        if (!ReferenceEquals(left.MenuIdentity, right.MenuIdentity)
            || !ReferenceEquals(left.PageIdentity, right.PageIdentity)
            || !ReferenceEquals(left.PlayerIdentity, right.PlayerIdentity)
            || left.CurrentToolIndex != right.CurrentToolIndex
            || left.Backpack.Count != right.Backpack.Count
            || left.Equipment.Count != right.Equipment.Count)
            return false;
        for (var index = 0; index < left.Backpack.Count; index++)
        {
            if (!SameItem(left.Backpack[index], right.Backpack[index]))
                return false;
        }
        return left.Equipment.All(slot => SameItem(
            slot.Item,
            FindSlot(right, slot.Kind, slot.Index)?.Item
        ));
    }

    private static bool SameItem(EquipmentRuntimeItem? left, EquipmentRuntimeItem? right) =>
        left is null || right is null
            ? left is null && right is null
            : ReferenceEquals(left.Identity, right.Identity)
                && left.Stack == right.Stack
                && left.MaximumStack == right.MaximumStack
                && string.Equals(left.QualifiedItemId, right.QualifiedItemId, StringComparison.Ordinal);

    private static EquipmentRuntimeSlot? FindSlot(
        EquipmentSlotCapture capture,
        UiEquipmentSlotKind kind,
        int index
    ) => capture.Equipment.SingleOrDefault(slot => slot.Kind == kind && slot.Index == index);

    private static ContinuationStep Failed(ErrorCode code, string message) =>
        new ContinuationStep.Failed(Error(code, message));
    private static Error Error(ErrorCode code, string message) => new()
    {
        Code = code,
        Message = message,
    };

    private sealed record PreparedEquipmentSlotMutation(
        object MenuIdentity,
        object PageIdentity,
        object PlayerIdentity,
        object Component,
        UiEquipmentSlotKind Kind,
        int Index,
        EquipmentSlotMutationPlan Plan
    );
    private sealed record PreparedResult(PreparedEquipmentSlotMutation? Value, Error? Error);
    private sealed record PlanResult(EquipmentSlotMutationPlan? Value, Error? Error)
    {
        public static PlanResult ErrorResult(Error error) => new(null, error);
    }
    private sealed record CaptureResult(EquipmentSlotCapture? Value, Error? Error);
    private sealed record SourceResult(int Slot, EquipmentRuntimeItem? Item, Error? Error);
    private sealed record EquipmentResolution(
        UiEquipmentSlotKind Kind,
        int Index,
        object? Component,
        Error? Error
    );
}

internal sealed class EquipmentSlotPostconditionException : Exception { }
