using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Objects.Trinkets;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal enum EquipmentSlotMutationKind
{
    Wear,
    Replace,
    Clear,
    NoChange,
}

internal enum EquipmentSlotMutationPlanStatus
{
    Ready,
    Invalid,
    Stale,
    NotReady,
}

internal sealed record EquipmentSlotMutationPlan(
    EquipmentSlotMutationKind Kind,
    int? SourceSlot,
    object? SourceIdentity,
    int? BackpackDestinationSlot,
    object? EquipmentBefore
)
{
    public bool Changed => Kind != EquipmentSlotMutationKind.NoChange;
}

internal sealed record EquipmentSlotMutationPlanResult(
    EquipmentSlotMutationPlanStatus Status,
    EquipmentSlotMutationPlan? Plan,
    string Message
);

/// <summary>只规划单个装备槽与玩家背包之间的对象归属，不执行游戏 API。</summary>
internal static class EquipmentSlotMutationPlanner
{
    public static EquipmentSlotMutationPlanResult Plan(
        bool clear,
        int? sourceSlot,
        object? sourceIdentity,
        IReadOnlyList<object?> backpack,
        object? equipmentBefore
    )
    {
        if (clear)
        {
            if (sourceSlot is not null || sourceIdentity is not null)
                return Invalid("清空装备槽不能同时提供源物品");
            if (equipmentBefore is null)
                return Ready(new EquipmentSlotMutationPlan(
                    EquipmentSlotMutationKind.NoChange,
                    null,
                    null,
                    null,
                    null
                ));
            var destination = FirstEmpty(backpack);
            return destination < 0
                ? new EquipmentSlotMutationPlanResult(
                    EquipmentSlotMutationPlanStatus.NotReady,
                    null,
                    "玩家背包已满；请先腾出一个背包格并重新查询"
                )
                : Ready(new EquipmentSlotMutationPlan(
                    EquipmentSlotMutationKind.Clear,
                    null,
                    null,
                    destination,
                    equipmentBefore
                ));
        }

        if (sourceSlot is null || sourceIdentity is null)
            return Invalid("穿戴装备必须提供源物品");
        if (sourceSlot < 0 || sourceSlot >= backpack.Count
            || !ReferenceEquals(backpack[sourceSlot.Value], sourceIdentity))
            return new EquipmentSlotMutationPlanResult(
                EquipmentSlotMutationPlanStatus.Stale,
                null,
                "源背包物品已变化"
            );
        return Ready(new EquipmentSlotMutationPlan(
            equipmentBefore is null
                ? EquipmentSlotMutationKind.Wear
                : EquipmentSlotMutationKind.Replace,
            sourceSlot,
            sourceIdentity,
            equipmentBefore is null ? null : sourceSlot,
            equipmentBefore
        ));
    }

    public static bool SamePlan(
        EquipmentSlotMutationPlan left,
        EquipmentSlotMutationPlan right
    ) => left.Kind == right.Kind
        && left.SourceSlot == right.SourceSlot
        && ReferenceEquals(left.SourceIdentity, right.SourceIdentity)
        && left.BackpackDestinationSlot == right.BackpackDestinationSlot
        && ReferenceEquals(left.EquipmentBefore, right.EquipmentBefore);

    private static int FirstEmpty(IReadOnlyList<object?> backpack)
    {
        for (var index = 0; index < backpack.Count; index++)
        {
            if (backpack[index] is null)
                return index;
        }
        return -1;
    }

    private static EquipmentSlotMutationPlanResult Ready(
        EquipmentSlotMutationPlan plan
    ) => new(EquipmentSlotMutationPlanStatus.Ready, plan, "");

    private static EquipmentSlotMutationPlanResult Invalid(string message) =>
        new(EquipmentSlotMutationPlanStatus.Invalid, null, message);
}

internal enum EquipmentSlotMutationPoint
{
    SourceRemoved,
    EquipmentChanged,
    DestinationWritten,
}

/// <summary>
/// O14a 专用写入后端。装备交换必须返回交换前对象；Shape 只用于恢复 Trinket 列表长度。
/// </summary>
internal interface IEquipmentSlotMutationBackend
{
    object? ReadBackpack(int slot);
    void WriteBackpack(int slot, object? item);
    object? ReadEquipment();
    object? ExchangeEquipment(object? item);
    void RestoreShape();
}

internal interface IEquipmentSlotMutationCommit
{
    void Complete();
    void Rollback();
}

