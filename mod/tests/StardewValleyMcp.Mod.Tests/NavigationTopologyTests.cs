using NUnit.Framework;

namespace StardewValleyMcp.Mod.Tests;

public sealed class NavigationTopologyTests
{
    [Test]
    public void SnapshotKeepsDistinctParallelExitsAndDoorSemantics()
    {
        var snapshot = Snapshot(
            Location(
                "Farm",
                Exit(NavigationEdgeKind.WalkThrough, 0, 2, "Town", 5, 5),
                Exit(NavigationEdgeKind.WalkThrough, 0, 8, "Town", 5, 5),
                Exit(NavigationEdgeKind.InteractDoor, 4, 4, "Town", 5, 5)
            ),
            Location("Town")
        );

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Edges, Has.Count.EqualTo(3));
            Assert.That(
                snapshot.Edges.Count(edge => edge.Kind == NavigationEdgeKind.WalkThrough),
                Is.EqualTo(2)
            );
            Assert.That(
                snapshot.Edges.Count(edge => edge.Kind == NavigationEdgeKind.InteractDoor),
                Is.EqualTo(1)
            );
            Assert.That(snapshot.Edges.Select(edge => edge.EdgeId), Is.Unique);
        });
    }

    [Test]
    public void SnapshotGroupsOnlyCardinallyContiguousWarpLaneTiles()
    {
        var snapshot = Snapshot(
            Location(
                "Farm",
                Exit(NavigationEdgeKind.WalkThrough, 0, 2, "Town", 5, 5),
                Exit(NavigationEdgeKind.WalkThrough, 0, 3, "Town", 5, 5),
                Exit(NavigationEdgeKind.WalkThrough, 0, 8, "Town", 5, 5)
            ),
            Location("Town")
        );

        Assert.That(
            snapshot.Edges
                .Where(edge => edge.Kind == NavigationEdgeKind.WalkThrough)
                .Select(edge => edge.Triggers.Count)
                .OrderBy(count => count),
            Is.EqualTo(new[] { 1, 2 })
        );
    }

    [Test]
    public void SnapshotUsesCaseInsensitiveFullRuntimeLocationIdentity()
    {
        var snapshot = Snapshot(
            Location(
                "Farm",
                Exit(NavigationEdgeKind.InteractDoor, 2, 2, "Coop-a-guid", 1, 1),
                Exit(NavigationEdgeKind.InteractDoor, 8, 2, "Coop-b-guid", 1, 1)
            ),
            Location("Coop-a-guid"),
            Location("Coop-b-guid")
        );

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.TryGetLocation("COOP-A-GUID", out var first), Is.True);
            Assert.That(first.LocationId, Is.EqualTo("Coop-a-guid"));
            Assert.That(
                snapshot.Edges.Select(edge => edge.TargetLocationId),
                Is.EquivalentTo(new[] { "Coop-a-guid", "Coop-b-guid" })
            );
        });
    }

    [Test]
    public void PlannerReturnsDeterministicConcreteMultiHopRoute()
    {
        var snapshot = Snapshot(
            Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 2, "Town", 1, 1)),
            Location("Town", Exit(NavigationEdgeKind.InteractDoor, 5, 5, "House", 2, 2)),
            Location("House")
        );

        var route = WorldRoutePlanner.FindRoute(snapshot, "farm", "HOUSE");
        var concreteRoute = route!;

        Assert.Multiple(() =>
        {
            Assert.That(route, Is.Not.Null);
            Assert.That(concreteRoute.Select(edge => edge.SourceLocationId), Is.EqualTo(new[] { "Farm", "Town" }));
            Assert.That(concreteRoute.Select(edge => edge.TargetLocationId), Is.EqualTo(new[] { "Town", "House" }));
            Assert.That(concreteRoute.Select(edge => edge.Kind), Is.EqualTo(new[]
            {
                NavigationEdgeKind.WalkThrough,
                NavigationEdgeKind.InteractDoor,
            }));
        });
    }

    [Test]
    public void PlannerReturnsNullWhenNoRouteExists()
    {
        var snapshot = Snapshot(Location("Farm"), Location("Island"));

        Assert.That(WorldRoutePlanner.FindRoute(snapshot, "Farm", "Island"), Is.Null);
    }

    private static WorldRouteSnapshot Snapshot(params NavigationLocationSource[] locations) =>
        WorldRouteSnapshot.Create(locations);

    private static NavigationLocationSource Location(
        string id,
        params NavigationExitSource[] exits
    ) => new(id, 20, 20, exits);

    private static NavigationExitSource Exit(
        NavigationEdgeKind kind,
        int x,
        int y,
        string target,
        int targetX,
        int targetY
    ) => new(kind, new NavigationTile(x, y), target, new NavigationTile(targetX, targetY));
}
