using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewValleyMcp.Mod;

internal enum NavigationEdgeKind
{
    WalkThrough,
    InteractDoor,
}

internal readonly record struct NavigationTile(int X, int Y);

internal sealed record NavigationLocationSource(
    string LocationId,
    int Width,
    int Height,
    IReadOnlyList<NavigationExitSource> Exits
);

internal sealed record NavigationExitSource(
    NavigationEdgeKind Kind,
    NavigationTile Trigger,
    string TargetLocationId,
    NavigationTile TargetLanding
);

internal sealed record NavigationLocationNode(string LocationId, int Width, int Height);

internal sealed class NavigationRouteEdge
{
    public NavigationRouteEdge(
        string edgeId,
        string sourceLocationId,
        string targetLocationId,
        NavigationEdgeKind kind,
        IEnumerable<NavigationTile> triggers,
        NavigationTile targetLanding
    )
    {
        EdgeId = edgeId;
        SourceLocationId = sourceLocationId;
        TargetLocationId = targetLocationId;
        Kind = kind;
        Triggers = Array.AsReadOnly(triggers
            .Distinct()
            .OrderBy(tile => tile.X)
            .ThenBy(tile => tile.Y)
            .ToArray());
        TargetLanding = targetLanding;
    }

    public string EdgeId { get; }
    public string SourceLocationId { get; }
    public string TargetLocationId { get; }
    public NavigationEdgeKind Kind { get; }
    public IReadOnlyList<NavigationTile> Triggers { get; }
    public NavigationTile TargetLanding { get; }
}

internal sealed class WorldRouteSnapshot
{
    private readonly IReadOnlyDictionary<string, NavigationLocationNode> _locations;

    private WorldRouteSnapshot(
        IReadOnlyDictionary<string, NavigationLocationNode> locations,
        IReadOnlyList<NavigationRouteEdge> edges
    )
    {
        _locations = locations;
        Edges = edges;
    }

    public IReadOnlyCollection<NavigationLocationNode> Locations => _locations.Values.ToArray();
    public IReadOnlyList<NavigationRouteEdge> Edges { get; }

    public bool TryGetLocation(string locationId, out NavigationLocationNode node) =>
        _locations.TryGetValue(locationId, out node!);

