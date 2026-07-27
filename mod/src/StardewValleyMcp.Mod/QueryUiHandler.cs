using StardewModdingAPI;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class QueryUiHandler : ICapabilityHandler
{
    private readonly OpaqueRefStore _refs;

    public QueryUiHandler(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    public string Id => "query_ui";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.QueryUi;
    public Error? Validate(CommandRequest request) => QueryUiRequestValidator.Validate(request);

    public CommandEvent Execute(string commandId, CommandRequest request)
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return Failed(commandId, ErrorCode.NotReady, "世界尚未就绪", "not_ready");

        try
        {
            var activeMenu = Game1.activeClickableMenu;
            var result = activeMenu is null
                ? UiProjector.ProjectNoMenu(_refs)
                : UiRuntimeProjector.Project(activeMenu, player, _refs);
            return Succeeded(commandId, result);
        }
        catch (UiProjectionException)
        {
            return Failed(
                commandId,
                ErrorCode.ExecutionFailed,
                "UI 基本事实不可读",
                "ui_projection_failed"
            );
        }
        catch
        {
            return Failed(commandId, ErrorCode.Internal, "UI 事实不可读", "internal");
        }
    }

    internal static CommandEvent Succeeded(string commandId, QueryUiResult result) => new()
    {
        CommandId = commandId,
        State = CommandState.Succeeded,
        Phase = "completed",
        ProgressPercent = 100,
        Result = new CapabilityResult { QueryUi = result },
    };

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

internal static class QueryUiRequestValidator
{
    public static Error? Validate(CommandRequest request) =>
        request.OperationCase == CommandRequest.OperationOneofCase.QueryUi
            ? null
            : new Error
            {
                Code = ErrorCode.InvalidArgument,
                Message = "query_ui 请求类型无效",
            };
}
