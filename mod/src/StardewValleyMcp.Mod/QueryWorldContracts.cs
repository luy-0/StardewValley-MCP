using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class QueryWorldRequestValidator
{
    public static Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != CommandRequest.OperationOneofCase.QueryWorld)
            return Invalid("query_world 请求类型无效");

        var query = request.QueryWorld;
        switch (query.RegionCase)
        {
            case QueryWorldRequest.RegionOneofCase.None:
                break;
            case QueryWorldRequest.RegionOneofCase.Area:
                if (!LocationIdPolicy.IsValid(query.Area.LocationId))
                    return Invalid("area.location_id 必须为 1..128 个 Unicode 标量且不能包含 NUL");
                if (query.Area.Width is < 1 or > 32 || query.Area.Height is < 1 or > 32)
                    return Invalid("area.width 与 area.height 必须位于 1..32");
                if ((ulong)query.Area.Width * query.Area.Height > 1024)
                    return Invalid("area 面积不能超过 1024");
                break;
            case QueryWorldRequest.RegionOneofCase.Around:
                if (query.Around.Center is null || !LocationIdPolicy.IsValid(query.Around.Center.LocationId))
                    return Invalid("around.center.location_id 必须为 1..128 个 Unicode 标量且不能包含 NUL");
                if (query.Around.Radius > 15)
                    return Invalid("around.radius 必须位于 0..15");
                break;
            default:
                return Invalid("region 类型无效");
        }

        if (query.MaxEntities > 512 || query.MaxCharacters > 512)
            return Invalid("max_entities 与 max_characters 必须为 0 或位于 1..512");
        if (query.HasIncludeEntities && !query.IncludeEntities && query.EntityKinds.Count > 0)
            return Invalid("include_entities=false 时不能提供 entity_kinds");
        if (query.EntityKinds.Any(kind =>
            kind == EntityKind.Unspecified || !Enum.IsDefined(typeof(EntityKind), kind)))
            return Invalid("entity_kinds 包含无效类型");
        return null;
    }

    private static Error Invalid(string message) =>
        new() { Code = ErrorCode.InvalidArgument, Message = message };
}

internal static class LocationIdPolicy
{
    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value)
        && !value.Contains('\0')
        && value.EnumerateRunes().Take(129).Count() <= 128;
}

internal static class WorldRevision
{
    public static string Compute(WorldSnapshot snapshot)
    {
        var material = snapshot.Clone();
        material.WorldRevision = "";
        return Convert.ToHexString(SHA256.HashData(material.ToByteArray())).ToLowerInvariant();
    }
}