/// <summary>只执行一个装备槽计划，并持有本次写入的局部回滚日志。</summary>
internal static class EquipmentSlotMutationExecutor
{
    public static IEquipmentSlotMutationCommit Commit(
        IEquipmentSlotMutationBackend backend,
        EquipmentSlotMutationPlan plan,
        Action<EquipmentSlotMutationPoint>? afterMutation = null
    )
    {
        var journal = new EquipmentSlotMutationJournal(backend, plan);
        if (!plan.Changed)
            return journal;
        try
        {
            if (plan.SourceSlot is int sourceSlot)
            {
                if (!ReferenceEquals(
                        backend.ReadBackpack(sourceSlot),
                        plan.SourceIdentity
                    ))
                    throw new InvalidOperationException("源背包物品已变化");
                backend.WriteBackpack(sourceSlot, null);
                afterMutation?.Invoke(EquipmentSlotMutationPoint.SourceRemoved);
            }

            if (!ReferenceEquals(backend.ReadEquipment(), plan.EquipmentBefore))
                throw new InvalidOperationException("目标装备已变化");
            var oldEquipment = backend.ExchangeEquipment(plan.SourceIdentity);
            if (!ReferenceEquals(oldEquipment, plan.EquipmentBefore))
                throw new InvalidOperationException("装备交换结果不一致");
            afterMutation?.Invoke(EquipmentSlotMutationPoint.EquipmentChanged);

            if (plan.EquipmentBefore is not null)
            {
                if (plan.BackpackDestinationSlot is not int destination
                    || backend.ReadBackpack(destination) is not null)
                    throw new InvalidOperationException("背包目的 Slot 已变化");
                backend.WriteBackpack(destination, plan.EquipmentBefore);
                afterMutation?.Invoke(EquipmentSlotMutationPoint.DestinationWritten);
            }
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
                throw new EquipmentSlotMutationRollbackException(
                    mutationError,
                    rollbackError
                );
            }
            throw;
        }
    }

    private sealed class EquipmentSlotMutationJournal : IEquipmentSlotMutationCommit
    {
        private readonly IEquipmentSlotMutationBackend _backend;
        private readonly EquipmentSlotMutationPlan _plan;
        private readonly IReadOnlyDictionary<int, object?> _backpackBefore;
        private bool _finished;

        public EquipmentSlotMutationJournal(
            IEquipmentSlotMutationBackend backend,
            EquipmentSlotMutationPlan plan
        )
        {
            _backend = backend;
            _plan = plan;
            var slots = new HashSet<int>();
            if (plan.SourceSlot is int source)
                slots.Add(source);
            if (plan.BackpackDestinationSlot is int destination)
                slots.Add(destination);
            _backpackBefore = slots.ToDictionary(slot => slot, backend.ReadBackpack);
        }

        public void Complete() => _finished = true;

        public void Rollback()
        {
            if (_finished)
                return;
            Exception? rollbackError = null;
            var mayRestoreEquipment = true;
            if (_plan.EquipmentBefore is not null
                && _plan.BackpackDestinationSlot is int destination)
            {
                try
                {
                    var destinationItem = _backend.ReadBackpack(destination);
                    if (ReferenceEquals(destinationItem, _plan.EquipmentBefore))
                        _backend.WriteBackpack(destination, null);
                    else if (destinationItem is not null)
                    {
                        mayRestoreEquipment = false;
                        rollbackError = new InvalidOperationException(
                            "回滚目的 Slot 已被其他对象占用"
                        );
                    }
                }
                catch (Exception error)
                {
                    mayRestoreEquipment = false;
                    rollbackError = error;
                }
            }
            if (mayRestoreEquipment)
            {
                try
                {
                    if (!ReferenceEquals(
                            _backend.ReadEquipment(),
                            _plan.EquipmentBefore
                        ))
                        _backend.ExchangeEquipment(_plan.EquipmentBefore);
                }
                catch (Exception error)
                {
                    rollbackError ??= error;
                }
            }
            foreach (var pair in _backpackBefore.OrderBy(pair => pair.Key))
            {
                try
                {
                    _backend.WriteBackpack(pair.Key, pair.Value);
                }
                catch (Exception error)
                {
                    rollbackError ??= error;
                }
            }
            try
            {
                _backend.RestoreShape();
            }
            catch (Exception error)
            {
                rollbackError ??= error;
            }
            _finished = true;
            if (rollbackError is not null)
                throw rollbackError;
        }
    }
}

