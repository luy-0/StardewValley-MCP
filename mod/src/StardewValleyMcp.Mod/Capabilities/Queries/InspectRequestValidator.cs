using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class InspectRequestValidator
{
    private const int MaximumRefs = 64;

    public static Error? Validate(CommandRequest request)
    {
        if (request.OperationCase != CommandRequest.OperationOneofCase.Inspect)
            return Invalid("inspect 请求类型无效");
        var refs = request.Inspect.Refs;
        if (refs.Count == 0)
            return Invalid("refs 不得为空");
        if (refs.Count > MaximumRefs)
            return new Error { Code = ErrorCode.OutOfRange, Message = "refs 数量必须在 1..64 之间" };
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in refs)
        {
            if (reference is null || !PublicStringPolicy.IsNonEmptyValid(reference.Value))
                return Invalid("refs 必须是 1..512 Unicode 标量且不得包含 NUL");
            if (!values.Add(reference.Value))
                return Invalid("refs 不得重复");
        }
        return null;
    }

    private static Error Invalid(string message) => new()
    {
        Code = ErrorCode.InvalidArgument,
        Message = message,
    };
}
