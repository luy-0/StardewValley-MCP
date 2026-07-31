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
        OpaqueRefStore refs
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
        var identities = new HashSet<(UiExtractorKind Extractor, UiElementKind Kind, int Index)>();
        foreach (var descriptor in descriptors.Where(item => item.IsValid()))
        {
            if (descriptor.Extractor != extractor
                || !identities.Add((descriptor.Extractor, descriptor.Kind, descriptor.Index)))
                throw new InvalidOperationException("UI descriptor identity 不唯一");
        }

        var session = refs.BeginUiProjection(menu);
        var snapshot = new UiSnapshot
        {
            MenuOpen = true,
            Menu = shell.Clone(),
        };
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
            resultWarnings.Add(new QueryWarning
            {
                Code = "UI_ELEMENT_PROJECTION_FAILED",
                Message = $"{skipped} 个 UI 元素无法安全投影",
            });
        }
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
}

internal sealed record UiElementDescriptor(
    UiExtractorKind Extractor,
    UiElementKind Kind,
    int Index,
    object Component,
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
    IReadOnlyList<UiDescriptorWarning>? DescriptorWarnings = null
)
{
    public IReadOnlyList<UiDescriptorWarning> Warnings =>
        DescriptorWarnings ?? Array.Empty<UiDescriptorWarning>();

    public bool IsValid() =>
        Extractor != UiExtractorKind.Unsupported
        && Kind is UiElementKind.Tab or UiElementKind.DialogueResponse or UiElementKind.ItemSlot
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
}

internal static class UiProjectionPolicy
{
    public static UiMenuClassification ClassifyExact(
        Type runtimeType,
        Type gameMenuType,
        Type dialogueBoxType,
        Type shopMenuType
    )
    {
        if (runtimeType == gameMenuType)
            return UiMenuClassification.GameMenu;
        if (runtimeType == dialogueBoxType)
            return UiMenuClassification.DialogueBox;
        if (runtimeType == shopMenuType)
            return UiMenuClassification.ShopMenu;
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

    public static bool ShopEnabled(ShopActivationFacts facts) =>
        facts.Visible
        && facts.SafetyReady
        && !facts.HasHeldItem
        && !facts.ReadOnly
        && (facts.UnlimitedStock || facts.Stock > 0)
        && facts.CurrencyAmount >= facts.Price
        && facts.HasRequiredTradeItem
        && !facts.HasCanPurchaseCheck
        && facts.VanillaSafeSalable;

    public static bool IsExactActivationKnownType(Type runtimeType, Type knownType) =>
        runtimeType == knownType;

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
}

internal readonly record struct UiBounds(int X, int Y, int Width, int Height);

internal readonly record struct ShopActivationFacts(
    bool Visible,
    bool SafetyReady,
    bool HasHeldItem,
    bool ReadOnly,
    bool UnlimitedStock,
    int Stock,
    long Price,
    long CurrencyAmount,
    bool HasRequiredTradeItem,
    bool HasCanPurchaseCheck,
    bool VanillaSafeSalable
);
