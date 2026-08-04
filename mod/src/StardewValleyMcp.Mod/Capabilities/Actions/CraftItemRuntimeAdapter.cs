using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal interface ICraftItemRuntimeAdapter
{
    CraftItemCapture Capture();
    CraftItemCommitResult Commit(
        CraftItemCapture capture,
        CraftItemRecipeBinding recipe,
        int craftCount
    );
}

internal enum CraftItemCaptureStatus
{
    Ready,
    NotReady,
    Unsupported,
    Unavailable,
}

internal enum CraftItemRuntimeStopReason
{
    Completed,
    MaterialsInsufficient,
    InventoryFull,
}

internal sealed record CraftItemRecipeBinding(
    int SourcePage,
    int GlobalIndex,
    object Component,
    object RecipeIdentity
);

internal sealed record CraftItemCapture(
    CraftItemCaptureStatus Status,
    object? MenuIdentity = null,
    object? PageIdentity = null,
    object? PlayerIdentity = null,
    string UiRevision = "",
    IReadOnlyList<CraftItemRecipeBinding>? Recipes = null,
    object? CommitState = null
);

internal sealed record CraftItemCommitResult(
    int CompletedCraftCount,
    CraftItemRuntimeStopReason StopReason,
    IReadOnlyList<CraftingOutputFact> Outputs,
    IReadOnlyList<CraftingMaterialConsumption> MaterialsConsumed,
    string PlayerInventoryRevision,
    string UiRevision
);

internal sealed class LiveCraftItemRuntimeAdapter : ICraftItemRuntimeAdapter
{
    private readonly OpaqueRefStore _refs;

    public LiveCraftItemRuntimeAdapter(OpaqueRefStore refs) => _refs = refs;

    public CraftItemCapture Capture()
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return new CraftItemCapture(CraftItemCaptureStatus.NotReady);
        if (Game1.activeClickableMenu is not { } active)
            return new CraftItemCapture(CraftItemCaptureStatus.NotReady);
        if (active.GetType() != typeof(GameMenu))
            return new CraftItemCapture(CraftItemCaptureStatus.Unsupported);

