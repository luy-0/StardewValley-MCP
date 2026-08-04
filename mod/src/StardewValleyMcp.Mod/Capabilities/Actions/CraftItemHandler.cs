using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class CraftItemHandler : ILongRunningCapabilityHandler
{
    private readonly OpaqueRefStore _refs;
    private readonly ICraftItemRuntimeAdapter _runtime;

    public CraftItemHandler(OpaqueRefStore refs)
        : this(refs, new LiveCraftItemRuntimeAdapter(refs)) { }

    internal CraftItemHandler(OpaqueRefStore refs, ICraftItemRuntimeAdapter runtime)
    {
        _refs = refs;
        _runtime = runtime;
    }

    public string Id => "craft_item";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.CraftItem;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("craft_item 请求类型无效");
        var value = request.CraftItem;
        if (!PublicStringPolicy.IsNonEmptyValid(value.RecipeRef?.Value))
            return Invalid("recipe_ref 格式无效");
        if (value.CraftCount is 0 or > 25)
            return Invalid("craft_count 必须在 1..25 之间");
        if (!IsRevision(value.UiRevision))
            return Invalid("ui_revision 格式无效");
        return null;
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new CraftItemContinuation(_refs, _runtime, request.CraftItem);

    private static bool IsRevision(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };
}

internal sealed class CraftItemContinuation : ICommandContinuation
{
    private readonly OpaqueRefStore _refs;
    private readonly ICraftItemRuntimeAdapter _runtime;
    private readonly CraftItemRequest _request;
    private PreparedCraftItem? _prepared;
    private bool _committing;

    public CraftItemContinuation(
        OpaqueRefStore refs,
        ICraftItemRuntimeAdapter runtime,
        CraftItemRequest request
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

        var capture = Capture();
        if (capture.Error is not null)
            return new ContinuationStep.Failed(capture.Error);
        var current = capture.Value!;
        if (!SameContext(current, _prepared))
            return Failed(ErrorCode.StaleRef, "Crafting 菜单、页面、玩家或配方组件已变化");
        var recipe = ResolveRecipe(current);
        if (recipe.Error is not null)
            return new ContinuationStep.Failed(recipe.Error);
        if (!ReferenceEquals(recipe.Value!.RecipeIdentity, _prepared.RecipeIdentity)
            || !ReferenceEquals(recipe.Value.Component, _prepared.Component))
            return Failed(ErrorCode.StaleRef, "配方绑定已变化");

        _committing = true;
        try
        {
            var committed = _runtime.Commit(
                current,
                recipe.Value,
                checked((int)_request.CraftCount)
            );
            if (committed.CompletedCraftCount == 0)
            {
                var message = committed.StopReason == CraftItemRuntimeStopReason.MaterialsInsufficient
                    ? "当前材料不足，未开始制作"
                    : "当前背包无法完整容纳产出，未开始制作";
                return Failed(ErrorCode.NotReady, message);
            }
            if (committed.CompletedCraftCount < 0
                || committed.CompletedCraftCount > _request.CraftCount
                || committed.Outputs.Count == 0
                || !IsRevision(committed.PlayerInventoryRevision)
                || !IsRevision(committed.UiRevision)
                || (committed.StopReason == CraftItemRuntimeStopReason.Completed)
                    != (committed.CompletedCraftCount == _request.CraftCount))
                return Failed(ErrorCode.Internal, "制作结果无效");

            var result = new CraftItemResult
            {
                RequestedCraftCount = _request.CraftCount,
                CompletedCraftCount = checked((uint)committed.CompletedCraftCount),
                StopReason = committed.StopReason switch
                {
                    CraftItemRuntimeStopReason.Completed => CraftItemStopReason.Completed,
                    CraftItemRuntimeStopReason.MaterialsInsufficient =>
                        CraftItemStopReason.MaterialsInsufficient,
                    CraftItemRuntimeStopReason.InventoryFull => CraftItemStopReason.InventoryFull,
                    _ => CraftItemStopReason.Unspecified,
                },
                PlayerInventoryRevision = committed.PlayerInventoryRevision,
                UiRevision = committed.UiRevision,
            };
            if (result.StopReason == CraftItemStopReason.Unspecified)
                return Failed(ErrorCode.Internal, "制作停止原因无效");
            result.Outputs.AddRange(committed.Outputs.Select(item => item.Clone()));
            result.MaterialsConsumed.AddRange(
                committed.MaterialsConsumed.Select(item => item.Clone())
            );
            return new ContinuationStep.Succeeded(new CapabilityResult
            {
                CraftItem = result,
            });
        }
        catch (Exception exception)
        {
            return Failed(
                ErrorCode.ExecutionFailed,
                $"制作提交失败：{exception.GetBaseException().Message}；如产物已被保留在游标，请重新查询当前界面与背包"
            );
        }
    }

