using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;
using SObject = StardewValley.Object;

namespace StardewValleyMcp.Mod;

internal enum DiscardInventoryCaptureStatus
{
    Ready,
    NotReady,
    Unavailable,
}

internal sealed record DiscardInventoryItemRuntimeFact(
    object Identity,
    int Stack,
    string Guard
);

internal sealed record DiscardInventoryCapture(
    DiscardInventoryCaptureStatus Status,
    object? MenuIdentity,
    object? PageIdentity,
    object? PlayerIdentity,
    object? BackingIdentity,
    InventorySnapshot? PlayerSnapshot,
    IReadOnlyList<DiscardInventoryItemRuntimeFact?> Backpack,
    int CurrentToolIndex,
    object? CurrentItemIdentity,
    int Money,
    int TrashCanLevel,
    IReadOnlyList<string> SpecialItems,
    object? CommitState
)
{
    public static DiscardInventoryCapture NotReady() =>
        Empty(DiscardInventoryCaptureStatus.NotReady);

    public static DiscardInventoryCapture Unavailable() =>
        Empty(DiscardInventoryCaptureStatus.Unavailable);

    private static DiscardInventoryCapture Empty(DiscardInventoryCaptureStatus status) =>
        new(
            status,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<DiscardInventoryItemRuntimeFact?>(),
            -1,
            null,
            -1,
            -1,
            Array.Empty<string>(),
            null
        );
}

internal sealed record DiscardInventoryPlan(
    int SourceSlot,
    object SourceIdentity,
    int SourceStack,
    int Quantity,
    object TrashIdentity,
    int ExpectedRemaining,
    int CurrentToolIndex,
    object? CurrentItemIdentity,
    int MoneyBefore,
    int ExpectedMoneyAfter,
    IReadOnlyList<string> SpecialItemsBefore,
    IReadOnlyList<string> ExpectedSpecialItemsAfter
);

internal interface IDiscardInventoryItemRuntimeAdapter
{
    DiscardInventoryCapture Capture();
    bool CanBeTrashed(DiscardInventoryCapture capture, int sourceSlot);
    DiscardInventoryPlan PrepareCommit(
        DiscardInventoryCapture capture,
        int sourceSlot,
        int quantity
    );
    void Commit(DiscardInventoryCapture capture, DiscardInventoryPlan plan);
}

internal interface ITrashSemantics
{
    bool CanBeTrashed(object item);
    object CloneForQuantity(object item, int quantity);
    int GetReclamationPrice(object item, object player);
    string? GetSpecialItemId(object item);
    void Trash(object item);
}

internal interface IDiscardInventoryItemBackend
{
    object? ReadSlot(int slot);
    int ReadStack(object item);
    void WriteStack(object item, int stack);
    void WriteSlot(int slot, object? item);
    int CurrentToolIndex { get; }
    object? CurrentItem { get; }
    int Money { get; }
    IReadOnlyList<string> ReadSpecialItems();
    void StopBeingHeld(object item);
    void StartBeingHeld(object item);
    void Trash(object item);
}

internal static class DiscardInventoryPlanBuilder
{
    public static DiscardInventoryPlan Prepare(
        DiscardInventoryCapture capture,
        int sourceSlot,
        int quantity,
        ITrashSemantics trash
    )
    {
        if (sourceSlot < 0
            || sourceSlot >= capture.Backpack.Count
            || capture.Backpack[sourceSlot] is not { } sourceFact
            || capture.PlayerIdentity is null
            || quantity <= 0
            || quantity > sourceFact.Stack)
            throw new InvalidOperationException("丢弃提交参数已变化");
        var source = sourceFact.Identity;
        if (!trash.CanBeTrashed(source))
            throw new ItemNotDiscardableException();
        var trashItem = quantity == sourceFact.Stack
            ? source
            : trash.CloneForQuantity(source, quantity);
        if (trashItem is null
            || quantity < sourceFact.Stack && ReferenceEquals(trashItem, source)
            || !trash.CanBeTrashed(trashItem))
            throw new InvalidOperationException("原生物品拆分结果不可安全丢弃");

        // 游戏用 -1 表示没有垃圾桶回收价；trashItem 只有在价格 > 0 时才加钱。
        var refund = Math.Max(0, trash.GetReclamationPrice(trashItem, capture.PlayerIdentity));
        var moneyAfter = checked(capture.Money + refund);
        var specialAfter = capture.SpecialItems.ToList();
        if (trash.GetSpecialItemId(trashItem) is { } specialItemId)
            specialAfter.Remove(specialItemId);
        return new DiscardInventoryPlan(
            sourceSlot,
            source,
            sourceFact.Stack,
            quantity,
            trashItem,
            sourceFact.Stack - quantity,
            capture.CurrentToolIndex,
            capture.CurrentItemIdentity,
            capture.Money,
            moneyAfter,
            capture.SpecialItems.ToArray(),
            specialAfter
        );
    }
}

