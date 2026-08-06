namespace StardewValleyMcp.Mod;

internal static class DefaultCapabilitySet
{
    internal static CapabilityRegistry Create(string modInstanceId)
    {
        var refs = new OpaqueRefStore(modInstanceId);
        return new CapabilityRegistry(new ICapabilityHandler[]
        {
            new SayHandler(),
            new EmoteHandler(),
            new FaceHandler(),
            new NavigateHandler(refs),
            new InteractHandler(refs),
            new UseToolHandler(refs),
            new EquipHandler(refs),
            new TransferInventoryItemHandler(refs),
            new SetEquipmentSlotHandler(refs),
            new MoveInventoryItemHandler(refs),
            new CraftItemHandler(refs),
            new PurchaseShopItemHandler(refs),
            new OpenMenuHandler(refs),
            new ActivateUiHandler(refs),
            new CloseMenuHandler(refs),
            new QueryRuntimeHandler(),
            new QueryPlayersHandler(new StardewPlayerRosterReader()),
            new QueryWorldHandler(refs),
            new QueryInventoryHandler(refs),
            new QueryUiHandler(refs),
            new InspectHandler(refs),
        });
    }
}
