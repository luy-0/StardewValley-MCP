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
        return fact;
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