    public static WorldRouteSnapshot Create(IEnumerable<NavigationLocationSource> sourceLocations)
    {
        var sources = sourceLocations
            .GroupBy(source => source.LocationId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(source => source.LocationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.LocationId, StringComparer.Ordinal)
            .ToArray();
        var locations = sources.ToDictionary(
            source => source.LocationId,
            source => new NavigationLocationNode(source.LocationId, source.Width, source.Height),
            StringComparer.OrdinalIgnoreCase
        );
        var unnumbered = new List<UnnumberedEdge>();
        foreach (var source in sources)
        {
            var validExits = source.Exits
                .Where(exit => locations.ContainsKey(exit.TargetLocationId))
                .Select(exit => exit with
                {
                    TargetLocationId = locations[exit.TargetLocationId].LocationId,
                })
                .ToArray();

            foreach (var door in validExits.Where(exit => exit.Kind == NavigationEdgeKind.InteractDoor))
            {
                unnumbered.Add(new UnnumberedEdge(
                    source.LocationId,
                    door.TargetLocationId,
                    door.Kind,
                    new[] { door.Trigger },
                    door.TargetLanding
                ));
            }

            var walkGroups = validExits
                .Where(exit => exit.Kind == NavigationEdgeKind.WalkThrough)
                .GroupBy(exit => new WalkGroupKey(
                    exit.TargetLocationId,
                    exit.TargetLanding.X,
                    exit.TargetLanding.Y
                ));
            foreach (var group in walkGroups)
            {
                foreach (var component in ConnectedComponents(group.Select(exit => exit.Trigger)))
                {
                    unnumbered.Add(new UnnumberedEdge(
                        source.LocationId,
                        group.Key.TargetLocationId,
                        NavigationEdgeKind.WalkThrough,
                        component,
                        new NavigationTile(group.Key.TargetX, group.Key.TargetY)
                    ));
                }
            }
        }

        var ordered = unnumbered
            .GroupBy(EdgeIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(edge => edge.SourceLocationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.SourceLocationId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind)
            .ThenBy(edge => edge.Triggers[0].X)
            .ThenBy(edge => edge.Triggers[0].Y)
            .ThenBy(edge => edge.TargetLocationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.TargetLocationId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetLanding.X)
            .ThenBy(edge => edge.TargetLanding.Y)
            .ToArray();
        var edges = ordered
            .Select((edge, index) => new NavigationRouteEdge(
                $"edge-{index:D4}",
                edge.SourceLocationId,
                edge.TargetLocationId,
                edge.Kind,
                edge.Triggers,
                edge.TargetLanding
            ))
            .ToArray();
        return new WorldRouteSnapshot(
            new Dictionary<string, NavigationLocationNode>(locations, StringComparer.OrdinalIgnoreCase),
            Array.AsReadOnly(edges)
        );
    }

    private static IReadOnlyList<IReadOnlyList<NavigationTile>> ConnectedComponents(
        IEnumerable<NavigationTile> values
    )
    {
        var remaining = values.ToHashSet();
        var components = new List<IReadOnlyList<NavigationTile>>();
        while (remaining.Count > 0)
        {
            var first = remaining
                .OrderBy(tile => tile.X)
                .ThenBy(tile => tile.Y)
                .First();
            var queue = new Queue<NavigationTile>();
            var component = new List<NavigationTile>();
            remaining.Remove(first);
            queue.Enqueue(first);
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                component.Add(tile);
                foreach (var neighbor in CardinalNeighbors(tile))
                {
                    if (remaining.Remove(neighbor))
                        queue.Enqueue(neighbor);
                }
            }
            components.Add(component
                .OrderBy(tile => tile.X)
                .ThenBy(tile => tile.Y)
                .ToArray());
        }
        return components;
    }

    private static IEnumerable<NavigationTile> CardinalNeighbors(NavigationTile tile)
    {
        yield return new NavigationTile(tile.X, tile.Y - 1);
        yield return new NavigationTile(tile.X + 1, tile.Y);
        yield return new NavigationTile(tile.X, tile.Y + 1);
        yield return new NavigationTile(tile.X - 1, tile.Y);
    }

    private static string EdgeIdentity(UnnumberedEdge edge) => string.Join(
        "|",
        edge.SourceLocationId,
        edge.Kind,
        edge.TargetLocationId,
        edge.TargetLanding.X,
        edge.TargetLanding.Y,
        string.Join(";", edge.Triggers.Select(tile => $"{tile.X},{tile.Y}"))
    );

    private sealed record WalkGroupKey(string TargetLocationId, int TargetX, int TargetY);

    private sealed record UnnumberedEdge(
        string SourceLocationId,
        string TargetLocationId,
        NavigationEdgeKind Kind,
        IReadOnlyList<NavigationTile> Triggers,
        NavigationTile TargetLanding
    );
}

internal interface IWorldRouteSnapshotBuilder
{
    WorldRouteSnapshot Build();
}

internal sealed class StardewWorldRouteSnapshotBuilder : IWorldRouteSnapshotBuilder
{
    public WorldRouteSnapshot Build()
    {
        var loaded = GameLocationIdentity.EnumerateLoadedInstances()
            .Select(item => item.Instance)
            .OfType<GameLocation>()
            .GroupBy(location => location.NameOrUniqueName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(
                location => location.NameOrUniqueName,
                location => location,
                StringComparer.OrdinalIgnoreCase
            );
        var sources = new List<NavigationLocationSource>();
        foreach (var location in loaded.Values)
        {
            var exits = new List<NavigationExitSource>();
            foreach (var warp in location.warps)
            {
                if (warp.npcOnly.Value || !loaded.TryGetValue(warp.TargetName, out var target))
                    continue;
                exits.Add(new NavigationExitSource(
                    NavigationEdgeKind.WalkThrough,
                    new NavigationTile(warp.X, warp.Y),
                    target.NameOrUniqueName,
                    new NavigationTile(warp.TargetX, warp.TargetY)
                ));
            }

            foreach (var door in location.doors.Pairs)
                AddDoor(location, loaded, door.Key, exits);
            foreach (var building in location.buildings)
            {
                if (building.GetIndoors() is not null)
                    AddDoor(location, loaded, building.getPointForHumanDoor(), exits);
            }

            var layer = location.Map?.Layers?.FirstOrDefault();
            sources.Add(new NavigationLocationSource(
                location.NameOrUniqueName,
                layer?.LayerWidth ?? 0,
                layer?.LayerHeight ?? 0,
                exits
            ));
        }
        return WorldRouteSnapshot.Create(sources);
    }

    private static void AddDoor(
        GameLocation source,
        IReadOnlyDictionary<string, GameLocation> loaded,
        Point trigger,
        ICollection<NavigationExitSource> exits
    )
    {
        var warp = source.getWarpFromDoor(trigger, Game1.player);
        if (warp is null || !loaded.TryGetValue(warp.TargetName, out var target))
            return;
        exits.Add(new NavigationExitSource(
            NavigationEdgeKind.InteractDoor,
            new NavigationTile(trigger.X, trigger.Y),
            target.NameOrUniqueName,
            new NavigationTile(warp.TargetX, warp.TargetY)
        ));
    }
}

internal static class WorldRoutePlanner
{
    public static IReadOnlyList<NavigationRouteEdge>? FindRoute(
        WorldRouteSnapshot snapshot,
        string sourceLocationId,
        string targetLocationId
    )
    {
        if (!snapshot.TryGetLocation(sourceLocationId, out var source)
            || !snapshot.TryGetLocation(targetLocationId, out var target))
            return null;
        if (string.Equals(source.LocationId, target.LocationId, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<NavigationRouteEdge>();

        var outgoing = snapshot.Edges
            .GroupBy(edge => edge.SourceLocationId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(edge => edge.EdgeId, StringComparer.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase
            );
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source.LocationId };
        var previous = new Dictionary<string, NavigationRouteEdge>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(source.LocationId);
        while (queue.Count > 0)
        {
            var locationId = queue.Dequeue();
            if (!outgoing.TryGetValue(locationId, out var edges))
                continue;
            foreach (var edge in edges)
            {
                if (!visited.Add(edge.TargetLocationId))
                    continue;
                previous[edge.TargetLocationId] = edge;
                if (string.Equals(
                    edge.TargetLocationId,
                    target.LocationId,
                    StringComparison.OrdinalIgnoreCase
                ))
                    return Reconstruct(previous, source.LocationId, target.LocationId);
                queue.Enqueue(edge.TargetLocationId);
            }
        }
        return null;
    }

    private static IReadOnlyList<NavigationRouteEdge> Reconstruct(
        IReadOnlyDictionary<string, NavigationRouteEdge> previous,
        string sourceLocationId,
        string targetLocationId
    )
    {
        var reversed = new List<NavigationRouteEdge>();
        var current = targetLocationId;
        while (!string.Equals(current, sourceLocationId, StringComparison.OrdinalIgnoreCase))
        {
            var edge = previous[current];
            reversed.Add(edge);
            current = edge.SourceLocationId;
        }
        reversed.Reverse();
        return reversed;
    }
}
