using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class NavigateHandlerTests
{
    [Test]
    public void ValidateFixesThePublicTargetArrivalAndDirectionRules()
    {
        var handler = NewHandler(out _, out _);
        var valid = PositionRequest("Farm", 5, 6, ArrivalMode.Exact);
        var refExact = RefRequest(ArrivalMode.Exact);
        var exactWithSide = PositionRequest("Farm", 5, 6, ArrivalMode.Exact);
        exactWithSide.Navigate.StandSide = Direction.Left;
        var unspecifiedFace = PositionRequest("Farm", 5, 6, ArrivalMode.Exact);
        unspecifiedFace.Navigate.FaceOnArrival = Direction.Unspecified;

        Assert.Multiple(() =>
        {
            Assert.That(handler.Validate(valid), Is.Null);
            Assert.That(handler.Validate(RefRequest(ArrivalMode.Adjacent)), Is.Null);
            Assert.That(handler.Validate(refExact)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(handler.Validate(exactWithSide)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(handler.Validate(unspecifiedFace)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(
                handler.Validate(new CommandRequest { Navigate = new NavigateRequest { Arrival = ArrivalMode.Exact } })?.Code,
                Is.EqualTo(ErrorCode.InvalidArgument)
            );
            Assert.That(
                handler.Validate(new CommandRequest { QueryRuntime = new QueryRuntimeRequest() })?.Code,
                Is.EqualTo(ErrorCode.InvalidArgument)
            );
        });
    }

    [Test]
    public void ExactAlreadyThereSucceedsWithStrictDestinationAndSingleRouteLocation()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 5, 6);
        navigation.State = Player("Farm", 5, 6);
        navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);

        var step = (ContinuationStep.Succeeded)handler
            .Start(CommandId, PositionRequest("Farm", 5, 6, ArrivalMode.Exact))
            .Tick(ContinuationStopSignal.None);
        var result = step.Result.Navigate;

        Assert.Multiple(() =>
        {
            Assert.That(result.Start, Is.EqualTo(result.Final));
            Assert.That(result.Final, Is.EqualTo(result.ResolvedDestination));
            Assert.That(result.RouteLocationIds, Is.EqualTo(new[] { "Farm" }));
            Assert.That(result.Execution.ElapsedTicks, Is.EqualTo(1));
            Assert.That(result.Execution.CompletionReason, Is.EqualTo("already_there"));
            Assert.That(resolver.RevalidateCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExactWaitsForOwnedPathAndFailsIfControllerEndsBeforeStrictArrival()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 9, 6);
        navigation.State = Player("Farm", 5, 6);
        navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = handler.Start(
            CommandId,
            PositionRequest("Farm", 9, 6, ArrivalMode.Exact)
        );

        var started = continuation.Tick(ContinuationStopSignal.None);
        var walking = continuation.Tick(ContinuationStopSignal.None);
        navigation.State = Player("Farm", 8, 6);
        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(walking, Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(failed.Error.Navigation, Is.Not.Null);
            Assert.That(failed.Error.Navigation.LastConfirmedPosition, Is.EqualTo(Position("Farm", 8, 6)));
            Assert.That(navigation.StopCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExactCompletesAfterTheOwnedPathReachesTheLockedTile()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 9, 6);
        navigation.State = Player("Farm", 5, 6);
        navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = handler.Start(
            CommandId,
            PositionRequest("Farm", 9, 6, ArrivalMode.Exact)
        );

        continuation.Tick(ContinuationStopSignal.None);
        navigation.State = Player("Farm", 9, 6);
        var step = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(step.Result.Navigate.Start, Is.EqualTo(Position("Farm", 5, 6)));
            Assert.That(step.Result.Navigate.Final, Is.EqualTo(Position("Farm", 9, 6)));
            Assert.That(step.Result.Navigate.ResolvedDestination, Is.EqualTo(Position("Farm", 9, 6)));
            Assert.That(step.Result.Navigate.Execution.CompletionReason, Is.EqualTo("arrived"));
            Assert.That(resolver.RevalidateCalls, Is.EqualTo(1));
            Assert.That(navigation.StopCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void WorldBecomingUnavailableCleansTheOwnedPathBeforeFailure()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 9, 6);
        navigation.State = Player("Farm", 5, 6);
        navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = handler.Start(
            CommandId,
            PositionRequest("Farm", 9, 6, ArrivalMode.Exact)
        );

        continuation.Tick(ContinuationStopSignal.None);
        navigation.State = new NavigationPlayerState(false, false, "", 0, 0, -1, false);
        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.NotReady));
            Assert.That(navigation.StopCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void AdjacentAutoSelectionUsesAReachableCandidateAndReportsItsWalkTile()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 2, 2);
        navigation.State = Player("Farm", 0, 0);
        navigation.StartResults.Enqueue(LocalNavigationStart.NoPath);
        navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = handler.Start(
            CommandId,
            PositionRequest("Farm", 2, 2, ArrivalMode.Adjacent)
        );

        continuation.Tick(ContinuationStopSignal.None);
        navigation.State = Player("Farm", 1, 2);
        var step = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(navigation.StartedTiles, Is.EqualTo(new[] { (2, 1), (1, 2) }));
            Assert.That(step.Result.Navigate.ResolvedDestination, Is.EqualTo(Position("Farm", 1, 2)));
            Assert.That(step.Result.Navigate.Final, Is.EqualTo(Position("Farm", 1, 2)));
        });
    }

    [Test]
    public void RequestedStandSideDoesNotSilentlyFallBack()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 5, 5);
        navigation.State = Player("Farm", 1, 1);
        navigation.StartResults.Enqueue(LocalNavigationStart.NoPath);
        var request = PositionRequest("Farm", 5, 5, ArrivalMode.Adjacent);
        request.Navigate.StandSide = Direction.Right;

        var failed = (ContinuationStep.Failed)handler
            .Start(CommandId, request)
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(navigation.StartedTiles, Is.EqualTo(new[] { (6, 5) }));
        });
    }

    [Test]
    public void ArrivalFacingIsAppliedAndVerifiedAfterStrictArrival()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 5, 6);
        navigation.State = Player("Farm", 5, 6, facing: 0);
        navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var request = PositionRequest("Farm", 5, 6, ArrivalMode.Exact);
        request.Navigate.FaceOnArrival = Direction.Left;

        var step = (ContinuationStep.Succeeded)handler
            .Start(CommandId, request)
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(navigation.FaceCalls, Is.EqualTo(new[] { 3 }));
            Assert.That(step.Result.Navigate.Final, Is.EqualTo(Position("Farm", 5, 6)));
        });
    }

    [Test]
    public void MovedRefFailsWithoutPretendingSuccess()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 5, 6, hasRef: true);
        resolver.RevalidateError = new Error
        {
            Code = ErrorCode.ExecutionFailed,
            Message = "目标已移动",
        };
        navigation.StartResults.Enqueue(LocalNavigationStart.AlreadyThere);
        var moved = (ContinuationStep.Failed)handler
            .Start(CommandId, RefRequest(ArrivalMode.Adjacent))
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(moved.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
        });
    }

    [Test]
    public void CancelAlwaysCleansTheOwnedPath() =>
        AssertStopCleansPath(ContinuationStopSignal.CancelRequested);

    [Test]
    public void DeadlineAlwaysCleansTheOwnedPath() =>
        AssertStopCleansPath(ContinuationStopSignal.DeadlineExceeded);

    [Test]
    public void SameMapDeadlineReportsZeroRouteSegmentsAndLastConfirmedPosition()
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 9, 6);
        navigation.State = Player("Farm", 5, 6);
        navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = handler.Start(
            CommandId,
            PositionRequest("Farm", 9, 6, ArrivalMode.Exact)
        );

        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        Assert.That(
            continuation.Tick(ContinuationStopSignal.DeadlineExceeded),
            Is.TypeOf<ContinuationStep.Stopped>()
        );
        var error = new Error { Code = ErrorCode.DeadlineExceeded, Message = "命令已超过期限" };
        ((IStopErrorContextProvider)continuation).EnrichStopError(
            ContinuationStopSignal.DeadlineExceeded,
            error
        );

        Assert.Multiple(() =>
        {
            Assert.That(error.Navigation.LastConfirmedPosition, Is.EqualTo(Position("Farm", 5, 6)));
            Assert.That(error.Navigation.RouteSegmentsTotal, Is.Zero);
            Assert.That(error.Navigation.RouteSegmentsCompleted, Is.Zero);
            Assert.That(error.Navigation.InterruptionReason, Is.EqualTo("deadline_exceeded"));
        });
    }

    private static void AssertStopCleansPath(ContinuationStopSignal signal)
    {
        var handler = NewHandler(out var resolver, out var navigation);
        resolver.Target = Target("Farm", 9, 6);
        navigation.State = Player("Farm", 5, 6);
        navigation.StartResults.Enqueue(LocalNavigationStart.Started);
        var continuation = handler.Start(
            CommandId,
            PositionRequest("Farm", 9, 6, ArrivalMode.Exact)
        );
        continuation.Tick(ContinuationStopSignal.None);

        var stopped = continuation.Tick(signal);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(navigation.StopCalls, Is.EqualTo(1));
        });
    }

    private static NavigateHandler NewHandler(
        out FakeTargetResolver resolver,
        out FakeNavigationDriver navigation
    )
    {
        resolver = new FakeTargetResolver();
        navigation = new FakeNavigationDriver();
        return new NavigateHandler(
            resolver,
            navigation,
            new FakeRouteSnapshotBuilder(),
            new FakeWarpTransitionDriver()
        );
    }

    private static CommandRequest PositionRequest(
        string locationId,
        int x,
        int y,
        ArrivalMode arrival
    ) => new()
    {
        Navigate = new NavigateRequest
        {
            Position = Position(locationId, x, y),
            Arrival = arrival,
        },
    };

    private static CommandRequest RefRequest(ArrivalMode arrival) => new()
    {
        Navigate = new NavigateRequest
        {
            TargetRef = new Ref { Value = "target-ref" },
            Arrival = arrival,
        },
    };

    private static LockedActionTarget Target(
        string locationId,
        int x,
        int y,
        bool hasRef = false
    ) => new(
        locationId,
        x,
        y,
        hasRef ? new Ref { Value = "target-ref" } : null,
        hasRef ? new object() : null
    );

    private static NavigationPlayerState Player(
        string locationId,
        int x,
        int y,
        int facing = 2
    ) => new(true, true, locationId, x, y, facing, false);

    private static WorldPosition Position(string locationId, int x, int y) =>
        new() { LocationId = locationId, X = x, Y = y };

    private const string CommandId = "55555555-5555-4555-8555-555555555555";

    private sealed class FakeTargetResolver : IActionTargetResolver
    {
        public LockedActionTarget Target { get; set; } =
            NavigateHandlerTests.Target("Farm", 5, 6);
        public Error? ResolveError { get; set; }
        public Error? RevalidateError { get; set; }
        public int RevalidateCalls { get; private set; }

        public ActionTargetResolution Resolve(WorldPosition? position, Ref? reference) =>
            ResolveError is null
                ? new ActionTargetResolution(Target, null)
                : new ActionTargetResolution(null, ResolveError);

        public Error? Revalidate(LockedActionTarget target)
        {
            RevalidateCalls++;
            return RevalidateError;
        }
    }

    private sealed class FakeNavigationDriver : ILocalNavigationDriver
    {
        public NavigationPlayerState State { get; set; } = Player("Farm", 5, 6);
        public Queue<LocalNavigationStart> StartResults { get; } = new();
        public List<(int X, int Y)> StartedTiles { get; } = new();
        public List<int> FaceCalls { get; } = new();
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
            FaceCalls.Add(direction);
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
        public WorldRouteSnapshot Build() => WorldRouteSnapshot.Create(Array.Empty<NavigationLocationSource>());
    }

    private sealed class FakeWarpTransitionDriver : IWarpTransitionDriver
    {
        public bool IsTransitionPending => false;
        public bool BeginWalkThrough(int direction) => true;
        public bool SubmitDoor(int x, int y) => true;
        public void Stop()
        {
        }
    }
}
