using StardewModdingAPI;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

/// <summary>
/// 执行 <c>equip</c> 所需的最小玩家背包变更。Ref 解析、Revision 比较与变更
/// 均在游戏主线程的命令 Continuation 中完成；Validate 只检查结构。
/// </summary>
internal sealed class EquipHandler : ILongRunningCapabilityHandler
{
    private readonly OpaqueRefStore _refs;
    private readonly IEquipInventoryAdapter _inventory;

    public EquipHandler(OpaqueRefStore refs)
        : this(refs, new LivePlayerEquipInventoryAdapter(refs))
    {
    }

    internal EquipHandler(OpaqueRefStore refs, IEquipInventoryAdapter inventory)
    {
        _refs = refs;
        _inventory = inventory;
    }

    public string Id => "equip";

    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.Equip;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("equip 请求类型无效");

        var equip = request.Equip;
        if (equip.SelectorCase == EquipRequest.SelectorOneofCase.None)
            return Invalid("equip 必须提供 slot_index 或 item_ref");

        if (equip.SelectorCase == EquipRequest.SelectorOneofCase.ItemRef)
        {
            if (!PublicStringPolicy.IsNonEmptyValid(equip.ItemRef?.Value))
                return Invalid("item_ref 格式无效");
            if (!IsRevisionToken(equip.InventoryRevision))
                return Invalid("item_ref 必须提供 inventory_revision");
        }
        else if (equip.HasInventoryRevision && !IsRevisionToken(equip.InventoryRevision))
        {
            return Invalid("inventory_revision 格式无效");
        }

        return null;
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new EquipContinuation(_refs, _inventory, request.Equip);

    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };

    private static bool IsRevisionToken(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>
/// 玩家背包的窄适配器；实现必须返回同一 Tick 捕获的完整 Snapshot、空槽与对象身份。
/// </summary>
internal interface IEquipInventoryAdapter
{
    EquipInventoryCapture Capture();
    void SetCurrentToolIndex(int slot);
}

internal enum EquipInventoryCaptureStatus
{
    Ready,
    NotReady,
    Unavailable,
}

internal sealed record EquipInventoryCapture(
    EquipInventoryCaptureStatus Status,
    InventorySnapshot? Snapshot,
    IReadOnlyList<object?> Items,
    int CurrentToolIndex
)
{
    public static EquipInventoryCapture NotReady() =>
        new(EquipInventoryCaptureStatus.NotReady, null, Array.Empty<object?>(), -1);

    public static EquipInventoryCapture Unavailable() =>
        new(EquipInventoryCaptureStatus.Unavailable, null, Array.Empty<object?>(), -1);
}

internal sealed class LivePlayerEquipInventoryAdapter : IEquipInventoryAdapter
{
    private readonly OpaqueRefStore _refs;

    public LivePlayerEquipInventoryAdapter(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    public EquipInventoryCapture Capture()
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return EquipInventoryCapture.NotReady();

        try
        {
            var view = InventoryViewResolver.CreatePlayer(player);
            var snapshot = InventoryProjector.Project(view, _refs, includeEmptySlots: true);
            return new EquipInventoryCapture(
                EquipInventoryCaptureStatus.Ready,
                snapshot,
                view.Slots.Cast<object?>().ToArray(),
                player.CurrentToolIndex
            );
        }
        catch
        {
            return EquipInventoryCapture.Unavailable();
        }
    }

    public void SetCurrentToolIndex(int slot)
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            throw new InvalidOperationException("世界尚未就绪");
        player.CurrentToolIndex = slot;
    }
}

internal sealed class EquipContinuation : ICommandContinuation
{
    private readonly OpaqueRefStore _refs;
    private readonly IEquipInventoryAdapter _inventory;
    private readonly EquipRequest _request;
    private ResolvedEquipSelection? _selection;

    public EquipContinuation(
        OpaqueRefStore refs,
        IEquipInventoryAdapter inventory,
        EquipRequest request
    )
    {
        _refs = refs;
        _inventory = inventory;
        _request = request.Clone();
    }

    public string Phase => _selection is null ? "resolving" : "ready_to_commit";
    public uint? ProgressPercent => _selection is null ? 0u : 50u;
    public bool CanCancel => true;

