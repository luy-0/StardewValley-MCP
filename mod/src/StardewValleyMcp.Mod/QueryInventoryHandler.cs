using StardewModdingAPI;
using StardewValley;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class QueryInventoryHandler : ICapabilityHandler
{
    private static readonly IReadOnlySet<RefKind> ContainerRefKinds = new HashSet<RefKind>
    {
        RefKind.WorldEntity,
        RefKind.Container,
    };

    private readonly OpaqueRefStore _refs;

    public QueryInventoryHandler(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    public string Id => "query_inventory";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.QueryInventory;
    public Error? Validate(CommandRequest request) =>
        QueryInventoryRequestValidator.Validate(request);

    public CommandEvent Execute(string commandId, CommandRequest request)
    {
        if (!Context.IsWorldReady || Game1.player is not { } player)
            return Failed(commandId, ErrorCode.NotReady, "世界尚未就绪", "not_ready");

        try
        {
            var query = request.QueryInventory;
            ReadableInventoryView view;
            if (SelectsPlayerInventory(query))
            {
                view = InventoryViewResolver.CreatePlayer(player);
            }
            else
            {
                var resolution = _refs.Resolve(
                    query.ContainerRef,
                    ContainerRefKinds,
                    out var resolved
                );
                if (resolution.Status != RefStatus.Resolved || resolved is null)
                    return FailedFromResolution(commandId, resolution);
                view = InventoryViewResolver.CreateContainer(resolved, player, _refs);
            }

            return Succeeded(
                commandId,
                InventoryProjector.Project(
                    view,
                    _refs,
                    query.IncludeEmptySlots
                )
            );
        }
        catch (InventoryViewException error)
        {
            return Failed(commandId, error.Code, error.Message, error.Phase);
        }
        catch
        {
            return Failed(commandId, ErrorCode.Internal, "库存事实不可读", "internal");
        }
    }

    internal static bool SelectsPlayerInventory(QueryInventoryRequest query) =>
        query.ContainerCase is QueryInventoryRequest.ContainerOneofCase.None
            or QueryInventoryRequest.ContainerOneofCase.PlayerInventory;

    internal static CommandEvent Succeeded(string commandId, InventorySnapshot snapshot) =>
        new()
        {
            CommandId = commandId,
            State = CommandState.Succeeded,
            Phase = "completed",
            ProgressPercent = 100,
            Result = new CapabilityResult
            {
                QueryInventory = new QueryInventoryResult { Snapshot = snapshot },
            },
        };

    internal static CommandEvent FailedFromResolution(
        string commandId,
        RefResolution resolution
    )
    {
        var code = resolution.Error?.Code ?? resolution.Status switch
        {
            RefStatus.Stale => ErrorCode.StaleRef,
            RefStatus.NotFound => ErrorCode.NotFound,
            RefStatus.Unsupported => ErrorCode.InvalidArgument,
            _ => ErrorCode.Internal,
        };
        var message = resolution.Error?.Message ?? "容器 Ref 解析失败";
        var phase = code switch
        {
            ErrorCode.InvalidArgument => "invalid_argument",
            ErrorCode.NotFound => "not_found",
            ErrorCode.StaleRef => "stale_ref",
            _ => "internal",
        };
        return Failed(commandId, code, message, phase);
    }

    private static CommandEvent Failed(
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