/// <summary>
/// Slot 拆出与原生垃圾桶调用之间只有一个明确的提交边界。进入 Trash 后不做表面补偿，
/// 因为金币累计、成就、音效与第三方回调均不能被证明可逆。
/// </summary>
internal static class DiscardInventoryItemCommitter
{
    public static void Commit(
        IDiscardInventoryItemBackend backend,
        DiscardInventoryPlan plan
    )
    {
        ValidateBefore(backend, plan);
        var stopAttempted = false;
        var stopped = false;
        try
        {
            if (plan.ExpectedRemaining > 0)
            {
                backend.WriteStack(plan.SourceIdentity, plan.ExpectedRemaining);
            }
            else
            {
                if (plan.CurrentToolIndex == plan.SourceSlot)
                {
                    stopAttempted = true;
                    backend.StopBeingHeld(plan.SourceIdentity);
                    stopped = true;
                }
                backend.WriteSlot(plan.SourceSlot, null);
            }
            ValidateDetached(backend, plan);
        }
        catch (Exception detachError)
        {
            var restored = !stopAttempted || stopped;
            restored &= TryRestoreBeforeTrash(backend, plan, stopped);
            throw new DiscardInventoryBeforeTrashException(detachError, restored);
        }

        try
        {
            backend.Trash(plan.TrashIdentity);
        }
        catch (Exception trashError)
        {
            throw new DiscardInventoryOutcomeUnknownException(trashError);
        }
    }

    private static void ValidateBefore(
        IDiscardInventoryItemBackend backend,
        DiscardInventoryPlan plan
    )
    {
        if (!ReferenceEquals(backend.ReadSlot(plan.SourceSlot), plan.SourceIdentity)
            || backend.ReadStack(plan.SourceIdentity) != plan.SourceStack
            || backend.CurrentToolIndex != plan.CurrentToolIndex
            || !ReferenceEquals(backend.CurrentItem, plan.CurrentItemIdentity)
            || backend.Money != plan.MoneyBefore
            || !SequenceEqual(backend.ReadSpecialItems(), plan.SpecialItemsBefore))
            throw new InvalidOperationException("丢弃提交上下文已变化");
    }

    private static void ValidateDetached(
        IDiscardInventoryItemBackend backend,
        DiscardInventoryPlan plan
    )
    {
        var source = backend.ReadSlot(plan.SourceSlot);
        if (plan.ExpectedRemaining > 0)
        {
            if (!ReferenceEquals(source, plan.SourceIdentity)
                || backend.ReadStack(plan.SourceIdentity) != plan.ExpectedRemaining)
                throw new InvalidOperationException("部分堆叠拆分未成立");
        }
        else if (source is not null)
        {
            throw new InvalidOperationException("完整堆叠未从背包移除");
        }

        var expectedCurrent = plan.CurrentToolIndex == plan.SourceSlot
            && plan.ExpectedRemaining == 0
                ? null
                : plan.CurrentItemIdentity;
        if (!ReferenceEquals(backend.CurrentItem, expectedCurrent)
            || backend.Money != plan.MoneyBefore
            || !SequenceEqual(backend.ReadSpecialItems(), plan.SpecialItemsBefore))
            throw new InvalidOperationException("丢弃前玩家状态发生意外变化");
    }

