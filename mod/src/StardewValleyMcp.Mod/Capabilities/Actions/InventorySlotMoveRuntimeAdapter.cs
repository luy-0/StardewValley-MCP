using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal enum InventorySlotMoveCaptureStatus
{
    Ready,
    NotReady,
    Unsupported,
    Unavailable,
}

internal sealed record InventorySlotMoveItem(
    object Identity,
    int Stack,
    ItemFact PublicFact
);

internal sealed record InventorySlotMoveCapture(
    InventorySlotMoveCaptureStatus Status,
    object? MenuIdentity,
    object? PageIdentity,
    object? PlayerIdentity,
    object? BackingIdentity,
    string UiRevision,
    InventorySnapshot? PlayerSnapshot,
    IReadOnlyList<InventorySlotMoveItem?> Backpack,
    IReadOnlyList<object> Components,
    int CurrentToolIndex,
    object? CommitState
)
{
    public static InventorySlotMoveCapture NotReady() =>
        Empty(InventorySlotMoveCaptureStatus.NotReady);
    public static InventorySlotMoveCapture Unsupported() =>
        Empty(InventorySlotMoveCaptureStatus.Unsupported);
    public static InventorySlotMoveCapture Unavailable() =>
        Empty(InventorySlotMoveCaptureStatus.Unavailable);

    private static InventorySlotMoveCapture Empty(InventorySlotMoveCaptureStatus status) =>
        new(
            status,
            null,
            null,
            null,
            null,
            "",
            null,
            Array.Empty<InventorySlotMoveItem?>(),
            Array.Empty<object>(),
            -1,
            null
        );
}

internal interface IInventorySlotMoveRuntimeAdapter
{
    InventorySlotMoveCapture Capture();
    IInventorySlotMutationCommit Commit(
        InventorySlotMoveCapture capture,
        InventorySlotMutationPlan plan
    );
}

internal interface IInventorySlotMoveBackend : IInventorySlotMutationBackend
{
    int CurrentToolIndex { get; }
    object? CurrentItem { get; }
    void StopBeingHeld(object item);
    void StartBeingHeld(object item);
    void RestoreShape();
}

/// <summary>在整个双槽事务前后处理一次当前手持生命周期。</summary>
internal static class InventorySlotMoveCommitter
{
    public static IInventorySlotMutationCommit Commit(
        IInventorySlotMoveBackend backend,
        InventorySlotMutationPlan plan
    )
    {
        if (!plan.Changed)
            return InventorySlotMutationExecutor.Commit(backend, plan);
        var currentIndex = backend.CurrentToolIndex;
        var currentParticipates = currentIndex == plan.SourceSlot
            || currentIndex == plan.DestinationSlot;
        var beforeCurrent = backend.CurrentItem;
        var expectedCurrent = currentIndex == plan.SourceSlot
            ? plan.SourceIdentity
            : currentIndex == plan.DestinationSlot
                ? plan.DestinationIdentity
                : beforeCurrent;
        if (!ReferenceEquals(beforeCurrent, expectedCurrent))
            throw new InvalidOperationException("当前手持物品与背包 Slot 不一致");

        IInventorySlotMutationCommit? inner = null;
        var stopped = false;
        object? afterCurrent = null;
        var startAttempted = false;
        try
        {
            if (currentParticipates && beforeCurrent is not null)
            {
                backend.StopBeingHeld(beforeCurrent);
                stopped = true;
            }
            inner = InventorySlotMutationExecutor.Commit(backend, plan);
            afterCurrent = backend.CurrentItem;
            if (currentParticipates && afterCurrent is not null)
            {
                startAttempted = true;
                backend.StartBeingHeld(afterCurrent);
            }
            return new HeldStateCommit(
                backend,
                inner,
                currentParticipates,
                beforeCurrent,
                afterCurrent
            );
        }
        catch (Exception mutationError)
        {
            Exception? recoveryError = null;
            if (startAttempted && afterCurrent is not null)
            {
                if (ReferenceEquals(backend.CurrentItem, afterCurrent))
                {
                    try
                    {
                        backend.StopBeingHeld(afterCurrent);
                    }
                    catch (Exception error)
                    {
                        recoveryError = error;
                    }
                }
                else
                {
                    recoveryError = new InvalidOperationException(
                        "回滚前当前槽已被未知对象替换"
                    );
                }
            }
            try
            {
                inner?.Rollback();
            }
            catch (Exception error)
            {
                recoveryError ??= error;
            }
            try
            {
                backend.RestoreShape();
            }
            catch (Exception error)
            {
                recoveryError ??= error;
            }
            if (currentParticipates && beforeCurrent is not null)
            {
                if (ReferenceEquals(backend.CurrentItem, beforeCurrent))
                {
                    try
                    {
                        backend.StartBeingHeld(beforeCurrent);
                    }
                    catch (Exception error)
                    {
                        recoveryError ??= error;
                    }
                }
                else
                {
                    recoveryError ??= new InvalidOperationException(
                        stopped
                            ? "回滚后原当前物品未恢复"
                            : "停止当前物品失败后身份已变化"
                    );
                }
            }
            if (recoveryError is not null)
                throw new InventorySlotMoveRecoveryException(mutationError, recoveryError);
            throw;
        }
    }

