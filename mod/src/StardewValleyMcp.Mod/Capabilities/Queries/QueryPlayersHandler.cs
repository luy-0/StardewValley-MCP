using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class QueryPlayersHandler : IImmediateCapabilityHandler
{
    private readonly IPlayerRosterReader _reader;

    public QueryPlayersHandler(IPlayerRosterReader reader)
    {
        _reader = reader;
    }

    public string Id => "query_players";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.QueryPlayers;

    public Error? Validate(CommandRequest request) => request.OperationCase == Operation
        ? null
        : new Error { Code = ErrorCode.InvalidArgument, Message = "query_players 请求类型无效" };

    public CommandEvent Execute(string commandId, CommandRequest request)
    {
        if (!_reader.IsWorldReady)
            return Failed(commandId, ErrorCode.NotReady, "世界尚未就绪", "not_ready");

        try
        {
            var snapshot = PlayerPresenceProjector.Project(_reader.Capture());
            return new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.Succeeded,
                Phase = "completed",
                ProgressPercent = 100,
                Result = new CapabilityResult
                {
                    QueryPlayers = new QueryPlayersResult { Snapshot = snapshot },
                },
            };
        }
        catch
        {
            return Failed(
                commandId,
                ErrorCode.ExecutionFailed,
                "玩家事实不可读",
                "player_projection_failed"
            );
        }
    }

    internal static CommandEvent Failed(
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
}
