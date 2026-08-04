using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal interface IInventoryTransferAdapter
{
    InventoryTransferCapture Capture();
    IInventoryTransferCommit Commit(
        InventoryTransferCapture capture,
        InventoryTransferDirection direction,
        InventoryTransferPlan plan
    );
}

internal interface IInventoryTransferCommit
{
    void Complete();
    void Rollback();
}

internal enum InventoryTransferCaptureStatus
{
    Ready,
    NotReady,
    Unsupported,
    Unavailable,
}

internal sealed record InventoryTransferCapture(
    InventoryTransferCaptureStatus Status,
    object? MenuIdentity = null,
    object? ContainerIdentity = null,
    string UiRevision = "",
    InventorySnapshot? PlayerSnapshot = null,
    InventorySnapshot? ContainerSnapshot = null,
    IReadOnlyList<InventoryTransferItem?>? PlayerItems = null,
    IReadOnlyList<InventoryTransferItem?>? ContainerItems = null,
    object? CommitState = null
);

internal sealed class LiveInventoryTransferAdapter : IInventoryTransferAdapter
{
    private readonly OpaqueRefStore _refs;

    public LiveInventoryTransferAdapter(OpaqueRefStore refs) => _refs = refs;

    public InventoryTransferCapture Capture()
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return new InventoryTransferCapture(InventoryTransferCaptureStatus.NotReady);
        if (Game1.activeClickableMenu is not { } active)
            return new InventoryTransferCapture(InventoryTransferCaptureStatus.NotReady);
        if (active.GetType() != typeof(ItemGrabMenu))
            return new InventoryTransferCapture(InventoryTransferCaptureStatus.Unsupported);
        var menu = (ItemGrabMenu)active;
        if (menu.heldItem is not null)
            return new InventoryTransferCapture(InventoryTransferCaptureStatus.NotReady);

        try
        {
            if (!ItemGrabMenuProjector.TryLocateSupportedContainer(
                    menu,
                    player,
                    out var chest,
                    out var location,
                    out var locatorKind,
                    out var x,
                    out var y
                ))
                return new InventoryTransferCapture(InventoryTransferCaptureStatus.Unsupported);
            if (!chest.GetMutex().IsLockHeld())
                return new InventoryTransferCapture(InventoryTransferCaptureStatus.NotReady);

            var playerView = InventoryViewResolver.CreatePlayer(player);
            var containerView = InventoryViewResolver.CreateAttachedContainer(
                chest,
                location,
                player,
                _refs,
                locatorKind,
                x,
                y,
                ContainerKindClassifier.IdentityGuard(chest, locatorKind)
            );
            if (!ItemGrabMenuProjector.IsCompleteInventoryMenu(
                    menu.inventory,
                    playerView,
                    allowVisualSuperset: true
                )
                || !ItemGrabMenuProjector.IsCompleteInventoryMenu(
                    menu.ItemsToGrabMenu,
                    containerView,
                    allowVisualSuperset: false
                )
                || playerView.BackingIdentity is not IList<Item> playerBacking
                || containerView.BackingIdentity is not IList<Item> containerBacking)
                return new InventoryTransferCapture(InventoryTransferCaptureStatus.Unavailable);

            var ui = UiRuntimeProjector.Capture(menu, player, _refs);
            if (ui.ElementSetCompleteness != UiElementSetCompleteness.Complete
                || ui.Result.Snapshot.Inventories.Count != 2)
                return new InventoryTransferCapture(InventoryTransferCaptureStatus.Unavailable);
            var playerSnapshot = InventoryProjector.Project(playerView, _refs, includeEmptySlots: true);
            var containerSnapshot = InventoryProjector.Project(containerView, _refs, includeEmptySlots: true);
            if (!UiLinksMatch(ui.Result.Snapshot, playerSnapshot, containerSnapshot))
                return new InventoryTransferCapture(InventoryTransferCaptureStatus.Unavailable);

            return new InventoryTransferCapture(
                InventoryTransferCaptureStatus.Ready,
                menu,
                chest,
                ui.Result.Snapshot.UiRevision,
                playerSnapshot,
                containerSnapshot,
                Wrap(playerView.Slots),
                Wrap(containerView.Slots),
                new LiveCommitState(playerBacking, containerBacking)
            );
        }
        catch
        {
            return new InventoryTransferCapture(InventoryTransferCaptureStatus.Unavailable);
        }
    }

    public IInventoryTransferCommit Commit(
        InventoryTransferCapture capture,
        InventoryTransferDirection direction,
        InventoryTransferPlan plan
    )
    {
        if (capture.CommitState is not LiveCommitState state)
            throw new InvalidOperationException("转移提交状态无效");
        var source = direction == InventoryTransferDirection.PlayerToContainer
            ? state.Player
            : state.Container;
        var target = direction == InventoryTransferDirection.PlayerToContainer
            ? state.Container
            : state.Player;
        if (plan.SourceSlot >= source.Count
            || source[plan.SourceSlot] is not { } sourceItem
            || !ReferenceEquals(sourceItem, plan.SourceIdentity))
            throw new InvalidOperationException("源物品已变化");

        var targetBackups = plan.Writes.Select(write => new TargetBackup(
            write.Slot,
            write.Slot < target.Count ? target[write.Slot] : null,
            write.Slot < target.Count ? target[write.Slot]?.Stack : null
        )).ToArray();
        var journal = new LiveTransferCommit(
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
                    if (write.Slot >= target.Count || !ReferenceEquals(target[write.Slot], existing))
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

    private static bool UiLinksMatch(
        UiSnapshot ui,
        InventorySnapshot player,
        InventorySnapshot container
    ) => ui.Inventories.Any(link =>
            link.Side == UiInventorySide.Player
            && link.InventoryRevision == player.InventoryRevision)
        && ui.Inventories.Any(link =>
            link.Side == UiInventorySide.Container
            && link.InventoryRevision == container.InventoryRevision
            && link.ContainerRef is not null
            && container.ContainerRef is not null
            && link.ContainerRef.Value == container.ContainerRef.Value);

    private static IReadOnlyList<InventoryTransferItem?> Wrap(IReadOnlyList<Item?> source) =>
        source.Select(item => item is null ? null : new InventoryTransferItem(
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

    private sealed record LiveCommitState(IList<Item> Player, IList<Item> Container);
    private sealed record TargetBackup(int Slot, Item? Item, int? Stack);

    private sealed class LiveTransferCommit : IInventoryTransferCommit
    {
        private readonly IList<Item> _source;
        private readonly IList<Item> _target;
        private readonly int _sourceSlot;
        private readonly Item _sourceItem;
        private readonly int _sourceStack;
        private readonly int _targetCount;
        private readonly IReadOnlyList<TargetBackup> _targetBackups;
        private bool _finished;

        public LiveTransferCommit(
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
