using System.Globalization;
using StardewValley;
using StardewValley.Tools;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class ItemFactProjector
{
    public static ItemFact Project(Item item, Ref? reference = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var qualifiedItemId = item.QualifiedItemId ?? "";
        var displayName = item.DisplayName ?? "";
        if (!PublicStringPolicy.IsValid(qualifiedItemId)
            || !PublicStringPolicy.IsValid(displayName))
            throw new InvalidOperationException("Item 公开字符串不符合协议约束");

        var fact = new ItemFact
        {
            QualifiedItemId = qualifiedItemId,
            DisplayName = displayName,
            Stack = UInt(item.Stack),
            Quality = UInt(item.Quality),
            Category = item.Category.ToString(CultureInfo.InvariantCulture),
            Tool = item is Tool,
            ToolLevel = item is Tool tool ? UInt(tool.UpgradeLevel) : 0,
        };
        if (reference is not null)
            fact.Ref = reference.Clone();
        ApplyToolKind(fact, item);
        if (item is WateringCan wateringCan)
            ApplyWateringCanFacts(
                fact,
                wateringCan.WaterLeft,
                wateringCan.waterCanMax,
                wateringCan.IsBottomless
            );
        return fact;
    }

    private static void ApplyToolKind(ItemFact fact, Item item)
    {
        var kind = item switch
        {
            Axe => ItemToolKind.Axe,
            Pickaxe => ItemToolKind.Pickaxe,
            Hoe => ItemToolKind.Hoe,
            WateringCan => ItemToolKind.WateringCan,
            MeleeWeapon weapon when weapon.isScythe() => ItemToolKind.Scythe,
            _ => ItemToolKind.Unspecified,
        };
        if (kind != ItemToolKind.Unspecified)
            fact.ToolKind = kind;
    }

    internal static void ApplyWateringCanFacts(
        ItemFact fact,
        int remaining,
        int capacity,
        bool bottomless
    )
    {
        ArgumentNullException.ThrowIfNull(fact);
        fact.WaterRemaining = UInt(remaining);
        fact.WaterCapacity = UInt(capacity);
        fact.Bottomless = bottomless;
    }

    private static uint UInt(int value) => checked((uint)Math.Max(0, value));
}

internal static class InventoryItemGuard
{
    public static string Create(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return $"{item.GetType().FullName ?? item.GetType().Name}:{item.QualifiedItemId ?? ""}";
    }
}

internal static class PublicStringPolicy
{
    public static bool IsValid(string? value, int maximumScalars = 512) =>
        value is not null
        && !value.Contains('\0')
        && value.EnumerateRunes().Take(maximumScalars + 1).Count() <= maximumScalars;

    public static bool IsNonEmptyValid(string? value, int maximumScalars = 512) =>
        !string.IsNullOrEmpty(value) && IsValid(value, maximumScalars);
}
