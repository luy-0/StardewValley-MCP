using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class InspectProjectionContext
{
    private readonly OpaqueRefStore _refs;
    private QueryUiResult? _ui;

    public InspectProjectionContext(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    internal OpaqueRefStore Refs => _refs;

    public QueryUiResult CaptureUi()
    {
        if (_ui is not null)
            return _ui;
        if (Game1.player is not { } player || Game1.activeClickableMenu is not { } menu)
            throw new InspectRefStaleException();
        _ui = UiRuntimeProjector.Project(menu, player, _refs);
        return _ui;
    }
}

internal static class InspectFactProjector
{
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
                    context.Refs
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
                var ui = context.CaptureUi();
                var fact = ui.Snapshot.Elements.FirstOrDefault(element =>
                    string.Equals(element.Ref?.Value, reference.Value, StringComparison.Ordinal));
                if (fact is null)
                    throw new InspectRefStaleException();
                item.UiElement = fact.Clone();
                item.UiElement.Ref = reference.Clone();
                warnings.AddRange(ui.Warnings.Where(warning =>
                    string.Equals(warning.Ref?.Value, reference.Value, StringComparison.Ordinal)));
                break;
            default:
                throw new InvalidOperationException("当前 Ref 类型不支持检查");
        }
        return new InspectProjectionResult(item, warnings);
    }
}

internal sealed record InspectProjectionResult(
    InspectedRef Item,
    IReadOnlyList<QueryWarning> Warnings
);

internal sealed class InspectRefStaleException : Exception
{
}
