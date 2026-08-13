using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class WorldFactProjectionTests
{
    [Test]
    public void CropGrowthUsesInstancePhaseDurationsAndPreservesOptionalPresence()
    {
        var growing = CoreCrop();
        CropFactProjector.ApplyGrowthFacts(
            growing,
            currentPhase: 1,
            dayOfCurrentPhase: 2,
            fullyGrown: false,
            dead: false,
            phaseDays: new[] { 1, 4, 2, 99999 },
            regrowDays: 3
        );

        var regrowing = CoreCrop();
        CropFactProjector.ApplyGrowthFacts(
            regrowing,
            currentPhase: 3,
            dayOfCurrentPhase: 2,
            fullyGrown: true,
            dead: false,
            phaseDays: new[] { 1, 4, 2, 99999 },
            regrowDays: 3
        );

        var dead = CoreCrop();
        CropFactProjector.ApplyGrowthFacts(
            dead,
            currentPhase: 3,
            dayOfCurrentPhase: 0,
            fullyGrown: false,
            dead: true,
            phaseDays: new[] { 1, 4, 2, 99999 },
            regrowDays: -1
        );

        Assert.Multiple(() =>
        {
            Assert.That(growing.GrowthPhaseCount, Is.EqualTo(4));
            Assert.That(growing.GrowthPhaseDay, Is.EqualTo(2));
            Assert.That(growing.GrowthPhaseDuration, Is.EqualTo(4));
            Assert.That(growing.GrowthDaysRemainingIfWatered, Is.EqualTo(4));
            Assert.That(growing.Mature, Is.False);
            Assert.That(growing.RegrowDays, Is.EqualTo(3));
            Assert.That(growing.HasRegrowDaysRemaining, Is.False);

            Assert.That(regrowing.Mature, Is.True);
            Assert.That(regrowing.GrowthDaysRemainingIfWatered, Is.Zero);
            Assert.That(regrowing.RegrowDaysRemaining, Is.EqualTo(2));
            Assert.That(regrowing.HasGrowthPhaseDay, Is.False);
            Assert.That(regrowing.HasGrowthPhaseDuration, Is.False);

            Assert.That(dead.Mature, Is.False);
            Assert.That(dead.HasGrowthDaysRemainingIfWatered, Is.False);
            Assert.That(dead.HasRegrowDays, Is.False);
        });
    }

    [Test]
    public void CropFertilizerDistinguishesNoneFromQualifiedIdentity()
    {
        var none = CoreCrop();
        CropFactProjector.ApplyFertilizerFacts(none, "0", hasFertilizer: false, _ => null);
        var present = CoreCrop();
        CropFactProjector.ApplyFertilizerFacts(
            present,
            "368",
            hasFertilizer: true,
            value => value == "368" ? "(O)368" : null
        );

        Assert.Multiple(() =>
        {
            Assert.That(none.HasHasFertilizer, Is.True);
            Assert.That(none.HasFertilizer, Is.False);
            Assert.That(none.HasFertilizerItemId, Is.False);
            Assert.That(present.HasFertilizer, Is.True);
            Assert.That(present.FertilizerItemId, Is.EqualTo("(O)368"));
        });
    }

    [Test]
    public void MachineStateIsUnknownForEveryInconsistentCombination()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MachineFactProjector.ClassifyState(false, 0, false), Is.EqualTo(MachineState.Idle));
            Assert.That(MachineFactProjector.ClassifyState(false, 120, true), Is.EqualTo(MachineState.Processing));
            Assert.That(MachineFactProjector.ClassifyState(true, 0, true), Is.EqualTo(MachineState.Ready));
            Assert.That(MachineFactProjector.ClassifyState(true, 0, false), Is.EqualTo(MachineState.Unknown));
            Assert.That(MachineFactProjector.ClassifyState(true, 120, true), Is.EqualTo(MachineState.Unknown));
            Assert.That(MachineFactProjector.ClassifyState(false, 120, false), Is.EqualTo(MachineState.Unknown));
            Assert.That(MachineFactProjector.ClassifyState(false, 0, true), Is.EqualTo(MachineState.Unknown));
        });
    }

    [Test]
    public void AnimalMaturityAndHarvestMethodStayLanguageIndependent()
    {
        var baby = new FarmAnimalFact();
        FarmAnimalFactProjector.ApplyMaturityFacts(baby, ageDays: 2, daysToMature: 5, daysToProduce: 1);
        var adult = new FarmAnimalFact();
        FarmAnimalFactProjector.ApplyMaturityFacts(adult, ageDays: 7, daysToMature: 5, daysToProduce: 2);

        Assert.Multiple(() =>
        {
            Assert.That(baby.Adult, Is.False);
            Assert.That(baby.DaysUntilMature, Is.EqualTo(3));
            Assert.That(baby.BaseDaysToProduce, Is.EqualTo(1));
            Assert.That(adult.Adult, Is.True);
            Assert.That(adult.DaysUntilMature, Is.Zero);
            Assert.That(adult.BaseDaysToProduce, Is.EqualTo(2));
            Assert.That(
                FarmAnimalFactProjector.HarvestMethod(0),
                Is.EqualTo(FarmAnimalProduceHarvestMethod.DropOvernight)
            );
            Assert.That(
                FarmAnimalFactProjector.HarvestMethod(1),
                Is.EqualTo(FarmAnimalProduceHarvestMethod.HarvestWithTool)
            );
            Assert.That(
                FarmAnimalFactProjector.HarvestMethod(2),
                Is.EqualTo(FarmAnimalProduceHarvestMethod.DigUp)
            );
            Assert.That(FarmAnimalFactProjector.HarvestMethod(null), Is.Null);
            Assert.That(
                FarmAnimalFactProjector.NormalizeBuildingId(
                    Guid.Parse("AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE")
                ),
                Is.EqualTo("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee")
            );
        });
    }

    [Test]
    public void AnimalFedTodayUsesFullnessThresholdTwoHundred()
    {
        var hungry = new FarmAnimalFact();
        var fed = new FarmAnimalFact();
        FarmAnimalFactProjector.ApplyCareFacts(hungry, fullness: 199, autoPettedToday: false);
        FarmAnimalFactProjector.ApplyCareFacts(fed, fullness: 200, autoPettedToday: true);

        Assert.Multiple(() =>
        {
            Assert.That(hungry.Fullness, Is.EqualTo(199));
            Assert.That(hungry.FedToday, Is.False);
            Assert.That(hungry.AutoPettedToday, Is.False);
            Assert.That(fed.Fullness, Is.EqualTo(200));
            Assert.That(fed.FedToday, Is.True);
            Assert.That(fed.AutoPettedToday, Is.True);
        });
    }

    [Test]
    public void FurnitureInteractionKindsAreConservativeAndCanonical()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                FurnitureFactProjector.ClassifyKinds(surface: false, storage: false, seatCapacity: 0, toggle: false),
                Is.Empty
            );
            Assert.That(
                FurnitureFactProjector.ClassifyKinds(surface: false, storage: false, seatCapacity: 1, toggle: false),
                Is.EqualTo(new[] { FurnitureInteractionKind.Seat })
            );
            Assert.That(
                FurnitureFactProjector.ClassifyKinds(surface: true, storage: true, seatCapacity: 0, toggle: true),
                Is.EqualTo(new[]
                {
                    FurnitureInteractionKind.Surface,
                    FurnitureInteractionKind.Storage,
                    FurnitureInteractionKind.Toggle,
                })
            );
            Assert.That(FurnitureFactProjector.HasCompleteRuntimeProfile(true, false, false), Is.True);
            Assert.That(FurnitureFactProjector.HasCompleteRuntimeProfile(false, true, false), Is.True);
            Assert.That(FurnitureFactProjector.HasCompleteRuntimeProfile(false, false, true), Is.True);
            Assert.That(FurnitureFactProjector.HasCompleteRuntimeProfile(false, false, false), Is.False);
            Assert.That(
                FurnitureFactProjector.IsKnownButUnclassifiedInteraction("(F)RetroCatalogue"),
                Is.True
            );
            Assert.That(
                FurnitureFactProjector.IsKnownButUnclassifiedInteraction("(F)Cauldron"),
                Is.False
            );
        });
    }

    [Test]
    public void FurnitureStaticCapabilitiesDoNotClaimCurrentTickActionability()
    {
        var decorative = FurnitureEntity("decorative", complete: true);
        var interactive = FurnitureEntity("interactive", complete: true, FurnitureInteractionKind.Seat);
        var incomplete = FurnitureEntity("incomplete", complete: false);
        var warnings = new List<QueryWarning>();

        FurnitureFactProjector.ApplyActionability(decorative, warnings);
        FurnitureFactProjector.ApplyActionability(interactive, warnings);
        FurnitureFactProjector.ApplyActionability(incomplete, warnings);

        Assert.Multiple(() =>
        {
            Assert.That(decorative.HasActionable, Is.True);
            Assert.That(decorative.Actionable, Is.False);
            Assert.That(interactive.HasActionable, Is.False);
            Assert.That(incomplete.HasActionable, Is.False);
            Assert.That(
                warnings.Select(item => item.Ref.Value),
                Is.EqualTo(new[] { "interactive", "incomplete" })
            );
            Assert.That(warnings.All(item => item.Code == "ENTITY_ACTIONABLE_UNKNOWN"), Is.True);
        });
    }

    [Test]
    public void MachineItemProjectionFailureKeepsCoreAndLeavesOptionalItemsAbsent()
    {
        var reference = new Ref { Value = "opaque-world-ref" };
        var warnings = new List<QueryWarning>();
        var fact = new MachineFact
        {
            QualifiedItemId = "(BC)12",
            State = MachineState.Processing,
        };

        MachineFactProjector.TryApplyHeldItem(
            fact,
            reference,
            warnings,
            () => throw new InvalidOperationException("held item projection failed")
        );
        MachineFactProjector.TryApplyInputItem(
            fact,
            reference,
            warnings,
            () => throw new InvalidOperationException("input item projection failed")
        );

        Assert.Multiple(() =>
        {
            Assert.That(fact.QualifiedItemId, Is.EqualTo("(BC)12"));
            Assert.That(fact.State, Is.EqualTo(MachineState.Processing));
            Assert.That(fact.HeldItem, Is.Null);
            Assert.That(fact.InputItem, Is.Null);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo("ENTITY_FACT_PARTIAL"));
            Assert.That(warnings[0].Ref.Value, Is.EqualTo("opaque-world-ref"));
        });
    }

    [Test]
    public void FurnitureDerivedProjectionFailureKeepsCoreAndLeavesOptionalsAbsent()
    {
        var reference = new Ref { Value = "opaque-furniture-ref" };
        var warnings = new List<QueryWarning>();
        var fact = new FurnitureFact
        {
            FurnitureKind = "table",
            QualifiedItemId = "(F)11",
            HasSurfaceItem = true,
            InteractionProfileComplete = true,
        };
        fact.InteractionKinds.Add(FurnitureInteractionKind.Surface);

        var surfaceRead = FurnitureFactProjector.TryApplySurfaceItem(
            fact,
            reference,
            warnings,
            () => throw new InvalidOperationException("surface item projection failed")
        );
        var storageRead = FurnitureFactProjector.TryApplyStorageItemCount(
            fact,
            reference,
            warnings,
            () => throw new InvalidOperationException("storage count read failed")
        );

        Assert.Multiple(() =>
        {
            Assert.That(surfaceRead, Is.False);
            Assert.That(storageRead, Is.False);
            Assert.That(fact.FurnitureKind, Is.EqualTo("table"));
            Assert.That(fact.QualifiedItemId, Is.EqualTo("(F)11"));
            Assert.That(fact.HasSurfaceItem, Is.True);
            Assert.That(fact.SurfaceItem, Is.Null);
            Assert.That(fact.HasStorageItemCount, Is.False);
            Assert.That(fact.InteractionKinds, Is.EqualTo(new[] { FurnitureInteractionKind.Surface }));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].Code, Is.EqualTo("ENTITY_FACT_PARTIAL"));
            Assert.That(warnings[0].Ref.Value, Is.EqualTo("opaque-furniture-ref"));
        });
    }

    [Test]
    public void CharacterOptionalFactFailureUsesCharacterWarningWithoutClearingCore()
    {
        var reference = new Ref { Value = "opaque-animal-ref" };
        var warnings = new List<QueryWarning>();
        var fact = new FarmAnimalFact
        {
            AnimalType = "White Chicken",
            PettedToday = true,
        };

        WorldFactProjectionGuard.TryApplyCharacter(
            reference,
            warnings,
            () => FarmAnimalFactProjector.ApplyCareFacts(fact, fullness: 256, autoPettedToday: false)
        );

        Assert.Multiple(() =>
        {
            Assert.That(fact.AnimalType, Is.EqualTo("White Chicken"));
            Assert.That(fact.PettedToday, Is.True);
            Assert.That(fact.HasFullness, Is.False);
            Assert.That(warnings.Single().Code, Is.EqualTo("CHARACTER_FACT_PARTIAL"));
            Assert.That(warnings.Single().Ref.Value, Is.EqualTo("opaque-animal-ref"));
        });
    }

    [Test]
    public void EachNewWorldDetailClassParticipatesInWorldRevision()
    {
        var snapshot = SnapshotWithAllDetails();
        var original = WorldRevision.Compute(snapshot);

        snapshot.Entities[0].Crop.GrowthPhaseDay++;
        var cropChanged = WorldRevision.Compute(snapshot);
        snapshot.Entities[1].Machine.State = MachineState.Ready;
        var machineChanged = WorldRevision.Compute(snapshot);
        snapshot.Entities[2].Furniture.IsOn = true;
        var furnitureChanged = WorldRevision.Compute(snapshot);
        snapshot.Characters[0].FarmAnimal.Fullness++;
        var animalChanged = WorldRevision.Compute(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(cropChanged, Is.Not.EqualTo(original));
            Assert.That(machineChanged, Is.Not.EqualTo(cropChanged));
            Assert.That(furnitureChanged, Is.Not.EqualTo(machineChanged));
            Assert.That(animalChanged, Is.Not.EqualTo(furnitureChanged));
        });
    }

    private static CropFact CoreCrop() => new()
    {
        CropId = "472",
        HarvestItemId = "(O)24",
    };

    private static WorldEntityFact FurnitureEntity(
        string reference,
        bool complete,
        params FurnitureInteractionKind[] kinds
    )
    {
        var fact = new WorldEntityFact
        {
            Ref = new Ref { Value = reference },
            Kind = EntityKind.Furniture,
            Furniture = new FurnitureFact { InteractionProfileComplete = complete },
        };
        fact.Furniture.InteractionKinds.AddRange(kinds);
        return fact;
    }

    private static WorldSnapshot SnapshotWithAllDetails() => new()
    {
        Area = new TileArea { LocationId = "Farm", Width = 1, Height = 1 },
        Entities =
        {
            new WorldEntityFact
            {
                Ref = new Ref { Value = "crop" },
                Kind = EntityKind.Crop,
                Crop = new CropFact { GrowthPhaseDay = 1 },
            },
            new WorldEntityFact
            {
                Ref = new Ref { Value = "machine" },
                Kind = EntityKind.Machine,
                Machine = new MachineFact { State = MachineState.Processing },
            },
            new WorldEntityFact
            {
                Ref = new Ref { Value = "furniture" },
                Kind = EntityKind.Furniture,
                Furniture = new FurnitureFact { IsOn = false },
            },
        },
        Characters =
        {
            new CharacterFact
            {
                Ref = new Ref { Value = "animal" },
                Kind = CharacterKind.FarmAnimal,
                FarmAnimal = new FarmAnimalFact { Fullness = 200 },
            },
        },
    };
}