    public ContinuationStep Tick(ContinuationStopSignal signal)
    {
        if (signal != ContinuationStopSignal.None)
            return new ContinuationStep.Stopped();

        if (_selection is null)
        {
            var resolved = ResolveSelection();
            if (resolved.Error is not null)
                return new ContinuationStep.Failed(resolved.Error);
            _selection = resolved.Selection!;
            return new ContinuationStep.Pending();
        }

        var selection = _selection;
        var current = Capture();
        if (current.Error is not null)
            return new ContinuationStep.Failed(current.Error);
        if (!Matches(selection, current.Value!))
            return Failed(ErrorCode.StaleRef, "玩家背包或目标物品已变化");

        if (current.Value!.CurrentToolIndex == selection.Slot)
            return Succeeded(selection.Slot, selection.Item, changed: false);

        try
        {
            _inventory.SetCurrentToolIndex(selection.Slot);
        }
        catch
        {
            return Failed(ErrorCode.ExecutionFailed, "无法切换当前装备");
        }

        var after = Capture();
        if (after.Error is not null)
            return new ContinuationStep.Failed(after.Error);
        if (after.Value!.CurrentToolIndex != selection.Slot
            || !MatchesSlotAndIdentity(after.Value, selection.Slot, selection.Identity))
            return Failed(ErrorCode.ExecutionFailed, "装备后置条件未成立");

        return Succeeded(selection.Slot, ItemAt(after.Value, selection.Slot)!, changed: true);
    }

    private SelectionResolution ResolveSelection()
    {
        var capture = Capture();
        if (capture.Error is not null)
            return new SelectionResolution(null, capture.Error);

        var value = capture.Value!;
        return _request.SelectorCase switch
        {
            EquipRequest.SelectorOneofCase.SlotIndex => ResolveSlot(value, _request.SlotIndex),
            EquipRequest.SelectorOneofCase.ItemRef => ResolveReference(value, _request.ItemRef),
            _ => new SelectionResolution(null, Error(ErrorCode.InvalidArgument, "equip selector 无效")),
        };
    }

    private SelectionResolution ResolveSlot(ValidatedEquipCapture capture, uint requestedSlot)
    {
        if (requestedSlot > int.MaxValue || requestedSlot >= capture.Snapshot.SlotCount)
            return new SelectionResolution(null, Error(ErrorCode.OutOfRange, "slot_index 超出玩家背包范围"));
        if (_request.HasInventoryRevision
            && !string.Equals(_request.InventoryRevision, capture.Snapshot.InventoryRevision, StringComparison.Ordinal))
            return new SelectionResolution(null, Error(ErrorCode.StaleRef, "inventory_revision 已失效"));

        var slot = checked((int)requestedSlot);
        var identity = capture.Items[slot];
        var item = ItemAt(capture, slot);
        if (identity is null || item is null)
            return new SelectionResolution(null, Error(ErrorCode.NotFound, "目标 Slot 为空"));
        return new SelectionResolution(
            new ResolvedEquipSelection(slot, identity, item.Clone(), capture.Snapshot.InventoryRevision),
            null
        );
    }

    private SelectionResolution ResolveReference(ValidatedEquipCapture capture, Ref reference)
    {
        var resolution = _refs.ResolveInventoryItem(reference);
        if (resolution.Status != InventoryItemResolveStatus.Resolved || resolution.Target is null)
        {
            var error = resolution.Status switch
            {
                InventoryItemResolveStatus.Stale => Error(ErrorCode.StaleRef, "item_ref 已失效"),
                InventoryItemResolveStatus.Unsupported => Error(ErrorCode.InvalidArgument, "item_ref 类型无效"),
                InventoryItemResolveStatus.NotFound => Error(ErrorCode.NotFound, "item_ref 不存在"),
                _ => Error(ErrorCode.Internal, "item_ref 无法解析"),
            };
            return new SelectionResolution(null, error);
        }
        if (resolution.Target.Provenance != InventoryItemProvenance.Player)
            return new SelectionResolution(null, Error(ErrorCode.InvalidArgument, "item_ref 不属于玩家背包"));
        if (!string.Equals(_request.InventoryRevision, capture.Snapshot.InventoryRevision, StringComparison.Ordinal))
            return new SelectionResolution(null, Error(ErrorCode.StaleRef, "inventory_revision 已失效"));

        var slot = resolution.Target.Slot;
        if (slot < 0 || slot >= capture.Items.Count)
            return new SelectionResolution(null, Error(ErrorCode.StaleRef, "item_ref 已失效"));
        var item = ItemAt(capture, slot);
        if (item is null || !ReferenceEquals(capture.Items[slot], resolution.Target.Target))
            return new SelectionResolution(null, Error(ErrorCode.StaleRef, "item_ref 已失效"));
        return new SelectionResolution(
            new ResolvedEquipSelection(slot, resolution.Target.Target, item.Clone(), capture.Snapshot.InventoryRevision),
            null
        );
    }