internal sealed class EquipmentSlotMutationRollbackException : Exception
{
    public EquipmentSlotMutationRollbackException(
        Exception mutationError,
        Exception rollbackError
    ) : base(
        "装备槽写入失败，且局部回滚存在错误",
        new AggregateException(mutationError, rollbackError)
    ) { }
}

internal enum EquipmentSlotCaptureStatus
{
    Ready,
    NotReady,
    Unsupported,
    Unavailable,
}

internal sealed record EquipmentRuntimeItem(
    object Identity,
    string QualifiedItemId,
    int Stack,
    int MaximumStack,
    ItemFact PublicFact
);

internal sealed record EquipmentRuntimeSlot(
    UiEquipmentSlotKind Kind,
    int Index,
    object Component,
    EquipmentRuntimeItem? Item
);

internal sealed record EquipmentSlotCapture(
    EquipmentSlotCaptureStatus Status,
    object? MenuIdentity,
    object? PageIdentity,
    object? PlayerIdentity,
    string UiRevision,
    InventorySnapshot? PlayerSnapshot,
    IReadOnlyList<EquipmentRuntimeItem?> Backpack,
    IReadOnlyList<EquipmentRuntimeSlot> Equipment,
    int CurrentToolIndex,
    object? CommitState
)
{
    public static EquipmentSlotCapture NotReady() => Empty(EquipmentSlotCaptureStatus.NotReady);
    public static EquipmentSlotCapture Unsupported() => Empty(EquipmentSlotCaptureStatus.Unsupported);
    public static EquipmentSlotCapture Unavailable() => Empty(EquipmentSlotCaptureStatus.Unavailable);

    private static EquipmentSlotCapture Empty(EquipmentSlotCaptureStatus status) => new(
        status,
        null,
        null,
        null,
        "",
        null,
        Array.Empty<EquipmentRuntimeItem?>(),
        Array.Empty<EquipmentRuntimeSlot>(),
        -1,
        null
    );
}

internal interface IEquipmentSlotRuntimeAdapter
{
    EquipmentSlotCapture Capture();
    bool IsSupported(EquipmentRuntimeItem item, UiEquipmentSlotKind kind);
    IEquipmentSlotMutationCommit Commit(
        EquipmentSlotCapture capture,
        UiEquipmentSlotKind kind,
        int index,
        EquipmentSlotMutationPlan plan
    );
}

/// <summary>O14a 对原版装备对象的静态兼容门禁。</summary>
internal static class EquipmentSlotCompatibility
{
    private static readonly IReadOnlySet<string> SpecialItemIds = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "(T)Pan",
        "(T)SteelPan",
        "(T)GoldPan",
        "(T)IridiumPan",
        "(O)71",
        "(P)15",
        "(H)71",
        "(H)SteelPanHat",
        "(H)GoldPanHat",
        "(H)IridiumPanHat",
    };

    internal static bool IsSpecial(string qualifiedItemId) =>
        SpecialItemIds.Contains(qualifiedItemId);

    internal static bool IsSupported(Item item, UiEquipmentSlotKind kind)
    {
        if (IsSpecial(item.QualifiedItemId)
            || item.Stack != 1
            || item.maximumStackSize() != 1)
            return false;
        return kind switch
        {
            UiEquipmentSlotKind.Hat => item.GetType() == typeof(Hat),
            UiEquipmentSlotKind.LeftRing or UiEquipmentSlotKind.RightRing =>
                IsSupportedRing(item),
            UiEquipmentSlotKind.Boots => item.GetType() == typeof(Boots),
            UiEquipmentSlotKind.Shirt => item.GetType() == typeof(Clothing)
                && ((Clothing)item).clothesType.Value == Clothing.ClothesType.SHIRT,
            UiEquipmentSlotKind.Pants => item.GetType() == typeof(Clothing)
                && ((Clothing)item).clothesType.Value == Clothing.ClothesType.PANTS,
            UiEquipmentSlotKind.Trinket => item.GetType() == typeof(Trinket),
            _ => false,
        };
    }

    private static bool IsSupportedRing(Item item)
    {
        if (item.GetType() == typeof(Ring))
            return true;
        if (item.GetType() != typeof(CombinedRing))
            return false;
        return ((CombinedRing)item).combinedRings.All(ring =>
            ring is not null && ring.GetType() == typeof(Ring));
    }
}

internal sealed class LiveEquipmentSlotRuntimeAdapter : IEquipmentSlotRuntimeAdapter
{
    private readonly OpaqueRefStore _refs;

