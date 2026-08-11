using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class UiProjector
{
    private const string NoMenuMarker = "no-menu";

    public static QueryUiResult ProjectNoMenu(OpaqueRefStore refs)
    {
        refs.CloseUiProjection();
        var snapshot = new UiSnapshot { MenuOpen = false };
        UiRevision.Finalize(snapshot, NoMenuMarker, UiExtractorKind.Unsupported, "");
        return new QueryUiResult { Snapshot = snapshot };
    }

    internal static QueryUiResult ProjectDescriptors(
        object menu,
        UiMenuFact shell,
        UiExtractorKind extractor,
        string actionState,
        IReadOnlyList<UiElementDescriptor> descriptors,
        IEnumerable<QueryWarning> warnings,
        IUiElementRefOwner owner,
        OpaqueRefStore refs,
        UiElementSetCompleteness completeness = UiElementSetCompleteness.Complete,
        IReadOnlyList<UiInventoryLink>? inventories = null
    )
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(shell);
        if (!PublicStringPolicy.IsNonEmptyValid(shell.MenuType)
            || !PublicStringPolicy.IsValid(shell.Title)
            || !PublicStringPolicy.IsValid(shell.DialogueText))
            throw new UiProjectionException("UI 基本事实不符合公开约束");
        if (extractor == UiExtractorKind.Unsupported && descriptors.Count != 0)
            throw new InvalidOperationException("Unsupported menu 不得投影元素");
        if (inventories is not null)
        {
            var sides = new HashSet<UiInventorySide>();
            foreach (var link in inventories)
            {
                if (link.Side is not UiInventorySide.Player and not UiInventorySide.Container
                    || !sides.Add(link.Side)
                    || !PublicStringPolicy.IsNonEmptyValid(link.InventoryRevision)
                    || link.Side == UiInventorySide.Player && link.ContainerRef is not null
                    || link.Side == UiInventorySide.Container && link.ContainerRef is null)
                    throw new UiProjectionException("UI 库存关联不符合公开约束");
            }
            var hasPlayerSlots = descriptors.Any(descriptor =>
                descriptor.InventorySide == UiInventorySide.Player);
            var hasOnlyPlayerLink = sides.SetEquals(new[] { UiInventorySide.Player });
            if (extractor == UiExtractorKind.ItemGrabSlot
                    && completeness == UiElementSetCompleteness.Complete
                    && descriptors.Count != 0
                    && !sides.SetEquals(new[]
                    {
                        UiInventorySide.Player,
                        UiInventorySide.Container,
                    })
                || extractor == UiExtractorKind.GameMenu
                    && (sides.Contains(UiInventorySide.Container)
                        || hasPlayerSlots != hasOnlyPlayerLink)
                || extractor is not UiExtractorKind.ItemGrabSlot and not UiExtractorKind.GameMenu
                    && sides.Count != 0)
                throw new UiProjectionException("UI 库存关联与 extractor 不一致");
        }
        var identities = new HashSet<(
            UiExtractorKind Extractor,
            UiElementKind Kind,
            UiInventorySide Side,
            UiEquipmentSlotKind EquipmentSlotKind,
            int Index
        )>();
        foreach (var descriptor in descriptors.Where(item => item.IsValid()))
        {
            if (descriptor.Extractor != extractor
                || !identities.Add((
                    descriptor.Extractor,
                    descriptor.Kind,
                    descriptor.InventorySide ?? UiInventorySide.Unspecified,
                    descriptor.EquipmentSlotKind ?? UiEquipmentSlotKind.Unspecified,
                    descriptor.Index
                )))
                throw new InvalidOperationException("UI descriptor identity 不唯一");
        }

        var session = refs.BeginUiProjection(menu);
        var snapshot = new UiSnapshot
        {
            MenuOpen = true,
            Menu = shell.Clone(),
        };
        if (inventories is not null)
            snapshot.Inventories.AddRange(inventories.Select(item => item.Clone()));
        var resultWarnings = warnings.Select(item => item.Clone()).ToList();
        var skipped = 0;
        foreach (var descriptor in descriptors)
        {
            if (!descriptor.IsValid())
            {
                skipped++;
                continue;
            }
            var reference = refs.ObserveUiElement(
                session,
                owner,
                new UiElementBindingIdentity(
                    descriptor.Extractor,
                    descriptor.Kind,
                    descriptor.InventorySide ?? UiInventorySide.Unspecified,
                    descriptor.EquipmentSlotKind ?? UiEquipmentSlotKind.Unspecified,
                    descriptor.Index,
                    descriptor.Component,
                    descriptor.SemanticTarget,
                    descriptor.Guard
                )
            );
            var fact = descriptor.ToFact(reference);
            snapshot.Elements.Add(fact);
            foreach (var warning in descriptor.Warnings)
            {
                resultWarnings.Add(new QueryWarning
                {
                    Code = warning.Code,
                    Message = warning.Message,
                    Ref = reference.Clone(),
                });
            }
        }
        if (skipped > 0)
        {
            completeness = UiElementSetCompleteness.Incomplete;
            resultWarnings.Add(new QueryWarning
            {
                Code = "UI_ELEMENT_PROJECTION_FAILED",
                Message = $"{skipped} 个 UI 元素无法安全投影",
            });
        }
        if (completeness == UiElementSetCompleteness.Complete)
            refs.CompleteUiProjection(session);
        UiRevision.Finalize(snapshot, session.MenuEpoch, extractor, actionState);

        var result = new QueryUiResult { Snapshot = snapshot };
        result.Warnings.AddRange(SortWarnings(resultWarnings));
        return result;
    }

    internal static IEnumerable<QueryWarning> SortWarnings(IEnumerable<QueryWarning> warnings) =>
        warnings
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Ref?.Value ?? "", StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .Select(item => item.Clone());

    internal static UiInventoryLink ToInventoryLink(
        UiInventorySide side,
        InventorySnapshot snapshot
    )
    {
        var link = new UiInventoryLink
        {
            Side = side,
            InventoryRevision = snapshot.InventoryRevision,
            SlotCount = snapshot.SlotCount,
        };
        if (snapshot.ContainerRef is not null)
            link.ContainerRef = snapshot.ContainerRef.Clone();
        return link;
    }
}

