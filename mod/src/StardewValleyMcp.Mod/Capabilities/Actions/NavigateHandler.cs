using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class NavigateHandler : ILongRunningCapabilityHandler
{
    private readonly IActionTargetResolver _targets;
    private readonly ILocalNavigationDriver _navigation;
    private readonly IWorldRouteSnapshotBuilder _routes;
    private readonly IWarpTransitionDriver _warp;

    public NavigateHandler(OpaqueRefStore refs)
        : this(
            new ActionTargetResolver(refs),
            new StardewLocalNavigationDriver(),
            new StardewWorldRouteSnapshotBuilder(),
            new StardewWarpTransitionDriver()
        )
    {
    }

    internal NavigateHandler(
        IActionTargetResolver targets,
        ILocalNavigationDriver navigation,
        IWorldRouteSnapshotBuilder routes,
        IWarpTransitionDriver warp
    )
    {
        _targets = targets;
        _navigation = navigation;
        _routes = routes;
        _warp = warp;
    }

    public string Id => "navigate";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.Navigate;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("navigate 请求类型无效");

        var navigate = request.Navigate;
        if (navigate.TargetCase == NavigateRequest.TargetOneofCase.None)
            return Invalid("navigate 必须提供 position 或 target_ref");
        if (navigate.TargetCase == NavigateRequest.TargetOneofCase.Position
            && !PublicStringPolicy.IsNonEmptyValid(navigate.Position?.LocationId, 128))
            return Invalid("position.location_id 格式无效");
        if (navigate.TargetCase == NavigateRequest.TargetOneofCase.TargetRef
            && !PublicStringPolicy.IsNonEmptyValid(navigate.TargetRef?.Value))
            return Invalid("target_ref 格式无效");
        if (navigate.Arrival is not ArrivalMode.Exact and not ArrivalMode.Adjacent)
            return Invalid("arrival 必须为 EXACT 或 ADJACENT");
        if (navigate.TargetCase == NavigateRequest.TargetOneofCase.TargetRef
            && navigate.Arrival != ArrivalMode.Adjacent)
            return Invalid("Ref 导航只支持 ADJACENT");
        if (navigate.HasStandSide
            && (navigate.Arrival != ArrivalMode.Adjacent
                || !TryDirection(navigate.StandSide, out _)))
            return Invalid("stand_side 只允许用于 ADJACENT");
        if (navigate.HasFaceOnArrival
            && !TryDirection(navigate.FaceOnArrival, out _))
            return Invalid("face_on_arrival 无效");
        return null;
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new NavigateContinuation(
            _targets,
            _navigation,
            _routes,
            _warp,
            request.Navigate
        );

    private static bool TryDirection(Direction direction, out int value)
    {
        value = direction switch
        {
            Direction.Up => 0,
            Direction.Right => 1,
            Direction.Down => 2,
            Direction.Left => 3,
            _ => -1,
        };
        return value >= 0;
    }

    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };

    private sealed class NavigateContinuation : ICommandContinuation
    {
        internal const int StableFramesRequired = 10;
        internal const int WalkTransitionProbeTicks = 30;

        private enum NavigationPhase
        {
            Resolving,
            WalkingToEdge,
            WaitingTransition,
            WaitingStable,
            Handoff,
            WaitingEdgeReady,
            WalkingFinal,
            Facing,
            Done,
        }

        private readonly IActionTargetResolver _targets;
        private readonly ILocalNavigationDriver _navigation;
        private readonly IWorldRouteSnapshotBuilder _routes;
        private readonly IWarpTransitionDriver _warp;
        private readonly NavigateRequest _request;
        private readonly List<string> _actualRoute = new();
        private NavigationPhase _phase;
        private LockedActionTarget? _target;
        private NavigationPlayerState? _start;
        private WorldRouteSnapshot? _snapshot;
        private IReadOnlyList<NavigationRouteEdge> _plan = Array.Empty<NavigationRouteEdge>();
        private int _edgeIndex;
        private Queue<NavigationRouteEdge> _edgeCandidates = new();
        private NavigationRouteEdge? _edge;
        private Queue<EdgeApproach> _edgeApproaches = new();
        private EdgeApproach? _approach;
        private ResolvedDestination? _destination;
        private uint _elapsedTicks;
        private int _stableFrames;
        private NavigationTile? _lastStableTile;
        private WorldPosition? _lastConfirmedPosition;
        private bool _doorSubmitted;
        private int _walkTransitionProbeTicks;
        private ContinuationStopSignal _deferredStop;

        public NavigateContinuation(
            IActionTargetResolver targets,
            ILocalNavigationDriver navigation,
            IWorldRouteSnapshotBuilder routes,
            IWarpTransitionDriver warp,
            NavigateRequest request
        )
        {
            _targets = targets;
            _navigation = navigation;
            _routes = routes;
            _warp = warp;
            _request = request.Clone();
        }

        public string Phase => _phase switch
        {
            NavigationPhase.Resolving => "resolving",
            NavigationPhase.WalkingToEdge => "walking_to_exit",
            NavigationPhase.WaitingTransition => _doorSubmitted
                ? "door_transition"
                : "walk_through_transition",
            NavigationPhase.WaitingStable => "waiting_location_stable",
            NavigationPhase.Handoff => "waiting_handoff",
            NavigationPhase.WaitingEdgeReady => "waiting_edge_ready",
            NavigationPhase.WalkingFinal => "walking",
            NavigationPhase.Facing => "facing",
            NavigationPhase.Done => "completed",
            _ => "resolving",
        };

        public uint? ProgressPercent => null;
        public bool CanCancel => _phase != NavigationPhase.WaitingStable
            && !(_phase == NavigationPhase.WaitingTransition && _doorSubmitted);

        public ContinuationStep Tick(ContinuationStopSignal signal)
        {
            _elapsedTicks++;
            if (signal != ContinuationStopSignal.None)
                return StopOrSettle(signal);

            return _phase switch
            {
                NavigationPhase.Resolving => ResolveAndStart(),
                NavigationPhase.WalkingToEdge => TickWalkingToEdge(),
                NavigationPhase.WaitingTransition => TickWaitingTransition(),
                NavigationPhase.WaitingStable => TickWaitingStable(),
                NavigationPhase.Handoff => TickHandoff(),
                NavigationPhase.WaitingEdgeReady => TickWaitingEdgeReady(),
                NavigationPhase.WalkingFinal => TickWalkingFinal(),
                _ => new ContinuationStep.Pending(),
            };
        }

        private ContinuationStep StopOrSettle(ContinuationStopSignal signal)
        {
            if (_phase == NavigationPhase.WaitingStable)
            {
                _deferredStop = signal;
                return TickWaitingStable();
            }
            if (_phase == NavigationPhase.WaitingTransition)
            {
                var current = Capture();
                if (current.IsReady
                    && _edge is not null
                    && !SameLocation(current.LocationId, _edge.SourceLocationId))
                {
                    _deferredStop = signal;
                    return AcceptTransition(current);
                }
            }
            Cleanup();
            return new ContinuationStep.Stopped();
        }

        private ContinuationStep ResolveAndStart()
        {
            var start = Capture();
            if (!start.IsReady)
                return StopAndFail(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (!start.CanMove)
                return StopAndFail(ErrorCode.NotReady, "玩家当前不能移动");

            var resolution = _request.TargetCase switch
            {
                NavigateRequest.TargetOneofCase.Position =>
                    _targets.Resolve(_request.Position, null),
                NavigateRequest.TargetOneofCase.TargetRef =>
                    _targets.Resolve(null, _request.TargetRef),
                _ => new ActionTargetResolution(
                    null,
                    Error(ErrorCode.InvalidArgument, "navigate 目标无效")
                ),
            };
            if (resolution.Error is not null || resolution.Target is null)
            {
                var error = resolution.Error ?? Error(ErrorCode.Internal, "目标解析结果无效");
                return StopAndFail(error.Code, error.Message);
            }

            _start = start;
            _target = resolution.Target;
            _actualRoute.Add(start.LocationId);
            if (SameLocation(_target.LocationId, start.LocationId))
                return BeginFinal(start);

            _snapshot = _routes.Build();
            _plan = WorldRoutePlanner.FindRoute(
                _snapshot,
                start.LocationId,
                _target.LocationId
            ) ?? Array.Empty<NavigationRouteEdge>();
            if (_plan.Count == 0)
                return StopAndFail(
                    ErrorCode.ExecutionFailed,
                    $"找不到从 '{start.LocationId}' 到 '{_target.LocationId}' 的正常 Warp 路线"
                );
            _edgeIndex = 0;
            return BeginLeg(start);
        }

        private ContinuationStep BeginLeg(NavigationPlayerState current)
        {
            var planned = _plan[_edgeIndex];
            _edgeCandidates = new Queue<NavigationRouteEdge>(_snapshot!.Edges
                .Where(edge => SameLocation(edge.SourceLocationId, planned.SourceLocationId)
                    && SameLocation(edge.TargetLocationId, planned.TargetLocationId))
                .OrderBy(edge => edge.EdgeId, StringComparer.Ordinal));
            return BeginNextEdge(current);
        }

        private ContinuationStep BeginNextEdge(NavigationPlayerState current)
        {
            if (_edgeCandidates.Count == 0)
                return StopAndFail(
                    ErrorCode.ExecutionFailed,
                    "当前路线段的所有具体出口均不可达或未触发"
                );

            _edge = _edgeCandidates.Dequeue();
            _doorSubmitted = false;
            _walkTransitionProbeTicks = 0;
            if (!SameLocation(current.LocationId, _edge.SourceLocationId))
                return StopAndFail(ErrorCode.ExecutionFailed, "路由执行位置与出口 Source 不一致");
            if (_snapshot is null
                || !_snapshot.TryGetLocation(_edge.SourceLocationId, out var sourceNode))
                return StopAndFail(ErrorCode.ExecutionFailed, "路由出口 Source 已失效");

            _edgeApproaches = new Queue<EdgeApproach>(
                _edge.Kind == NavigationEdgeKind.WalkThrough
                    ? WalkApproaches(current, sourceNode, _edge)
                    : DoorApproaches(current, sourceNode, _edge)
            );
            return StartNextEdgeApproach(current);
        }

        private ContinuationStep StartNextEdgeApproach(NavigationPlayerState current)
        {
            _navigation.Stop();
            while (_edgeApproaches.Count > 0)
            {
                var approach = _edgeApproaches.Peek();
                var start = _navigation.Start(approach.X, approach.Y);
                if (start == LocalNavigationStart.NotReady)
                {
                    _phase = NavigationPhase.WaitingEdgeReady;
                    return new ContinuationStep.Pending();
                }
                _edgeApproaches.Dequeue();
                if (start == LocalNavigationStart.NoPath)
                    continue;
                _approach = approach;
                _phase = NavigationPhase.WalkingToEdge;
                if (start == LocalNavigationStart.Started)
                    return new ContinuationStep.Pending();
                return TriggerEdge(current);
            }
            return BeginNextEdge(current);
        }

        private ContinuationStep TickWaitingEdgeReady()
        {
            var current = Capture();
            if (!current.IsReady || !current.CanMove)
                return new ContinuationStep.Pending();
            if (_edge is null)
                return StopAndFail(ErrorCode.Internal, "等待 Warp 入口时路线状态无效");
            if (!SameLocation(current.LocationId, _edge.SourceLocationId))
            {
                if (SameLocation(current.LocationId, _edge.TargetLocationId))
                    return AcceptTransition(current);
                return StopAndFail(
                    ErrorCode.ExecutionFailed,
                    "等待 Warp 入口可用期间 Location 已偏离路线"
                );
            }
            return StartNextEdgeApproach(current);
        }

        private ContinuationStep TickWalkingToEdge()
        {
            var current = Capture();
            if (!current.IsReady)
                return StopAndFail(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (_edge is null || _approach is null)
                return StopAndFail(ErrorCode.Internal, "当前出口执行状态无效");
            if (!SameLocation(current.LocationId, _edge.SourceLocationId))
                return AcceptTransition(current);
            if (current.OwnedPathActive)
                return new ContinuationStep.Pending();
            if (current.X != _approach.X || current.Y != _approach.Y)
                return StartNextEdgeApproach(current);
            return TriggerEdge(current);
        }

        private ContinuationStep TriggerEdge(NavigationPlayerState current)
        {
            if (_edge is null || _approach is null)
                return StopAndFail(ErrorCode.Internal, "当前出口执行状态无效");
            if (!SameLocation(current.LocationId, _edge.SourceLocationId))
                return AcceptTransition(current);

            _navigation.Stop();
            if (current.FacingDirection != _approach.Direction
                && !_navigation.TryFace(_approach.Direction))
                return StopAndFail(ErrorCode.NotReady, "当前状态不能面向 Warp 入口");

            if (_edge.Kind == NavigationEdgeKind.WalkThrough)
            {
                _walkTransitionProbeTicks = 0;
                if (!_warp.BeginWalkThrough(_approach.Direction))
                    return StopAndFail(ErrorCode.NotReady, "当前状态不能触发 walk-through Warp");
            }
            else
            {
                _doorSubmitted = true;
                if (!_warp.SubmitDoor(_approach.Trigger.X, _approach.Trigger.Y))
                    return StopAndFail(ErrorCode.ExecutionFailed, "门动作未被游戏接受");
            }

            _phase = NavigationPhase.WaitingTransition;
            var after = Capture();
            return after.IsReady && !SameLocation(after.LocationId, _edge.SourceLocationId)
                ? AcceptTransition(after)
                : new ContinuationStep.Pending();
        }

        private ContinuationStep TickWaitingTransition()
        {
            var current = Capture();
            if (!current.IsReady)
                return new ContinuationStep.Pending();
            if (_edge is null)
                return StopAndFail(ErrorCode.Internal, "当前出口执行状态无效");
            if (SameLocation(current.LocationId, _edge.SourceLocationId))
            {
                if (_warp.IsTransitionPending)
                    return new ContinuationStep.Pending();
                if (_edge.Kind == NavigationEdgeKind.InteractDoor)
                    return new ContinuationStep.Pending();
                _walkTransitionProbeTicks++;
                if (_walkTransitionProbeTicks >= WalkTransitionProbeTicks)
                {
                    _warp.Stop();
                    return StartNextEdgeApproach(current);
                }
                if (_approach is null)
                    return StopAndFail(ErrorCode.Internal, "walk-through 出口状态无效");
                if (!_warp.BeginWalkThrough(_approach.Direction))
                    return StopAndFail(ErrorCode.NotReady, "当前状态不能继续触发 walk-through Warp");
                var after = Capture();
                return after.IsReady && !SameLocation(after.LocationId, _edge.SourceLocationId)
                    ? AcceptTransition(after)
                    : new ContinuationStep.Pending();
            }
            return AcceptTransition(current);
        }

        private ContinuationStep AcceptTransition(NavigationPlayerState current)
        {
            Cleanup();
            if (_edge is null)
                return FailOrStop(ErrorCode.Internal, "当前出口执行状态无效");
            if (!SameLocation(current.LocationId, _edge.TargetLocationId))
            {
                return FailOrStop(
                    ErrorCode.ExecutionFailed,
                    $"Warp 进入了错误 Location '{current.LocationId}'，预期 '{_edge.TargetLocationId}'"
                );
            }

            _stableFrames = 0;
            _lastStableTile = null;
            _phase = NavigationPhase.WaitingStable;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep TickWaitingStable()
        {
            var current = Capture();
            if (!current.IsReady)
            {
                ResetStable();
                return new ContinuationStep.Pending();
            }
            if (_edge is null || !SameLocation(current.LocationId, _edge.TargetLocationId))
            {
                return FailOrStop(
                    ErrorCode.ExecutionFailed,
                    "Warp 后 Location 在稳定门禁期间发生变化"
                );
            }
            if (!current.CanMove || current.OwnedPathActive)
            {
                ResetStable();
                return new ContinuationStep.Pending();
            }

            var tile = new NavigationTile(current.X, current.Y);
            if (_lastStableTile == tile)
                _stableFrames++;
            else
            {
                _lastStableTile = tile;
                _stableFrames = 1;
            }
            if (_stableFrames < StableFramesRequired)
                return new ContinuationStep.Pending();

            if (_actualRoute.Count == 0
                || !SameLocation(_actualRoute[^1], current.LocationId))
                _actualRoute.Add(current.LocationId);

            if (_deferredStop != ContinuationStopSignal.None)
            {
                Cleanup();
                return new ContinuationStep.Stopped();
            }
            _edgeIndex++;
            _phase = NavigationPhase.Handoff;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep TickHandoff()
        {
            var current = Capture();
            if (!current.IsReady || !current.CanMove)
                return new ContinuationStep.Pending();
            if (_edge is null || !SameLocation(current.LocationId, _edge.TargetLocationId))
                return StopAndFail(
                    ErrorCode.ExecutionFailed,
                    "Warp handoff 前 Location 已偏离预期 Target"
                );
            return _edgeIndex < _plan.Count
                ? BeginLeg(current)
                : BeginFinal(current);
        }

        private ContinuationStep BeginFinal(NavigationPlayerState current)
        {
            if (_target is null || !SameLocation(current.LocationId, _target.LocationId))
                return StopAndFail(ErrorCode.ExecutionFailed, "最终 Location 与锁定目标不一致");
            foreach (var destination in CandidateDestinations(current, _target, _request))
            {
                var result = _navigation.Start(destination.X, destination.Y);
                if (result == LocalNavigationStart.NotReady)
                    return StopAndFail(ErrorCode.NotReady, "玩家当前不能开始最终导航");
                if (result == LocalNavigationStart.NoPath)
                    continue;
                _destination = destination;
                _phase = NavigationPhase.WalkingFinal;
                if (result == LocalNavigationStart.Started)
                    return new ContinuationStep.Pending();
                return CompleteAlreadyThere(current);
            }
            return StopAndFail(ErrorCode.ExecutionFailed, "目标 Tile 不可达");
        }

        private ContinuationStep TickWalkingFinal()
        {
            var current = Capture();
            if (!current.IsReady)
                return StopAndFail(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (_target is null || !SameLocation(current.LocationId, _target.LocationId))
                return StopAndFail(ErrorCode.ExecutionFailed, "最终导航期间进入了其他 Location");
            if (current.OwnedPathActive)
                return new ContinuationStep.Pending();

            var targetError = _targets.Revalidate(_target);
            if (targetError is not null)
                return StopAndFail(targetError.Code, targetError.Message);
            if (_destination is null
                || current.X != _destination.X
                || current.Y != _destination.Y)
                return StopAndFail(ErrorCode.ExecutionFailed, "寻路结束但未严格到达目标 Tile");

            _navigation.Stop();
            return CompleteFacingAndSucceed(current);
        }

        private ContinuationStep CompleteAlreadyThere(NavigationPlayerState current)
        {
            var targetError = _targets.Revalidate(_target!);
            if (targetError is not null)
                return StopAndFail(targetError.Code, targetError.Message);
            return CompleteFacingAndSucceed(current);
        }

        private ContinuationStep CompleteFacingAndSucceed(NavigationPlayerState current)
        {
            if (_request.HasFaceOnArrival)
            {
                TryDirection(_request.FaceOnArrival, out var direction);
                _phase = NavigationPhase.Facing;
                if (current.FacingDirection != direction && !_navigation.TryFace(direction))
                    return StopAndFail(ErrorCode.NotReady, "当前状态不能完成抵达朝向");
                current = Capture();
                if (current.FacingDirection != direction)
                    return StopAndFail(ErrorCode.ExecutionFailed, "抵达朝向后置条件未成立");
            }
            return Succeed(current);
        }

        private ContinuationStep Succeed(NavigationPlayerState current)
        {
            var start = _start!;
            var destination = _destination!;
            var result = new NavigateResult
            {
                Start = Position(start.LocationId, start.X, start.Y),
                Final = Position(current.LocationId, current.X, current.Y),
                ResolvedDestination = Position(
                    _target!.LocationId,
                    destination.X,
                    destination.Y
                ),
                Execution = new ExecutionStats
                {
                    ElapsedTicks = _elapsedTicks,
                    CompletionReason = destination.AlreadyThere && _actualRoute.Count == 1
                        ? "already_there"
                        : "arrived",
                },
            };
            result.RouteLocationIds.Add(_actualRoute);
            _phase = NavigationPhase.Done;
            return new ContinuationStep.Succeeded(new CapabilityResult { Navigate = result });
        }

        private ContinuationStep StopAndFail(ErrorCode code, string message)
        {
            Cleanup();
            return Fail(code, message, _lastConfirmedPosition);
        }

        private ContinuationStep FailOrStop(ErrorCode code, string message) =>
            _deferredStop == ContinuationStopSignal.None
                ? Fail(code, message, _lastConfirmedPosition)
                : new ContinuationStep.Stopped();

        private void Cleanup()
        {
            _navigation.Stop();
            _warp.Stop();
        }

        private void ResetStable()
        {
            _stableFrames = 0;
            _lastStableTile = null;
        }

        private NavigationPlayerState Capture()
        {
            var current = _navigation.Capture();
            if (current.IsReady && !string.IsNullOrWhiteSpace(current.LocationId))
                _lastConfirmedPosition = Position(current.LocationId, current.X, current.Y);
            return current;
        }

        private static ContinuationStep.Failed Fail(
            ErrorCode code,
            string message,
            WorldPosition? lastConfirmedPosition = null
        ) => new(Error(code, message, lastConfirmedPosition));

        private static Error Error(
            ErrorCode code,
            string message,
            WorldPosition? lastConfirmedPosition = null
        )
        {
            var error = new Error { Code = code, Message = message };
            if (lastConfirmedPosition is not null)
                error.Navigation = new NavigationFailureContext
                {
                    LastConfirmedPosition = lastConfirmedPosition.Clone(),
                };
            return error;
        }

        private static WorldPosition Position(string locationId, int x, int y) =>
            new() { LocationId = locationId, X = x, Y = y };

        private static bool SameLocation(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<ResolvedDestination> CandidateDestinations(
            NavigationPlayerState start,
            LockedActionTarget target,
            NavigateRequest request
        )
        {
            if (request.Arrival == ArrivalMode.Exact)
            {
                yield return new ResolvedDestination(
                    target.X,
                    target.Y,
                    start.X == target.X && start.Y == target.Y
                );
                yield break;
            }

            IEnumerable<DestinationCandidate> candidates = request.HasStandSide
                ? new[] { OffsetForSide(target.X, target.Y, request.StandSide) }
                : new[]
                {
                    OffsetForSide(target.X, target.Y, Direction.Up),
                    OffsetForSide(target.X, target.Y, Direction.Right),
                    OffsetForSide(target.X, target.Y, Direction.Down),
                    OffsetForSide(target.X, target.Y, Direction.Left),
                }
                    .OrderBy(tile => Math.Abs(tile.X - start.X) + Math.Abs(tile.Y - start.Y))
                    .ThenBy(tile => tile.Order);

            foreach (var candidate in candidates)
            {
                yield return new ResolvedDestination(
                    candidate.X,
                    candidate.Y,
                    start.X == candidate.X && start.Y == candidate.Y
                );
            }
        }

        private static IReadOnlyList<EdgeApproach> DoorApproaches(
            NavigationPlayerState current,
            NavigationLocationNode source,
            NavigationRouteEdge edge
        ) => edge.Triggers
            .SelectMany(trigger => Enumerable.Range(0, 4).Select(direction =>
            {
                var (dx, dy) = DirectionOffset(direction);
                return new EdgeApproach(
                    trigger.X - dx,
                    trigger.Y - dy,
                    direction,
                    trigger
                );
            }))
            .Where(candidate => IsInside(source, candidate.X, candidate.Y))
            .OrderBy(candidate => Math.Abs(candidate.X - current.X) + Math.Abs(candidate.Y - current.Y))
            .ThenBy(candidate => candidate.Direction)
            .ToArray();

        private static IReadOnlyList<EdgeApproach> WalkApproaches(
            NavigationPlayerState current,
            NavigationLocationNode source,
            NavigationRouteEdge edge
        )
        {
            var result = new List<EdgeApproach>();
            var seen = new HashSet<(int X, int Y, int Direction)>();
            foreach (var trigger in edge.Triggers
                .OrderBy(tile => Math.Abs(tile.X - current.X) + Math.Abs(tile.Y - current.Y))
                .ThenBy(tile => tile.X)
                .ThenBy(tile => tile.Y))
            {
                foreach (var direction in OrderedDirections(trigger, source))
                {
                    var (dx, dy) = DirectionOffset(direction);
                    var candidate = new EdgeApproach(
                        trigger.X - dx,
                        trigger.Y - dy,
                        direction,
                        trigger
                    );
                    if (IsInside(source, candidate.X, candidate.Y)
                        && seen.Add((candidate.X, candidate.Y, candidate.Direction)))
                        result.Add(candidate);
                }
            }
            return result;
        }

        private static IEnumerable<int> OrderedDirections(
            NavigationTile trigger,
            NavigationLocationNode source
        )
        {
            var ordered = new List<int>();
            if (trigger.X < 0)
                ordered.Add(3);
            if (source.Width > 0 && trigger.X >= source.Width)
                ordered.Add(1);
            if (trigger.Y < 0)
                ordered.Add(0);
            if (source.Height > 0 && trigger.Y >= source.Height)
                ordered.Add(2);
            if (trigger.Y <= 0)
                ordered.Add(0);
            if (source.Width > 0 && trigger.X >= source.Width - 1)
                ordered.Add(1);
            if (source.Height > 0 && trigger.Y >= source.Height - 1)
                ordered.Add(2);
            if (trigger.X <= 0)
                ordered.Add(3);
            ordered.AddRange(new[] { 0, 1, 2, 3 });
            return ordered.Distinct();
        }

        private static bool IsInside(NavigationLocationNode node, int x, int y) =>
            x >= 0 && y >= 0
            && (node.Width <= 0 || x < node.Width)
            && (node.Height <= 0 || y < node.Height);

        private static (int X, int Y) DirectionOffset(int direction) => direction switch
        {
            0 => (0, -1),
            1 => (1, 0),
            2 => (0, 1),
            3 => (-1, 0),
            _ => (0, 0),
        };

        private static DestinationCandidate OffsetForSide(
            int x,
            int y,
            Direction side
        ) => side switch
        {
            Direction.Up => new DestinationCandidate(x, y - 1, 0),
            Direction.Right => new DestinationCandidate(x + 1, y, 1),
            Direction.Down => new DestinationCandidate(x, y + 1, 2),
            Direction.Left => new DestinationCandidate(x - 1, y, 3),
            _ => throw new InvalidOperationException("stand_side 无效"),
        };

        private sealed record DestinationCandidate(int X, int Y, int Order);
        private sealed record ResolvedDestination(int X, int Y, bool AlreadyThere);
        private sealed record EdgeApproach(
            int X,
            int Y,
            int Direction,
            NavigationTile Trigger
        );
    }
}
