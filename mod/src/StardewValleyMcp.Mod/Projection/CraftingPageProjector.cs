using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed record CapturedCraftingRecipeElement(
    int SourcePage,
    int GlobalIndex,
    object Component,
    object RecipeIdentity,
    string Guard,
    UiBounds Bounds,
    bool ComponentVisible,
    CraftingRecipeFact Fact
);

internal sealed record CraftingPageProjectionResult(
    UiElementSetCompleteness Completeness,
    string ActionState
)
{
    public static CraftingPageProjectionResult Complete(string actionState = "") =>
        new(UiElementSetCompleteness.Complete, actionState);
    public static CraftingPageProjectionResult Incomplete(string actionState = "") =>
        new(UiElementSetCompleteness.Incomplete, actionState);
}

/// <summary>投影精确原版 Crafting 页已构建的全部配方。</summary>
internal static class CraftingPageProjector
{
    internal const int RecipeLimit = 256;

    public static CraftingPageProjectionResult Extract(
        GameMenu menu,
        Farmer player,
        UiBounds viewport,
        List<UiElementDescriptor> output,
        List<QueryWarning> warnings
    )
    {
        if (menu.currentTab != GameMenu.craftingTab)
            return CraftingPageProjectionResult.Complete();
        var actionState = "crafting:unavailable";
        try
        {
            if (menu.currentTab < 0 || menu.currentTab >= menu.pages.Count
                || menu.pages[menu.currentTab] is not { } current)
                return Incomplete(warnings, actionState, "当前 Crafting 页暂时不可读");
            if (current.GetType() != typeof(CraftingPage))
            {
                warnings.Add(Warning(
                    "UI_GAME_MENU_PAGE_UNSUPPORTED",
                    "当前 Crafting 页不是受支持的原版页面"
                ));
                return CraftingPageProjectionResult.Complete();
            }
            if (!ReferenceEquals(player, Game1.player))
                return Incomplete(warnings, actionState, "当前玩家与 Crafting 页不一致");

            var page = (CraftingPage)current;
            actionState = $"crafting:{page.currentCraftingPage}:held:{page.heldItem is not null}";
            if (page.cooking)
            {
                warnings.Add(Warning(
                    "UI_CRAFTING_PAGE_UNSUPPORTED",
                    "当前烹饪页不属于本版本投影范围"
                ));
                return CraftingPageProjectionResult.Complete(actionState);
            }
            if (page.currentCraftingPage < 0
                || page.currentCraftingPage >= page.pagesOfCraftingRecipes.Count)
                return Incomplete(warnings, actionState, "Crafting 页码暂时不可读");

            var total = page.pagesOfCraftingRecipes.Sum(recipes => recipes?.Count ?? 0);
            if (total > RecipeLimit)
                return Incomplete(warnings, actionState, "Crafting 配方数量超过公开上限");
            var materialItems = CaptureMaterialItems(page._materialContainers);
            var captured = new List<CapturedCraftingRecipeElement>(total);
            for (var pageIndex = 0; pageIndex < page.pagesOfCraftingRecipes.Count; pageIndex++)
            {
                var recipes = page.pagesOfCraftingRecipes[pageIndex]
                    ?? throw new UiProjectionException("Crafting 配方页不可读");
                foreach (var pair in recipes)
                {
                    var component = pair.Key;
                    var recipe = pair.Value;
                    if (component is null || component.GetType() != typeof(ClickableTextureComponent)
                        || recipe is null || recipe.GetType() != typeof(CraftingRecipe))
                        throw new UiProjectionException("Crafting 配方组件不受支持");
                    var index = checked(component.myID - 201);
                    var fact = CaptureFact(recipe, player, materialItems);
                    captured.Add(new CapturedCraftingRecipeElement(
                        pageIndex,
                        index,
                        component,
                        recipe,
                        $"crafting-recipe:{pageIndex}:{index}:{recipe.name}",
                        Bounds(component.bounds),
                        component.visible,
                        fact
                    ));
                }
            }
            output.AddRange(CreateDescriptors(captured, page.currentCraftingPage, viewport));
            return CraftingPageProjectionResult.Complete(actionState);
        }
        catch
        {
            return Incomplete(warnings, actionState, "Crafting 配方的公开事实暂时不可读");
        }
    }

