using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class InteractHandler : ILongRunningCapabilityHandler
{
    private readonly IActionTargetResolver _targets;
    private readonly IInteractionDriver _interaction;

    public InteractHandler(OpaqueRefStore refs)
        : this(new ActionTargetResolver(refs), new StardewInteractionDriver())
    {
    }

    internal InteractHandler(
        IActionTargetResolver targets,
        IInteractionDriver interaction
    )
    {
        _targets = targets;
        _interaction = interaction;
    }

    public string Id => "interact";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.Interact;

    public Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != Operation)
            return Invalid("interact 请求类型无效");
        return request.Interact.TargetCase switch
        {
            InteractRequest.TargetOneofCase.Position
                when !PublicStringPolicy.IsNonEmptyValid(
                    request.Interact.Position?.LocationId,
                    128
                ) => Invalid("position.location_id 格式无效"),
            InteractRequest.TargetOneofCase.TargetRef
                when !PublicStringPolicy.IsNonEmptyValid(
                    request.Interact.TargetRef?.Value
                ) => Invalid("target_ref 格式无效"),
            InteractRequest.TargetOneofCase.None =>
                Invalid("interact 必须提供 position 或 target_ref"),
            _ => null,
        };
    }

    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new InteractContinuation(_targets, _interaction, request.Interact);

    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };

    private sealed class InteractContinuation : ICommandContinuation
    {
        internal const int MicroMoveLimit = 15;
        internal const int EffectWaitLimit = 45;

        private enum InteractPhase
        {
            Resolving,
            Facing,
            Aligning,
            ReadyToSubmit,
            Observing,
            Done,
        }

        private readonly IActionTargetResolver _targets;
        private readonly IInteractionDriver _interaction;
        private readonly InteractRequest _request;
        private InteractPhase _phase;
        private LockedActionTarget? _target;
        private InteractionObservation? _before;
        private int _direction;
        private int _startX;
        private int _startY;
        private int _microMoveTicks;
        private int _effectWaitTicks;
        private uint _elapsedTicks;
        private bool _microMoveStarted;
        private bool _submitted;

        public InteractContinuation(
            IActionTargetResolver targets,
            IInteractionDriver interaction,
            InteractRequest request
        )
        {
            _targets = targets;
            _interaction = interaction;
            _request = request.Clone();
        }

        public string Phase => _phase switch
        {
            InteractPhase.Resolving => "resolving",
            InteractPhase.Facing => "facing",
            InteractPhase.Aligning => "aligning_grab_tile",
            InteractPhase.ReadyToSubmit => "ready_to_interact",
            InteractPhase.Observing => "observing_effect",
            InteractPhase.Done => "completed",
            _ => "resolving",
        };

        public uint? ProgressPercent => null;
        public bool CanCancel => !_submitted;

        public ContinuationStep Tick(ContinuationStopSignal signal)
        {
            _elapsedTicks++;
            if (signal != ContinuationStopSignal.None)
            {
                _interaction.StopMicroMove();
                return new ContinuationStep.Stopped();
            }

            return _phase switch
            {
                InteractPhase.Resolving => Resolve(),
                InteractPhase.Facing => Face(),
                InteractPhase.Aligning => Align(),
                InteractPhase.ReadyToSubmit => Submit(),
                InteractPhase.Observing => ObserveEffect(),
                _ => new ContinuationStep.Pending(),
            };
        }

        private ContinuationStep Resolve()
        {
            var resolution = _request.TargetCase switch
            {
                InteractRequest.TargetOneofCase.Position =>
                    _targets.Resolve(_request.Position, null),
                InteractRequest.TargetOneofCase.TargetRef =>
                    _targets.Resolve(null, _request.TargetRef),
                _ => new ActionTargetResolution(
                    null,
                    Error(ErrorCode.InvalidArgument, "interact 目标无效")
                ),
            };
            if (resolution.Error is not null || resolution.Target is null)
                return Fail(resolution.Error ?? Error(ErrorCode.Internal, "目标解析失败"));

            var current = _interaction.Observe(resolution.Target.X, resolution.Target.Y);
            var precondition = ValidatePreconditions(resolution.Target, current);
            if (precondition is not null)
                return Fail(precondition);

            _target = resolution.Target;
            _startX = current.PlayerX;
            _startY = current.PlayerY;
            _direction = DirectionFrom(
                current.PlayerX,
                current.PlayerY,
                resolution.Target.X,
                resolution.Target.Y
            );
            _phase = InteractPhase.Facing;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep Face()
        {
            var error = RevalidateBeforeSubmit();
            if (error is not null)
                return Fail(error);
            if (!_interaction.TryFace(_direction))
                return Fail(Error(ErrorCode.NotReady, "玩家当前不能面朝交互目标"));

            var current = ObserveTarget();
            if (GrabAligned(current))
            {
                _phase = InteractPhase.ReadyToSubmit;
                return new ContinuationStep.Pending();
            }
            if (!_interaction.BeginMicroMove(_direction))
                return Fail(Error(ErrorCode.NotReady, "玩家当前不能进行交互对齐"));
            _microMoveStarted = true;
            _phase = InteractPhase.Aligning;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep Align()
        {
            var current = ObserveTarget();
            if (!current.IsReady)
                return Fail(Error(ErrorCode.NotReady, "游戏世界尚未就绪"));
            if (!SameLocation(current.LocationId, _target!.LocationId))
                return Fail(Error(ErrorCode.ExecutionFailed, "玩家在交互前离开目标 Location"));
            if (current.PlayerX != _startX || current.PlayerY != _startY)
                return Fail(Error(ErrorCode.ExecutionFailed, "交互对齐越过了起始 Tile"));
            if (GrabAligned(current))
            {
                _interaction.StopMicroMove();
                _microMoveStarted = false;
                _phase = InteractPhase.ReadyToSubmit;
                return new ContinuationStep.Pending();
            }
            _microMoveTicks++;
            if (_microMoveTicks >= MicroMoveLimit)
                return Fail(Error(ErrorCode.ExecutionFailed, "无法将 Grab Tile 对齐目标"));
            if (!_interaction.BeginMicroMove(_direction))
                return Fail(Error(ErrorCode.NotReady, "玩家当前不能继续交互对齐"));
            return new ContinuationStep.Pending();
        }

        private ContinuationStep Submit()
        {
            var error = RevalidateBeforeSubmit();
            if (error is not null)
                return Fail(error);
            var current = ObserveTarget();
            var precondition = ValidatePreconditions(_target!, current);
            if (precondition is not null)
                return Fail(precondition);
            if (!GrabAligned(current) || current.FacingDirection != _direction)
                return Fail(Error(ErrorCode.ExecutionFailed, "提交前交互目标未正确对齐"));

            _before = current;
            _submitted = true;
            try
            {
                _interaction.Submit(_target!.X, _target.Y);
            }
            catch
            {
                return Fail(Error(ErrorCode.ExecutionFailed, "游戏拒绝提交交互动作"));
            }
            _phase = InteractPhase.Observing;
            return new ContinuationStep.Pending();
        }

        private ContinuationStep ObserveEffect()
        {
            var after = ObserveTarget();
            if (!after.IsReady)
                return Fail(Error(ErrorCode.NotReady, "交互后游戏世界不可观察"));

            var reason = CompletionReason(_before!, after);
            if (reason is not null)
            {
                _phase = InteractPhase.Done;
                return Succeed(after, reason);
            }

            _effectWaitTicks++;
            if (_effectWaitTicks >= EffectWaitLimit)
                return Fail(Error(ErrorCode.ExecutionFailed, "交互未产生可关联效果"));
            return new ContinuationStep.Pending();
        }

        private Error? RevalidateBeforeSubmit()
        {
            var error = _targets.Revalidate(_target!);
            if (error is not null)
                return error;
            var current = ObserveTarget();
            if (!current.IsReady)
                return Error(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (!SameLocation(current.LocationId, _target!.LocationId))
                return Error(ErrorCode.ExecutionFailed, "玩家不在目标 Location");
            return null;
        }

        private InteractionObservation ObserveTarget() =>
            _interaction.Observe(_target!.X, _target.Y);

        private static Error? ValidatePreconditions(
            LockedActionTarget target,
            InteractionObservation current
        )
        {
            if (!current.IsReady)
                return Error(ErrorCode.NotReady, "游戏世界尚未就绪");
            if (!SameLocation(current.LocationId, target.LocationId))
                return Error(ErrorCode.InvalidArgument, "interact 目标必须位于玩家当前 Location");
            if (Math.Abs(target.X - current.PlayerX) + Math.Abs(target.Y - current.PlayerY) != 1)
                return Error(ErrorCode.OutOfRange, "interact 目标必须与玩家 cardinal-adjacent");
            if (!current.HeldItemAllowed)
                return Error(ErrorCode.NotReady, "请先清空手持非工具物品");
            if (!current.CanAct)
                return Error(ErrorCode.NotReady, "玩家当前不能安全交互");
            return null;
        }

        private ContinuationStep.Succeeded Succeed(
            InteractionObservation after,
            string reason
        ) => new(new CapabilityResult
        {
            Interact = new InteractResult
            {
                Target = new WorldPosition
                {
                    LocationId = _target!.LocationId,
                    X = _target.X,
                    Y = _target.Y,
                },
                Energy = new ResourceChange
                {
                    Before = _before!.Energy,
                    After = after.Energy,
                    Delta = after.Energy - _before.Energy,
                },
                Execution = new ExecutionStats
                {
                    ElapsedTicks = _elapsedTicks,
                    CompletionReason = reason,
                },
            },
        });

        private ContinuationStep Fail(Error error)
        {
            if (_microMoveStarted)
            {
                _interaction.StopMicroMove();
                _microMoveStarted = false;
            }
            return new ContinuationStep.Failed(error);
        }

        private static string? CompletionReason(
            InteractionObservation before,
            InteractionObservation after
        )
        {
            if (!SameLocation(before.LocationId, after.LocationId))
                return "location_changed";
            if (before.MenuState != after.MenuState && after.MenuState != "none")
                return after.MenuState.Contains("DialogueBox", StringComparison.Ordinal)
                    ? "dialogue_opened"
                    : "menu_opened";
            if (before.InventoryState != after.InventoryState)
                return "inventory_changed";
            if (before.RelationshipState != after.RelationshipState)
                return "relationship_changed";
            if (before.TargetState != after.TargetState)
                return "target_state_changed";
            return null;
        }

        private bool GrabAligned(InteractionObservation current) =>
            current.GrabX == _target!.X && current.GrabY == _target.Y;

        private static int DirectionFrom(int startX, int startY, int targetX, int targetY)
        {
            if (targetY < startY)
                return 0;
            if (targetX > startX)
                return 1;
            if (targetY > startY)
                return 2;
            return 3;
        }

        private static bool SameLocation(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static Error Error(ErrorCode code, string message) => new()
        {
            Code = code,
            Message = message,
        };
    }
}