    public LiveEquipmentSlotRuntimeAdapter(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    public EquipmentSlotCapture Capture()
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return EquipmentSlotCapture.NotReady();
        if (Game1.activeClickableMenu is not { } active
            || active.GetType() != typeof(GameMenu))
            return EquipmentSlotCapture.Unsupported();

        try
        {
            var menu = (GameMenu)active;
            if (menu.currentTab != GameMenu.inventoryTab
                || menu.currentTab < 0
                || menu.currentTab >= menu.pages.Count
                || menu.pages[menu.currentTab] is not { } current
                || current.GetType() != typeof(InventoryPage)
                || player.CursorSlotItem is not null)
                return EquipmentSlotCapture.NotReady();

            var page = (InventoryPage)current;
            var playerView = InventoryViewResolver.CreatePlayerForMenu(
                player,
                page.inventory.capacity
            );
            if (!InventoryPageProjector.IsCompleteBackpackMenu(page.inventory, playerView))
                return EquipmentSlotCapture.NotReady();

            var ui = UiRuntimeProjector.Capture(menu, player, _refs);
            if (ui.ElementSetCompleteness != UiElementSetCompleteness.Complete
                || ui.Result.Snapshot.Menu?.MenuKind != MenuKind.Inventory)
                return EquipmentSlotCapture.NotReady();
            var link = ui.Result.Snapshot.Inventories.SingleOrDefault(value =>
                value.Side == UiInventorySide.Player);
            if (link is null)
                return EquipmentSlotCapture.Unavailable();

            var snapshot = InventoryProjector.Project(
                playerView,
                _refs,
                includeEmptySlots: true
            );
            if (!string.Equals(
                    link.InventoryRevision,
                    snapshot.InventoryRevision,
                    StringComparison.Ordinal
                )
                || snapshot.SlotCount != playerView.Capacity
                || playerView.Slots.Count != playerView.Capacity)
                return EquipmentSlotCapture.Unavailable();

            var equipment = CaptureEquipment(page, player);
            if (equipment is null)
                return EquipmentSlotCapture.Unavailable();
            var backpack = playerView.Slots.Select(RuntimeItem).ToArray();
            if (HasDuplicateIdentity(backpack, equipment))
                return EquipmentSlotCapture.Unavailable();

            return new EquipmentSlotCapture(
                EquipmentSlotCaptureStatus.Ready,
                menu,
                page,
                player,
                ui.Result.Snapshot.UiRevision,
                snapshot,
                backpack,
                equipment,
                player.CurrentToolIndex,
                new LiveCommitState(player, playerView.BackingIdentity)
            );
        }
        catch
        {
            return EquipmentSlotCapture.Unavailable();
        }
    }

    public IEquipmentSlotMutationCommit Commit(
        EquipmentSlotCapture capture,
        UiEquipmentSlotKind kind,
        int index,
        EquipmentSlotMutationPlan plan
    )
    {
        if (capture.CommitState is not LiveCommitState state
            || !ReferenceEquals(state.Player, Game1.player)
            || !ReferenceEquals(state.BackpackIdentity, state.Player.Items)
            || state.Player.CurrentToolIndex != capture.CurrentToolIndex)
            throw new InvalidOperationException("装备槽提交上下文已变化");
        var backend = new LiveMutationBackend(state.Player, kind, index);
        return EquipmentSlotMutationExecutor.Commit(backend, plan);
    }

    public bool IsSupported(EquipmentRuntimeItem item, UiEquipmentSlotKind kind) =>
        item.Identity is Item runtime
        && EquipmentSlotCompatibility.IsSupported(runtime, kind);

    private static IReadOnlyList<EquipmentRuntimeSlot>? CaptureEquipment(
        InventoryPage page,
        Farmer player
    )
    {
        var output = new List<EquipmentRuntimeSlot>();
        var identities = new HashSet<(UiEquipmentSlotKind Kind, int Index)>();
        foreach (var component in page.equipmentIcons)
        {
            if (component is null
                || !InventoryPageProjector.TryClassifyEquipmentComponent(
                    component.name,
                    component.myID,
                    out var kind,
                    out var index
                )
                || !identities.Add((kind, index)))
                return null;
            output.Add(new EquipmentRuntimeSlot(
                kind,
                index,
                component,
                RuntimeItem(ReadEquipment(player, kind, index))
            ));
        }
        return output;
    }