    private PreparationResult Prepare()
    {
        var capture = Capture();
        if (capture.Error is not null)
            return new PreparationResult(null, capture.Error);
        var recipe = ResolveRecipe(capture.Value!);
        if (recipe.Error is not null)
            return new PreparationResult(null, recipe.Error);
        return new PreparationResult(new PreparedCraftItem(
            capture.Value!.MenuIdentity!,
            capture.Value.PageIdentity!,
            capture.Value.PlayerIdentity!,
            recipe.Value!.Component,
            recipe.Value.RecipeIdentity
        ), null);
    }

    private CaptureResult Capture()
    {
        var capture = _runtime.Capture();
        if (capture.Status != CraftItemCaptureStatus.Ready)
        {
            var error = capture.Status switch
            {
                CraftItemCaptureStatus.NotReady => Error(
                    ErrorCode.NotReady,
                    "当前 Crafting 页面尚未准备好或游标持有物品"
                ),
                CraftItemCaptureStatus.Unsupported => Error(
                    ErrorCode.NotReady,
                    "当前菜单不支持制作物品"
                ),
                _ => Error(ErrorCode.Internal, "当前 Crafting 事实不可读"),
            };
            return new CaptureResult(null, error);
        }
        if (capture.MenuIdentity is null
            || capture.PageIdentity is null
            || capture.PlayerIdentity is null
            || capture.Recipes is null
            || capture.CommitState is null
            || !IsRevision(capture.UiRevision))
            return new CaptureResult(null, Error(ErrorCode.Internal, "Crafting 捕获无效"));
        if (!string.Equals(_request.UiRevision, capture.UiRevision, StringComparison.Ordinal))
            return new CaptureResult(null, Error(ErrorCode.StaleRef, "UI Revision 已失效"));
        return new CaptureResult(capture, null);
    }

    private RecipeResult ResolveRecipe(CraftItemCapture capture)
    {
        var resolved = _refs.ResolveUiElement(_request.RecipeRef);
        if (resolved.Status != UiElementResolveStatus.Resolved || resolved.Target is null)
        {
            var error = resolved.Status switch
            {
                UiElementResolveStatus.Stale => Error(ErrorCode.StaleRef, "recipe_ref 已失效"),
                UiElementResolveStatus.NotFound => Error(ErrorCode.NotFound, "recipe_ref 不存在"),
                UiElementResolveStatus.Unsupported => Error(ErrorCode.InvalidArgument, "recipe_ref 类型无效"),
                _ => Error(ErrorCode.Internal, "recipe_ref 当前不可解析"),
            };
            return new RecipeResult(null, error);
        }
        var target = resolved.Target;
        if (target.Extractor != UiExtractorKind.GameMenu
            || target.PublicKind != UiElementKind.CraftingRecipe
            || target.InventorySide != UiInventorySide.Unspecified
            || target.EquipmentSlotKind != UiEquipmentSlotKind.Unspecified
            || target.Component is null)
            return new RecipeResult(null, Error(ErrorCode.InvalidArgument, "Ref 不是 Crafting 配方"));
        var recipe = capture.Recipes!.SingleOrDefault(item =>
            item.GlobalIndex == target.Index
            && ReferenceEquals(item.Component, target.Component)
            && ReferenceEquals(item.RecipeIdentity, target.Target));
        return recipe is null
            ? new RecipeResult(null, Error(ErrorCode.StaleRef, "配方组件或对象已变化"))
            : new RecipeResult(recipe, null);
    }

    private static bool SameContext(CraftItemCapture capture, PreparedCraftItem prepared) =>
        ReferenceEquals(capture.MenuIdentity, prepared.MenuIdentity)
        && ReferenceEquals(capture.PageIdentity, prepared.PageIdentity)
        && ReferenceEquals(capture.PlayerIdentity, prepared.PlayerIdentity);

    private static bool IsRevision(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static ContinuationStep Failed(ErrorCode code, string message) =>
        new ContinuationStep.Failed(Error(code, message));
    private static Error Error(ErrorCode code, string message) => new()
    {
        Code = code,
        Message = message,
    };

    private sealed record PreparedCraftItem(
        object MenuIdentity,
        object PageIdentity,
        object PlayerIdentity,
        object Component,
        object RecipeIdentity
    );
    private sealed record CaptureResult(CraftItemCapture? Value, Error? Error);
    private sealed record RecipeResult(CraftItemRecipeBinding? Value, Error? Error);
    private sealed record PreparationResult(PreparedCraftItem? Value, Error? Error);
}