        try
        {
            var menu = (GameMenu)active;
            if (menu.currentTab != GameMenu.craftingTab
                || menu.currentTab < 0
                || menu.currentTab >= menu.pages.Count
                || menu.pages[menu.currentTab] is not { } current)
                return new CraftItemCapture(CraftItemCaptureStatus.NotReady);
            if (current.GetType() != typeof(CraftingPage))
                return new CraftItemCapture(CraftItemCaptureStatus.Unsupported);
            var page = (CraftingPage)current;
            if (page.cooking || page.heldItem is not null)
                return new CraftItemCapture(CraftItemCaptureStatus.NotReady);

            var ui = UiRuntimeProjector.Capture(menu, player, _refs);
            if (ui.ElementSetCompleteness != UiElementSetCompleteness.Complete)
                return new CraftItemCapture(CraftItemCaptureStatus.Unavailable);
            var recipes = CaptureRecipes(page);
            return new CraftItemCapture(
                CraftItemCaptureStatus.Ready,
                menu,
                page,
                player,
                ui.Result.Snapshot.UiRevision,
                recipes,
                new LiveCraftItemCommitState(menu, page, player)
            );
        }
        catch
        {
            return new CraftItemCapture(CraftItemCaptureStatus.Unavailable);
        }
    }

    public CraftItemCommitResult Commit(
        CraftItemCapture capture,
        CraftItemRecipeBinding binding,
        int craftCount
    )
    {
        if (capture.CommitState is not LiveCraftItemCommitState state
            || craftCount is <= 0 or > 25
            || binding.RecipeIdentity is not CraftingRecipe recipe
            || recipe.GetType() != typeof(CraftingRecipe)
            || !ReferenceEquals(Game1.activeClickableMenu, state.Menu)
            || !ReferenceEquals(Game1.player, state.Player)
            || state.Page.cooking
            || state.Page.heldItem is not null
            || !ContainsBinding(state.Page, binding))
            throw new InvalidOperationException("制作提交上下文已变化");

        var outputs = new Dictionary<(string Id, string Name), uint>();
        var materials = recipe.recipeList.ToDictionary(
            pair => pair.Key,
            _ => 0u,
            StringComparer.Ordinal
        );
        var completed = 0;
        var stop = CraftItemRuntimeStopReason.Completed;
        while (completed < craftCount)
        {
            var materialItems = CaptureMaterialItems(state.Page._materialContainers);
            if (!recipe.doesFarmerHaveIngredientsInInventory(materialItems))
            {
                stop = CraftItemRuntimeStopReason.MaterialsInsufficient;
                break;
            }
            var crafted = recipe.createItem();
            if (crafted is null
                || PreparePlayerInsertion(state, crafted) is null)
            {
                stop = CraftItemRuntimeStopReason.InventoryFull;
                break;
            }
            var producedQuantity = checked((uint)crafted.Stack);
            var outputKey = (crafted.QualifiedItemId, crafted.DisplayName);
            var bookkeepingItem = crafted.getOne();
            bookkeepingItem.Stack = crafted.Stack;

            try
            {
                recipe.consumeIngredients(state.Page._materialContainers);
            }
            catch
            {
                PreserveCreatedOutput(state.Page, crafted);
                throw;
            }

            // 原版先把产物放到 Crafting 游标，再更新任务与统计；在后续任一
            // 回调失败时保留这个可观察的恢复点，避免随机产物只留在局部变量中。
            state.Page.heldItem = crafted;
            UpdateCraftingQuest(state.Player, recipe, bookkeepingItem);
            if (!state.Player.craftingRecipes.ContainsKey(recipe.name))
                throw new InvalidOperationException("玩家已知配方状态已变化");
            state.Player.craftingRecipes[recipe.name] += recipe.numberProducedPerCraft;
            Game1.stats.checkForCraftingAchievements();

            if (!ReferenceEquals(state.Page.heldItem, crafted))
                throw new InvalidOperationException("制作产物恢复点已变化");
            var insertion = PreparePlayerInsertion(state, crafted)
                ?? throw new InvalidOperationException("制作后背包无法完整容纳产物");
            var commit = InventoryTransferRuntimeCommitter.Commit(
                insertion.Source,
                insertion.Target,
                insertion.Plan
            );
            try
            {
                state.Page.heldItem = insertion.Source[0];
                if (state.Page.heldItem is not null)
                    throw new InvalidOperationException("制作产物未能完整进入背包");
                commit.Complete();
            }
            catch
            {
                commit.Rollback();
                state.Page.heldItem = crafted;
                throw;
            }

            outputs[outputKey] = checked(outputs.GetValueOrDefault(outputKey) + producedQuantity);
            foreach (var pair in recipe.recipeList)
                materials[pair.Key] = checked(materials[pair.Key] + (uint)pair.Value);
            completed++;
        }

        var after = Capture();
        if (after.Status != CraftItemCaptureStatus.Ready
            || !ReferenceEquals(after.MenuIdentity, state.Menu)
            || !ReferenceEquals(after.PageIdentity, state.Page)
            || !ReferenceEquals(after.PlayerIdentity, state.Player))
            throw new InvalidOperationException("制作后页面事实不可确认");
        var playerView = InventoryViewResolver.CreatePlayer(state.Player);
        var playerSnapshot = InventoryProjector.Project(
            playerView,
            _refs,
            includeEmptySlots: true
        );

        return new CraftItemCommitResult(
            completed,
            stop,
            outputs.OrderBy(pair => pair.Key.Id, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Name, StringComparer.Ordinal)
                .Select(pair => new CraftingOutputFact
                {
                    QualifiedItemId = pair.Key.Id,
                    DisplayName = pair.Key.Name,
                    Quantity = pair.Value,
                })
                .ToArray(),
            materials.Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new CraftingMaterialConsumption
                {
                    IngredientKey = pair.Key,
                    Quantity = pair.Value,
                })
                .ToArray(),
            playerSnapshot.InventoryRevision,
            after.UiRevision
        );
    }

    private static IReadOnlyList<CraftItemRecipeBinding> CaptureRecipes(CraftingPage page)
    {
        var recipes = new List<CraftItemRecipeBinding>();
        for (var pageIndex = 0; pageIndex < page.pagesOfCraftingRecipes.Count; pageIndex++)
        {
            var source = page.pagesOfCraftingRecipes[pageIndex]
                ?? throw new InvalidOperationException("Crafting 配方页不可读");
            foreach (var pair in source)
            {
                if (pair.Key is null || pair.Key.GetType() != typeof(ClickableTextureComponent)
                    || pair.Value is null || pair.Value.GetType() != typeof(CraftingRecipe))
                    throw new InvalidOperationException("Crafting 配方组件不受支持");
                recipes.Add(new CraftItemRecipeBinding(
                    pageIndex,
                    checked(pair.Key.myID - 201),
                    pair.Key,
                    pair.Value
                ));
            }
        }
        var ordered = recipes.OrderBy(item => item.GlobalIndex).ToArray();
        if (ordered.Length > CraftingPageProjector.RecipeLimit)
            throw new InvalidOperationException("Crafting 配方数量超过上限");
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].GlobalIndex != index)
                throw new InvalidOperationException("Crafting 配方序号无效");
        }
        return ordered;
    }

    private static bool ContainsBinding(
        CraftingPage page,
        CraftItemRecipeBinding binding
    ) => binding.SourcePage >= 0
        && binding.SourcePage < page.pagesOfCraftingRecipes.Count
        && page.pagesOfCraftingRecipes[binding.SourcePage] is { } recipes
        && binding.Component is ClickableTextureComponent component
        && recipes.TryGetValue(component, out var recipe)
        && ReferenceEquals(recipe, binding.RecipeIdentity)
        && component.myID - 201 == binding.GlobalIndex;

    private static IList<Item> CaptureMaterialItems(List<IInventory>? containers)
    {
        var items = new List<Item>();
        if (containers is null)
            return items;
        foreach (var container in containers)
        {
            if (container is null)
                throw new InvalidOperationException("Crafting 材料容器不可读");
            items.AddRange(container.Where(item => item is not null)!);
        }
        return items;
    }

    private static PlayerOutputInsertion? PreparePlayerInsertion(
        LiveCraftItemCommitState state,
        Item crafted
    )
    {
        var playerView = InventoryViewResolver.CreatePlayerForMenu(
            state.Player,
            state.Page.inventory.capacity
        );
        if (playerView.BackingIdentity is not IList<Item> playerBacking)
            throw new InvalidOperationException("玩家背包写入目标不可用");
        IList<Item> source = new List<Item> { crafted };
        var planned = InventoryTransferPlanner.Plan(
            0,
            InventoryTransferRuntimeItemFactory.Wrap(
                new Item?[] { crafted }
            ),
            InventoryTransferRuntimeItemFactory.Wrap(playerView.Slots),
            crafted.Stack
        );
        return planned.Status == InventoryTransferPlanStatus.Success
            ? new PlayerOutputInsertion(source, playerBacking, planned.Value!)
            : null;
    }

    private static void UpdateCraftingQuest(
        Farmer player,
        CraftingRecipe recipe,
        Item crafted
    )
    {
        player.NotifyQuests(
            quest => quest.OnRecipeCrafted(recipe, crafted, probe: false),
            onlyOneQuest: false
        );
    }

    private static void PreserveCreatedOutput(CraftingPage page, Item crafted)
    {
        if (page.heldItem is null)
            page.heldItem = crafted;
    }

    private sealed record LiveCraftItemCommitState(
        GameMenu Menu,
        CraftingPage Page,
        Farmer Player
    );

    private sealed record PlayerOutputInsertion(
        IList<Item> Source,
        IList<Item> Target,
        InventoryTransferPlan Plan
    );
}
