using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class QueryInventoryRequestValidator
{
    public static Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != CommandRequest.OperationOneofCase.QueryInventory)
            return Invalid("query_inventory 请求类型无效");

        var query = request.QueryInventory;
        return query.ContainerCase switch
        {
            QueryInventoryRequest.ContainerOneofCase.None => null,
            QueryInventoryRequest.ContainerOneofCase.PlayerInventory => null,
            QueryInventoryRequest.ContainerOneofCase.ContainerRef
                when query.ContainerRef is not null
                    && PublicStringPolicy.IsNonEmptyValid(query.ContainerRef.Value) => null,
            QueryInventoryRequest.ContainerOneofCase.ContainerRef =>
                Invalid("container_ref.value 必须为 1..512 个 Unicode 标量且不能包含 NUL"),
            _ => Invalid("container 类型无效"),
        };
    }

    private static Error Invalid(string message) =>
        new() { Code = ErrorCode.InvalidArgument, Message = message };
}