    internal static IReadOnlyList<UiElementDescriptor> CreateDescriptors(
        IReadOnlyList<CapturedCraftingRecipeElement> captured,
        int currentPage,
        UiBounds viewport
    )
    {
        if (captured.Count > RecipeLimit || currentPage < 0)
            throw new UiProjectionException("Crafting 配方数量或页码无效");
        var ordered = captured.OrderBy(item => item.GlobalIndex).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].GlobalIndex != index
                || ordered[index].SourcePage < 0
                || ordered[index].Component is null
                || ordered[index].RecipeIdentity is null
                || !PublicStringPolicy.IsNonEmptyValid(ordered[index].Guard))
                throw new UiProjectionException("Crafting 配方全局序号或绑定无效");
        }
        return ordered.Select(item =>
        {
            var visible = item.SourcePage == currentPage
                && UiProjectionPolicy.IsVisible(
                    item.Bounds,
                    item.ComponentVisible,
                    viewport
                );
            var center = UiProjectionPolicy.Center(item.Bounds);
            return new UiElementDescriptor(
                UiExtractorKind.GameMenu,
                UiElementKind.CraftingRecipe,
                item.GlobalIndex,
                item.Component,
                item.RecipeIdentity,
                item.Guard,
                item.Fact.DisplayName,
                visible,
                false,
                center.X,
                center.Y,
                CraftingRecipe: item.Fact
            );
        }).ToArray();
    }

    private static CraftingRecipeFact CaptureFact(
        CraftingRecipe recipe,
        Farmer player,
        IList<Item> materialItems
    )
    {
        var materials = new List<CraftingMaterialProjectionSource>(recipe.recipeList.Count);
        foreach (var pair in recipe.recipeList)
        {
#pragma warning disable CS0618
            var available = checked(
                player.getItemCount(pair.Key)
                + player.getItemCountInList(materialItems, pair.Key)
            );
#pragma warning restore CS0618
            materials.Add(new CraftingMaterialProjectionSource(
                pair.Key,
                recipe.getNameFromIndex(pair.Key),
                pair.Value,
                available
            ));
        }
        var outputs = recipe.itemToProduce.Select(itemId =>
        {
            var requestedId = recipe.bigCraftable ? $"(BC){itemId}" : $"(O){itemId}";
            var data = ItemRegistry.GetDataOrErrorItem(requestedId);
            if (data.IsErrorItem)
                throw new UiProjectionException("Crafting 配方产出无法解析");
            return new CraftingOutputProjectionSource(
                data.QualifiedItemId,
                data.DisplayName,
                recipe.numberProducedPerCraft
            );
        }).ToArray();
        return CraftingRecipeFactProjector.Project(new CraftingRecipeProjectionSource(
            recipe.name,
            recipe.DisplayName,
            player.craftingRecipes.ContainsKey(recipe.name),
            recipe.doesFarmerHaveIngredientsInInventory(materialItems),
            materials,
            outputs
        ));
    }

    private static IList<Item> CaptureMaterialItems(List<IInventory>? containers)
    {
        var items = new List<Item>();
        if (containers is null)
            return items;
        foreach (var container in containers)
        {
            if (container is null)
                throw new UiProjectionException("Crafting 材料容器不可读");
            items.AddRange(container.Where(item => item is not null)!);
        }
        return items;
    }

    private static CraftingPageProjectionResult Incomplete(
        List<QueryWarning> warnings,
        string actionState,
        string message
    )
    {
        warnings.Add(Warning("UI_CRAFTING_CAPTURE_INCOMPLETE", message));
        return CraftingPageProjectionResult.Incomplete(actionState);
    }

    private static QueryWarning Warning(string code, string message) =>
        new() { Code = code, Message = message };
    private static UiBounds Bounds(Rectangle bounds) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}