    private sealed class HeldStateCommit : IInventorySlotMutationCommit
    {
        private readonly IInventorySlotMoveBackend _backend;
        private readonly IInventorySlotMutationCommit _inner;
        private readonly bool _currentParticipates;
        private readonly object? _beforeCurrent;
        private readonly object? _afterCurrent;
        private bool _finished;

        public HeldStateCommit(
            IInventorySlotMoveBackend backend,
            IInventorySlotMutationCommit inner,
            bool currentParticipates,
            object? beforeCurrent,
            object? afterCurrent
        )
        {
            _backend = backend;
            _inner = inner;
            _currentParticipates = currentParticipates;
            _beforeCurrent = beforeCurrent;
            _afterCurrent = afterCurrent;
        }

        public void Complete()
        {
            _inner.Complete();
            _finished = true;
        }

        public void Rollback()
        {
            if (_finished)
                return;
            Exception? firstError = null;
            if (_currentParticipates && _afterCurrent is not null)
            {
                if (ReferenceEquals(_backend.CurrentItem, _afterCurrent))
                {
                    try
                    {
                        _backend.StopBeingHeld(_afterCurrent);
                    }
                    catch (Exception error)
                    {
                        firstError = error;
                    }
                }
                else
                {
                    firstError = new InvalidOperationException(
                        "回滚前当前槽已被未知对象替换"
                    );
                }
            }
            try
            {
                _inner.Rollback();
            }
            catch (Exception error)
            {
                firstError ??= error;
            }
            try
            {
                _backend.RestoreShape();
            }
            catch (Exception error)
            {
                firstError ??= error;
            }
            if (_currentParticipates && _beforeCurrent is not null)
            {
                if (ReferenceEquals(_backend.CurrentItem, _beforeCurrent))
                {
                    try
                    {
                        _backend.StartBeingHeld(_beforeCurrent);
                    }
                    catch (Exception error)
                    {
                        firstError ??= error;
                    }
                }
                else
                {
                    firstError ??= new InvalidOperationException(
                        "回滚后原当前物品未恢复"
                    );
                }
            }
            _finished = true;
            if (firstError is not null)
                throw firstError;
        }
    }
}

internal sealed class InventorySlotMoveRecoveryException : Exception
{
    public InventorySlotMoveRecoveryException(
        Exception mutationError,
        Exception recoveryError
    ) : base(
        "背包 Slot 写入失败，且 held-state 或内容恢复存在错误",
        new AggregateException(mutationError, recoveryError)
    ) { }
}

internal sealed class LiveInventorySlotMoveRuntimeAdapter : IInventorySlotMoveRuntimeAdapter
{
    private readonly OpaqueRefStore _refs;

