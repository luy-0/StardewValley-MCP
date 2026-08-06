namespace StardewValleyMcp.Mod;

internal static class RuntimeProjectionPolicy
{
    public static string HomeLocationId(string savedId, string resolvedId)
    {
        if (PublicStringPolicy.IsNonEmptyValid(resolvedId))
            return resolvedId;
        return PublicStringPolicy.IsNonEmptyValid(savedId) ? savedId : "";
    }
}
