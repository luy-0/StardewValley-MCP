using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class PurchaseShopItemHandler : ILongRunningCapabilityHandler
{
    private readonly OpaqueRefStore _refs;
    private readonly IShopPurchaseRuntimeAdapter _runtime;

    public PurchaseShopItemHandler(OpaqueRefStore refs)
        : this(refs, new LiveShopPurchaseRuntimeAdapter(refs)) { }

    internal PurchaseShopItemHandler(
        OpaqueRefStore refs,
        IShopPurchaseRuntimeAdapter runtime
    )
    {
        _refs = refs;
        _runtime = runtime;
    }

    public string Id => "purchase_shop_item";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.PurchaseShopItem;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("purchase_shop_item 请求类型无效");
        var value = request.PurchaseShopItem;
        if (!PublicStringPolicy.IsNonEmptyValid(value.SaleRef?.Value))
            return Invalid("sale_ref 格式无效");
        if (value.PurchaseCount is 0 or > 25)
            return Invalid("purchase_count 必须在 1..25 之间");
        if (!IsRevision(value.UiRevision))
            return Invalid("ui_revision 格式无效");
        return null;
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new PurchaseShopItemContinuation(_refs, _runtime, request.PurchaseShopItem);

    private static bool IsRevision(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };
}

internal sealed class PurchaseShopItemContinuation : ICommandContinuation
{
    private readonly OpaqueRefStore _refs;
    private readonly IShopPurchaseRuntimeAdapter _runtime;
    private readonly PurchaseShopItemRequest _request;
    private PreparedShopPurchase? _prepared;
    private bool _committing;

    public PurchaseShopItemContinuation(
        OpaqueRefStore refs,
        IShopPurchaseRuntimeAdapter runtime,
        PurchaseShopItemRequest request
    )
    {
        _refs = refs;
        _runtime = runtime;
        _request = request.Clone();
    }

    public string Phase => _prepared is null ? "preflight" : "ready_to_commit";
    public uint? ProgressPercent => _prepared is null ? 0u : 50u;
    public bool CanCancel => !_committing;

    public ContinuationStep Tick(ContinuationStopSignal signal)
    {
        if (signal != ContinuationStopSignal.None)
            return new ContinuationStep.Stopped();
        if (_prepared is null)
        {
            var preparation = Prepare();
            if (preparation.Error is not null)
                return new ContinuationStep.Failed(preparation.Error);
            _prepared = preparation.Value!;
            return new ContinuationStep.Pending();
        }

        var preparationAgain = Prepare();
        if (preparationAgain.Error is not null)
            return new ContinuationStep.Failed(preparationAgain.Error);
        var current = preparationAgain.Value!;
        if (!SamePrepared(_prepared, current))
            return Failed(ErrorCode.StaleRef, "商店、商品、价格、库存、金币或背包状态已变化");

        _committing = true;
        try
        {
            var committed = _runtime.Commit(current.Capture, current.Plan);
            if (committed.PurchaseCount != _request.PurchaseCount
                || committed.Item is null
                || committed.Item.Ref is not null
                || committed.Item.Stack == 0
                || committed.MoneyBefore < committed.MoneyAfter
                || committed.TotalPrice != committed.MoneyBefore - committed.MoneyAfter
                || !IsRevision(committed.PlayerInventoryRevision)
                || !IsRevision(committed.UiRevision))
                return Failed(ErrorCode.Internal, "购买结果无效");

            var result = new PurchaseShopItemResult
            {
                PurchaseCount = committed.PurchaseCount,
                Item = committed.Item.Clone(),
                TotalPrice = committed.TotalPrice,
                MoneyBefore = committed.MoneyBefore,
                MoneyAfter = committed.MoneyAfter,
                PlayerInventoryRevision = committed.PlayerInventoryRevision,
                UiRevision = committed.UiRevision,
            };
            if (committed.StockRemaining.HasValue)
                result.StockRemaining = committed.StockRemaining.Value;
            return new ContinuationStep.Succeeded(new CapabilityResult
            {
                PurchaseShopItem = result,
            });
        }
        catch
        {
            return Failed(
                ErrorCode.ExecutionFailed,
                "购买提交失败；请重新查询商店与背包，不能直接重试"
            );
        }
    }

    private PreparationResult Prepare()
    {
        var capture = Capture();
        if (capture.Error is not null)
            return new PreparationResult(null, capture.Error);
        var binding = ResolveSale(capture.Value!);
        if (binding.Error is not null)
            return new PreparationResult(null, binding.Error);
        var planned = _runtime.Plan(
            capture.Value!,
            binding.Value!,
            checked((int)_request.PurchaseCount)
        );
        if (planned.Status != ShopPurchasePlanStatus.Ready || planned.Plan is null)
        {
            var code = planned.Status switch
            {
                ShopPurchasePlanStatus.NotReady => ErrorCode.NotReady,
                ShopPurchasePlanStatus.Unsupported => ErrorCode.NotReady,
                ShopPurchasePlanStatus.Stale => ErrorCode.StaleRef,
                _ => ErrorCode.Internal,
            };
            return new PreparationResult(null, Error(code, planned.Message));
        }
        return new PreparationResult(new PreparedShopPurchase(
            capture.Value!,
            binding.Value!,
            planned.Plan
        ), null);
    }

