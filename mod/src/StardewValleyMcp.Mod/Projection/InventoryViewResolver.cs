using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed record ReadableInventoryView(
    IInventoryRefOwner RefOwner,
    string ContainerKind,
    ChestInventoryBacking BackingKind,
    int Capacity,
    IReadOnlyList<Item?> Slots,
    object BackingIdentity,
    Ref? ContainerRef,
    int RevisionSelectedSlot
);

internal static class InventoryViewResolver
{
    public static ReadableInventoryView CreatePlayer(Farmer player)
    {
        var capacity = player.MaxItems;
        var slots = Capture(player.Items, capacity);
        return new ReadableInventoryView(
            new PlayerInventoryRefOwner(player),
            "player",
            ChestInventoryBacking.Player,
            capacity,
            slots,
            player.Items,
            null,
            player.CurrentToolIndex
        );
    }

    public static ReadableInventoryView CreateContainer(
        ResolvedOpaqueRef resolved,
        Farmer player,
        OpaqueRefStore refs
    )
    {
        if (resolved.Target is not Chest chest)
            throw Invalid("World Entity 不是可读取容器");
        if (resolved.LocatorKind is not RefLocatorKind.Object and not RefLocatorKind.Fridge)
            throw Invalid("World Entity 不是可读取容器");
        if (resolved.Kind == RefKind.WorldEntity
            && (!string.Equals(resolved.Role, "world", StringComparison.Ordinal)
                || !resolved.Guard.StartsWith("container:", StringComparison.Ordinal)))
            throw Invalid("World Entity 不包含 ContainerFact");
        if (resolved.Kind == RefKind.Container
            && !string.Equals(resolved.Role, "inventory-view", StringComparison.Ordinal))
            throw Invalid("Container Ref 类型无效");

        return CreateAttachedContainer(
            chest,
            resolved.Location,
            player,
            refs,
            resolved.LocatorKind,
            resolved.X,
            resolved.Y,
            resolved.Guard
        );
    }

    internal static ReadableInventoryView CreateAttachedContainer(
        Chest chest,
        GameLocation location,
        Farmer player,
        OpaqueRefStore refs,
        RefLocatorKind locatorKind,
        int x,
        int y,
        string expectedGuard
    )
    {
        if (!TryReadCurrentContainer(
            chest,
            location,
            player,
            locatorKind,
            expectedGuard,
            out var backing,
            out var backingKind,
            out var capacity
        ))
            throw Stale("容器 Ref 已失效");

        var viewRef = refs.GetOrCreate(
            chest,
            location,
            RefKind.Container,
            locatorKind,
            x,
            y,
            expectedGuard,
            "inventory-view"
        );
        var slots = Capture(backing, capacity);
        return new ReadableInventoryView(
            new ChestInventoryRefOwner(
                chest,
                location,
                player,
                locatorKind,
                expectedGuard
            ),
            ContainerKindClassifier.Classify(chest, locatorKind),
            backingKind,
            capacity,
            slots,
            backing,
            viewRef,
            int.MinValue
        );
    }

    internal static bool TryReadCurrentContainer(
        Chest chest,
        GameLocation location,
        Farmer player,
        RefLocatorKind locatorKind,
        string expectedGuard,
        out IList<Item> backing,
        out ChestInventoryBacking backingKind,
        out int capacity
    )
    {
        backing = Array.Empty<Item>();
        backingKind = ChestInventoryBacking.Local;
        capacity = 0;
        try
        {
            if (!Context.IsWorldReady
                || !ReferenceEquals(Game1.player, player)
                || !GameLocationIdentity.IsCurrent(location.NameOrUniqueName, location))
                return false;
            var attached = locatorKind switch
            {
                RefLocatorKind.Object => location.Objects.Values.Any(item =>
                    ReferenceEquals(item, chest)),
                RefLocatorKind.Fridge =>
                    ReferenceEquals(location.GetFridge(onlyUnlocked: false), chest)
                    && ReferenceEquals(location.GetFridge(), chest),
                _ => false,
            };
            if (!attached
                || !string.Equals(
                    ContainerKindClassifier.IdentityGuard(chest, locatorKind),
                    expectedGuard,
                    StringComparison.Ordinal
                ))
                return false;
            capacity = chest.GetActualCapacity();
            backing = ChestInventoryReader.GetExistingSlots(chest, player, out backingKind);
            ValidateBounds(capacity, backing.Count);
            return true;
        }
        catch (InventoryViewException)
        {
            throw;
        }
        catch
        {
            throw new InventoryViewException(
                ErrorCode.Internal,
                "容器库存不可读",
                "internal"
            );
        }
    }

    private static Item?[] Capture(IList<Item> source, int capacity)
    {
        ValidateBounds(capacity, source.Count);
        var slots = new Item?[capacity];
        for (var index = 0; index < source.Count; index++)
            slots[index] = source[index];
        return slots;
    }

