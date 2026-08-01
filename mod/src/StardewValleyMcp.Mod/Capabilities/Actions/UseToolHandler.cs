using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class UseToolHandler : ILongRunningCapabilityHandler
{
    private readonly IActionTargetResolver _targets;
    private readonly IToolUseDriver _toolUse;

    public UseToolHandler(OpaqueRefStore refs)
        : this(new ActionTargetResolver(refs), new StardewToolUseDriver())
    {
    }

    internal UseToolHandler(IActionTargetResolver targets, IToolUseDriver toolUse)
    {
        _targets = targets;
        _toolUse = toolUse;
    }

    public string Id => "use_tool";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.UseTool;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("use_tool 请求类型无效");
        if (request.UseTool.ChargeLevel > 5)
            return Invalid("charge_level 必须位于 0..5");
        return request.UseTool.TargetCase switch
        {
            UseToolRequest.TargetOneofCase.Position
                when !PublicStringPolicy.IsNonEmptyValid(
                    request.UseTool.Position?.LocationId,
                    128
                ) => Invalid("position.location_id 格式无效"),
            UseToolRequest.TargetOneofCase.TargetRef
                when !PublicStringPolicy.IsNonEmptyValid(
                    request.UseTool.TargetRef?.Value
                ) => Invalid("target_ref 格式无效"),
            UseToolRequest.TargetOneofCase.None =>
                Invalid("use_tool 必须提供 position 或 target_ref"),
            _ => null,
        };
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new UseToolContinuation(_targets, _toolUse, request.UseTool);

    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };

    private sealed class UseToolContinuation : ICommandContinuation
    {
        private const int StableTicksRequired = 2;

        private enum ToolUsePhase
        {
            Resolving,
            Facing,
            ReadyToSubmit,
            AwaitingAccepted,
            Charging,
            Settling,
            Done,
        }

        private readonly IActionTargetResolver _targets;
        private readonly IToolUseDriver _toolUse;
        private readonly UseToolRequest _request;
        private ToolUsePhase _phase;
        private LockedActionTarget? _target;
        private object? _toolIdentity;
        private SupportedToolKind _toolKind;
        private string _toolQualifiedItemId = "";
        private int _direction;
        private int _initialSwingTicker;
        private int _actualChargeLevel;
        private int _stableTicks;
        private double _energyBefore;
        private uint _elapsedTicks;
        private bool _submitted;
        private bool _accepted;
        private bool _releaseObserved;
        private bool _releaseQueued;
        private ContinuationStopSignal _deferredStop;

        public UseToolContinuation(
            IActionTargetResolver targets,
            IToolUseDriver toolUse,
            UseToolRequest request
        )
        {
            _targets = targets;
            _toolUse = toolUse;
            _request = request.Clone();
        }

        public string Phase => _phase switch
        {
            ToolUsePhase.Resolving => "resolving",
            ToolUsePhase.Facing => "facing",
            ToolUsePhase.ReadyToSubmit => "ready_to_use_tool",
            ToolUsePhase.AwaitingAccepted => "waiting_tool_accepted",
            ToolUsePhase.Charging => "charging_tool",
            ToolUsePhase.Settling => "waiting_tool_settled",
            ToolUsePhase.Done => "completed",
            _ => "resolving",
        };

        public uint? ProgressPercent => null;
        public bool CanCancel => !_submitted;

        public ContinuationStep Tick(ContinuationStopSignal signal)
        {
            _elapsedTicks++;
            if (signal != ContinuationStopSignal.None)
                return StopOrSettle(signal);

            return _phase switch
            {
                ToolUsePhase.Resolving => Resolve(),
                ToolUsePhase.Facing => Face(),
                ToolUsePhase.ReadyToSubmit => Submit(),
                ToolUsePhase.AwaitingAccepted => AwaitAccepted(),
                ToolUsePhase.Charging => ChargeOrRelease(),
                ToolUsePhase.Settling => Settle(),
                _ => new ContinuationStep.Pending(),
            };
        }

        private ContinuationStep Resolve()
        {
            var resolution = _request.TargetCase switch
            {
                UseToolRequest.TargetOneofCase.Position =>
                    _targets.Resolve(_request.Position, null),
                UseToolRequest.TargetOneofCase.TargetRef =>
                    _targets.Resolve(null, _request.TargetRef),
                _ => new ActionTargetResolution(
                    null,
                    Error(ErrorCode.InvalidArgument, "use_tool 目标无效")
                ),
            };
            if (resolution.Error is not null || resolution.Target is null)
                return Fail(resolution.Error ?? Error(ErrorCode.Internal, "目标解析失败"));

            var current = _toolUse.Observe();
            var precondition = ValidatePreconditions(resolution.Target, current);
            if (precondition is not null)
                return Fail(precondition);

            _target = resolution.Target;
            _toolIdentity = current.ToolIdentity;
            _toolKind = current.ToolKind;
            _toolQualifiedItemId = current.ToolQualifiedItemId;
            _initialSwingTicker = current.SwingTicker;
            _energyBefore = current.Energy;
            _direction = DirectionFrom(
                current.PlayerX,
                current.PlayerY,
                _target.X,
                _target.Y
            );
            _phase = ToolUsePhase.Facing;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep Face()
        {
            var error = RevalidateBeforeSubmit(out var current);
            if (error is not null)
                return Fail(error);
            if (!_toolUse.TryFace(_direction, _toolIdentity!))
                return Fail(Error(ErrorCode.NotReady, "玩家当前不能面朝工具目标"));

            current = _toolUse.Observe();
            if (!SameTool(current) || current.FacingDirection != _direction)
                return Fail(Error(ErrorCode.ExecutionFailed, "工具目标朝向未就绪"));
            _phase = ToolUsePhase.ReadyToSubmit;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep Submit()
        {
            var error = RevalidateBeforeSubmit(out var current);
            if (error is not null)
                return Fail(error);
            if (current.FacingDirection != _direction)
                return Fail(Error(ErrorCode.ExecutionFailed, "提交前工具目标朝向已改变"));

            _submitted = true;
            if (!_toolUse.BeginUse(_toolIdentity!, _target!.X, _target.Y))
                return Fail(Error(ErrorCode.ExecutionFailed, "游戏拒绝提交工具动作"));
            _phase = ToolUsePhase.AwaitingAccepted;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep AwaitAccepted()
        {
            var current = ObserveAfterSubmit();
            if (current is null)
                return Fail(Error(ErrorCode.ExecutionFailed, "工具在动作接受前被替换"));
            if (!TryAccept(current))
                return new ContinuationStep.Pending();

            if (UsesExplicitRelease(current))
            {
                _phase = ToolUsePhase.Charging;
                return ChargeOrRelease(current);
            }

            _releaseObserved = true;
            _phase = ToolUsePhase.Settling;
            return Settle(current);
        }

        private ContinuationStep ChargeOrRelease() =>
            ObserveAfterSubmit() is { } current
                ? ChargeOrRelease(current)
                : Fail(Error(ErrorCode.ExecutionFailed, "工具在蓄力阶段被替换"));

        private ContinuationStep ChargeOrRelease(ToolUseObservation current)
        {
            if (!current.IsReady)
                return Fail(Error(ErrorCode.NotReady, "工具动作期间游戏世界不可观察"));

            var requested = (int)_request.ChargeLevel;
            TrackActualCharge(current);
            if (!current.UsingTool || !current.CanReleaseTool)
            {
                if (current.SwingTicker != _initialSwingTicker || IsStable(current))
                {
                    _releaseObserved = true;
                    _phase = ToolUsePhase.Settling;
                    return Settle(current);
                }
                return Fail(Error(ErrorCode.ExecutionFailed, "工具在达到请求蓄力前失去释放状态"));
            }

            if (current.ToolPower < requested)
            {
                if (!_toolUse.IncreaseCharge(_toolIdentity!))
                    return Fail(Error(ErrorCode.ExecutionFailed, "游戏拒绝增加工具蓄力"));
                return new ContinuationStep.Pending();
            }
            if (current.ToolPower > requested)
                return Fail(Error(ErrorCode.ExecutionFailed, "工具蓄力超过请求等级"));

            _actualChargeLevel = current.ToolPower;
            if (!_releaseQueued)
            {
                if (!_toolUse.Release(_toolIdentity!))
                    return Fail(Error(ErrorCode.ExecutionFailed, "游戏拒绝释放工具动作"));
                _releaseQueued = true;
            }
            _releaseObserved = true;
            _phase = ToolUsePhase.Settling;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep Settle() =>
            ObserveAfterSubmit() is { } current
                ? Settle(current)
                : Fail(Error(ErrorCode.ExecutionFailed, "工具在动作收敛前被替换"));

        private ContinuationStep Settle(ToolUseObservation current)
        {
            TrackActualCharge(current);
            if (!_accepted || !_releaseObserved || !IsStable(current))
            {
                _stableTicks = 0;
                return new ContinuationStep.Pending();
            }

            _stableTicks++;
            if (_stableTicks < StableTicksRequired)
                return new ContinuationStep.Pending();
            if (_deferredStop != ContinuationStopSignal.None)
                return new ContinuationStep.Stopped();

            _phase = ToolUsePhase.Done;
            return Succeed(current);
        }

        private ContinuationStep StopOrSettle(ContinuationStopSignal signal)
        {
            if (!_submitted)
                return new ContinuationStep.Stopped();
            _deferredStop = signal;

            var current = ObserveAfterSubmit();
            if (current is null)
                return new ContinuationStep.Stopped();
            TryAccept(current);

            if (_toolKind is SupportedToolKind.Hoe or SupportedToolKind.WateringCan
                && current.UsingTool
                && current.CanReleaseTool
                && !_releaseQueued)
            {
                if (_toolUse.Release(_toolIdentity!))
                {
                    _releaseQueued = true;
                    _releaseObserved = true;
                }
            }
            else if (_accepted && !UsesExplicitRelease(current))
            {
                _releaseObserved = true;
            }

            _phase = ToolUsePhase.Settling;
            return Settle(current);
        }

        private bool TryAccept(ToolUseObservation current)
        {
            if (_accepted)
                return true;
            _accepted = _toolKind switch
            {
                SupportedToolKind.Axe or SupportedToolKind.Pickaxe =>
                    current.SwingTicker != _initialSwingTicker,
                SupportedToolKind.Scythe => current.UsingTool && !current.CanMove,
                SupportedToolKind.Hoe or SupportedToolKind.WateringCan =>
                    current.SwingTicker != _initialSwingTicker
                    || (current.UsingTool && !current.CanMove),
                _ => false,
            };
            return _accepted;
        }

        private Error? RevalidateBeforeSubmit(out ToolUseObservation current)
        {
            current = _toolUse.Observe();
            var targetError = _targets.Revalidate(_target!);
            if (targetError is not null)
                return targetError;
            if (!current.IsReady)
                return Error(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (!SameLocation(current.LocationId, _target!.LocationId))
                return Error(ErrorCode.ExecutionFailed, "玩家在提交前离开目标 Location");
            if (!SameTool(current))
                return Error(ErrorCode.ExecutionFailed, "工具在提交前被替换");
            if (!current.CanSubmit)
                return Error(ErrorCode.NotReady, "玩家当前不能安全使用工具");
            return null;
        }

        private Error? ValidatePreconditions(
            LockedActionTarget target,
            ToolUseObservation current
        )
        {
            if (!current.IsReady)
                return Error(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (!SameLocation(current.LocationId, target.LocationId))
                return Error(ErrorCode.InvalidArgument, "use_tool 目标必须位于玩家当前 Location");
            if (Math.Abs(target.X - current.PlayerX) + Math.Abs(target.Y - current.PlayerY) != 1)
                return Error(ErrorCode.OutOfRange, "use_tool 目标必须与玩家 cardinal-adjacent");
            if (current.ToolIdentity is null)
                return Error(ErrorCode.NotReady, "请先装备一个受支持工具");
            if (current.ToolKind == SupportedToolKind.Unsupported)
                return Error(ErrorCode.InvalidArgument, "当前工具类型不受 use_tool 支持");
            if (_request.ChargeLevel > current.MaxChargeLevel)
                return Error(ErrorCode.InvalidArgument, "charge_level 超过当前工具实际支持等级");
            if (current.ToolKind is not (SupportedToolKind.Hoe or SupportedToolKind.WateringCan)
                && _request.ChargeLevel != 0)
                return Error(ErrorCode.InvalidArgument, "当前工具只支持 charge_level=0");
            if (!current.CanSubmit)
                return Error(ErrorCode.NotReady, "玩家当前不能安全使用工具");
            return null;
        }

        private ToolUseObservation? ObserveAfterSubmit()
        {
            var current = _toolUse.Observe();
            return current.IsReady
                && SameLocation(current.LocationId, _target!.LocationId)
                && SameTool(current)
                    ? current
                    : null;
        }

        private bool SameTool(ToolUseObservation current) =>
            ReferenceEquals(current.ToolIdentity, _toolIdentity)
            && current.ToolKind == _toolKind
            && string.Equals(
                current.ToolQualifiedItemId,
                _toolQualifiedItemId,
                StringComparison.Ordinal
            );

        private void TrackActualCharge(ToolUseObservation current)
        {
            if (_toolKind is SupportedToolKind.Hoe or SupportedToolKind.WateringCan)
                _actualChargeLevel = Math.Max(_actualChargeLevel, current.ToolPower);
        }

        private static bool UsesExplicitRelease(ToolUseObservation current) =>
            current.ToolKind is SupportedToolKind.Hoe or SupportedToolKind.WateringCan
            && current.UsingTool
            && current.CanReleaseTool;

        private bool IsStable(ToolUseObservation current) =>
            !current.UsingTool
            && (_toolKind == SupportedToolKind.Scythe || !current.CanReleaseTool)
            && current.CanMove
            && !current.PauseForSingleAnimation
            && current.LastClickIsZero;

        private ContinuationStep.Succeeded Succeed(ToolUseObservation after) =>
            new(new CapabilityResult
            {
                UseTool = new UseToolResult
                {
                    Target = new WorldPosition
                    {
                        LocationId = _target!.LocationId,
                        X = _target.X,
                        Y = _target.Y,
                    },
                    ToolQualifiedItemId = _toolQualifiedItemId,
                    ChargeLevel = (uint)_actualChargeLevel,
                    Energy = new ResourceChange
                    {
                        Before = _energyBefore,
                        After = after.Energy,
                        Delta = after.Energy - _energyBefore,
                    },
                    Execution = new ExecutionStats
                    {
                        ElapsedTicks = _elapsedTicks,
                        CompletionReason = "tool_action_settled",
                    },
                },
            });

        private static int DirectionFrom(int fromX, int fromY, int toX, int toY)
        {
            if (toY < fromY)
                return 0;
            if (toX > fromX)
                return 1;
            if (toY > fromY)
                return 2;
            return 3;
        }

        private static bool SameLocation(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static ContinuationStep.Failed Fail(Error error) => new(error);

        private static Error Error(ErrorCode code, string message) => new()
        {
            Code = code,
            Message = message,
        };
    }
}
