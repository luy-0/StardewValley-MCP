using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class UiRuntimeProjector
{
    private const int GameMenuLimit = 32;
    private const int DialogueLimit = 64;

    public static QueryUiResult Project(
        IClickableMenu menu,
        Farmer player,
        OpaqueRefStore refs
    ) => Capture(menu, player, refs).Result;

    internal static UiRuntimeProjectionCapture Capture(
        IClickableMenu menu,
        Farmer player,
        OpaqueRefStore refs
    )
    {
        var runtimeType = menu.GetType();
        var classification = UiProjectionPolicy.ClassifyExact(
            runtimeType,
            typeof(GameMenu),
            typeof(DialogueBox),
            typeof(ShopMenu),
            typeof(ItemGrabMenu)
        );
        var warnings = new List<QueryWarning>();
        var dialogueCapture = classification == UiMenuClassification.DialogueBox
            ? CaptureDialogue((DialogueBox)menu, warnings)
            : null;
        var shell = CreateShell(menu, runtimeType, classification, dialogueCapture, warnings);
        var viewport = new UiBounds(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
        var descriptors = new List<UiElementDescriptor>();
        var inventories = new List<UiInventoryLink>();
        var extractor = UiExtractorKind.Unsupported;
        var actionState = "";
        var completeness = UiElementSetCompleteness.Complete;

        switch (classification)
        {
            case UiMenuClassification.GameMenu:
            {
                var gameMenu = (GameMenu)menu;
                extractor = UiExtractorKind.GameMenuTab;
                actionState = $"tab:{gameMenu.currentTab}";
                completeness = ExtractGameMenu(gameMenu, viewport, descriptors, warnings);
                break;
            }
            case UiMenuClassification.DialogueBox:
            {
                var dialogue = (DialogueBox)menu;
                extractor = UiProjectionPolicy.DialogueExtractor(dialogue.isQuestion);
                completeness = ExtractDialogue(
                    dialogue,
                    dialogueCapture!,
                    viewport,
                    descriptors,
                    warnings,
                    out actionState
                );
                break;
            }
            case UiMenuClassification.ShopMenu:
            {
                var shop = (ShopMenu)menu;
                extractor = UiExtractorKind.ShopSaleRow;
                completeness = ExtractShop(
                    shop,
                    player,
                    viewport,
                    descriptors,
                    warnings,
                    out actionState
                );
                break;
            }
            case UiMenuClassification.ItemGrabMenu:
            {
                extractor = UiExtractorKind.ItemGrabSlot;
                var itemGrab = ItemGrabMenuProjector.Extract(
                    (ItemGrabMenu)menu,
                    player,
                    refs,
                    viewport,
                    descriptors,
                    inventories,
                    warnings
                );
                actionState = "item-grab";
                completeness = itemGrab.Completeness;
                if (!itemGrab.Supported)
                {
                    extractor = UiExtractorKind.Unsupported;
                    actionState = "";
                    descriptors.Clear();
                    inventories.Clear();
                }
                break;
            }
            default:
                warnings.Add(Warning(
                    "UI_MENU_UNSUPPORTED",
                    "当前菜单类型仅提供公共外壳"
                ));
                break;
        }

        if (descriptors.Any(descriptor => !descriptor.IsValid()))
            completeness = UiElementSetCompleteness.Incomplete;
        var owner = new RuntimeUiElementRefOwner(menu, extractor, refs);
        var result = UiProjector.ProjectDescriptors(
            menu,
            shell,
            extractor,
            actionState,
            descriptors,
            warnings,
            owner,
            refs,
            completeness,
            inventories
        );
        return new UiRuntimeProjectionCapture(result, completeness);
    }

    private static UiMenuFact CreateShell(
        IClickableMenu menu,
        Type runtimeType,
        UiMenuClassification classification,
        CapturedDialogueMenu? dialogueCapture,
        List<QueryWarning> warnings
    )
    {
        var menuType = runtimeType.Name;
        if (!PublicStringPolicy.IsNonEmptyValid(menuType))
            throw new UiProjectionException("Menu Type 不符合公开约束");

        var shell = new UiMenuFact
        {
            MenuType = menuType,
            Modal = UiProjectionPolicy.IsExactModal(
                runtimeType,
                typeof(DialogueBox),
                typeof(LetterViewerMenu)
            ),
        };
        try
        {
            switch (classification)
            {
                case UiMenuClassification.GameMenu:
                {
                    var gameMenu = (GameMenu)menu;
                    var kind = MenuKindForTab(gameMenu.currentTab);
                    if (kind.HasValue)
                        shell.MenuKind = kind.Value;
                    shell.Title = gameMenu.tabs
                        .FirstOrDefault(tab => TabIndex(tab.name) == gameMenu.currentTab)
                        ?.label ?? "";
                    break;
                }
                case UiMenuClassification.DialogueBox:
                {
                    shell.Title = dialogueCapture!.Title;
                    shell.DialogueText = dialogueCapture.Text;
                    break;
                }
                case UiMenuClassification.ShopMenu:
                    shell.Title = ((ShopMenu)menu).ShopId ?? "";
                    break;
            }
        }
        catch
        {
            shell.Title = "";
            shell.DialogueText = "";
            warnings.Add(Warning(
                "UI_MENU_FACT_UNAVAILABLE",
                "当前菜单的非关键公开事实不可读"
            ));
        }

        if (!PublicStringPolicy.IsValid(shell.Title)
            || !PublicStringPolicy.IsValid(shell.DialogueText))
        {
            shell.Title = "";
            shell.DialogueText = "";
            warnings.Add(Warning(
                "UI_MENU_FACT_UNAVAILABLE",
                "当前菜单的非关键公开事实不符合约束"
            ));
        }
        return shell;
    }

    private static CapturedDialogueMenu CaptureDialogue(
        DialogueBox menu,
        List<QueryWarning> warnings
    )
    {
        var title = "";
        var text = "";
        var titleReadable = true;
        var textReadable = true;
        try
        {
            title = menu.characterDialogue?.speaker?.getName() ?? "";
        }
        catch
        {
            titleReadable = false;
        }
        try
        {
            text = menu.getCurrentString() ?? "";
        }
        catch
        {
            textReadable = false;
        }
        var titleValid = PublicStringPolicy.IsValid(title);
        var textValid = PublicStringPolicy.IsValid(text);
        if (!titleReadable || !textReadable || !titleValid || !textValid)
        {
            if (!titleReadable || !titleValid)
                title = "";
            if (!textReadable || !textValid)
                text = "";
            warnings.Add(Warning(
                "UI_MENU_FACT_UNAVAILABLE",
                "当前菜单的非关键公开事实不可读"
            ));
        }
        return new CapturedDialogueMenu(title, text, textReadable && textValid);
    }

    private static UiElementSetCompleteness ExtractGameMenu(
        GameMenu menu,
        UiBounds viewport,
        List<UiElementDescriptor> output,
        List<QueryWarning> warnings
    )
    {
        if (menu.tabs.Count > GameMenuLimit)
        {
            warnings.Add(LimitWarning());
            return UiElementSetCompleteness.Incomplete;
        }
        var skipped = 0;
        foreach (var component in menu.tabs)
        {
            if (component is null)
            {
                skipped++;
                continue;
            }
            var index = TabIndex(component.name);
            var label = component.label ?? "";
            if (index < 0 || !PublicStringPolicy.IsValid(label))
            {
                skipped++;
                continue;
            }
            var bounds = Bounds(component.bounds);
            var visible = UiProjectionPolicy.IsVisible(bounds, component.visible, viewport);
            var center = UiProjectionPolicy.Center(bounds);
            output.Add(new UiElementDescriptor(
                UiExtractorKind.GameMenuTab,
                UiElementKind.Tab,
                index,
                component,
                component,
                $"game-menu-tab:{index}",
                label,
                visible,
                visible && index != menu.currentTab,
                center.X,
                center.Y
            ));
        }
        AddSkippedWarning(skipped, warnings);
        return skipped == 0
            ? UiElementSetCompleteness.Complete
            : UiElementSetCompleteness.Incomplete;
    }

    private static UiElementSetCompleteness ExtractDialogue(
        DialogueBox menu,
        CapturedDialogueMenu capture,
        UiBounds viewport,
        List<UiElementDescriptor> output,
        List<QueryWarning> warnings,
        out string actionState
    )
    {
        var fullyPresented = capture.TextReadable
            && menu.characterIndexInDialogue >= capture.Text.Length - 1;
        actionState = $"dialogue:{menu.isQuestion}:{menu.transitioning}:{menu.safetyTimer <= 0}:{fullyPresented}";
        if (!menu.isQuestion)
        {
            if (!capture.TextReadable)
                return UiElementSetCompleteness.Incomplete;
            try
            {
                var brokenUpPageCount = menu.characterDialoguesBrokenUp?.Count ?? 0;
                var plainDialogueCount = menu.dialogues?.Count ?? 0;
                var hasNextPage = UiProjectionPolicy.DialogueHasNextPage(
                    menu.characterDialogue is not null,
                    menu.characterDialogue?.isCurrentStringContinuedOnNextScreen ?? false,
                    brokenUpPageCount,
                    plainDialogueCount
                );
                var characterDialogueIndex = menu.characterDialogue?.currentDialogueIndex ?? -1;
                var guard = $"dialogue-advance:{characterDialogueIndex}:{brokenUpPageCount}:{plainDialogueCount}:{hasNextPage}:{capture.Text}";
                output.Add(new UiElementDescriptor(
                    UiExtractorKind.DialogueAdvance,
                    UiElementKind.DialogueAdvance,
                    0,
                    null,
                    menu,
                    guard,
                    UiProjectionPolicy.DialogueAdvanceLabel(hasNextPage),
                    true,
                    UiProjectionPolicy.DialogueEnabled(
                        true,
                        menu.transitioning,
                        menu.safetyTimer <= 0,
                        capture.TextReadable,
                        menu.characterIndexInDialogue,
                        capture.Text.Length
                    ),
                    0,
                    0
                ));
                return UiElementSetCompleteness.Complete;
            }
            catch
            {
                warnings.Add(ProjectionWarning());
                return UiElementSetCompleteness.Incomplete;
            }
        }
        if (menu.responses is null || menu.responses.Length > DialogueLimit)
        {
            if (menu.responses?.Length > DialogueLimit)
                warnings.Add(LimitWarning());
            else
                warnings.Add(ProjectionWarning());
            return UiElementSetCompleteness.Incomplete;
        }
        if (menu.responses.Length > 0 && (menu.responseCC is null || menu.responseCC.Count == 0))
        {
            warnings.Add(Warning(
                "UI_ELEMENTS_NOT_PRESENTED",
                "对话选项尚未由游戏呈现"
            ));
            return UiElementSetCompleteness.Incomplete;
        }
        if (menu.responseCC is null || menu.responseCC.Count != menu.responses.Length)
        {
            warnings.Add(ProjectionWarning());
            return UiElementSetCompleteness.Incomplete;
        }

        var skipped = 0;
        for (var index = 0; index < menu.responses.Length; index++)
        {
            var response = menu.responses[index];
            var component = menu.responseCC[index];
            if (response is null || component is null
                || !PublicStringPolicy.IsValid(response.responseText))
            {
                skipped++;
                continue;
            }
            var bounds = Bounds(component.bounds);
            var visible = UiProjectionPolicy.IsVisible(bounds, component.visible, viewport);
            var center = UiProjectionPolicy.Center(bounds);
            output.Add(new UiElementDescriptor(
                UiExtractorKind.DialogueResponse,
                UiElementKind.DialogueResponse,
                index,
                component,
                response,
                $"dialogue-response:{index}:{response.responseKey ?? ""}",
                response.responseText,
                visible,
                UiProjectionPolicy.DialogueEnabled(
                    visible,
                    menu.transitioning,
                    menu.safetyTimer <= 0,
                    capture.TextReadable,
                    menu.characterIndexInDialogue,
                    capture.Text.Length
                ),
                center.X,
                center.Y
            ));
        }
        AddSkippedWarning(skipped, warnings);
        return skipped == 0
            ? UiElementSetCompleteness.Complete
            : UiElementSetCompleteness.Incomplete;
    }

    private static UiElementSetCompleteness ExtractShop(
        ShopMenu menu,
        Farmer player,
        UiBounds viewport,
        List<UiElementDescriptor> output,
        List<QueryWarning> warnings,
        out string actionState
    )
    {
        actionState = $"shop:{menu.currentItemIndex}:{menu.currency}:{menu.safetyTimer <= 0}:{menu.heldItem is null}:{menu.readOnly}:{menu.canPurchaseCheck is null}";
        var selected = UiProjectionPolicy.SelectShopViewport(
            menu.currentItemIndex,
            menu.forSaleButtons.Count,
            menu.forSale.Count
        );
        if (selected is null)
        {
            warnings.Add(LimitWarning());
            return UiElementSetCompleteness.Incomplete;
        }

        var skipped = 0;
        for (var row = 0; row < selected.Count; row++)
        {
            var absoluteIndex = selected[row];
            var component = menu.forSaleButtons[row];
            var salable = menu.forSale[absoluteIndex];
            if (component is null || salable is null
                || !menu.itemPriceAndStock.TryGetValue(salable, out var stockInfo)
                || stockInfo.Price < 0
                || stockInfo.Stock < 0)
            {
                skipped++;
                continue;
            }

            try
            {
                var label = salable.DisplayName ?? "";
                if (!PublicStringPolicy.IsValid(label))
                {
                    skipped++;
                    continue;
                }
                var bounds = Bounds(component.bounds);
                var visible = UiProjectionPolicy.IsVisible(bounds, component.visible, viewport);
                var center = UiProjectionPolicy.Center(bounds);
                var descriptorWarnings = new List<UiDescriptorWarning>();
                ItemFact? itemFact = null;
                if (salable is Item item)
                {
                    try
                    {
                        itemFact = ItemFactProjector.Project(item);
                    }
                    catch
                    {
                        descriptorWarnings.Add(DescriptorWarning(
                            "UI_ITEM_FACT_UNAVAILABLE",
                            "当前商品的 Item 事实不可读"
                        ));
                    }
                }
                else
                {
                    descriptorWarnings.Add(DescriptorWarning(
                        "UI_ITEM_FACT_UNAVAILABLE",
                        "当前商品不是可投影的 Item"
                    ));
                }

                if (menu.currency != 0)
                {
                    descriptorWarnings.Add(DescriptorWarning(
                        "UI_PRICE_CURRENCY_UNREPRESENTED",
                        "当前价格使用非金币货币"
                    ));
                }
                var tradeRequired = true;
                if (!string.IsNullOrEmpty(stockInfo.TradeItem))
                {
                    descriptorWarnings.Add(DescriptorWarning(
                        "UI_PRICE_PARTIAL",
                        "当前商品还需要协议未表示的交换物"
                    ));
                    var requiredCount = stockInfo.TradeItemCount
                        ?? ShopMenu.numberRequiredForExtraItemTrade;
#pragma warning disable CS0618
                    tradeRequired = requiredCount >= 0
                        && player.getItemCount(stockInfo.TradeItem) >= requiredCount;
#pragma warning restore CS0618
                }
                if (menu.canPurchaseCheck is not null)
                {
                    descriptorWarnings.Add(DescriptorWarning(
                        "UI_ELEMENT_ACTIVATION_UNCERTAIN",
                        "当前商品存在不可无副作用验证的购买条件"
                    ));
                }
                var vanillaSafeSalable = UiProjectionPolicy.IsExactActivationKnownType(
                    salable.GetType(),
                    typeof(StardewValley.Object)
                );
                if (!vanillaSafeSalable)
                {
                    descriptorWarnings.Add(DescriptorWarning(
                        "UI_ELEMENT_ACTIVATION_UNCERTAIN",
                        "第三方商品的购买条件不能无副作用验证"
                    ));
                }

                var unlimited = stockInfo.Stock == ShopMenu.infiniteStock;
                var enabled = UiProjectionPolicy.ShopEnabled(new ShopActivationFacts(
                    visible,
                    menu.safetyTimer <= 0,
                    menu.heldItem is not null,
                    menu.readOnly,
                    unlimited,
                    stockInfo.Stock,
                    stockInfo.Price,
                    ShopMenu.getPlayerCurrencyAmount(player, menu.currency),
                    tradeRequired,
                    menu.canPurchaseCheck is not null,
                    vanillaSafeSalable
                ));
                output.Add(new UiElementDescriptor(
                    UiExtractorKind.ShopSaleRow,
                    UiElementKind.ItemSlot,
                    absoluteIndex,
                    component,
                    salable,
                    $"shop-sale-row:{absoluteIndex}",
                    label,
                    visible,
                    enabled,
                    center.X,
                    center.Y,
                    itemFact,
                    stockInfo.Price,
                    unlimited ? null : checked((uint)stockInfo.Stock),
                    descriptorWarnings
                ));
            }
            catch
            {
                skipped++;
            }
        }
        AddSkippedWarning(skipped, warnings);
        return skipped == 0
            ? UiElementSetCompleteness.Complete
            : UiElementSetCompleteness.Incomplete;
    }

    internal static int TabIndex(string? name) => name switch
    {
        "inventory" => GameMenu.inventoryTab,
        "skills" => GameMenu.skillsTab,
        "social" => GameMenu.socialTab,
        "map" => GameMenu.mapTab,
        "crafting" => GameMenu.craftingTab,
        "animals" => GameMenu.animalsTab,
        "powers" => GameMenu.powersTab,
        "collections" => GameMenu.collectionsTab,
        "options" => GameMenu.optionsTab,
        "exit" => GameMenu.exitTab,
        _ => -1,
    };

    internal static MenuKind? MenuKindForTab(int tab) => tab switch
    {
        var value when value == GameMenu.inventoryTab => MenuKind.Inventory,
        var value when value == GameMenu.skillsTab => MenuKind.Skills,
        var value when value == GameMenu.socialTab => MenuKind.Social,
        var value when value == GameMenu.mapTab => MenuKind.Map,
        var value when value == GameMenu.craftingTab => MenuKind.Crafting,
        var value when value == GameMenu.collectionsTab => MenuKind.Collections,
        var value when value == GameMenu.optionsTab => MenuKind.Options,
        _ => null,
    };

    private static UiBounds Bounds(Rectangle bounds) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static void AddSkippedWarning(int skipped, List<QueryWarning> warnings)
    {
        if (skipped > 0)
            warnings.Add(ProjectionWarning(skipped));
    }

    private static QueryWarning ProjectionWarning(int count = 1) => Warning(
        "UI_ELEMENT_PROJECTION_FAILED",
        $"{count} 个 UI 元素无法安全投影"
    );

    private static QueryWarning LimitWarning() => Warning(
        "UI_ELEMENTS_LIMIT_UNSUPPORTED",
        "当前菜单元素数量超过 V1 完整投影上限"
    );

    private static QueryWarning Warning(string code, string message) =>
        new() { Code = code, Message = message };

    private static UiDescriptorWarning DescriptorWarning(string code, string message) =>
        new(code, message);

    internal static UiElementLookup ResolveCurrentElement(
        IClickableMenu menu,
        UiExtractorKind extractor,
        UiElementBindingIdentity identity,
        OpaqueRefStore refs
    )
    {
        var warnings = new List<QueryWarning>();
        var descriptors = new List<UiElementDescriptor>();
        var viewport = new UiBounds(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
        UiElementSetCompleteness completeness;
        switch (extractor)
        {
            case UiExtractorKind.GameMenuTab when menu.GetType() == typeof(GameMenu):
                completeness = ExtractGameMenu((GameMenu)menu, viewport, descriptors, warnings);
                break;
            case UiExtractorKind.DialogueResponse when menu.GetType() == typeof(DialogueBox):
                var dialogue = (DialogueBox)menu;
                completeness = ExtractDialogue(
                    dialogue,
                    CaptureDialogue(dialogue, warnings),
                    viewport,
                    descriptors,
                    warnings,
                    out _
                );
                break;
            case UiExtractorKind.DialogueAdvance when menu.GetType() == typeof(DialogueBox):
                var advance = (DialogueBox)menu;
                completeness = ExtractDialogue(
                    advance,
                    CaptureDialogue(advance, warnings),
                    viewport,
                    descriptors,
                    warnings,
                    out _
                );
                break;
            case UiExtractorKind.ShopSaleRow when menu.GetType() == typeof(ShopMenu):
                if (Game1.player is not { } player)
                    return new UiElementLookup(UiElementLookupStatus.Unavailable);
                completeness = ExtractShop(
                    (ShopMenu)menu,
                    player,
                    viewport,
                    descriptors,
                    warnings,
                    out _
                );
                break;
            case UiExtractorKind.ItemGrabSlot when menu.GetType() == typeof(ItemGrabMenu):
                if (Game1.player is not { } itemGrabPlayer)
                    return new UiElementLookup(UiElementLookupStatus.Unavailable);
                var inventories = new List<UiInventoryLink>();
                var capture = ItemGrabMenuProjector.Extract(
                    (ItemGrabMenu)menu,
                    itemGrabPlayer,
                    refs,
                    viewport,
                    descriptors,
                    inventories,
                    warnings
                );
                if (!capture.Supported)
                    return new UiElementLookup(UiElementLookupStatus.Stale);
                completeness = capture.Completeness;
                break;
            default:
                return new UiElementLookup(UiElementLookupStatus.Stale);
        }

        if (completeness == UiElementSetCompleteness.Incomplete
            || descriptors.Any(descriptor => !descriptor.IsValid()))
            return new UiElementLookup(UiElementLookupStatus.Unavailable);
        var current = descriptors.FirstOrDefault(descriptor =>
            descriptor.Extractor == identity.Extractor
            && descriptor.Kind == identity.PublicKind
            && (descriptor.InventorySide ?? UiInventorySide.Unspecified)
                == identity.InventorySide
            && descriptor.Index == identity.Index);
        if (current is null)
            return new UiElementLookup(UiElementLookupStatus.Stale);
        return ReferenceEquals(current.Component, identity.Component)
            && ReferenceEquals(current.SemanticTarget, identity.SemanticTarget)
            && string.Equals(current.Guard, identity.Guard, StringComparison.Ordinal)
            ? new UiElementLookup(
                UiElementLookupStatus.Resolved,
                current.Component,
                current.SemanticTarget,
                current.Guard
            )
            : new UiElementLookup(UiElementLookupStatus.Stale);
    }
}

internal sealed record CapturedDialogueMenu(
    string Title,
    string Text,
    bool TextReadable
);

internal sealed class RuntimeUiElementRefOwner : IUiElementRefOwner
{
    private readonly WeakReference<IClickableMenu> _menu;
    private readonly UiExtractorKind _extractor;
    private readonly OpaqueRefStore _refs;

    public RuntimeUiElementRefOwner(
        IClickableMenu menu,
        UiExtractorKind extractor,
        OpaqueRefStore refs
    )
    {
        _menu = new WeakReference<IClickableMenu>(menu);
        _extractor = extractor;
        _refs = refs;
    }

    public bool TryGetMenuIdentity(out object menu)
    {
        if (_menu.TryGetTarget(out var current))
        {
            menu = current;
            return true;
        }
        menu = null!;
        return false;
    }

    public UiElementLookup ResolveCurrentElement(UiElementBindingIdentity identity)
    {
        try
        {
            if (!_menu.TryGetTarget(out var menu)
                || !ReferenceEquals(Game1.activeClickableMenu, menu)
                || identity.Extractor != _extractor)
                return new UiElementLookup(UiElementLookupStatus.Stale);

            return UiRuntimeProjector.ResolveCurrentElement(menu, _extractor, identity, _refs);
        }
        catch
        {
            return new UiElementLookup(UiElementLookupStatus.Unavailable);
        }
    }

}
