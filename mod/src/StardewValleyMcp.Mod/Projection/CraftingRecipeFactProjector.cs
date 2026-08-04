using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed record CraftingMaterialProjectionSource(
    string IngredientKey,
    string DisplayName,
    int RequiredQuantity,
    int AvailableQuantity
);

internal sealed record CraftingOutputProjectionSource(
    string QualifiedItemId,
    string DisplayName,
    int Quantity
);

internal sealed record CraftingRecipeProjectionSource(
    string RecipeKey,
    string DisplayName,
    bool Known,
    bool Craftable,
    IReadOnlyList<CraftingMaterialProjectionSource> Materials,
    IReadOnlyList<CraftingOutputProjectionSource> PossibleOutputs
);

/// <summary>将已捕获的配方数据确定性投影为公开事实。</summary>
internal static class CraftingRecipeFactProjector
{
    public static CraftingRecipeFact Project(CraftingRecipeProjectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!PublicStringPolicy.IsNonEmptyValid(source.RecipeKey)
            || !PublicStringPolicy.IsNonEmptyValid(source.DisplayName)
            || source.Materials is null
            || source.PossibleOutputs is null
            || source.PossibleOutputs.Count == 0)
            throw new UiProjectionException("配方基本事实不符合公开约束");

        var materialKeys = new HashSet<string>(StringComparer.Ordinal);
        var materials = source.Materials
            .OrderBy(item => item.IngredientKey, StringComparer.Ordinal)
            .Select(item =>
            {
                if (!PublicStringPolicy.IsNonEmptyValid(item.IngredientKey)
                    || !PublicStringPolicy.IsNonEmptyValid(item.DisplayName)
                    || item.RequiredQuantity <= 0
                    || item.AvailableQuantity < 0
                    || !materialKeys.Add(item.IngredientKey))
                    throw new UiProjectionException("配方材料事实不符合公开约束");
                return new CraftingMaterialRequirement
                {
                    IngredientKey = item.IngredientKey,
                    DisplayName = item.DisplayName,
                    RequiredQuantity = checked((uint)item.RequiredQuantity),
                    AvailableQuantity = checked((uint)item.AvailableQuantity),
                };
            })
            .ToArray();

        var outputs = source.PossibleOutputs
            .OrderBy(item => item.QualifiedItemId, StringComparer.Ordinal)
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .Select(item =>
            {
                if (!PublicStringPolicy.IsNonEmptyValid(item.QualifiedItemId)
                    || !PublicStringPolicy.IsNonEmptyValid(item.DisplayName)
                    || item.Quantity <= 0)
                    throw new UiProjectionException("配方产出事实不符合公开约束");
                return new CraftingOutputFact
                {
                    QualifiedItemId = item.QualifiedItemId,
                    DisplayName = item.DisplayName,
                    Quantity = checked((uint)item.Quantity),
                };
            })
            .ToArray();

        var fact = new CraftingRecipeFact
        {
            RecipeKey = source.RecipeKey,
            DisplayName = source.DisplayName,
            Known = source.Known,
            Craftable = source.Known && source.Craftable,
        };
        fact.Materials.AddRange(materials);
        fact.PossibleOutputs.AddRange(outputs);
        return fact;
    }
}
