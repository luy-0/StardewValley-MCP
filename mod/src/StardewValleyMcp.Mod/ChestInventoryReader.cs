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
    {
        var backing = ChestInventorySelection.Select(
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

internal enum ChestInventoryBacking
{
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
