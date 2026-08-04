using StardewModdingAPI;
using System.Reflection;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;
using SObject = StardewValley.Object;

namespace StardewValleyMcp.Mod;

internal interface IShopPurchaseRuntimeAdapter
{
    ShopPurchaseCapture Capture();
    ShopPurchasePlanResult Plan(
        ShopPurchaseCapture capture,
        ShopSaleBinding sale,
        int purchaseCount
    );
    ShopPurchaseCommitResult Commit(ShopPurchaseCapture capture, ShopPurchasePlan plan);
}

internal enum ShopPurchaseCaptureStatus
{
    Ready,
    NotReady,
    Unsupported,
    Unavailable,
}

internal enum ShopPurchasePlanStatus
{
    Ready,
    NotReady,
    Unsupported,
    Stale,
    Unavailable,
}

internal sealed record ShopSaleBinding(
    int AbsoluteIndex,
    object Component,
    object SalableIdentity,
    int UnitPrice,
    int Stock
);

internal sealed record ShopPurchaseCapture(
    ShopPurchaseCaptureStatus Status,
    object? MenuIdentity = null,
    object? PlayerIdentity = null,
    string UiRevision = "",
    IReadOnlyList<ShopSaleBinding>? Sales = null,
    InventorySnapshot? PlayerInventory = null,
    object? CommitState = null
);

internal sealed record ShopPurchasePlan(
    ShopSaleBinding Sale,
    int PurchaseCount,
    int OutputQuantity,
    int TotalPrice,
    int MoneyBefore,
    bool UnlimitedStock,
    string PlayerInventoryRevision,
    string OutputGuard,
    object OutputPrototype
)
{
    public bool SameAs(ShopPurchasePlan other) =>
        ReferenceEquals(Sale.Component, other.Sale.Component)
        && ReferenceEquals(Sale.SalableIdentity, other.Sale.SalableIdentity)
        && Sale.AbsoluteIndex == other.Sale.AbsoluteIndex
        && Sale.UnitPrice == other.Sale.UnitPrice
        && Sale.Stock == other.Sale.Stock
        && PurchaseCount == other.PurchaseCount
        && OutputQuantity == other.OutputQuantity
        && TotalPrice == other.TotalPrice
        && MoneyBefore == other.MoneyBefore
        && UnlimitedStock == other.UnlimitedStock
        && string.Equals(
            PlayerInventoryRevision,
            other.PlayerInventoryRevision,
            StringComparison.Ordinal
        )
        && string.Equals(OutputGuard, other.OutputGuard, StringComparison.Ordinal);
}

internal sealed record ShopPurchasePlanResult(
    ShopPurchasePlanStatus Status,
    ShopPurchasePlan? Plan,
    string Message
);

internal sealed record ShopPurchaseCommitResult(
    uint PurchaseCount,
    ItemFact Item,
    uint TotalPrice,
    uint MoneyBefore,
    uint MoneyAfter,
    uint? StockRemaining,
    string PlayerInventoryRevision,
    string UiRevision
);