    private CaptureResolution Capture()
    {
        var capture = _inventory.Capture();
        return capture.Status switch
        {
            EquipInventoryCaptureStatus.NotReady => new CaptureResolution(
                null,
                Error(ErrorCode.NotReady, "世界尚未就绪")
            ),
            EquipInventoryCaptureStatus.Unavailable => new CaptureResolution(
                null,
                Error(ErrorCode.Internal, "玩家背包不可读")
            ),
            EquipInventoryCaptureStatus.Ready => ValidateCapture(capture),
            _ => new CaptureResolution(null, Error(ErrorCode.Internal, "玩家背包状态无效")),
        };
    }

    private static CaptureResolution ValidateCapture(EquipInventoryCapture capture)
    {
        if (capture.Snapshot is null
            || capture.Snapshot.SlotCount > int.MaxValue
            || capture.Items.Count != checked((int)capture.Snapshot.SlotCount)
            || capture.Snapshot.Slots.Count != capture.Items.Count
            || !IsRevision(capture.Snapshot.InventoryRevision))
            return new CaptureResolution(null, Error(ErrorCode.Internal, "玩家背包快照无效"));
        for (var index = 0; index < capture.Items.Count; index++)
        {
            var slot = capture.Snapshot.Slots[index];
            if (slot.Index != (uint)index || (capture.Items[index] is null) != (slot.Item is null))
                return new CaptureResolution(null, Error(ErrorCode.Internal, "玩家背包快照无效"));
        }
        return new CaptureResolution(
            new ValidatedEquipCapture(capture.Snapshot, capture.Items, capture.CurrentToolIndex),
            null
        );
    }

    private static bool Matches(ResolvedEquipSelection selection, ValidatedEquipCapture capture) =>
        string.Equals(selection.RevisionBeforeCommit, capture.Snapshot.InventoryRevision, StringComparison.Ordinal)
        && MatchesSlotAndIdentity(capture, selection.Slot, selection.Identity);

    private static bool MatchesSlotAndIdentity(
        ValidatedEquipCapture capture,
        int slot,
        object identity
    ) => slot >= 0
        && slot < capture.Items.Count
        && capture.Items[slot] is not null
        && ItemAt(capture, slot) is not null
        && ReferenceEquals(capture.Items[slot], identity);

    private static ItemFact? ItemAt(ValidatedEquipCapture capture, int slot) =>
        slot >= 0 && slot < capture.Snapshot.Slots.Count
            ? capture.Snapshot.Slots[slot].Item
            : null;

    private static ContinuationStep Failed(ErrorCode code, string message) =>
        new ContinuationStep.Failed(Error(code, message));

    private static ContinuationStep Succeeded(int slot, ItemFact item, bool changed) =>
        new ContinuationStep.Succeeded(new CapabilityResult
        {
            Equip = new EquipResult
            {
                SlotIndex = checked((uint)slot),
                Item = item.Clone(),
                Changed = changed,
            },
        });

    private static Error Error(ErrorCode code, string message) => new() { Code = code, Message = message };
    private static bool IsRevision(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record ResolvedEquipSelection(
        int Slot,
        object Identity,
        ItemFact Item,
        string RevisionBeforeCommit
    );

    private sealed record ValidatedEquipCapture(
        InventorySnapshot Snapshot,
        IReadOnlyList<object?> Items,
        int CurrentToolIndex
    );

    private sealed record CaptureResolution(ValidatedEquipCapture? Value, Error? Error);
    private sealed record SelectionResolution(ResolvedEquipSelection? Selection, Error? Error);
}
