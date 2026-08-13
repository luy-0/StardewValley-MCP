using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class WorldFactProjectionGuard
{
    public static bool TryApplyEntity(
        Ref reference,
        ICollection<QueryWarning> warnings,
        Action apply
    ) => TryApply(reference, warnings, "ENTITY_FACT_PARTIAL", "实体的部分派生事实暂时不可读", apply);

    public static bool TryApplyCharacter(
        Ref reference,
        ICollection<QueryWarning> warnings,
        Action apply
    ) => TryApply(reference, warnings, "CHARACTER_FACT_PARTIAL", "角色的部分派生事实暂时不可读", apply);

    private static bool TryApply(
        Ref reference,
        ICollection<QueryWarning> warnings,
        string code,
        string message,
        Action apply
    )
    {
        try
        {
            apply();
            return true;
        }
        catch
        {
            if (!warnings.Any(warning =>
                warning.Code == code
                && string.Equals(warning.Ref?.Value, reference.Value, StringComparison.Ordinal)))
            {
                warnings.Add(new QueryWarning
                {
                    Code = code,
                    Message = message,
                    Ref = reference.Clone(),
                });
            }
            return false;
        }
    }
}
