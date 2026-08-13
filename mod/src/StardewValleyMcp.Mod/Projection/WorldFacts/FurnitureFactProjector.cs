using StardewValley.Objects;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal static class FurnitureFactProjector
{
    private static readonly HashSet<string> OtherInteractiveFurnitureIds = new(StringComparer.Ordinal)
    {
        "(F)1402",
        "(F)RetroCatalogue",
        "(F)TrashCatalogue",
        "(F)JunimoCatalogue",
        "(F)WizardCatalogue",
        "(F)JojaCatalogue",
        "(F)1308",
        "(F)1226",
        "(F)1309",
    };

    internal static bool IsKnownButUnclassifiedInteraction(string? qualifiedItemId) =>
        qualifiedItemId is not null && OtherInteractiveFurnitureIds.Contains(qualifiedItemId);

    public static FurnitureFact Project(
        Furniture furniture,
        IEnumerable<WorldPosition> occupiedTiles,
        Ref reference,
        ICollection<QueryWarning> warnings
    )
    {
        var furnitureType = furniture.furniture_type.Value;
        var detail = new FurnitureFact
        {
            FurnitureKind = KindName(furnitureType),
            Rotation = UInt(furniture.currentRotation.Value),
            RotationCount = UInt(furniture.rotations.Value),
            CanRotate = furniture.rotations.Value >= 2,
            InteractionProfileComplete = true,
        };
        detail.OccupiedTiles.AddRange(occupiedTiles);

        var qualifiedItemId = furniture.QualifiedItemId ?? "";
        if (PublicStringPolicy.IsNonEmptyValid(qualifiedItemId))
            detail.QualifiedItemId = qualifiedItemId;
        else
        {
            detail.InteractionProfileComplete = false;
            WorldFactProjectionGuard.TryApplyEntity(
                reference,
                warnings,
                () => throw new InvalidOperationException("家具 QID 无法安全公开")
            );
        }

        var runtimeProfileComplete = HasCompleteRuntimeProfile(
            furniture.GetType() == typeof(Furniture),
            furniture.GetType() == typeof(StorageFurniture),
            furniture.GetType() == typeof(FishTankFurniture)
        );
        if (!runtimeProfileComplete)
        {
            detail.InteractionProfileComplete = false;
            WorldFactProjectionGuard.TryApplyEntity(
                reference,
                warnings,
                () => throw new InvalidOperationException("家具子类型存在尚未分类的原生交互")
            );
        }

        var seatRead = WorldFactProjectionGuard.TryApplyEntity(reference, warnings, () =>
        {
            var capacity = furniture.GetSeatCapacity();
            var occupied = furniture.GetSittingFarmerCount();
            if (capacity < 0 || occupied < 0)
                throw new InvalidOperationException("家具座位状态无效");
            detail.SeatCapacity = UInt(capacity);
            detail.OccupiedSeats = UInt(occupied);
        });
        if (!seatRead)
            detail.InteractionProfileComplete = false;

        var isSurface = false;
        var surfaceRead = WorldFactProjectionGuard.TryApplyEntity(
            reference,
            warnings,
            () => isSurface = furniture.IsTable()
        );
        if (!surfaceRead)
            detail.InteractionProfileComplete = false;
        if (isSurface)
        {
            detail.HasSurfaceItem = furniture.heldObject.Value is not null;
            if (furniture.heldObject.Value is { } item)
            {
                var itemRead = TryApplySurfaceItem(
                    detail,
                    reference,
                    warnings,
                    () => ItemFactProjector.Project(item)
                );
                if (!itemRead)
                    detail.InteractionProfileComplete = false;
            }
        }

        if (furniture is StorageFurniture storage)
        {
            var storageRead = TryApplyStorageItemCount(
                detail,
                reference,
                warnings,
                () => UInt(storage.heldItems.Count(item => item is not null))
            );
            if (!storageRead)
                detail.InteractionProfileComplete = false;
        }

        var toggle = furnitureType is 14 or 16
            || string.Equals(qualifiedItemId, "(F)Cauldron", StringComparison.Ordinal);
        if (toggle)
        {
            detail.IsOn = furniture.IsOn;
        }

        if (IsKnownButUnclassifiedInteraction(qualifiedItemId))
        {
            detail.InteractionProfileComplete = false;
            WorldFactProjectionGuard.TryApplyEntity(
                reference,
                warnings,
                () => throw new InvalidOperationException("家具存在尚未分类的原生交互")
            );
        }

        detail.InteractionKinds.AddRange(
            ClassifyKinds(
                isSurface,
                furniture is StorageFurniture,
                detail.HasSeatCapacity ? checked((int)detail.SeatCapacity) : 0,
                toggle
            )
        );
        return detail;
    }

    internal static bool TryApplySurfaceItem(
        FurnitureFact detail,
        Ref reference,
        ICollection<QueryWarning> warnings,
        Func<ItemFact> project
    ) => WorldFactProjectionGuard.TryApplyEntity(
        reference,
        warnings,
        () => detail.SurfaceItem = project()
    );

    internal static bool TryApplyStorageItemCount(
        FurnitureFact detail,
        Ref reference,
        ICollection<QueryWarning> warnings,
        Func<uint> read
    ) => WorldFactProjectionGuard.TryApplyEntity(
        reference,
        warnings,
        () => detail.StorageItemCount = read()
    );

    internal static IReadOnlyList<FurnitureInteractionKind> ClassifyKinds(
        bool surface,
        bool storage,
        int seatCapacity,
        bool toggle
    )
    {
        var kinds = new List<FurnitureInteractionKind>();
        if (seatCapacity > 0)
            kinds.Add(FurnitureInteractionKind.Seat);
        if (surface)
            kinds.Add(FurnitureInteractionKind.Surface);
        if (storage)
            kinds.Add(FurnitureInteractionKind.Storage);
        if (toggle)
            kinds.Add(FurnitureInteractionKind.Toggle);
        return kinds;
    }

    internal static bool HasCompleteRuntimeProfile(
        bool exactFurnitureType,
        bool exactStorageFurnitureType,
        bool exactFishTankFurnitureType
    ) => exactFurnitureType || exactStorageFurnitureType || exactFishTankFurnitureType;

    internal static void ApplyActionability(
        WorldEntityFact fact,
        ICollection<QueryWarning> warnings
    )
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(warnings);
        if (fact.Furniture is { HasInteractionProfileComplete: true, InteractionProfileComplete: true }
            && fact.Furniture.InteractionKinds.Count == 0)
        {
            fact.Actionable = false;
            return;
        }

        fact.ClearActionable();
        WorldEntityProjectionGuard.MarkActionableUnknown(fact, warnings);
    }

    internal static string KindName(int value) => value switch
    {
        0 => "chair",
        1 => "bench",
        2 => "couch",
        3 => "armchair",
        4 => "dresser",
        5 => "long_table",
        6 => "painting",
        7 => "lamp",
        8 => "decor",
        10 => "bookcase",
        11 => "table",
        12 => "rug",
        13 => "window",
        14 => "fireplace",
        15 => "bed",
        16 => "torch",
        17 => "sconce",
        _ => "other",
    };

    private static uint UInt(int value) => checked((uint)Math.Max(0, value));
}