    internal static void ValidateBounds(int capacity, int count)
    {
        if (capacity < 0)
            throw new InventoryViewException(ErrorCode.Internal, "库存容量无效", "internal");
        if (count > capacity)
            throw new InventoryViewException(ErrorCode.Internal, "库存内容超过容量", "internal");
    }

    internal static InventorySlotLookupStatus ClassifyPlayerSlot(
        bool worldReady,
        bool ownerAlive,
        bool currentPlayer,
        int capacity,
        int count,
        int slot
    )
    {
        if (!worldReady || !ownerAlive || !currentPlayer)
            return InventorySlotLookupStatus.Stale;
        if (capacity < 0 || count > capacity)
            return InventorySlotLookupStatus.Unavailable;
        return slot < 0 || slot >= capacity
            ? InventorySlotLookupStatus.Stale
            : InventorySlotLookupStatus.Resolved;
    }

    private static InventoryViewException Invalid(string message) =>
        new(ErrorCode.InvalidArgument, message, "invalid_argument");

    private static InventoryViewException Stale(string message) =>
        new(ErrorCode.StaleRef, message, "stale_ref");
}

internal sealed class PlayerInventoryRefOwner : IInventoryRefOwner
{
    private readonly WeakReference<Farmer> _player;

    public PlayerInventoryRefOwner(Farmer player)
    {
        _player = new WeakReference<Farmer>(player);
    }

    public InventoryItemProvenance Provenance => InventoryItemProvenance.Player;

    public bool TryGetIdentity(out object identity)
    {
        identity = null!;
        if (!_player.TryGetTarget(out var player))
            return false;
        identity = player;
        return true;
    }

    public InventorySlotLookup ResolveCurrentSlot(int slot)
    {
        try
        {
            var ownerAlive = _player.TryGetTarget(out var player) && player is not null;
            if (!ownerAlive || player is null)
                return new InventorySlotLookup(InventorySlotLookupStatus.Stale);
            var capacity = player.MaxItems;
            var items = player.Items;
            var status = InventoryViewResolver.ClassifyPlayerSlot(
                Context.IsWorldReady,
                ownerAlive,
                ReferenceEquals(Game1.player, player),
                capacity,
                items.Count,
                slot
            );
            if (status != InventorySlotLookupStatus.Resolved)
                return new InventorySlotLookup(status);
            var target = slot < items.Count ? items[slot] : null;
            var guard = target is Item item ? InventoryItemGuard.Create(item) : "";
            return new InventorySlotLookup(
                InventorySlotLookupStatus.Resolved,
                target,
                guard
            );
        }
        catch
        {
            return new InventorySlotLookup(InventorySlotLookupStatus.Unavailable);
        }
    }
}

internal sealed class ChestInventoryRefOwner : IInventoryRefOwner
{
    private readonly WeakReference<Chest> _chest;
    private readonly WeakReference<GameLocation> _location;
    private readonly WeakReference<Farmer> _player;
    private readonly RefLocatorKind _locatorKind;
    private readonly string _guard;

    public ChestInventoryRefOwner(
        Chest chest,
        GameLocation location,
        Farmer player,
        RefLocatorKind locatorKind,
        string guard
    )
    {
        _chest = new WeakReference<Chest>(chest);
        _location = new WeakReference<GameLocation>(location);
        _player = new WeakReference<Farmer>(player);
        _locatorKind = locatorKind;
        _guard = guard;
    }

    public InventoryItemProvenance Provenance => InventoryItemProvenance.Container;

    public bool TryGetIdentity(out object identity)
    {
        identity = null!;
        if (!_chest.TryGetTarget(out var chest))
            return false;
        identity = chest;
        return true;
    }

    public InventorySlotLookup ResolveCurrentSlot(int slot)
    {
        try
        {
            if (!_chest.TryGetTarget(out var chest)
                || !_location.TryGetTarget(out var location)
                || !_player.TryGetTarget(out var player))
                return new InventorySlotLookup(InventorySlotLookupStatus.Stale);
            if (!InventoryViewResolver.TryReadCurrentContainer(
                    chest,
                    location,
                    player,
                    _locatorKind,
                    _guard,
                    out var backing,
                    out _,
                    out var capacity
                ))
                return new InventorySlotLookup(InventorySlotLookupStatus.Stale);
            if (slot < 0 || slot >= capacity)
                return new InventorySlotLookup(InventorySlotLookupStatus.Stale);
            var target = slot < backing.Count ? backing[slot] : null;
            var guard = target is Item item ? InventoryItemGuard.Create(item) : "";
            return new InventorySlotLookup(
                InventorySlotLookupStatus.Resolved,
                target,
                guard
            );
        }
        catch
        {
            return new InventorySlotLookup(InventorySlotLookupStatus.Unavailable);
        }
    }
}

internal sealed class InventoryViewException : Exception
{
    public InventoryViewException(ErrorCode code, string message, string phase)
        : base(message)
    {
        Code = code;
        Phase = phase;
    }

    public ErrorCode Code { get; }
    public string Phase { get; }
}
