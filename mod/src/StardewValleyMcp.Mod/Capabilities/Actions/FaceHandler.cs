using StardewModdingAPI;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal interface IFaceGameApi
{
    bool IsReady { get; }
    bool CanFace { get; }
    int FacingDirection { get; }
    void FaceDirection(int direction);
}

internal sealed class FaceHandler : ILongRunningCapabilityHandler
{
    private readonly IFaceGameApi _game;

    public FaceHandler() : this(new StardewFaceGameApi()) { }

    internal FaceHandler(IFaceGameApi game) => _game = game;

    public string Id => "face";
    public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.Face;

    public Error? Validate(CommandRequest request) => request.OperationCase != Operation || !TryMapDirection(request.Face.Direction, out _)
        ? new Error { Code = ErrorCode.InvalidArgument, Message = "face direction 无效" }
        : null;

    public ICommandContinuation Start(string commandId, CommandRequest request)
    {
        TryMapDirection(request.Face.Direction, out var direction);
        return new FaceContinuation(commandId, request.Face.Direction, direction, _game);
    }

    private static bool TryMapDirection(Direction direction, out int value)
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

    private sealed class FaceContinuation : ICommandContinuation
    {
        private readonly string _commandId;
        private readonly Direction _requestedDirection;
        private readonly int _requestedFacing;
        private readonly IFaceGameApi _game;
        private bool _submitted;

        public FaceContinuation(string commandId, Direction requestedDirection, int requestedFacing, IFaceGameApi game)
        {
            _commandId = commandId;
            _requestedDirection = requestedDirection;
            _requestedFacing = requestedFacing;
            _game = game;
        }

        public string Phase => _submitted ? "observing_direction" : "ready_to_face";
        public uint? ProgressPercent => null;
        public bool CanCancel => !_submitted;

        public ContinuationStep Tick(ContinuationStopSignal signal)
        {
            if (signal != ContinuationStopSignal.None)
                return new ContinuationStep.Stopped();
            if (!_game.IsReady || !_game.CanFace)
                return new ContinuationStep.Failed(new Error { Code = ErrorCode.NotReady, Message = "当前状态不能改变朝向" });
            if (!_submitted && _game.FacingDirection == _requestedFacing)
                return Succeeded(changed: false);
            if (!_submitted)
            {
                _game.FaceDirection(_requestedFacing);
                _submitted = true;
            }
            return _game.FacingDirection == _requestedFacing
                ? Succeeded(changed: true)
                : new ContinuationStep.Pending();
        }

        private ContinuationStep.Succeeded Succeeded(bool changed) => new(new CapabilityResult
        {
            Face = new FaceResult { FinalDirection = _requestedDirection, Changed = changed },
        });
    }
}

internal sealed class StardewFaceGameApi : IFaceGameApi
{
    public bool IsReady => Context.IsWorldReady && Game1.player is not null;
    public bool CanFace => IsReady && Game1.activeClickableMenu is null && Game1.player.CanMove && !Game1.player.UsingTool;
    public int FacingDirection => Game1.player?.FacingDirection ?? -1;
    public void FaceDirection(int direction) => Game1.player.faceDirection(direction);
}
