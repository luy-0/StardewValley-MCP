using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class QueryWorldHandler : IImmediateCapabilityHandler
{
    private const uint DefaultMaximum = 256;
    private readonly OpaqueRefStore _refs;

    public QueryWorldHandler(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    public string Id => "query_world";
    public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.QueryWorld;
    public Error? Validate(CommandRequest request) => QueryWorldRequestValidator.Validate(request);

    public CommandEvent Execute(string commandId, CommandRequest request)
    {
        if (!Context.IsWorldReady || Game1.player?.currentLocation is not { } currentLocation)
            return Failed(commandId, ErrorCode.NotReady, "世界尚未就绪", "not_ready");

        var query = request.QueryWorld;
        string? defaultLocationId = null;
        var defaultPlayerTile = Point.Zero;
        if (query.RegionCase == QueryWorldRequest.RegionOneofCase.None)
        {
            try
            {
                defaultLocationId = currentLocation.NameOrUniqueName;
            }
            catch
            {
                return Failed(commandId, ErrorCode.ExecutionFailed, "Location ID 不可读", "invalid_location_id");
            }
            if (!LocationIdPolicy.IsValid(defaultLocationId))
                return Failed(commandId, ErrorCode.ExecutionFailed, "Location ID 不符合公开约束", "invalid_location_id");
            try
            {
                defaultPlayerTile = Game1.player.TilePoint;
            }
            catch
            {
                return Failed(commandId, ErrorCode.ExecutionFailed, "玩家位置不可读", "location_projection_failed");
            }
        }

        var region = ResolveRequestedRegion(query, defaultLocationId, defaultPlayerTile);
        var location = GameLocationIdentity.FindExact(region.LocationId);
        if (location is null)
            return Failed(commandId, ErrorCode.NotFound, "未找到指定的已加载 Location", "not_found");
        string publicLocationId;
        try
        {
            publicLocationId = location.NameOrUniqueName;
        }
        catch
        {
            return Failed(commandId, ErrorCode.ExecutionFailed, "Location ID 不可读", "invalid_location_id");
        }
        if (!LocationIdPolicy.IsValid(publicLocationId))
            return Failed(commandId, ErrorCode.ExecutionFailed, "Location ID 不符合公开约束", "invalid_location_id");

        bool outdoors;
        try
        {
            outdoors = location.IsOutdoors;
        }
        catch
        {
            return Failed(commandId, ErrorCode.ExecutionFailed, "Location 基础事实不可读", "location_projection_failed");
        }

        int mapWidth;
        int mapHeight;
        try
        {
            if (location.Map?.Layers is not { Count: > 0 } layers)
                return Failed(commandId, ErrorCode.ExecutionFailed, "Location 地图不可读", "failed");
            mapWidth = layers[0].LayerWidth;
            mapHeight = layers[0].LayerHeight;
        }
        catch
        {
            return Failed(commandId, ErrorCode.ExecutionFailed, "Location 地图不可读", "failed");
        }

        var clipped = Clip(region, mapWidth, mapHeight);
        if (clipped is null)
            return Failed(commandId, ErrorCode.OutOfRange, "请求区域与地图边界不相交", "out_of_range");

        var includeTiles = !query.HasIncludeTiles || query.IncludeTiles;
        var includeEntities = !query.HasIncludeEntities || query.IncludeEntities;
        var includeCharacters = !query.HasIncludeCharacters || query.IncludeCharacters;
        var projectionWarnings = new List<QueryWarning>();
        var snapshot = new WorldSnapshot
        {
            Area = new TileArea
            {
                LocationId = publicLocationId,
                X = clipped.Value.X,
                Y = clipped.Value.Y,
                Width = checked((uint)clipped.Value.Width),
                Height = checked((uint)clipped.Value.Height),
            },
            Outdoors = outdoors,
        };

        if (includeTiles)
        {
            try
            {
                snapshot.Tiles.AddRange(WorldProjector.ProjectTiles(location, clipped.Value));
            }
            catch
            {
                return Failed(
                    commandId,
                    ErrorCode.ExecutionFailed,
                    "Location Tile 事实不可读",
                    "tile_projection_failed"
                );
            }
        }
        if (includeEntities)
        {
            snapshot.Entities.AddRange(
                WorldProjector.ProjectEntities(
                        location,
                        clipped.Value,
                        query.EntityKinds.ToHashSet(),
                        _refs,
                        projectionWarnings
                    )
            );
        }
        if (includeCharacters)
        {
            snapshot.Characters.AddRange(
                WorldProjector.ProjectCharacters(location, clipped.Value, _refs, projectionWarnings)
            );
        }

        var maxEntities = query.MaxEntities == 0 ? DefaultMaximum : query.MaxEntities;
        var maxCharacters = query.MaxCharacters == 0 ? DefaultMaximum : query.MaxCharacters;
        var result = FinalizeResult(snapshot, projectionWarnings, maxEntities, maxCharacters);

        return new CommandEvent
        {
            CommandId = commandId,
            State = CommandState.Succeeded,
            Phase = "completed",
            ProgressPercent = 100,
            Result = new CapabilityResult
            {
                QueryWorld = result,
            },
        };
    }

    internal static QueryWorldResult FinalizeResult(
        WorldSnapshot snapshot,
        IEnumerable<QueryWarning> projectionWarnings,
        uint maxEntities,
        uint maxCharacters
    )
    {
        var sortedEntities = snapshot.Entities.OrderBy(fact => fact.Ref.Value, StringComparer.Ordinal).ToList();
        var sortedCharacters = snapshot.Characters.OrderBy(fact => fact.Ref.Value, StringComparer.Ordinal).ToList();
        snapshot.Entities.Clear();
        snapshot.Entities.AddRange(sortedEntities);
        snapshot.Characters.Clear();
        snapshot.Characters.AddRange(sortedCharacters);

        // Hash the complete selected scan before output truncation so changes beyond a
        // caller's limit still advance the revision.
        snapshot.WorldRevision = WorldRevision.Compute(snapshot);

        snapshot.EntitiesTruncated = snapshot.Entities.Count > maxEntities;
        snapshot.CharactersTruncated = snapshot.Characters.Count > maxCharacters;
        while (snapshot.Entities.Count > maxEntities)
            snapshot.Entities.RemoveAt(snapshot.Entities.Count - 1);
        while (snapshot.Characters.Count > maxCharacters)
            snapshot.Characters.RemoveAt(snapshot.Characters.Count - 1);

        var result = new QueryWorldResult { Snapshot = snapshot };
        var returnedRefs = snapshot.Entities
            .Select(fact => fact.Ref.Value)
            .Concat(snapshot.Characters.Select(fact => fact.Ref.Value))
            .ToHashSet(StringComparer.Ordinal);
        result.Warnings.AddRange(projectionWarnings.Where(warning =>
            warning.Ref is null || returnedRefs.Contains(warning.Ref.Value)));
        foreach (var door in snapshot.Entities.Where(fact =>
            fact.Kind == EntityKind.Door && fact.Door is not null && !fact.Door.HasLocked))
        {
            result.Warnings.Add(new QueryWarning
            {
                Code = "DOOR_ACCESS_UNKNOWN",
                Message = "无法通过无副作用只读 API 可靠判断该门的当前准入状态",
                Ref = door.Ref.Clone(),
            });
        }

        return result;
    }

    private static RequestedRegion ResolveRequestedRegion(
        QueryWorldRequest query,
        string? defaultLocationId,
        Point playerTile
    )
    {
        if (query.RegionCase == QueryWorldRequest.RegionOneofCase.Area)
            return new RequestedRegion(
                query.Area.LocationId,
                query.Area.X,
                query.Area.Y,
                query.Area.Width,
                query.Area.Height
            );

        WorldPosition center;
        uint radius;
        if (query.RegionCase == QueryWorldRequest.RegionOneofCase.Around)
        {
            center = query.Around.Center;
            radius = query.Around.Radius;
        }
        else
        {
            center = new WorldPosition
            {
                LocationId = defaultLocationId!,
                X = playerTile.X,
                Y = playerTile.Y,
            };
            radius = 8;
        }

        var diameter = checked(radius * 2 + 1);
        return new RequestedRegion(
            center.LocationId,
            checked((long)center.X - radius),
            checked((long)center.Y - radius),
            diameter,
            diameter
        );
    }

    private static ScanArea? Clip(RequestedRegion region, int mapWidth, int mapHeight)
    {
        var x0 = Math.Max(0L, region.X);
        var y0 = Math.Max(0L, region.Y);
        var x1 = Math.Min((long)mapWidth, checked(region.X + region.Width));
        var y1 = Math.Min((long)mapHeight, checked(region.Y + region.Height));
        if (x0 >= x1 || y0 >= y1)
            return null;
        return new ScanArea(
            checked((int)x0),
            checked((int)y0),
            checked((int)(x1 - x0)),
            checked((int)(y1 - y0))
        );
    }

    private static CommandEvent Failed(
        string commandId,
        ErrorCode code,
        string message,
        string phase
    ) => new()
    {
        CommandId = commandId,
        State = CommandState.Failed,
        Phase = phase,
        Error = new Error { Code = code, Message = message },
    };

    private readonly record struct RequestedRegion(
        string LocationId,
        long X,
        long Y,
        uint Width,
        uint Height
    );
}
