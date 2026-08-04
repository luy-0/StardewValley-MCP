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

            var playerView = InventoryViewResolver.CreatePlayerForMenu(
                player,
                menu.inventory.capacity
            );
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
                InventoryTransferRuntimeItemFactory.Wrap(playerView.Slots),
                InventoryTransferRuntimeItemFactory.Wrap(containerView.Slots),
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
        return InventoryTransferRuntimeCommitter.Commit(source, target, plan);
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

    private sealed record LiveCommitState(IList<Item> Player, IList<Item> Container);
}
