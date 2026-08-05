namespace StardewValleyMcp.Mod;

internal static class RuntimeProjectionPolicy
{
    public static string HomeLocationId(string savedId) =>
        PublicStringPolicy.IsNonEmptyValid(savedId) ? savedId : "";
}
