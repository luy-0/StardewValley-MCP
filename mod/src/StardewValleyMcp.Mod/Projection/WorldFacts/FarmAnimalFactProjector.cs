using StardewValley;
using StardewValley.GameData.FarmAnimals;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class FarmAnimalFactProjector
{
    public static FarmAnimalFact Project(
        FarmAnimal animal,
        Ref reference,
        ICollection<QueryWarning> warnings
    )
    {
        var currentProduce = animal.currentProduce.Value ?? "";
        var detail = new FarmAnimalFact
        {
            AnimalType = animal.type.Value ?? "",
            ProduceReady = !string.IsNullOrEmpty(currentProduce),
            PettedToday = animal.wasPet.Value,
            Friendship = animal.friendshipTowardFarmer.Value,
            Happiness = animal.happiness.Value,
        };

        WorldFactProjectionGuard.TryApplyCharacter(reference, warnings, () =>
        {
            ApplyCareFacts(detail, animal.fullness.Value, animal.wasAutoPet.Value);
        });

        WorldFactProjectionGuard.TryApplyCharacter(reference, warnings, () =>
        {
            detail.AgeDays = UInt(animal.age.Value);
            detail.DaysSinceLastProduce = UInt(animal.daysSinceLastLay.Value);
            var data = animal.GetAnimalData()
                ?? throw new InvalidOperationException("动物数据定义不可用");
            ApplyMaturityFacts(detail, animal.age.Value, data.DaysToMature, data.DaysToProduce);
        });
        if (!string.IsNullOrEmpty(currentProduce))
        {
            WorldFactProjectionGuard.TryApplyCharacter(reference, warnings, () =>
            {
                var method = HarvestMethod(animal.GetHarvestType() is { } value ? (int)value : null);
                if (method is null)
                    throw new InvalidOperationException("动物产物收取方式不可用");
                detail.ProduceHarvestMethod = method.Value;
            });
        }

        if (!string.IsNullOrEmpty(currentProduce))
        {
            detail.ProduceQuality = UInt(animal.produceQuality.Value);
            WorldFactProjectionGuard.TryApplyCharacter(reference, warnings, () =>
            {
                var qualified = ItemRegistry.QualifyItemId(currentProduce);
                if (!PublicStringPolicy.IsNonEmptyValid(qualified))
                    throw new InvalidOperationException("动物产物 ID 无法安全公开");
                detail.ProduceItemId = qualified;
            });
        }

        WorldFactProjectionGuard.TryApplyCharacter(reference, warnings, () =>
        {
            var home = animal.home;
            detail.HasHomeBuilding = home is not null;
            if (home is null)
                return;
            detail.HomeBuildingId = NormalizeBuildingId(home.id.Value);
            var buildingType = home.buildingType.Value ?? "";
            if (!PublicStringPolicy.IsNonEmptyValid(buildingType))
                throw new InvalidOperationException("动物所属建筑类型不可公开");
            detail.HomeBuildingType = buildingType;
            detail.InHomeBuilding = animal.IsHome;
        });

        return detail;
    }

    internal static void ApplyMaturityFacts(
        FarmAnimalFact detail,
        int ageDays,
        int daysToMature,
        int daysToProduce
    )
    {
        ArgumentNullException.ThrowIfNull(detail);
        if (ageDays < 0 || daysToMature < 0 || daysToProduce < 0)
            throw new InvalidOperationException("动物成熟或产出周期数据无效");
        detail.Adult = ageDays >= daysToMature;
        detail.DaysUntilMature = UInt(Math.Max(0, daysToMature - ageDays));
        detail.BaseDaysToProduce = UInt(daysToProduce);
    }

    internal static void ApplyCareFacts(
        FarmAnimalFact detail,
        int fullness,
        bool autoPettedToday
    )
    {
        ArgumentNullException.ThrowIfNull(detail);
        if (fullness is < 0 or > 255)
            throw new InvalidOperationException("动物饱食值超出公开范围");
        detail.Fullness = checked((uint)fullness);
        detail.FedToday = fullness >= 200;
        detail.AutoPettedToday = autoPettedToday;
    }

    internal static FarmAnimalProduceHarvestMethod? HarvestMethod(int? method) =>
        method switch
        {
            (int)FarmAnimalHarvestType.DropOvernight => FarmAnimalProduceHarvestMethod.DropOvernight,
            (int)FarmAnimalHarvestType.HarvestWithTool => FarmAnimalProduceHarvestMethod.HarvestWithTool,
            (int)FarmAnimalHarvestType.DigUp => FarmAnimalProduceHarvestMethod.DigUp,
            _ => null,
        };

    internal static string NormalizeBuildingId(Guid id)
    {
        if (id == Guid.Empty)
            throw new InvalidOperationException("动物所属建筑 UUID 不可用");
        return id.ToString("D").ToLowerInvariant();
    }

    private static uint UInt(int value) => checked((uint)Math.Max(0, value));
}
