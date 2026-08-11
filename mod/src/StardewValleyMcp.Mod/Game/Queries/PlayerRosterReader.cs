using StardewModdingAPI;
using StardewValley;

namespace StardewValleyMcp.Mod;

internal interface IPlayerRosterReader
{
    bool IsWorldReady { get; }
    IReadOnlyList<PlayerPresenceCapture> Capture();
}

internal sealed record PlayerPresenceCapture(
    long PlayerId,
    string DisplayName,
    bool Online,
    bool IsHost,
    bool IsSelf,
    PlayerLivePresenceCapture? Live,
    string SavedHomeLocationId,
    string ResolvedHomeLocationId
);

internal sealed record PlayerLivePresenceCapture(
    string? LocationId,
    int? X,
    int? Y,
    int? FacingDirection,
    double? Energy,
    double? MaxEnergy,
    bool? IsInBed
);

/// <summary>只负责在游戏主线程读取 Farmer 数据，不承担 Proto 投影。</summary>
internal sealed class StardewPlayerRosterReader : IPlayerRosterReader
{
    public bool IsWorldReady => Context.IsWorldReady && Game1.player is not null;

    public IReadOnlyList<PlayerPresenceCapture> Capture()
    {
        var localPlayer = Game1.player ?? throw new InvalidOperationException("当前玩家不可读");
        var localId = localPlayer.UniqueMultiplayerID;
        var hostId = Game1.MasterPlayer.UniqueMultiplayerID;

        var onlineById = Game1.getOnlineFarmers()
            .ToDictionary(farmer => farmer.UniqueMultiplayerID);
        onlineById[localId] = localPlayer;

        var allById = Game1.getAllFarmers()
            .GroupBy(farmer => farmer.UniqueMultiplayerID)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var pair in onlineById)
            allById[pair.Key] = pair.Value;

        if (!allById.ContainsKey(localId))
            allById.Add(localId, localPlayer);

        return allById.Values
            .Select(farmer => CaptureFarmer(
                farmer,
                onlineById.ContainsKey(farmer.UniqueMultiplayerID),
                localId,
                hostId
            ))
            .ToList();
    }

    private static PlayerPresenceCapture CaptureFarmer(
        Farmer farmer,
        bool online,
        long localId,
        long hostId
    )
    {
        var playerId = farmer.UniqueMultiplayerID;
        var displayName = ReadOrDefault(() => farmer.Name, "");
        var savedHome = ReadOrDefault(() => farmer.homeLocation.Value, "");
        var resolvedHome = ReadOrDefault(
            () => Utility.getHomeOfFarmer(farmer)?.NameOrUniqueName ?? "",
            ""
        );

        return new PlayerPresenceCapture(
            playerId,
            displayName,
            online,
            playerId == hostId,
            playerId == localId,
            online ? CaptureLive(farmer) : null,
            savedHome,
            resolvedHome
        );
    }

    private static PlayerLivePresenceCapture CaptureLive(Farmer farmer)
    {
        string? locationId = null;
        int? x = null;
        int? y = null;
        try
        {
            if (farmer.currentLocation is { } location)
            {
                var tile = farmer.TilePoint;
                locationId = location.NameOrUniqueName;
                x = tile.X;
                y = tile.Y;
            }
        }
        catch
        {
            locationId = null;
            x = null;
            y = null;
        }

        var facing = ReadNullable(() => farmer.FacingDirection);
        var energy = ReadNullable(() => (double)farmer.Stamina);
        var maxEnergy = ReadNullable(() => (double)farmer.MaxStamina);
        var isInBed = ReadNullable(() => farmer.isInBed.Value);
        return new PlayerLivePresenceCapture(
            locationId,
            x,
            y,
            facing,
            energy,
            maxEnergy,
            isInBed
        );
    }

    private static T ReadOrDefault<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private static T? ReadNullable<T>(Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}