    private static bool TryRestoreBeforeTrash(
        IDiscardInventoryItemBackend backend,
        DiscardInventoryPlan plan,
        bool stopped
    )
    {
        try
        {
            var current = backend.ReadSlot(plan.SourceSlot);
            if (plan.ExpectedRemaining > 0)
            {
                if (!ReferenceEquals(current, plan.SourceIdentity))
                    return false;
                var stack = backend.ReadStack(plan.SourceIdentity);
                if (stack != plan.SourceStack && stack != plan.ExpectedRemaining)
                    return false;
                if (stack != plan.SourceStack)
                    backend.WriteStack(plan.SourceIdentity, plan.SourceStack);
            }
            else
            {
                if (current is not null && !ReferenceEquals(current, plan.SourceIdentity))
                    return false;
                if (current is null)
                    backend.WriteSlot(plan.SourceSlot, plan.SourceIdentity);
                if (stopped)
                    backend.StartBeingHeld(plan.SourceIdentity);
            }
            return ReferenceEquals(backend.ReadSlot(plan.SourceSlot), plan.SourceIdentity)
                && backend.ReadStack(plan.SourceIdentity) == plan.SourceStack
                && backend.CurrentToolIndex == plan.CurrentToolIndex
                && ReferenceEquals(backend.CurrentItem, plan.CurrentItemIdentity)
                && backend.Money == plan.MoneyBefore
                && SequenceEqual(backend.ReadSpecialItems(), plan.SpecialItemsBefore);
        }
        catch
        {
            return false;
        }
    }

    private static bool SequenceEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right
    ) => left.Count == right.Count && left.SequenceEqual(right, StringComparer.Ordinal);
}

internal sealed class DiscardInventoryBeforeTrashException : Exception
{
    public DiscardInventoryBeforeTrashException(Exception inner, bool rollbackConfirmed)
        : base("物品在进入游戏垃圾桶前拆出失败", inner)
    {
        RollbackConfirmed = rollbackConfirmed;
    }

    public bool RollbackConfirmed { get; }
}

internal sealed class DiscardInventoryOutcomeUnknownException : Exception
{
    public DiscardInventoryOutcomeUnknownException(Exception inner)
        : base("游戏垃圾桶调用已经开始，但提交结果无法确认", inner) { }
}

internal sealed class LiveTrashSemantics : ITrashSemantics
{
    public bool CanBeTrashed(object item) => ((Item)item).canBeTrashed();

    public object CloneForQuantity(object item, int quantity)
    {
        var clone = ((Item)item).getOne();
        clone.Stack = quantity;
        return clone;
    }

    public int GetReclamationPrice(object item, object player) =>
        Utility.getTrashReclamationPrice((Item)item, (Farmer)player);

    public string? GetSpecialItemId(object item) => (item as SObject)?.ItemId;

    public void Trash(object item) => Utility.trashItem((Item)item);
}

