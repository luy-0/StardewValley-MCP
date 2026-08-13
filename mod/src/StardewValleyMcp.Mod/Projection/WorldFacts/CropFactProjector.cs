using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class CropFactProjector
{
    public static CropFact Project(
        HoeDirt dirt,
        Ref reference,
        ICollection<QueryWarning> warnings
    )
    {
        var crop = dirt.crop ?? throw new InvalidOperationException("Crop 投影缺少作物实例");
        var cropId = crop.netSeedIndex.Value ?? "";
        var harvestId = crop.indexOfHarvest.Value ?? "";
        var dead = crop.dead.Value;
        var detail = new CropFact
        {
            CropId = cropId,
            HarvestItemId = QualifyObjectId(harvestId),
            GrowthPhase = UInt(crop.currentPhase.Value),
            ReadyForHarvest = WorldProjectionPolicy.CropReady(dead, dirt.readyForHarvest()),
            Watered = dirt.state.Value == 1,
            Dead = dead,
            Regrows = crop.RegrowsAfterHarvest(),
            HarvestAction = WorldProjectionPolicy.HarvestActionFor(
                crop.GetHarvestMethod() == StardewValley.GameData.Crops.HarvestMethod.Scythe
            ),
        };

        WorldFactProjectionGuard.TryApplyEntity(reference, warnings, () =>
        {
            var fertilizer = dirt.fertilizer.Value ?? "";
            ApplyFertilizerFacts(detail, fertilizer, dirt.HasFertilizer(), ItemRegistry.QualifyItemId);
        });

        WorldFactProjectionGuard.TryApplyEntity(reference, warnings, () =>
        {
            var data = crop.GetData();
            var regrowDays = data?.RegrowDays ?? -1;
            ApplyGrowthFacts(
                detail,
                crop.currentPhase.Value,
                crop.dayOfCurrentPhase.Value,
                crop.fullyGrown.Value,
                crop.dead.Value,
                crop.phaseDays.ToArray(),
                regrowDays
            );
        });

        WorldFactProjectionGuard.TryApplyEntity(
            reference,
            warnings,
            () => detail.NeedsWatering = dirt.needsWatering()
        );
        return detail;
    }

    internal static void ApplyGrowthFacts(
        CropFact detail,
        int currentPhase,
        int dayOfCurrentPhase,
        bool fullyGrown,
        bool dead,
        IReadOnlyList<int> phaseDays,
        int regrowDays
    )
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(phaseDays);
        if (phaseDays.Count == 0 || currentPhase < 0 || currentPhase >= phaseDays.Count)
            throw new InvalidOperationException("作物阶段数据不完整");

        detail.GrowthPhaseCount = checked((uint)phaseDays.Count);
        var mature = !dead && currentPhase >= phaseDays.Count - 1;
        detail.Mature = mature;

        if (!dead && !fullyGrown && !mature)
        {
            var duration = phaseDays[currentPhase];
            if (duration < 0 || duration == 99999 || dayOfCurrentPhase < 0)
                throw new InvalidOperationException("当前作物阶段数据无效");
            detail.GrowthPhaseDay = UInt(dayOfCurrentPhase);
            detail.GrowthPhaseDuration = UInt(duration);
        }

        if (!dead)
        {
            detail.GrowthDaysRemainingIfWatered = mature
                ? 0
                : GrowthDaysRemaining(currentPhase, dayOfCurrentPhase, phaseDays);
        }

        if (regrowDays > 0)
        {
            detail.RegrowDays = UInt(regrowDays);
            if (fullyGrown && mature)
                detail.RegrowDaysRemaining = UInt(dayOfCurrentPhase);
        }
    }

    internal static uint GrowthDaysRemaining(
        int currentPhase,
        int dayOfCurrentPhase,
        IReadOnlyList<int> phaseDays
    )
    {
        if (phaseDays.Count == 0 || currentPhase < 0 || currentPhase >= phaseDays.Count - 1)
            return 0;
        if (dayOfCurrentPhase < 0)
            throw new InvalidOperationException("作物阶段日数无效");

        long remaining = Math.Max(0, phaseDays[currentPhase] - dayOfCurrentPhase);
        for (var index = currentPhase + 1; index < phaseDays.Count - 1; index++)
        {
            var duration = phaseDays[index];
            if (duration < 0 || duration == 99999)
                throw new InvalidOperationException("作物阶段持续时间无效");
            remaining += duration;
        }
        return checked((uint)remaining);
    }

    internal static void ApplyFertilizerFacts(
        CropFact detail,
        string rawFertilizerId,
        bool hasFertilizer,
        Func<string, string?> qualify
    )
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(qualify);
        detail.HasFertilizer = hasFertilizer;
        if (!hasFertilizer)
        {
            detail.ClearFertilizerItemId();
            return;
        }

        var qualified = qualify(rawFertilizerId);
        if (!PublicStringPolicy.IsNonEmptyValid(qualified))
            throw new InvalidOperationException("肥料 ID 无法安全公开");
        detail.FertilizerItemId = qualified;
    }

    private static string QualifyObjectId(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return "";
        var qualified = ItemRegistry.QualifyItemId(itemId);
        if (!PublicStringPolicy.IsNonEmptyValid(qualified))
            throw new InvalidOperationException("作物收获物 ID 无法安全公开");
        return qualified;
    }

    private static uint UInt(int value) => checked((uint)Math.Max(0, value));
}