    private static bool HasDuplicateIdentity(
        IReadOnlyList<EquipmentRuntimeItem?> backpack,
        IReadOnlyList<EquipmentRuntimeSlot> equipment
    )
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var item in backpack.Concat(equipment.Select(slot => slot.Item)))
        {
            if (item is not null && !seen.Add(item.Identity))
                return true;
        }
        return false;
    }

    private static EquipmentRuntimeItem? RuntimeItem(Item? item) => item is null
        ? null
        : new EquipmentRuntimeItem(
            item,
            item.QualifiedItemId,
            item.Stack,
            item.maximumStackSize(),
            ItemFactProjector.Project(item)
        );

    private static Item? ReadEquipment(Farmer player, UiEquipmentSlotKind kind, int index) =>
        kind switch
        {
            UiEquipmentSlotKind.Hat => player.hat.Value,
            UiEquipmentSlotKind.LeftRing => player.leftRing.Value,
            UiEquipmentSlotKind.RightRing => player.rightRing.Value,
            UiEquipmentSlotKind.Boots => player.boots.Value,
            UiEquipmentSlotKind.Shirt => player.shirtItem.Value,
            UiEquipmentSlotKind.Pants => player.pantsItem.Value,
            UiEquipmentSlotKind.Trinket when index >= 0 && index < player.trinketItems.Count =>
                player.trinketItems[index],
            UiEquipmentSlotKind.Trinket => null,
            _ => throw new InvalidOperationException("装备槽类型不受支持"),
        };

    private sealed record LiveCommitState(Farmer Player, object BackpackIdentity);

    private sealed class LiveMutationBackend : IEquipmentSlotMutationBackend
    {
        private readonly Farmer _player;
        private readonly UiEquipmentSlotKind _kind;
        private readonly int _index;
        private readonly int _trinketLength;

        public LiveMutationBackend(Farmer player, UiEquipmentSlotKind kind, int index)
        {
            _player = player;
            _kind = kind;
            _index = index;
            _trinketLength = player.trinketItems.Count;
        }

        public object? ReadBackpack(int slot) =>
            slot >= 0 && slot < _player.MaxItems && slot < _player.Items.Count
                ? _player.Items[slot]
                : null;

        public void WriteBackpack(int slot, object? value)
        {
            if (slot < 0 || slot >= _player.MaxItems || value is not null and not Item)
                throw new InvalidOperationException("玩家背包写入无效");
            while (_player.Items.Count <= slot)
                _player.Items.Add(null);
            var before = _player.Items[slot];
            if (ReferenceEquals(before, value))
                return;
            if (slot == _player.CurrentToolIndex && before is not null)
                before.actionWhenStopBeingHeld(_player);
            before?.onDetachedFromParent();
            ((Item?)value)?.onDetachedFromParent();
            _player.Items[slot] = (Item?)value;
            if (slot == _player.CurrentToolIndex && value is Item item)
                item.actionWhenBeingHeld(_player);
        }

        public object? ReadEquipment() =>
            LiveEquipmentSlotRuntimeAdapter.ReadEquipment(_player, _kind, _index);

        public object? ExchangeEquipment(object? value)
        {
            if (value is not null and not Item)
                throw new InvalidOperationException("装备槽写入对象无效");
            var item = (Item?)value;
            return _kind switch
            {
                UiEquipmentSlotKind.Hat => _player.Equip((Hat?)item, _player.hat),
                UiEquipmentSlotKind.LeftRing => _player.Equip((Ring?)item, _player.leftRing),
                UiEquipmentSlotKind.RightRing => _player.Equip((Ring?)item, _player.rightRing),
                UiEquipmentSlotKind.Boots => _player.Equip((Boots?)item, _player.boots),
                UiEquipmentSlotKind.Shirt => _player.Equip((Clothing?)item, _player.shirtItem),
                UiEquipmentSlotKind.Pants => _player.Equip((Clothing?)item, _player.pantsItem),
                UiEquipmentSlotKind.Trinket => ExchangeTrinket((Trinket?)item),
                _ => throw new InvalidOperationException("装备槽类型不受支持"),
            };
        }

        public void RestoreShape()
        {
            while (_player.trinketItems.Count > _trinketLength)
                _player.trinketItems.RemoveAt(_player.trinketItems.Count - 1);
        }

        private Trinket? ExchangeTrinket(Trinket? item)
        {
            if (_index < 0 || _index >= Farmer.MaximumTrinkets)
                throw new InvalidOperationException("饰品槽序号无效");
            while (_player.trinketItems.Count <= _index)
                _player.trinketItems.Add(null);
            var old = _player.trinketItems[_index];
            old?.onDetachedFromParent();
            item?.onDetachedFromParent();
            _player.trinketItems[_index] = item;
            return old;
        }
    }
}
