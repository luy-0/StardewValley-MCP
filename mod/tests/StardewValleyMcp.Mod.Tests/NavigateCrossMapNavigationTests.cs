using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class NavigateCrossMapNavigationTests
{
    [Test]
    public void WalkThroughWarpWaitsForStableTargetThenReportsActualRoute()
    {
        var fixture = Fixture(
            Target("Town", 2, 2),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 5, "Town", 1, 5)),
                Location("Town")
            ),
            Player("Farm", 4, 5)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var continuation = fixture.Handler.Start(CommandId, Request("Town", 2, 2));

        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        fixture.Navigation.State = Player("Farm", 1, 5, facing: 3);
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        fixture.Navigation.State = Player("Town", 2, 2);
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());

        var succeeded = CompleteStability(continuation);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Warp.WalkDirections, Is.EqualTo(new[] { 3 }));
            Assert.That(fixture.Warp.Doors, Is.Empty);
            Assert.That(succeeded.Result.Navigate.RouteLocationIds, Is.EqualTo(new[] { "Farm", "Town" }));
            Assert.That(succeeded.Result.Navigate.Final, Is.EqualTo(Position("Town", 2, 2)));
            Assert.That(succeeded.Result.Navigate.ResolvedDestination, Is.EqualTo(Position("Town", 2, 2)));
        });
    }

    [Test]
    public void DoorWarpSubmitsOneNativeSemanticDoorAction()
    {
        var fixture = Fixture(
            Target("FarmHouse", 3, 9),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.InteractDoor, 5, 5, "FarmHouse", 3, 9)),
                Location("FarmHouse")
            ),
            Player("Farm", 5, 7)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var continuation = fixture.Handler.Start(CommandId, Request("FarmHouse", 3, 9));

        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Farm", 5, 6, facing: 0);
        continuation.Tick(ContinuationStopSignal.None);
        for (var i = 0; i < 3; i++)
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        fixture.Navigation.State = Player("FarmHouse", 3, 9);
        continuation.Tick(ContinuationStopSignal.None);
        var succeeded = CompleteStability(continuation);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Warp.Doors, Is.EqualTo(new[] { (5, 5) }));
            Assert.That(fixture.Warp.WalkDirections, Is.Empty);
            Assert.That(succeeded.Result.Navigate.RouteLocationIds, Is.EqualTo(new[] { "Farm", "FarmHouse" }));
        });
    }

    [Test]
    public void UnreachableParallelExitFallsBackToNextConcreteEdge()
    {
        var fixture = Fixture(
            Target("Town", 2, 2),
            Snapshot(
                Location(
                    "Farm",
                    Exit(NavigationEdgeKind.WalkThrough, 0, 2, "Town", 1, 5),
                    Exit(NavigationEdgeKind.WalkThrough, 0, 8, "Town", 1, 5)
                ),
                Location("Town")
            ),
            Player("Farm", 5, 5)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.NoPath);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.NoPath);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.NoPath);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = fixture.Handler.Start(CommandId, Request("Town", 2, 2));

        var step = continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(step, Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(continuation.Phase, Is.EqualTo("walking_to_exit"));
            Assert.That(
                fixture.Navigation.StartedTiles,
                Is.EqualTo(new[] { (1, 2), (0, 3), (0, 1), (1, 8) })
            );
        });
    }

    [Test]
    public void WalkThroughProbeTimeoutReleasesDirectionAndTriesNextApproach()
    {
        var fixture = Fixture(
            Target("Town", 2, 2),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 5, "Town", 1, 5)),
                Location("Town")
            ),
            Player("Farm", 1, 5, facing: 3)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = fixture.Handler.Start(CommandId, Request("Town", 2, 2));
        continuation.Tick(ContinuationStopSignal.None);

        for (var i = 0; i < WalkTransitionProbeTicks - 1; i++)
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        var fallback = continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Farm", 0, 6, facing: 0);
        continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(fallback, Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(fixture.Navigation.StartedTiles, Is.EqualTo(new[] { (1, 5), (0, 6) }));
            Assert.That(fixture.Warp.WalkDirections, Is.EqualTo(new[] { 3, 0 }));
            Assert.That(fixture.Warp.StopCalls, Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public void PendingGameWarpAtProbeThresholdWaitsForObservedTargetLocation()
    {
        var fixture = Fixture(
            Target("Town", 2, 2),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 5, "Town", 1, 5)),
                Location("Town")
            ),
            Player("Farm", 1, 5, facing: 3)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var continuation = fixture.Handler.Start(CommandId, Request("Town", 2, 2));
        continuation.Tick(ContinuationStopSignal.None);
        for (var i = 0; i < WalkTransitionProbeTicks - 1; i++)
            continuation.Tick(ContinuationStopSignal.None);

        fixture.Warp.IsTransitionPending = true;
        for (var i = 0; i < 5; i++)
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());

        fixture.Warp.IsTransitionPending = false;
        fixture.Navigation.State = Player("Town", 2, 2);
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var succeeded = CompleteStability(continuation);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded.Result.Navigate.RouteLocationIds, Is.EqualTo(new[] { "Farm", "Town" }));
            Assert.That(fixture.Warp.WalkDirections, Is.EqualTo(new[] { 3 }));
            Assert.That(fixture.Navigation.StartedTiles, Is.EqualTo(new[] { (1, 5), (2, 2) }));
        });
    }

    [Test]
    public void StateMachineExecutesMultiHopRouteAndReportsEachStableLocation()
    {
        var fixture = Fixture(
            Target("House", 2, 2),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 5, "Town", 1, 5)),
                Location("Town", Exit(NavigationEdgeKind.WalkThrough, 19, 5, "House", 1, 5)),
                Location("House")
            ),
            Player("Farm", 4, 5)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var continuation = fixture.Handler.Start(CommandId, Request("House", 2, 2));

        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Farm", 1, 5, facing: 3);
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Town", 1, 5);
        continuation.Tick(ContinuationStopSignal.None);
        Assert.That(AdvanceStability(continuation), Is.TypeOf<ContinuationStep.Pending>());
        Assert.That(continuation.Phase, Is.EqualTo("waiting_handoff"));
        continuation.Tick(ContinuationStopSignal.None);

        fixture.Navigation.State = Player("Town", 18, 5, facing: 1);
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("House", 2, 2);
        continuation.Tick(ContinuationStopSignal.None);
        var succeeded = CompleteStability(continuation);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Warp.WalkDirections, Is.EqualTo(new[] { 3, 1 }));
            Assert.That(
                succeeded.Result.Navigate.RouteLocationIds,
                Is.EqualTo(new[] { "Farm", "Town", "House" })
            );
        });
    }

    [Test]
    public void StableWarpDefersNextLegUntilLaterMovableHandoffTick()
    {
        var fixture = Fixture(
            Target("House", 2, 2),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 5, "Town", 1, 5)),
                Location("Town", Exit(NavigationEdgeKind.WalkThrough, 19, 5, "House", 1, 5)),
                Location("House")
            ),
            Player("Farm", 4, 5)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = fixture.Handler.Start(CommandId, Request("House", 2, 2));

        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Farm", 1, 5, facing: 3);
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Town", 1, 5);
        continuation.Tick(ContinuationStopSignal.None);
        var stable = AdvanceStability(continuation);

        Assert.Multiple(() =>
        {
            Assert.That(stable, Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(continuation.Phase, Is.EqualTo("waiting_handoff"));
            Assert.That(fixture.Navigation.StartedTiles, Is.EqualTo(new[] { (1, 5) }));
        });

        fixture.Navigation.State = Player("Town", 1, 5, canMove: false);
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        Assert.That(fixture.Navigation.StartedTiles, Has.Count.EqualTo(1));

        fixture.Navigation.State = Player("Town", 1, 5);
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        Assert.Multiple(() =>
        {
            Assert.That(continuation.Phase, Is.EqualTo("walking_to_exit"));
            Assert.That(fixture.Navigation.StartedTiles, Is.EqualTo(new[] { (1, 5), (18, 5) }));
        });
    }

    [Test]
    public void WrongTransitionLocationFailsAndCleansBothMovementOwners()
    {
        var fixture = Fixture(
            Target("Town", 2, 2),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 5, "Town", 1, 5)),
                Location("Town"),
                Location("Beach")
            ),
            Player("Farm", 1, 5, facing: 3)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var continuation = fixture.Handler.Start(CommandId, Request("Town", 2, 2));

        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Beach", 4, 4);
        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(fixture.Navigation.StopCalls, Is.GreaterThanOrEqualTo(2));
            Assert.That(fixture.Warp.StopCalls, Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public void StabilityGateResetsWhenPlayerCannotMove()
    {
        var fixture = StartedTransitionFixture();
        var continuation = fixture.Handler.Start(CommandId, Request("Town", 2, 2));

        BeginAndAcceptTransition(fixture, continuation);
        for (var i = 0; i < 4; i++)
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        fixture.Navigation.State = Player("Town", 2, 2, canMove: false);
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        fixture.Navigation.State = Player("Town", 2, 2);
        for (var i = 0; i < StableFramesRequired - 1; i++)
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());

        Assert.That(
            continuation.Tick(ContinuationStopSignal.None),
            Is.TypeOf<ContinuationStep.Pending>()
        );
        Assert.That(
            continuation.Tick(ContinuationStopSignal.None),
            Is.TypeOf<ContinuationStep.Succeeded>()
        );
    }

    [Test]
    public void CancelWhileHoldingWalkThroughDirectionStopsImmediately()
    {
        var fixture = Fixture(
            Target("Town", 2, 2),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 5, "Town", 1, 5)),
                Location("Town")
            ),
            Player("Farm", 1, 5, facing: 3)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var continuation = fixture.Handler.Start(CommandId, Request("Town", 2, 2));
        continuation.Tick(ContinuationStopSignal.None);

        var stopped = continuation.Tick(ContinuationStopSignal.CancelRequested);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(fixture.Warp.WalkDirections, Is.EqualTo(new[] { 3 }));
            Assert.That(fixture.Warp.StopCalls, Is.GreaterThanOrEqualTo(1));
            Assert.That(fixture.Navigation.StopCalls, Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public void DeadlineAfterObservedTransitionSettlesBeforeStopping()
    {
        var fixture = StartedTransitionFixture();
        var continuation = fixture.Handler.Start(CommandId, Request("Town", 2, 2));
        BeginAndAcceptTransition(fixture, continuation);

        Assert.That(continuation.CanCancel, Is.False);
        for (var i = 0; i < StableFramesRequired - 1; i++)
            Assert.That(
                continuation.Tick(ContinuationStopSignal.DeadlineExceeded),
                Is.TypeOf<ContinuationStep.Pending>()
            );

        Assert.That(
            continuation.Tick(ContinuationStopSignal.DeadlineExceeded),
            Is.TypeOf<ContinuationStep.Stopped>()
        );
    }

    private static FixtureState StartedTransitionFixture()
    {
        var fixture = Fixture(
            Target("Town", 2, 2),
            Snapshot(
                Location("Farm", Exit(NavigationEdgeKind.WalkThrough, 0, 5, "Town", 1, 5)),
                Location("Town")
            ),
            Player("Farm", 4, 5)
        );
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        fixture.Navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        return fixture;
    }

    private static void BeginAndAcceptTransition(
        FixtureState fixture,
        ICommandContinuation continuation
    )
    {
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Farm", 1, 5, facing: 3);
        continuation.Tick(ContinuationStopSignal.None);
        fixture.Navigation.State = Player("Town", 2, 2);
        continuation.Tick(ContinuationStopSignal.None);
    }

    private static ContinuationStep.Succeeded CompleteStability(ICommandContinuation continuation)
    {
        AdvanceStability(continuation);
        return (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);
    }

    private static ContinuationStep AdvanceStability(ICommandContinuation continuation)
    {
        ContinuationStep step = new ContinuationStep.Pending();
        for (var i = 0; i < StableFramesRequired; i++)
            step = continuation.Tick(ContinuationStopSignal.None);
        return step;
    }

    private static FixtureState Fixture(
        LockedActionTarget target,
        WorldRouteSnapshot snapshot,
        NavigationPlayerState state
    )
    {
        var resolver = new FakeTargetResolver { Target = target };
        var navigation = new FakeNavigationDriver { State = state };
        var routes = new FakeRouteSnapshotBuilder { Snapshot = snapshot };
        var warp = new FakeWarpTransitionDriver();
        return new FixtureState(
            new NavigateHandler(resolver, navigation, routes, warp),
            navigation,
            warp
        );
    }

    private static CommandRequest Request(string locationId, int x, int y) => new()
    {
        Navigate = new NavigateRequest
        {
            Position = Position(locationId, x, y),
            Arrival = ArrivalMode.Exact,
        },
    };

    private static LockedActionTarget Target(string locationId, int x, int y) =>
        new(locationId, x, y, null, null);

    private static NavigationPlayerState Player(
        string locationId,
        int x,
        int y,
        int facing = 2,
        bool canMove = true
    ) => new(true, canMove, locationId, x, y, facing, false);

    private static WorldPosition Position(string locationId, int x, int y) =>
        new() { LocationId = locationId, X = x, Y = y };

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

    private const string CommandId = "66666666-6666-4666-8666-666666666666";
    private const int StableFramesRequired = 10;
    private const int WalkTransitionProbeTicks = 30;

    private sealed record FixtureState(
        NavigateHandler Handler,
        FakeNavigationDriver Navigation,
        FakeWarpTransitionDriver Warp
    );

    private sealed class FakeTargetResolver : IActionTargetResolver
    {
        public LockedActionTarget Target { get; init; } =
            NavigateCrossMapNavigationTests.Target("Farm", 0, 0);

        public ActionTargetResolution Resolve(WorldPosition? position, Ref? reference) =>
            new(Target, null);

        public Error? Revalidate(LockedActionTarget target) => null;
    }

    private sealed class FakeNavigationDriver : ILocalNavigationDriver
    {
        public NavigationPlayerState State { get; set; } = Player("Farm", 0, 0);
        public Queue<LocalNavigationStart> StartResults { get; } = new();
        public List<(int X, int Y)> StartedTiles { get; } = new();
        public int StopCalls { get; private set; }

        public NavigationPlayerState Capture() => State;

        public LocalNavigationStart Start(int x, int y)
        {
            StartedTiles.Add((x, y));
            var result = StartResults.Count > 0
                ? StartResults.Dequeue()
                : LocalNavigationStart.NoPath;
            if (result == LocalNavigationStart.Started)
                State = State with { OwnedPathActive = true };
            return result;
        }

        public bool TryFace(int direction)
        {
            State = State with { FacingDirection = direction };
            return true;
        }

        public void Stop()
        {
            StopCalls++;
            State = State with { OwnedPathActive = false };
        }
    }

    private sealed class FakeRouteSnapshotBuilder : IWorldRouteSnapshotBuilder
    {
        public WorldRouteSnapshot Snapshot { get; init; } = Snapshot(Location("Farm"));
        public WorldRouteSnapshot Build() => Snapshot;
    }

    private sealed class FakeWarpTransitionDriver : IWarpTransitionDriver
    {
        public bool IsTransitionPending { get; set; }
        public List<int> WalkDirections { get; } = new();
        public List<(int X, int Y)> Doors { get; } = new();
        public int StopCalls { get; private set; }

        public bool BeginWalkThrough(int direction)
        {
            WalkDirections.Add(direction);
            return true;
        }

        public bool SubmitDoor(int x, int y)
        {
            Doors.Add((x, y));
            return true;
        }

        public void Stop() => StopCalls++;
    }
}