internal enum UiElementSetCompleteness
{
    Complete,
    Incomplete,
}

internal sealed record UiRuntimeProjectionCapture(
    QueryUiResult Result,
    UiElementSetCompleteness ElementSetCompleteness
);

internal sealed record UiElementDescriptor(
    UiExtractorKind Extractor,
    UiElementKind Kind,
    int Index,
    object? Component,
    object SemanticTarget,
    string Guard,
    string Label,
    bool Visible,
    bool Enabled,
    int CenterX,
    int CenterY,
    ItemFact? Item = null,
    long? Price = null,
    uint? Stock = null,
    IReadOnlyList<UiDescriptorWarning>? DescriptorWarnings = null,
    UiInventorySide? InventorySide = null,
    Ref? ItemRef = null,
    UiEquipmentSlotKind? EquipmentSlotKind = null,
    CraftingRecipeFact? CraftingRecipe = null
)
{
    public IReadOnlyList<UiDescriptorWarning> Warnings =>
        DescriptorWarnings ?? Array.Empty<UiDescriptorWarning>();

    public bool IsValid() =>
        Extractor != UiExtractorKind.Unsupported
        && Kind is UiElementKind.Tab
            or UiElementKind.DialogueResponse
            or UiElementKind.DialogueAdvance
            or UiElementKind.ItemSlot
            or UiElementKind.EquipmentSlot
            or UiElementKind.CraftingRecipe
        && (Kind == UiElementKind.DialogueAdvance ? Component is null : Component is not null)
        && (InventorySide is null
            || Kind == UiElementKind.ItemSlot
                && InventorySide is (UiInventorySide.Player or UiInventorySide.Container)
                && Item is null
                && Price is null
                && Stock is null
                && EquipmentSlotKind is null
                && CraftingRecipe is null
                && !Enabled)
        && (ItemRef is null || InventorySide is not null)
        && (EquipmentSlotKind is null
            || Kind == UiElementKind.EquipmentSlot
                && EquipmentSlotKind != UiEquipmentSlotKind.Unspecified
                && InventorySide is null
                && ItemRef is null
                && Price is null
                && Stock is null
                && CraftingRecipe is null
                && (Item is null || Item.Ref is null)
                && !Enabled)
        && (Kind != UiElementKind.EquipmentSlot || EquipmentSlotKind is not null)
        && (CraftingRecipe is null
            || Kind == UiElementKind.CraftingRecipe
                && Component is not null
                && InventorySide is null
                && ItemRef is null
                && EquipmentSlotKind is null
                && Item is null
                && Price is null
                && Stock is null
                && !Enabled)
        && (Kind != UiElementKind.CraftingRecipe || CraftingRecipe is not null)
        && Index >= 0
        && PublicStringPolicy.IsValid(Label)
        && !string.IsNullOrEmpty(Guard);

    public UiElementFact ToFact(Ref reference)
    {
        var fact = new UiElementFact
        {
            Ref = reference.Clone(),
            Kind = Kind,
            Label = Label,
            Visible = Visible,
            Enabled = Enabled,
            Center = new PixelPoint { X = CenterX, Y = CenterY },
            Index = checked((uint)Index),
        };
        if (Item is not null)
            fact.Item = Item.Clone();
        if (Price.HasValue)
            fact.Price = Price.Value;
        if (Stock.HasValue)
            fact.Stock = Stock.Value;
        if (InventorySide.HasValue)
            fact.InventorySide = InventorySide.Value;
        if (ItemRef is not null)
            fact.ItemRef = ItemRef.Clone();
        if (EquipmentSlotKind.HasValue)
            fact.EquipmentSlotKind = EquipmentSlotKind.Value;
        if (CraftingRecipe is not null)
            fact.CraftingRecipe = CraftingRecipe.Clone();
        return fact;
    }
}