    private CaptureResult Capture()
    {
        var capture = _runtime.Capture();
        if (capture.Status != ShopPurchaseCaptureStatus.Ready)
        {
            var error = capture.Status switch
            {
                ShopPurchaseCaptureStatus.NotReady => Error(
                    ErrorCode.NotReady,
                    "当前商店尚未准备好或游标持有商品"
                ),
                ShopPurchaseCaptureStatus.Unsupported => Error(
                    ErrorCode.NotReady,
                    "当前菜单或商店类型不支持原子购买"
                ),
                _ => Error(ErrorCode.Internal, "当前商店事实不可读"),
            };
            return new CaptureResult(null, error);
        }
        if (capture.MenuIdentity is null
            || capture.PlayerIdentity is null
            || capture.Sales is null
            || capture.PlayerInventory is null
            || capture.CommitState is null
            || !IsRevision(capture.UiRevision)
            || !IsRevision(capture.PlayerInventory.InventoryRevision))
            return new CaptureResult(null, Error(ErrorCode.Internal, "商店捕获无效"));
        if (!string.Equals(_request.UiRevision, capture.UiRevision, StringComparison.Ordinal))
            return new CaptureResult(null, Error(ErrorCode.StaleRef, "UI Revision 已失效"));
        return new CaptureResult(capture, null);
    }

    private SaleResult ResolveSale(ShopPurchaseCapture capture)
    {
        var resolved = _refs.ResolveUiElement(_request.SaleRef);
        if (resolved.Status != UiElementResolveStatus.Resolved || resolved.Target is null)
        {
            var error = resolved.Status switch
            {
                UiElementResolveStatus.Stale => Error(ErrorCode.StaleRef, "sale_ref 已失效"),
                UiElementResolveStatus.NotFound => Error(ErrorCode.NotFound, "sale_ref 不存在"),
                UiElementResolveStatus.Unsupported => Error(ErrorCode.InvalidArgument, "sale_ref 类型无效"),
                _ => Error(ErrorCode.Internal, "sale_ref 当前不可解析"),
            };
            return new SaleResult(null, error);
        }
        var target = resolved.Target;
        if (target.Extractor != UiExtractorKind.ShopSaleRow
            || target.PublicKind != UiElementKind.ItemSlot
            || target.InventorySide != UiInventorySide.Unspecified
            || target.EquipmentSlotKind != UiEquipmentSlotKind.Unspecified
            || target.Component is null)
            return new SaleResult(null, Error(ErrorCode.InvalidArgument, "Ref 不是商店商品行"));
        var sale = capture.Sales!.SingleOrDefault(item =>
            item.AbsoluteIndex == target.Index
            && ReferenceEquals(item.Component, target.Component)
            && ReferenceEquals(item.SalableIdentity, target.Target));
        return sale is null
            ? new SaleResult(null, Error(ErrorCode.StaleRef, "商品组件或对象已变化"))
            : new SaleResult(sale, null);
    }

    private static bool SamePrepared(PreparedShopPurchase left, PreparedShopPurchase right) =>
        ReferenceEquals(left.Capture.MenuIdentity, right.Capture.MenuIdentity)
        && ReferenceEquals(left.Capture.PlayerIdentity, right.Capture.PlayerIdentity)
        && ReferenceEquals(left.Sale.Component, right.Sale.Component)
        && ReferenceEquals(left.Sale.SalableIdentity, right.Sale.SalableIdentity)
        && left.Sale.AbsoluteIndex == right.Sale.AbsoluteIndex
        && left.Plan.SameAs(right.Plan);

    private static bool IsRevision(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static ContinuationStep Failed(ErrorCode code, string message) =>
        new ContinuationStep.Failed(Error(code, message));
    private static Error Error(ErrorCode code, string message) => new()
    {
        Code = code,
        Message = message,
    };

    private sealed record PreparedShopPurchase(
        ShopPurchaseCapture Capture,
        ShopSaleBinding Sale,
        ShopPurchasePlan Plan
    );
    private sealed record CaptureResult(ShopPurchaseCapture? Value, Error? Error);
    private sealed record SaleResult(ShopSaleBinding? Value, Error? Error);
    private sealed record PreparationResult(PreparedShopPurchase? Value, Error? Error);
}
