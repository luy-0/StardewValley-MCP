using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class ItemGrabMenuProjector
{
    private const int ElementLimit = 128;

    public static ItemGrabProjectionResult Extract(
        ItemGrabMenu menu,
        Farmer player,
        OpaqueRefStore refs,
        UiBounds viewport,
        List<UiElementDescriptor> output,
        List<UiInventoryLink> inventories,
        List<QueryWarning> warnings
    )
    {
        try
        {
            if (!TryLocateSupportedContainer(
                    menu,
                    player,
                    out var chest,
                    out var location,
                    out var locatorKind,
                    out var x,
                    out var y
                ))
            {
                warnings.Add(Warning(
                    "UI_MENU_UNSUPPORTED",
                    "当前物品菜单不是受支持的原版箱子或冰箱"
                ));
                return ItemGrabProjectionResult.Unsupported;
            }

            if (menu.heldItem is not null)
            {
                warnings.Add(Warning(
                    "UI_INVENTORY_CAPTURE_INCOMPLETE",
                    "游标仍持有物品，当前两侧库存无法完整确认"
                ));
                return ItemGrabProjectionResult.Incomplete;
            }

            var playerView = InventoryViewResolver.CreatePlayer(player);
            var containerView = InventoryViewResolver.CreateAttachedContainer(
                chest,
                location,
                player,
                refs,
                locatorKind,
                x,
                y,
                ContainerKindClassifier.IdentityGuard(chest, locatorKind)
            );
            if (!IsCompleteInventoryMenu(menu.inventory, playerView, allowVisualSuperset: true)
                || !IsCompleteInventoryMenu(
                    menu.ItemsToGrabMenu,
                    containerView,
                    allowVisualSuperset: false
                ))
            {
                warnings.Add(Warning(
                    "UI_INVENTORY_CAPTURE_INCOMPLETE",
                    "当前物品菜单的库存槽位无法完整确认"
                ));
                return ItemGrabProjectionResult.Incomplete;
            }
            if (checked(playerView.Capacity + containerView.Capacity) > ElementLimit)
            {
                warnings.Add(Warning(
                    "UI_ELEMENTS_LIMIT_UNSUPPORTED",
                    "当前物品菜单元素数量超过 V1 完整投影上限"
                ));
                return ItemGrabProjectionResult.Incomplete;
            }

            var playerSnapshot = InventoryProjector.Project(
                playerView,
                refs,
                includeEmptySlots: true
            );
            var containerSnapshot = InventoryProjector.Project(
                containerView,
                refs,
                includeEmptySlots: true
            );
            inventories.Add(UiProjector.ToInventoryLink(UiInventorySide.Player, playerSnapshot));
            inventories.Add(UiProjector.ToInventoryLink(UiInventorySide.Container, containerSnapshot));
            AddSlots(menu.inventory, playerSnapshot, UiInventorySide.Player, viewport, output);
            AddSlots(
                menu.ItemsToGrabMenu,
                containerSnapshot,
                UiInventorySide.Container,
                viewport,
                output
            );
            return ItemGrabProjectionResult.Complete;
        }
        catch
        {
            warnings.Add(Warning(
                "UI_INVENTORY_CAPTURE_INCOMPLETE",
                "当前物品菜单的库存事实暂时不可读"
            ));
            return ItemGrabProjectionResult.Incomplete;
        }
    }

    internal static bool IsSupportedMenuShape(ItemGrabMenu menu) =>
        menu.source == ItemGrabMenu.source_chest
        && !menu.shippingBin
        && !menu.reverseGrab
        && menu.showReceivingMenu
        && !menu.destroyItemOnClick
        && menu.context is Chest candidate
        && candidate.GetType() == typeof(Chest)
        && candidate.GlobalInventoryId is null
        && candidate.SpecialChestType is Chest.SpecialChestTypes.None
            or Chest.SpecialChestTypes.BigChest;

    internal static bool TryLocateSupportedContainer(
        ItemGrabMenu menu,
        Farmer player,
        out Chest chest,
        out GameLocation location,
        out RefLocatorKind locatorKind,
        out int x,
        out int y
    )
    {
        chest = null!;
        location = null!;
        locatorKind = RefLocatorKind.Object;
        x = 0;
        y = 0;
        if (!IsSupportedMenuShape(menu)
            || menu.context is not Chest candidate
            || !ReferenceEquals(Game1.player, player)
            || Game1.currentLocation is not { } current)
            return false;

        if (ReferenceEquals(current.GetFridge(onlyUnlocked: false), candidate)
            && ReferenceEquals(current.GetFridge(), candidate)
            && current.GetFridgePosition() is { } fridgePosition
            && menu.sourceItem is null)
        {
            chest = candidate;
            location = current;
            locatorKind = RefLocatorKind.Fridge;
            x = fridgePosition.X;
            y = fridgePosition.Y;
            return true;
        }

        Vector2? attachedTile = null;
        foreach (var pair in current.Objects.Pairs)
        {
            if (ReferenceEquals(pair.Value, candidate))
            {
                attachedTile = pair.Key;
                break;
            }
        }
        if (attachedTile is null || !ReferenceEquals(menu.sourceItem, candidate)
            || candidate.fridge.Value)
            return false;
        chest = candidate;
        location = current;
        locatorKind = RefLocatorKind.Object;
        x = checked((int)attachedTile.Value.X);
        y = checked((int)attachedTile.Value.Y);
        return true;
    }

    internal static bool IsCompleteInventoryMenu(
        InventoryMenu? menu,
        ReadableInventoryView view,
        bool allowVisualSuperset
    )
    {
        if (menu is null
            || !ReferenceEquals(menu.actualInventory, view.BackingIdentity)
            || !HasCompleteSlotCoverage(
                view.Capacity,
                menu.capacity,
                menu.inventory.Select(component => component?.name).ToArray(),
                allowVisualSuperset
            ))
            return false;
        return true;
    }

    internal static bool HasCompleteSlotCoverage(
        int authoritativeCapacity,
        int visualCapacity,
        IReadOnlyList<string?> componentNames,
        bool allowVisualSuperset
    )
    {
        if (authoritativeCapacity < 0
            || visualCapacity < authoritativeCapacity
            || !allowVisualSuperset && visualCapacity != authoritativeCapacity
            || componentNames.Count != visualCapacity)
            return false;
        for (var index = 0; index < authoritativeCapacity; index++)
        {
            if (!string.Equals(
                    componentNames[index],
                    index.ToString(),
                    StringComparison.Ordinal
                ))
                return false;
        }
        return true;
    }

    internal static IReadOnlyList<UiElementDescriptor> CreateSlotDescriptors(
        IReadOnlyList<object> components,
        InventorySnapshot snapshot,
        UiInventorySide side,
        Func<object, UiBounds> readBounds,
        Func<object, bool> readVisible,
        UiBounds viewport
    )
    {
        if (components.Count != snapshot.SlotCount || snapshot.Slots.Count != snapshot.SlotCount)
            throw new UiProjectionException("库存槽位与 UI 组件数量不一致");
        var output = new List<UiElementDescriptor>(components.Count);
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            var slot = snapshot.Slots[index];
            var bounds = readBounds(component);
            var visible = UiProjectionPolicy.IsVisible(bounds, readVisible(component), viewport);
            var center = UiProjectionPolicy.Center(bounds);
            output.Add(new UiElementDescriptor(
                UiExtractorKind.ItemGrabSlot,
                UiElementKind.ItemSlot,
                index,
                component,
                component,
                $"item-grab-slot:{side}:{index}",
                slot.Item?.DisplayName ?? "",
                visible,
                false,
                center.X,
                center.Y,
                InventorySide: side,
                ItemRef: slot.Item?.Ref
            ));
        }
        return output;
    }

    private static void AddSlots(
        InventoryMenu menu,
        InventorySnapshot snapshot,
        UiInventorySide side,
        UiBounds viewport,
        List<UiElementDescriptor> output
    ) => output.AddRange(CreateSlotDescriptors(
        menu.inventory.Take(checked((int)snapshot.SlotCount)).Cast<object>().ToArray(),
        snapshot,
        side,
        component => Bounds(((ClickableComponent)component).bounds),
        component => ((ClickableComponent)component).visible,
        viewport
    ));

    private static UiBounds Bounds(Rectangle bounds) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static QueryWarning Warning(string code, string message) =>
        new() { Code = code, Message = message };
}

internal readonly record struct ItemGrabProjectionResult(
    bool Supported,
    UiElementSetCompleteness Completeness
)
{
    public static ItemGrabProjectionResult Complete { get; } =
        new(true, UiElementSetCompleteness.Complete);
    public static ItemGrabProjectionResult Incomplete { get; } =
        new(true, UiElementSetCompleteness.Incomplete);
    public static ItemGrabProjectionResult Unsupported { get; } =
        new(false, UiElementSetCompleteness.Complete);
}