internal readonly record struct UiDescriptorWarning(string Code, string Message);

internal sealed class UiProjectionException : Exception
{
    public UiProjectionException(string message)
        : base(message)
    {
    }
}

internal enum UiMenuClassification
{
    Unsupported,
    GameMenu,
    DialogueBox,
    ShopMenu,
    ItemGrabMenu,
}

internal static class UiProjectionPolicy
{
    public static UiMenuClassification ClassifyExact(
        Type runtimeType,
        Type gameMenuType,
        Type dialogueBoxType,
        Type shopMenuType,
        Type itemGrabMenuType
    )
    {
        if (runtimeType == gameMenuType)
            return UiMenuClassification.GameMenu;
        if (runtimeType == dialogueBoxType)
            return UiMenuClassification.DialogueBox;
        if (runtimeType == shopMenuType)
            return UiMenuClassification.ShopMenu;
        if (runtimeType == itemGrabMenuType)
            return UiMenuClassification.ItemGrabMenu;
        return UiMenuClassification.Unsupported;
    }

    public static bool IsExactModal(
        Type runtimeType,
        Type dialogueBoxType,
        Type letterViewerType
    ) => runtimeType == dialogueBoxType || runtimeType == letterViewerType;

    public static bool IsVisible(UiBounds bounds, bool componentVisible, UiBounds viewport) =>
        componentVisible
        && bounds.Width > 0
        && bounds.Height > 0
        && viewport.Width > 0
        && viewport.Height > 0
        && bounds.X < (long)viewport.X + viewport.Width
        && (long)bounds.X + bounds.Width > viewport.X
        && bounds.Y < (long)viewport.Y + viewport.Height
        && (long)bounds.Y + bounds.Height > viewport.Y;

    public static (int X, int Y) Center(UiBounds bounds) =>
        (checked(bounds.X + bounds.Width / 2), checked(bounds.Y + bounds.Height / 2));

    public static IReadOnlyList<int>? SelectShopViewport(
        int currentItemIndex,
        int buttonCount,
        int saleCount
    )
    {
        if (currentItemIndex < 0 || buttonCount < 0 || saleCount < 0)
            throw new UiProjectionException("Shop viewport 索引无效");
        if (buttonCount > 16)
            return null;
        var selected = new List<int>(buttonCount);
        for (var row = 0; row < buttonCount; row++)
        {
            var absolute = checked(currentItemIndex + row);
            if (absolute >= saleCount)
                break;
            selected.Add(absolute);
        }
        return selected;
    }

    public static bool CanActivateGameMenuElement(
        UiExtractorKind extractor,
        UiElementKind resolvedKind,
        UiElementKind factKind,
        Type runtimeType,
        Type gameMenuType
    ) => extractor == UiExtractorKind.GameMenu
        && resolvedKind == UiElementKind.Tab
        && factKind == UiElementKind.Tab
        && runtimeType == gameMenuType;

    public static bool DialogueEnabled(
        bool visible,
        bool transitioning,
        bool safetyReady,
        bool textReadable,
        int characterIndex,
        int textLength
    ) => visible
        && !transitioning
        && safetyReady
        && textReadable
        && characterIndex >= textLength - 1;

    public static bool DialogueHasNextPage(
        bool hasCharacterDialogue,
        bool continuedOnNextScreen,
        int brokenUpPageCount,
        int plainDialogueCount
    ) => hasCharacterDialogue
        ? continuedOnNextScreen || brokenUpPageCount > 1
        : plainDialogueCount > 1;

    public static string DialogueAdvanceLabel(bool hasNextPage) =>
        hasNextPage ? "继续" : "结束";

    public static UiExtractorKind DialogueExtractor(bool isQuestion) =>
        isQuestion ? UiExtractorKind.DialogueResponse : UiExtractorKind.DialogueAdvance;

    public static UiDialogueKind? DialogueKind(string? questionKey, bool eventUp) =>
        !eventUp && questionKey == "Sleep" ? UiDialogueKind.SleepConfirmation : null;
}

internal readonly record struct UiBounds(int X, int Y, int Width, int Height);
