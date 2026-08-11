using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class RuntimeProjectionPolicy
{
    public static string HomeLocationId(string savedId, string resolvedId)
    {
        if (PublicStringPolicy.IsNonEmptyValid(resolvedId))
            return resolvedId;
        return PublicStringPolicy.IsNonEmptyValid(savedId) ? savedId : "";
    }

    public static DailyLuckTier ClassifyDailyLuck(double dailyLuck, double sharedDailyLuck)
    {
        // 原版电视在最后用 DailyLuck == 0 覆盖共享好运／厄运文案。
        if (dailyLuck == 0)
            return DailyLuckTier.Neutral;
        if (sharedDailyLuck == -0.12 || dailyLuck < -0.07)
            return DailyLuckTier.VeryUnlucky;
        if (dailyLuck < -0.02)
            return DailyLuckTier.Unlucky;
        if (sharedDailyLuck == 0.12 || dailyLuck > 0.07)
            return DailyLuckTier.VeryLucky;
        if (dailyLuck > 0.02)
            return DailyLuckTier.Lucky;
        return DailyLuckTier.Neutral;
    }
}
