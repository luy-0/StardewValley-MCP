using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class RuntimeProjectionPolicyTests
{
    [TestCase(0.0, -0.12, DailyLuckTier.Neutral)]
    [TestCase(0.0, 0.12, DailyLuckTier.Neutral)]
    [TestCase(0.01, -0.12, DailyLuckTier.VeryUnlucky)]
    [TestCase(-0.08, 0.0, DailyLuckTier.VeryUnlucky)]
    [TestCase(-0.07, 0.0, DailyLuckTier.Unlucky)]
    [TestCase(-0.02, 0.0, DailyLuckTier.Neutral)]
    [TestCase(0.01, 0.12, DailyLuckTier.VeryLucky)]
    [TestCase(0.08, 0.0, DailyLuckTier.VeryLucky)]
    [TestCase(0.07, 0.0, DailyLuckTier.Lucky)]
    [TestCase(0.02, 0.0, DailyLuckTier.Neutral)]
    public void DailyLuckTierMatchesTelevisionForecastOrdering(
        double dailyLuck,
        double sharedDailyLuck,
        DailyLuckTier expected
    )
    {
        Assert.That(RuntimeProjectionPolicy.ClassifyDailyLuck(dailyLuck, sharedDailyLuck), Is.EqualTo(expected));
    }
}
