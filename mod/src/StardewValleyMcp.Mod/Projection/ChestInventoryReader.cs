using StardewValley;
using StardewValley.Objects;

namespace StardewValleyMcp.Mod;

/// <summary>
/// Reads the inventory currently backing a chest without calling any GetOrCreate path.
/// Missing shared inventories are observable as empty and are never materialized by a query.
/// </summary>
internal static class ChestInventoryReader
{
    public static IEnumerable<Item> EnumerateSlots(Chest chest, Farmer player)
        => GetExistingSlots(chest, player, out _);

    public static IList<Item> GetExistingSlots(
        Chest chest,
        Farmer player,
        out ChestInventoryBacking backing
    )
    {
        backing = ChestInventorySelection.Select(
            chest.GlobalInventoryId is not null,
            chest.SpecialChestType == Chest.SpecialChestTypes.MiniShippingBin,
            player.team.useSeparateWallets.Value,
            chest.SpecialChestType == Chest.SpecialChestTypes.JunimoChest
        );

        if (backing == ChestInventoryBacking.Global)
        {
            var globalInventoryId = chest.GlobalInventoryId!;
            return player.team.globalInventories.TryGetValue(globalInventoryId, out var global)
                ? global
                : Array.Empty<Item>();
        }

        if (backing == ChestInventoryBacking.SeparateWallet)
        {
            return chest.separateWalletItems.TryGetValue(player.UniqueMultiplayerID, out var wallet)
                ? wallet
                : Array.Empty<Item>();
        }

        if (backing == ChestInventoryBacking.Junimo)
        {
            return player.team.globalInventories.TryGetValue(
                FarmerTeam.GlobalInventoryId_JunimoChest,
                out var junimo
            )
                ? junimo
                : Array.Empty<Item>();
        }

        return chest.Items;
    }
}

internal static class ContainerKindClassifier
{
    public static string Classify(Chest chest, RefLocatorKind locatorKind) =>
        locatorKind == RefLocatorKind.Fridge || chest.fridge.Value
            ? "fridge"
            : chest.SpecialChestType switch
            {
                Chest.SpecialChestTypes.JunimoChest => "junimo_chest",
                Chest.SpecialChestTypes.MiniShippingBin => "mini_shipping_bin",
                Chest.SpecialChestTypes.AutoLoader => "auto_loader",
                Chest.SpecialChestTypes.BigChest => "big_chest",
                _ when chest.playerChest.Value => "chest",
                _ => "container",
            };

    public static string IdentityGuard(
        Chest chest,
        RefLocatorKind locatorKind
    ) => $"container:{chest.GetType().FullName}:{chest.QualifiedItemId}:{Classify(chest, locatorKind)}";
}

internal enum ChestInventoryBacking
{
    Player,
    Global,
    SeparateWallet,
    Junimo,
    Local,
}

/// <summary>
/// Pure selector for the already-existing inventory backing. The caller performs only TryGet
/// lookups for shared stores; selection has no GetOrCreate outcome.
/// </summary>
internal static class ChestInventorySelection
{
    public static ChestInventoryBacking Select(
        bool hasGlobalInventoryId,
        bool isMiniShippingBin,
        bool usesSeparateWallets,
        bool isJunimoChest
    )
    {
        if (hasGlobalInventoryId)
            return ChestInventoryBacking.Global;
        if (isMiniShippingBin && usesSeparateWallets)
            return ChestInventoryBacking.SeparateWallet;
        if (isJunimoChest)
            return ChestInventoryBacking.Junimo;
        return ChestInventoryBacking.Local;
    }
}
