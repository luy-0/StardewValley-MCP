using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class NavigateHandler : ILongRunningCapabilityHandler
{
    private readonly IActionTargetResolver _targets;
    private readonly ILocalNavigationDriver _navigation;

    public NavigateHandler(OpaqueRefStore refs)
        : this(new ActionTargetResolver(refs), new StardewLocalNavigationDriver())
    {
    }

    internal NavigateHandler(
        IActionTargetResolver targets,
        ILocalNavigationDriver navigation
    )
    {
        _targets = targets;
        _navigation = navigation;
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
        private readonly IActionTargetResolver _targets;
        private readonly ILocalNavigationDriver _navigation;
        private readonly NavigateRequest _request;
        private LockedActionTarget? _target;
        private NavigationPlayerState? _start;
        private ResolvedDestination? _destination;
        private uint _elapsedTicks;
        private bool _facing;

        public NavigateContinuation(
            IActionTargetResolver targets,
            ILocalNavigationDriver navigation,
            NavigateRequest request
        )
        {
            _targets = targets;
            _navigation = navigation;
            _request = request.Clone();
        }

        public string Phase => _target is null
            ? "resolving"
            : _destination is null
                ? "selecting_destination"
                : _facing
                    ? "facing"
                    : "walking";

        public uint? ProgressPercent => null;
        public bool CanCancel => true;

        public ContinuationStep Tick(ContinuationStopSignal signal)
        {
            _elapsedTicks++;
            if (signal != ContinuationStopSignal.None)
            {
                _navigation.Stop();
                return new ContinuationStep.Stopped();
            }

            if (_target is null)
                return ResolveAndStart();

            var current = _navigation.Capture();
            if (!current.IsReady)
                return StopAndFail(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (!string.Equals(
                current.LocationId,
                _start!.LocationId,
                StringComparison.OrdinalIgnoreCase
            ))
                return StopAndFail(ErrorCode.ExecutionFailed, "同图导航期间进入了其他 Location");
            if (current.OwnedPathActive)
                return new ContinuationStep.Pending();

            var targetError = _targets.Revalidate(_target);
            if (targetError is not null)
                return StopAndFail(targetError.Code, targetError.Message);
            if (current.X != _destination!.X || current.Y != _destination.Y)
                return StopAndFail(ErrorCode.ExecutionFailed, "寻路结束但未严格到达目标 Tile");

            _navigation.Stop();
            if (_request.HasFaceOnArrival)
            {
                TryDirection(_request.FaceOnArrival, out var direction);
                _facing = true;
                if (current.FacingDirection != direction && !_navigation.TryFace(direction))
                    return Fail(ErrorCode.NotReady, "当前状态不能完成抵达朝向");
                current = _navigation.Capture();
                if (current.FacingDirection != direction)
                    return Fail(ErrorCode.ExecutionFailed, "抵达朝向后置条件未成立");
            }
            return Succeed(current);
        }

        private ContinuationStep ResolveAndStart()
        {
            var start = _navigation.Capture();
            if (!start.IsReady)
                return Fail(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (!start.CanMove)
                return Fail(ErrorCode.NotReady, "玩家当前不能移动");

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
                return new ContinuationStep.Failed(
                    resolution.Error ?? Error(ErrorCode.Internal, "目标解析结果无效")
                );
            if (!string.Equals(
                resolution.Target.LocationId,
                start.LocationId,
                StringComparison.OrdinalIgnoreCase
            ))
                return Fail(ErrorCode.NotReady, "当前阶段仅支持同一 Location 内导航");

            _start = start;
            _target = resolution.Target;
            foreach (var destination in CandidateDestinations(start, _target, _request))
            {
                var result = _navigation.Start(destination.X, destination.Y);
                if (result == LocalNavigationStart.NotReady)
                    return Fail(ErrorCode.NotReady, "玩家当前不能开始导航");
                if (result == LocalNavigationStart.NoPath)
                    continue;
                _destination = destination;
                if (result == LocalNavigationStart.Started)
                    return new ContinuationStep.Pending();
                return CompleteAlreadyThere(start);
            }
            return Fail(ErrorCode.ExecutionFailed, "目标 Tile 不可达");
        }

        private ContinuationStep CompleteAlreadyThere(NavigationPlayerState current)
        {
            var targetError = _targets.Revalidate(_target!);
            if (targetError is not null)
                return new ContinuationStep.Failed(targetError);
            if (_request.HasFaceOnArrival)
            {
                TryDirection(_request.FaceOnArrival, out var direction);
                _facing = true;
                if (current.FacingDirection != direction && !_navigation.TryFace(direction))
                    return Fail(ErrorCode.NotReady, "当前状态不能完成抵达朝向");
                current = _navigation.Capture();
                if (current.FacingDirection != direction)
                    return Fail(ErrorCode.ExecutionFailed, "抵达朝向后置条件未成立");
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
                    start.LocationId,
                    destination.X,
                    destination.Y
                ),
                Execution = new ExecutionStats
                {
                    ElapsedTicks = _elapsedTicks,
                    CompletionReason = destination.AlreadyThere
                        ? "already_there"
                        : "arrived",
                },
            };
            result.RouteLocationIds.Add(start.LocationId);
            return new ContinuationStep.Succeeded(new CapabilityResult { Navigate = result });
        }

        private ContinuationStep StopAndFail(ErrorCode code, string message)
        {
            _navigation.Stop();
            return Fail(code, message);
        }

        private static ContinuationStep.Failed Fail(ErrorCode code, string message) =>
            new(Error(code, message));

        private static Error Error(ErrorCode code, string message) =>
            new() { Code = code, Message = message };

        private static WorldPosition Position(string locationId, int x, int y) =>
            new() { LocationId = locationId, X = x, Y = y };

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
                yield return new ResolvedDestination(
                    candidate.X,
                    candidate.Y,
                    start.X == candidate.X && start.Y == candidate.Y
                );
        }

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
    }
}
