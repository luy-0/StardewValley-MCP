using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class InventoryPageProjector
{
    private static readonly IReadOnlySet<UiEquipmentSlotKind> FixedSlotKinds =
        new HashSet<UiEquipmentSlotKind>
        {
            UiEquipmentSlotKind.Hat,
            UiEquipmentSlotKind.LeftRing,
            UiEquipmentSlotKind.RightRing,
            UiEquipmentSlotKind.Boots,
            UiEquipmentSlotKind.Shirt,
            UiEquipmentSlotKind.Pants,
        };

    internal static GameMenuPageState CapturePageState(
        GameMenu menu,
        List<QueryWarning> warnings
    )
    {
        try
        {
            var currentPage = menu.GetCurrentPage();
            if (currentPage is not null)
            {
                return new GameMenuPageState(
                    UiElementSetCompleteness.Complete,
                    currentPage.readyToClose()
                );
            }
        }
        catch
        {
            // 统一按不可完整观测处理。
        }
        warnings.Add(Warning(
            "UI_GAME_MENU_CAPTURE_INCOMPLETE",
            "当前页面切换状态暂时不可读"
        ));
        return new GameMenuPageState(UiElementSetCompleteness.Incomplete, false);
    }

    public static InventoryPageProjectionResult Extract(
        GameMenu menu,
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
            if (menu.currentTab != GameMenu.inventoryTab)
                return InventoryPageProjectionResult.Complete;
            if (menu.currentTab < 0 || menu.currentTab >= menu.pages.Count)
            {
                warnings.Add(Warning(
                    "UI_INVENTORY_CAPTURE_INCOMPLETE",
                    "当前背包页面索引暂时不可读"
                ));
                return InventoryPageProjectionResult.Incomplete;
            }
            var currentPage = menu.pages[menu.currentTab];
            if (currentPage is null)
            {
                warnings.Add(Warning(
                    "UI_INVENTORY_CAPTURE_INCOMPLETE",
                    "当前背包页面暂时不可读"
                ));
                return InventoryPageProjectionResult.Incomplete;
            }
            if (currentPage.GetType() != typeof(InventoryPage))
            {
                warnings.Add(Warning(
                    "UI_GAME_MENU_PAGE_UNSUPPORTED",
                    "当前背包页面不是受支持的原版页面"
                ));
                return InventoryPageProjectionResult.Complete;
            }
            if (!ReferenceEquals(Game1.player, player))
            {
                warnings.Add(Warning(
                    "UI_INVENTORY_CAPTURE_INCOMPLETE",
                    "当前玩家与背包页面不一致"
                ));
                return InventoryPageProjectionResult.Incomplete;
            }
            if (player.CursorSlotItem is not null)
            {
                warnings.Add(Warning(
                    "UI_INVENTORY_CURSOR_ITEM_UNSUPPORTED",
                    "游标仍持有物品，当前背包页面无法完整确认"
                ));
                return InventoryPageProjectionResult.Incomplete;
            }

            var page = (InventoryPage)currentPage;
            var playerView = InventoryViewResolver.CreatePlayer(player);
            if (!IsCompleteBackpackMenu(page.inventory, playerView))
            {
                warnings.Add(Warning(
                    "UI_INVENTORY_CAPTURE_INCOMPLETE",
                    "当前背包页面的槽位无法完整确认"
                ));
                return InventoryPageProjectionResult.Incomplete;
            }
            if (!TryCaptureEquipmentSlots(page, player, out var equipment))
            {
                warnings.Add(Warning(
                    "UI_INVENTORY_CAPTURE_INCOMPLETE",
                    "当前背包页面的装备槽无法完整确认"
                ));
                return InventoryPageProjectionResult.Incomplete;
            }

            var playerSnapshot = InventoryProjector.Project(
                playerView,
                refs,
                includeEmptySlots: true
            );
            var playerLink = UiProjector.ToInventoryLink(
                UiInventorySide.Player,
                playerSnapshot
            );
            var backpackDescriptors = CreateBackpackDescriptors(
                page.inventory.inventory
                    .Take(checked((int)playerSnapshot.SlotCount))
                    .Cast<object>()
                    .ToArray(),
                playerSnapshot,
                component => Bounds(((ClickableComponent)component).bounds),
                component => ((ClickableComponent)component).visible,
                viewport
            );
            var equipmentDescriptors = CreateEquipmentDescriptors(equipment, viewport);
            inventories.Add(playerLink);
            output.AddRange(backpackDescriptors);
            output.AddRange(equipmentDescriptors);
            return InventoryPageProjectionResult.Complete;
        }
        catch
        {
            warnings.Add(Warning(
                "UI_INVENTORY_CAPTURE_INCOMPLETE",
                "当前背包页面的公开事实暂时不可读"
            ));
            return InventoryPageProjectionResult.Incomplete;
        }
    }

    internal static bool IsCompleteBackpackMenu(
        InventoryMenu? menu,
        ReadableInventoryView view
    ) => menu is not null
        && menu.GetType() == typeof(InventoryMenu)
        && menu.playerInventory
        && ReferenceEquals(menu.actualInventory, view.BackingIdentity)
        && HasCompleteBackpackCoverage(
            view.Capacity,
            menu.capacity,
            menu.inventory.Select(component => component?.name).ToArray(),
            menu.inventory.Select(component => component?.myID ?? -1).ToArray()
        );

    internal static bool HasCompleteBackpackCoverage(
        int authoritativeCapacity,
        int visualCapacity,
        IReadOnlyList<string?> componentNames,
        IReadOnlyList<int> componentIds
    )
    {
        if (authoritativeCapacity < 0
            || visualCapacity < authoritativeCapacity
            || componentNames.Count != visualCapacity
            || componentIds.Count != visualCapacity)
            return false;
        for (var index = 0; index < authoritativeCapacity; index++)
        {
            if (!string.Equals(
                    componentNames[index],
                    index.ToString(),
                    StringComparison.Ordinal
                )
                || componentIds[index] != index)
                return false;
        }
        return true;
    }

    internal static IReadOnlyList<UiElementDescriptor> CreateBackpackDescriptors(
        IReadOnlyList<object> components,
        InventorySnapshot snapshot,
        Func<object, UiBounds> readBounds,
        Func<object, bool> readVisible,
        UiBounds viewport
    )
    {
        if (components.Count != snapshot.SlotCount
            || snapshot.Slots.Count != snapshot.SlotCount)
            throw new UiProjectionException("背包槽位与 UI 组件数量不一致");
        var output = new List<UiElementDescriptor>(components.Count);
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            var slot = snapshot.Slots[index];
            var bounds = readBounds(component);
            var visible = UiProjectionPolicy.IsVisible(bounds, readVisible(component), viewport);
            var center = UiProjectionPolicy.Center(bounds);
            output.Add(new UiElementDescriptor(
                UiExtractorKind.GameMenu,
                UiElementKind.ItemSlot,
                index,
                component,
                component,
                $"inventory-page-backpack:{index}",
                slot.Item?.DisplayName ?? "",
                visible,
                false,
                center.X,
                center.Y,
                InventorySide: UiInventorySide.Player,
                ItemRef: slot.Item?.Ref
            ));
        }
        return output;
    }

    internal static IReadOnlyList<UiElementDescriptor> CreateEquipmentDescriptors(
        IReadOnlyList<CapturedUiEquipmentSlot> slots,
        UiBounds viewport
    )
    {
        var identities = new HashSet<(UiEquipmentSlotKind Kind, int Index)>();
        var output = new List<UiElementDescriptor>(slots.Count);
        foreach (var slot in slots)
        {
            if (slot.Kind == UiEquipmentSlotKind.Unspecified
                || slot.Index < 0
                || !identities.Add((slot.Kind, slot.Index))
                || slot.Item?.Ref is not null)
                throw new UiProjectionException("装备槽身份或事实无效");
            var visible = UiProjectionPolicy.IsVisible(
                slot.Bounds,
                slot.Visible,
                viewport
            );
            var center = UiProjectionPolicy.Center(slot.Bounds);
            output.Add(new UiElementDescriptor(
                UiExtractorKind.GameMenu,
                UiElementKind.EquipmentSlot,
                slot.Index,
                slot.Component,
                slot.Component,
                $"inventory-page-equipment:{slot.Kind}:{slot.Index}",
                slot.Item?.DisplayName ?? "",
                visible,
                false,
                center.X,
                center.Y,
                Item: slot.Item,
                EquipmentSlotKind: slot.Kind
            ));
        }
        return output;
    }

    internal static bool TryClassifyEquipmentComponent(
        string? name,
        int componentId,
        out UiEquipmentSlotKind kind,
        out int index
    )
    {
        kind = UiEquipmentSlotKind.Unspecified;
        index = 0;
        switch (name)
        {
            case "Hat" when componentId == InventoryPage.region_hat:
                kind = UiEquipmentSlotKind.Hat;
                return true;
            case "Left Ring" when componentId == InventoryPage.region_ring1:
                kind = UiEquipmentSlotKind.LeftRing;
                return true;
            case "Right Ring" when componentId == InventoryPage.region_ring2:
                kind = UiEquipmentSlotKind.RightRing;
                return true;
            case "Boots" when componentId == InventoryPage.region_boots:
                kind = UiEquipmentSlotKind.Boots;
                return true;
            case "Shirt" when componentId == InventoryPage.region_shirt:
                kind = UiEquipmentSlotKind.Shirt;
                return true;
            case "Pants" when componentId == InventoryPage.region_pants:
                kind = UiEquipmentSlotKind.Pants;
                return true;
            case "Trinket" when componentId >= InventoryPage.region_trinkets:
                kind = UiEquipmentSlotKind.Trinket;
                index = componentId - InventoryPage.region_trinkets;
                return true;
            default:
                return false;
        }
    }

    private static bool TryCaptureEquipmentSlots(
        InventoryPage page,
        Farmer player,
        out IReadOnlyList<CapturedUiEquipmentSlot> slots
    )
    {
        var captured = new List<CapturedUiEquipmentSlot>();
        var identities = new HashSet<(UiEquipmentSlotKind Kind, int Index)>();
        foreach (var component in page.equipmentIcons)
        {
            if (component is null
                || !TryClassifyEquipmentComponent(
                    component.name,
                    component.myID,
                    out var kind,
                    out var index
                )
                || !identities.Add((kind, index)))
            {
                slots = Array.Empty<CapturedUiEquipmentSlot>();
                return false;
            }
            var item = EquipmentItem(player, kind, index);
            captured.Add(new CapturedUiEquipmentSlot(
                kind,
                index,
                component,
                Bounds(component.bounds),
                component.visible,
                item is null ? null : ItemFactProjector.Project(item)
            ));
        }

        if (!FixedSlotKinds.All(kind => identities.Contains((kind, 0)))
            || identities.Count(identity => identity.Kind != UiEquipmentSlotKind.Trinket)
                != FixedSlotKinds.Count)
        {
            slots = Array.Empty<CapturedUiEquipmentSlot>();
            return false;
        }
        var trinketOrdinals = identities
            .Where(identity => identity.Kind == UiEquipmentSlotKind.Trinket)
            .Select(identity => identity.Index)
            .OrderBy(index => index)
            .ToArray();
        var trinketsUnlocked = player.stats.Get("trinketSlots") != 0;
        var expectedTrinkets = trinketsUnlocked ? Farmer.MaximumTrinkets : 0;
        if (expectedTrinkets < 0
            || trinketOrdinals.Length != expectedTrinkets
            || !trinketOrdinals.SequenceEqual(Enumerable.Range(0, expectedTrinkets)))
        {
            slots = Array.Empty<CapturedUiEquipmentSlot>();
            return false;
        }
        slots = captured;
        return true;
    }

    private static Item? EquipmentItem(
        Farmer player,
        UiEquipmentSlotKind kind,
        int index
    ) => kind switch
    {
        UiEquipmentSlotKind.Hat => player.hat.Value,
        UiEquipmentSlotKind.LeftRing => player.leftRing.Value,
        UiEquipmentSlotKind.RightRing => player.rightRing.Value,
        UiEquipmentSlotKind.Boots => player.boots.Value,
        UiEquipmentSlotKind.Shirt => player.shirtItem.Value,
        UiEquipmentSlotKind.Pants => player.pantsItem.Value,
        UiEquipmentSlotKind.Trinket when index < player.trinketItems.Count =>
            player.trinketItems[index],
        UiEquipmentSlotKind.Trinket => null,
        _ => throw new UiProjectionException("装备槽类型不受支持"),
    };

    private static UiBounds Bounds(Rectangle bounds) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static QueryWarning Warning(string code, string message) =>
        new() { Code = code, Message = message };
}

internal readonly record struct InventoryPageProjectionResult(
    UiElementSetCompleteness Completeness
)
{
    public static InventoryPageProjectionResult Complete { get; } =
        new(UiElementSetCompleteness.Complete);
    public static InventoryPageProjectionResult Incomplete { get; } =
        new(UiElementSetCompleteness.Incomplete);
}

internal readonly record struct GameMenuPageState(
    UiElementSetCompleteness Completeness,
    bool ReadyToClose
);

internal sealed record CapturedUiEquipmentSlot(
    UiEquipmentSlotKind Kind,
    int Index,
    object Component,
    UiBounds Bounds,
    bool Visible,
    ItemFact? Item
);