internal sealed class LiveDiscardInventoryItemRuntimeAdapter
    : IDiscardInventoryItemRuntimeAdapter
{
    private readonly OpaqueRefStore _refs;
    private readonly ITrashSemantics _trash;

    public LiveDiscardInventoryItemRuntimeAdapter(OpaqueRefStore refs)
        : this(refs, new LiveTrashSemantics()) { }

    internal LiveDiscardInventoryItemRuntimeAdapter(
        OpaqueRefStore refs,
        ITrashSemantics trash
    )
    {
        _refs = refs;
        _trash = trash;
    }

    public DiscardInventoryCapture Capture()
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return DiscardInventoryCapture.NotReady();
        try
        {
            if (!TryResolveAllowedPlayerState(player, out var menu, out var page))
                return DiscardInventoryCapture.NotReady();
            var view = InventoryViewResolver.CreatePlayer(player);
            if (player.CurrentToolIndex < 0
                || player.CurrentToolIndex >= view.Capacity
                || !ReferenceEquals(player.CurrentItem, view.Slots[player.CurrentToolIndex])
                || player.Money < 0
                || player.trashCanLevel < 0)
                return DiscardInventoryCapture.NotReady();
            var snapshot = InventoryProjector.Project(view, _refs, includeEmptySlots: true);
            var backpack = view.Slots.Select(item => item is null
                ? null
                : new DiscardInventoryItemRuntimeFact(
                    item,
                    item.Stack,
                    InventoryItemGuard.Create(item)
                )).ToArray();
            var identities = backpack
                .Where(item => item is not null)
                .Select(item => item!.Identity)
                .ToArray();
            if (identities.Distinct(ReferenceEqualityComparer.Instance).Count()
                != identities.Length)
                return DiscardInventoryCapture.Unavailable();
            return new DiscardInventoryCapture(
                DiscardInventoryCaptureStatus.Ready,
                menu,
                page,
                player,
                view.BackingIdentity,
                snapshot,
                backpack,
                player.CurrentToolIndex,
                player.CurrentItem,
                player.Money,
                player.trashCanLevel,
                player.specialItems.ToArray(),
                new LiveCommitState(player, view.BackingIdentity)
            );
        }
        catch
        {
            return DiscardInventoryCapture.Unavailable();
        }
    }

    public bool CanBeTrashed(DiscardInventoryCapture capture, int sourceSlot)
    {
        var item = Source(capture, sourceSlot);
        return _trash.CanBeTrashed(item);
    }

    public DiscardInventoryPlan PrepareCommit(
        DiscardInventoryCapture capture,
        int sourceSlot,
        int quantity
    ) => DiscardInventoryPlanBuilder.Prepare(capture, sourceSlot, quantity, _trash);

    public void Commit(DiscardInventoryCapture capture, DiscardInventoryPlan plan)
    {
        LiveCommitState state;
        try
        {
            if (capture.CommitState is not LiveCommitState current
                || !ReferenceEquals(current.Player, Game1.player)
                || !ReferenceEquals(current.BackingIdentity, current.Player.Items))
                throw new InvalidOperationException("玩家背包提交上下文已变化");
            state = current;
        }
        catch (Exception error)
        {
            throw new DiscardInventoryBeforeTrashException(error, rollbackConfirmed: true);
        }
        DiscardInventoryItemCommitter.Commit(
            new LiveDiscardBackend(state.Player, _trash),
            plan
        );
    }

    private static object Source(DiscardInventoryCapture capture, int sourceSlot)
    {
        if (sourceSlot < 0
            || sourceSlot >= capture.Backpack.Count
            || capture.Backpack[sourceSlot] is not { } source)
            throw new InvalidOperationException("丢弃源 Slot 已变化");
        return source.Identity;
    }

    private static bool TryResolveAllowedPlayerState(
        Farmer player,
        out object? menuIdentity,
        out object? pageIdentity
    )
    {
        menuIdentity = null;
        pageIdentity = null;
        if (player.CursorSlotItem is not null
            || player.TemporaryItem is not null
            || player.UsingTool
            || player.isEating
            || Game1.currentMinigame is not null
            || Game1.eventUp
            || Game1.farmEvent is not null
            || Game1.dialogueUp)
            return false;

        if (Game1.activeClickableMenu is null)
            return player.CanMove;
        if (Game1.activeClickableMenu.GetType() != typeof(GameMenu))
            return false;
        var menu = (GameMenu)Game1.activeClickableMenu;
        if (menu.currentTab != GameMenu.inventoryTab
            || menu.currentTab < 0
            || menu.currentTab >= menu.pages.Count
            || menu.pages[menu.currentTab] is not { } current
            || current.GetType() != typeof(InventoryPage))
            return false;
        var page = (InventoryPage)current;
        var view = InventoryViewResolver.CreatePlayerForMenu(
            player,
            page.inventory.capacity
        );
        if (!InventoryPageProjector.IsCompleteBackpackMenu(page.inventory, view))
            return false;
        menuIdentity = menu;
        pageIdentity = page;
        return true;
    }

    private sealed record LiveCommitState(Farmer Player, object BackingIdentity);

    private sealed class LiveDiscardBackend : IDiscardInventoryItemBackend
    {
        private readonly Farmer _player;
        private readonly ITrashSemantics _trash;

        public LiveDiscardBackend(Farmer player, ITrashSemantics trash)
        {
            _player = player;
            _trash = trash;
        }

        public int CurrentToolIndex => _player.CurrentToolIndex;
        public object? CurrentItem => _player.CurrentItem;
        public int Money => _player.Money;

        public object? ReadSlot(int slot) =>
            slot >= 0 && slot < _player.MaxItems && slot < _player.Items.Count
                ? _player.Items[slot]
                : null;

        public int ReadStack(object item) => ((Item)item).Stack;

        public void WriteStack(object item, int stack) => ((Item)item).Stack = stack;

        public void WriteSlot(int slot, object? item)
        {
            if (slot < 0
                || slot >= _player.MaxItems
                || slot >= _player.Items.Count
                || item is not null and not Item)
                throw new InvalidOperationException("玩家背包 Slot 写入无效");
            _player.Items[slot] = (Item?)item;
        }

        public IReadOnlyList<string> ReadSpecialItems() =>
            _player.specialItems.ToArray();

        public void StopBeingHeld(object item) =>
            ((Item)item).actionWhenStopBeingHeld(_player);

        public void StartBeingHeld(object item) =>
            ((Item)item).actionWhenBeingHeld(_player);

        public void Trash(object item) => _trash.Trash(item);
    }
}

internal sealed class ItemNotDiscardableException : Exception { }
