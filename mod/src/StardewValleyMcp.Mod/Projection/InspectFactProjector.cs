using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class InspectProjectionContext
{
    private readonly OpaqueRefStore _refs;
    private UiRuntimeProjectionCapture? _ui;

    public InspectProjectionContext(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    internal OpaqueRefStore Refs => _refs;

    public UiRuntimeProjectionCapture CaptureUi()
    {
        if (_ui is not null)
            return _ui;
        if (Game1.player is not { } player || Game1.activeClickableMenu is not { } menu)
            throw new InspectRefStaleException();
        _ui = UiRuntimeProjector.Capture(menu, player, _refs);
        return _ui;
    }
}

internal static class InspectFactProjector
{
    // WorldEntity 与 Character 必须复用 query_world 的公开 Fact 投影，保证同一 Ref
    // 在两条查询线路中的事实语义与 optional presence 一致。若未来确需高成本详情，
    // 应另行设计明确命名的公共能力，不能在 inspect 内维护第二套同名事实。
    public static InspectProjectionResult Project(
        Ref reference,
        InspectableRefTarget target,
        InspectProjectionContext context
    )
    {
        var item = new InspectedRef();
        var warnings = new List<QueryWarning>();
        switch (target)
        {
            case WorldEntityInspectTarget world:
                item.WorldEntity = WorldProjector.ProjectResolvedEntity(
                    world.Value,
                    reference,
                    context.Refs,
                    warnings
                );
                break;
            case CharacterInspectTarget character:
                item.Character = WorldProjector.ProjectResolvedCharacter(
                    character.Value,
                    reference,
                    context.Refs,
                    warnings
                );
                break;
            case InventoryItemInspectTarget inventoryItem:
                if (inventoryItem.Value.Target is not Item gameItem)
                    throw new InvalidOperationException("Item Ref 目标类型无效");
                item.InventoryItem = ItemFactProjector.Project(gameItem, reference);
                break;
            case ContainerInspectTarget container:
                if (Game1.player is not { } player)
                    throw new InvalidOperationException("玩家事实不可读");
                var view = InventoryViewResolver.CreateContainer(
                    container.Value,
                    player,
                    context.Refs
                );
                item.Inventory = InventoryProjector.Project(view, context.Refs, includeEmptySlots: false);
                item.Inventory.ContainerRef = reference.Clone();
                break;
            case UiElementInspectTarget:
                item.UiElement = ProjectUiElement(reference, context.CaptureUi(), warnings);
                break;
            default:
                throw new InvalidOperationException("当前 Ref 类型不支持检查");
        }
        return new InspectProjectionResult(item, warnings);
    }

    internal static UiElementFact ProjectUiElement(
        Ref reference,
        UiRuntimeProjectionCapture capture,
        ICollection<QueryWarning> warnings
    )
    {
        var fact = capture.Result.Snapshot.Elements.FirstOrDefault(element =>
            string.Equals(element.Ref?.Value, reference.Value, StringComparison.Ordinal));
        if (fact is null)
        {
            if (capture.ElementSetCompleteness == UiElementSetCompleteness.Incomplete)
                throw new InspectFactUnavailableException();
            throw new InspectRefStaleException();
        }
        foreach (var warning in capture.Result.Warnings.Where(warning =>
            string.Equals(warning.Ref?.Value, reference.Value, StringComparison.Ordinal)))
            warnings.Add(warning.Clone());
        var projected = fact.Clone();
        projected.Ref = reference.Clone();
        return projected;
    }
}

internal sealed record InspectProjectionResult(
    InspectedRef Item,
    IReadOnlyList<QueryWarning> Warnings
);

internal sealed class InspectRefStaleException : Exception
{
}

internal sealed class InspectFactUnavailableException : Exception
{
}