internal sealed class LiveShopPurchaseRuntimeAdapter : IShopPurchaseRuntimeAdapter
{
    private static readonly MethodInfo? TryPurchaseMethod = typeof(ShopMenu).GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic
        )
        .SingleOrDefault(candidate => candidate.Name == "tryToPurchaseItem"
            && candidate.ReturnType == typeof(bool)
            && candidate.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(new[]
                {
                    typeof(ISalable), typeof(ISalable), typeof(int), typeof(int), typeof(int),
                }));
    private static readonly FieldInfo? StorageShopField = typeof(ShopMenu).GetField(
        "_isStorageShop",
        BindingFlags.Instance | BindingFlags.NonPublic
    );
    private readonly OpaqueRefStore _refs;

    public LiveShopPurchaseRuntimeAdapter(OpaqueRefStore refs) => _refs = refs;

    public ShopPurchaseCapture Capture()
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.NotReady);
        if (Game1.activeClickableMenu is not { } active)
            return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.NotReady);
        if (active.GetType() != typeof(ShopMenu))
            return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.Unsupported);
        if (TryPurchaseMethod is null || StorageShopField?.FieldType != typeof(bool))
            return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.Unavailable);

        try
        {
            var menu = (ShopMenu)active;
            if (menu.heldItem is not null || menu.readOnly || menu.safetyTimer > 0)
                return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.NotReady);
            if (menu.currency != 0 || StorageShopField.GetValue(menu) is not false)
                return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.Unsupported);

            var ui = UiRuntimeProjector.Capture(menu, player, _refs);
            if (ui.ElementSetCompleteness != UiElementSetCompleteness.Complete)
                return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.Unavailable);
            var selected = UiProjectionPolicy.SelectShopViewport(
                menu.currentItemIndex,
                menu.forSaleButtons.Count,
                menu.forSale.Count
            );
            if (selected is null)
                return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.Unavailable);
            var sales = new List<ShopSaleBinding>();
            for (var row = 0; row < selected.Count; row++)
            {
                var absolute = selected[row];
                var component = menu.forSaleButtons[row];
                var salable = menu.forSale[absolute];
                if (component is null || salable is null
                    || !menu.itemPriceAndStock.TryGetValue(salable, out var stock)
                    || stock.Price < 0 || stock.Stock < 0)
                    return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.Unavailable);
                sales.Add(new ShopSaleBinding(
                    absolute,
                    component,
                    salable,
                    stock.Price,
                    stock.Stock
                ));
            }
            var playerView = InventoryViewResolver.CreatePlayer(player);
            var playerInventory = InventoryProjector.Project(
                playerView,
                _refs,
                includeEmptySlots: true
            );
            return new ShopPurchaseCapture(
                ShopPurchaseCaptureStatus.Ready,
                menu,
                player,
                ui.Result.Snapshot.UiRevision,
                sales,
                playerInventory,
                new LiveShopPurchaseCommitState(menu, player)
            );
        }
        catch
        {
            return new ShopPurchaseCapture(ShopPurchaseCaptureStatus.Unavailable);
        }
    }

    public ShopPurchasePlanResult Plan(
        ShopPurchaseCapture capture,
        ShopSaleBinding sale,
        int purchaseCount
    )
    {
        if (capture.CommitState is not LiveShopPurchaseCommitState state
            || capture.PlayerInventory is null
            || purchaseCount is <= 0 or > 25
            || sale.SalableIdentity is not SObject item
            || item.GetType() != typeof(SObject))
            return Unsupported("当前商品不是首版支持的普通原版实物");
        if (!ReferenceEquals(Game1.activeClickableMenu, state.Menu)
            || !ReferenceEquals(Game1.player, state.Player)
            || !ContainsBinding(state.Menu, sale))
            return Stale("商店或商品绑定已变化");

        try
        {
            if (state.Menu.currency != 0
                || state.Menu.readOnly
                || state.Menu.heldItem is not null
                || state.Menu.safetyTimer > 0)
                return NotReady("当前商店尚未准备好购买");
            if (state.Menu.canPurchaseCheck is not null
                || state.Menu.onPurchase is not null
                || state.Menu.buyBackItems.Contains(item))
                return Unsupported("当前商品具有首版不执行的购买回调或回购语义");
            if (!state.Menu.itemPriceAndStock.TryGetValue(item, out var stock)
                || stock.Price != sale.UnitPrice
                || stock.Stock != sale.Stock)
                return Stale("商品价格或库存已变化");
            if (!string.IsNullOrEmpty(stock.TradeItem)
                || stock.ActionsOnPurchase is { Count: > 0 })
                return Unsupported("首版不支持交换物价格或购买动作");
            if (item.IsRecipe
                || item.isLostItem
                || item.specialItem
                || item.QualifiedItemId is "(O)434" or "(O)858")
                return Unsupported("首版不支持配方、遗失物或特殊商品");
            var unlimited = stock.Stock == ShopMenu.infiniteStock || item.IsInfiniteStock();
            if (!unlimited && stock.Stock < purchaseCount)
                return NotReady("当前库存不足，未执行购买");
            if (item.Stack <= 0)
                return Unavailable("商品模板数量无效");
            var outputQuantity = checked(item.Stack * purchaseCount);
            var output = item.GetSalableInstance() as Item;
            if (output is null || output.GetType() != typeof(SObject))
                return Unsupported("当前商品无法生成普通原版实物");
            if (outputQuantity > output.maximumStackSize())
                return NotReady("购买数量超过单个物品堆叠上限");
            output.Stack = outputQuantity;
            state.Player.GetItemReceiveBehavior(
                output,
                out var needsInventorySpace,
                out _
            );
            if (!needsInventorySpace)
                return Unsupported("首版不支持会自动消费或转换的商品");
            if (!output.CanBuyItem(state.Player)
                || !state.Player.couldInventoryAcceptThisItem(output))
                return NotReady("当前背包无法完整容纳商品");
            var totalPrice = checked(stock.Price * purchaseCount);
            var money = ShopMenu.getPlayerCurrencyAmount(state.Player, state.Menu.currency);
            if (money < totalPrice)
                return NotReady("当前金币不足，未执行购买");
            return Ready(new ShopPurchasePlan(
                sale,
                purchaseCount,
                outputQuantity,
                totalPrice,
                money,
                unlimited,
                capture.PlayerInventory.InventoryRevision,
                InventoryItemGuard.Create(output),
                output
            ));
        }
        catch (OverflowException)
        {
            return Unsupported("购买数量、价格或物品数量超出支持范围");
        }
        catch
        {
            return Unavailable("当前商品购买事实不可读");
        }
    }

    public ShopPurchaseCommitResult Commit(
        ShopPurchaseCapture capture,
        ShopPurchasePlan plan
    )
    {
        if (capture.CommitState is not LiveShopPurchaseCommitState state
            || plan.Sale.SalableIdentity is not SObject item
            || plan.OutputPrototype is not Item
            || !ReferenceEquals(Game1.activeClickableMenu, state.Menu)
            || !ReferenceEquals(Game1.player, state.Player)
            || !ContainsBinding(state.Menu, plan.Sale)
            || state.Menu.heldItem is not null)
            throw new InvalidOperationException("购买提交上下文已变化");
        if (!state.Menu.itemPriceAndStock.TryGetValue(item, out var beforeStock)
            || beforeStock.Price != plan.Sale.UnitPrice
            || beforeStock.Stock != plan.Sale.Stock
            || state.Player.Money != plan.MoneyBefore)
            throw new InvalidOperationException("购买价格、库存或金币已变化");

        var soldOut = (bool)(TryPurchaseMethod?.Invoke(
            state.Menu,
            new object?[] { item, null, plan.PurchaseCount, 0, 0 }
        ) ?? throw new InvalidOperationException("原版购买入口不可用"));
        if (!state.Menu.itemPriceAndStock.TryGetValue(item, out var afterStock))
            throw new InvalidOperationException("购买后库存事实不可读");
        var expectedStock = plan.UnlimitedStock
            ? beforeStock.Stock
            : checked(beforeStock.Stock - plan.PurchaseCount);
        var moneyAfter = ShopMenu.getPlayerCurrencyAmount(state.Player, state.Menu.currency);
        if (moneyAfter != plan.MoneyBefore - plan.TotalPrice
            || afterStock.Stock != expectedStock
            || soldOut != (!plan.UnlimitedStock && expectedStock <= 0)
            || state.Menu.heldItem is not Item purchased
            || purchased.GetType() != typeof(SObject)
            || purchased.Stack != plan.OutputQuantity
            || !string.Equals(
                InventoryItemGuard.Create(purchased),
                plan.OutputGuard,
                StringComparison.Ordinal
            ))
            throw new InvalidOperationException("原版购买结果与计划不一致");

        var purchasedFact = ItemFactProjector.Project(purchased);
        var remainder = state.Player.addItemToInventory(purchased);
        state.Menu.heldItem = remainder;
        if (remainder is not null)
            throw new InvalidOperationException("购买商品未能完整进入背包");

        if (soldOut)
        {
            if (!state.Menu.itemPriceAndStock.Remove(item)
                || plan.Sale.AbsoluteIndex < 0
                || plan.Sale.AbsoluteIndex >= state.Menu.forSale.Count
                || !ReferenceEquals(state.Menu.forSale[plan.Sale.AbsoluteIndex], item))
                throw new InvalidOperationException("售罄商品列表已变化");
            state.Menu.forSale.RemoveAt(plan.Sale.AbsoluteIndex);
            state.Menu.currentItemIndex = Math.Max(
                0,
                Math.Min(state.Menu.currentItemIndex, state.Menu.forSale.Count - 4)
            );
        }

        var after = Capture();
        if (after.Status != ShopPurchaseCaptureStatus.Ready
            || !ReferenceEquals(after.MenuIdentity, state.Menu)
            || !ReferenceEquals(after.PlayerIdentity, state.Player)
            || after.PlayerInventory is null)
            throw new InvalidOperationException("购买后商店或背包事实不可确认");
        return new ShopPurchaseCommitResult(
            checked((uint)plan.PurchaseCount),
            purchasedFact,
            checked((uint)plan.TotalPrice),
            checked((uint)plan.MoneyBefore),
            checked((uint)moneyAfter),
            plan.UnlimitedStock ? null : checked((uint)expectedStock),
            after.PlayerInventory.InventoryRevision,
            after.UiRevision
        );
    }

    private static bool ContainsBinding(ShopMenu menu, ShopSaleBinding sale)
    {
        var row = sale.AbsoluteIndex - menu.currentItemIndex;
        return sale.AbsoluteIndex >= 0
            && sale.AbsoluteIndex < menu.forSale.Count
            && row >= 0
            && row < menu.forSaleButtons.Count
            && ReferenceEquals(menu.forSale[sale.AbsoluteIndex], sale.SalableIdentity)
            && ReferenceEquals(menu.forSaleButtons[row], sale.Component);
    }

    private static ShopPurchasePlanResult Ready(ShopPurchasePlan plan) =>
        new(ShopPurchasePlanStatus.Ready, plan, "");
    private static ShopPurchasePlanResult NotReady(string message) =>
        new(ShopPurchasePlanStatus.NotReady, null, message);
    private static ShopPurchasePlanResult Unsupported(string message) =>
        new(ShopPurchasePlanStatus.Unsupported, null, message);
    private static ShopPurchasePlanResult Stale(string message) =>
        new(ShopPurchasePlanStatus.Stale, null, message);
    private static ShopPurchasePlanResult Unavailable(string message) =>
        new(ShopPurchasePlanStatus.Unavailable, null, message);

    private sealed record LiveShopPurchaseCommitState(ShopMenu Menu, Farmer Player);
}