    public LiveInventorySlotMoveRuntimeAdapter(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    public InventorySlotMoveCapture Capture()
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return InventorySlotMoveCapture.NotReady();
        if (Game1.activeClickableMenu is not { } active
            || active.GetType() != typeof(GameMenu))
            return InventorySlotMoveCapture.Unsupported();
        try
        {
            var menu = (GameMenu)active;
            if (menu.currentTab != GameMenu.inventoryTab
                || menu.currentTab < 0
                || menu.currentTab >= menu.pages.Count
                || menu.pages[menu.currentTab] is not { } current
                || current.GetType() != typeof(InventoryPage)
                || player.CursorSlotItem is not null)
                return InventorySlotMoveCapture.NotReady();
            var page = (InventoryPage)current;
            var view = InventoryViewResolver.CreatePlayer(player);
            if (!InventoryPageProjector.IsCompleteBackpackMenu(page.inventory, view)
                || player.CurrentToolIndex < 0
                || player.CurrentToolIndex >= view.Capacity
                || !ReferenceEquals(player.CurrentItem, view.Slots[player.CurrentToolIndex]))
                return InventorySlotMoveCapture.NotReady();

            var ui = UiRuntimeProjector.Capture(menu, player, _refs);
            if (ui.ElementSetCompleteness != UiElementSetCompleteness.Complete
                || ui.Result.Snapshot.Menu?.MenuKind != MenuKind.Inventory)
                return InventorySlotMoveCapture.NotReady();
            var snapshot = InventoryProjector.Project(view, _refs, includeEmptySlots: true);
            var playerLink = ui.Result.Snapshot.Inventories.SingleOrDefault(link =>
                link.Side == UiInventorySide.Player);
            if (playerLink is null
                || !string.Equals(
                    playerLink.InventoryRevision,
                    snapshot.InventoryRevision,
                    StringComparison.Ordinal
                )
                || snapshot.SlotCount != view.Capacity
                || view.Slots.Count != view.Capacity)
                return InventorySlotMoveCapture.Unavailable();

            var backpack = view.Slots.Select(RuntimeItem).ToArray();
            var identities = backpack
                .Where(item => item is not null)
                .Select(item => item!.Identity)
                .ToArray();
            if (identities.Distinct(ReferenceEqualityComparer.Instance).Count()
                != identities.Length)
                return InventorySlotMoveCapture.Unavailable();
            var components = page.inventory.inventory
                .Take(view.Capacity)
                .Cast<object>()
                .ToArray();
            if (components.Length != view.Capacity)
                return InventorySlotMoveCapture.Unavailable();

            return new InventorySlotMoveCapture(
                InventorySlotMoveCaptureStatus.Ready,
                menu,
                page,
                player,
                view.BackingIdentity,
                ui.Result.Snapshot.UiRevision,
                snapshot,
                backpack,
                components,
                player.CurrentToolIndex,
                new LiveCommitState(player, view.BackingIdentity, player.Items.Count)
            );
        }
        catch
        {
            return InventorySlotMoveCapture.Unavailable();
        }
    }

    public IInventorySlotMutationCommit Commit(
        InventorySlotMoveCapture capture,
        InventorySlotMutationPlan plan
    )
    {
        if (capture.CommitState is not LiveCommitState state
            || !ReferenceEquals(state.Player, Game1.player)
            || !ReferenceEquals(state.BackingIdentity, state.Player.Items)
            || state.Player.CurrentToolIndex != capture.CurrentToolIndex)
            throw new InvalidOperationException("背包 Slot 提交上下文已变化");
        return InventorySlotMoveCommitter.Commit(
            new LiveMoveBackend(state.Player, state.OriginalCount),
            plan
        );
    }

    private static InventorySlotMoveItem? RuntimeItem(Item? item) => item is null
        ? null
        : new InventorySlotMoveItem(
            item,
            item.Stack,
            ItemFactProjector.Project(item)
        );

    private sealed record LiveCommitState(
        Farmer Player,
        object BackingIdentity,
        int OriginalCount
    );

    private sealed class LiveMoveBackend : IInventorySlotMoveBackend
    {
        private readonly Farmer _player;
        private readonly int _originalCount;

        public LiveMoveBackend(Farmer player, int originalCount)
        {
            _player = player;
            _originalCount = originalCount;
        }

        public int CurrentToolIndex => _player.CurrentToolIndex;
        public object? CurrentItem => _player.CurrentItem;

        public object? ReadSlot(int slot) =>
            slot >= 0 && slot < _player.MaxItems && slot < _player.Items.Count
                ? _player.Items[slot]
                : null;

        public void WriteSlot(int slot, object? value)
        {
            if (slot < 0 || slot >= _player.MaxItems || value is not null and not Item)
                throw new InvalidOperationException("玩家背包 Slot 写入无效");
            while (_player.Items.Count <= slot)
                _player.Items.Add(null);
            var before = _player.Items[slot];
            if (ReferenceEquals(before, value))
                return;
            before?.onDetachedFromParent();
            ((Item?)value)?.onDetachedFromParent();
            _player.Items[slot] = (Item?)value;
        }

        public void StopBeingHeld(object item) =>
            ((Item)item).actionWhenStopBeingHeld(_player);
        public void StartBeingHeld(object item) =>
            ((Item)item).actionWhenBeingHeld(_player);

        public void RestoreShape()
        {
            while (_player.Items.Count > _originalCount)
            {
                var index = _player.Items.Count - 1;
                if (_player.Items[index] is not null)
                    throw new InvalidOperationException("回滚后背包尾部仍有物品");
                _player.Items.RemoveAt(index);
            }
        }
    }
}
