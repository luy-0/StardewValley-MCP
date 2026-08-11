using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class QueryWorldModContractTests
{
    [Test]
    public void FarmingAndBedProjectionPoliciesExposeSkillControlFacts()
    {
        var interact = WorldProjectionPolicy.HarvestActionFor(usesScythe: false);
        var scythe = WorldProjectionPolicy.HarvestActionFor(usesScythe: true);
        var sleep = WorldProjectionPolicy.SleepPosition("Cabin_123", 7, 9);

        Assert.Multiple(() =>
        {
            Assert.That(interact, Is.EqualTo(CropHarvestAction.Interact));
            Assert.That(scythe, Is.EqualTo(CropHarvestAction.Scythe));
            Assert.That(sleep.LocationId, Is.EqualTo("Cabin_123"));
            Assert.That(sleep.X, Is.EqualTo(7));
            Assert.That(sleep.Y, Is.EqualTo(9));
        });
    }

    [Test]
    public void CropReadinessUsesNativeHoeDirtResultAndRejectsDeadCrop()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorldProjectionPolicy.CropReady(dead: false, gameReady: true), Is.True);
            Assert.That(WorldProjectionPolicy.CropReady(dead: false, gameReady: false), Is.False);
            Assert.That(WorldProjectionPolicy.CropReady(dead: true, gameReady: true), Is.False);
        });
    }

    [Test]
    public void RuntimeHomePolicyPreservesSavedUniqueIdentityWithoutScanningLocations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                RuntimeProjectionPolicy.HomeLocationId("FarmHouse", "FarmHouse_123"),
                Is.EqualTo("FarmHouse_123")
            );
            Assert.That(
                RuntimeProjectionPolicy.HomeLocationId("Cabin_123", ""),
                Is.EqualTo("Cabin_123")
            );
            Assert.That(RuntimeProjectionPolicy.HomeLocationId("", ""), Is.Empty);
        });
    }

    [Test]
    public void ValidatorRejectsInvalidRegionLimitsBeforeExecution()
    {
        var invalidArea = Request(new QueryWorldRequest
        {
            Area = new TileArea { LocationId = "Farm", Width = 32, Height = 33 },
        });
        var invalidRadius = Request(new QueryWorldRequest
        {
            Around = new RadiusArea
            {
                Center = new WorldPosition { LocationId = "Farm" },
                Radius = 16,
            },
        });
        var conflictingFilter = Request(new QueryWorldRequest
        {
            IncludeEntities = false,
            EntityKinds = { EntityKind.Tree },
        });
        var unknownEntityKind = Request(new QueryWorldRequest
        {
            EntityKinds = { (EntityKind)999 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(QueryWorldRequestValidator.Validate(invalidArea)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(QueryWorldRequestValidator.Validate(invalidRadius)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(QueryWorldRequestValidator.Validate(conflictingFilter)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(QueryWorldRequestValidator.Validate(unknownEntityKind)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        });
    }

    [Test]
    public void ValidatorAcceptsBoundaryValuesAndOmittedIncludeFields()
    {
        var request = Request(new QueryWorldRequest
        {
            Area = new TileArea { LocationId = "Farm", Width = 32, Height = 32 },
            MaxEntities = 512,
            MaxCharacters = 512,
        });

        Assert.That(QueryWorldRequestValidator.Validate(request), Is.Null);
    }

    [Test]
    public void LocationIdLimitCountsUnicodeScalarValues()
    {
        var accepted = Request(new QueryWorldRequest
        {
            Area = new TileArea
            {
                LocationId = string.Concat(Enumerable.Repeat("😀", 128)),
                Width = 1,
                Height = 1,
            },
        });
        var rejected = Request(new QueryWorldRequest
        {
            Area = new TileArea
            {
                LocationId = string.Concat(Enumerable.Repeat("😀", 129)),
                Width = 1,
                Height = 1,
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(QueryWorldRequestValidator.Validate(accepted), Is.Null);
            Assert.That(QueryWorldRequestValidator.Validate(rejected)?.Code, Is.EqualTo(ErrorCode.InvalidArgument));
        });
    }

    [Test]
    public void WorldRevisionIsDeterministicLowerHexSha256AndTracksFacts()
    {
        var snapshot = new WorldSnapshot
        {
            Area = new TileArea { LocationId = "Farm", Width = 1, Height = 1 },
            Tiles =
            {
                new TileFact
                {
                    Position = new WorldPosition { LocationId = "Farm", X = 0, Y = 0 },
                    Passable = true,
                },
            },
        };

        var first = WorldRevision.Compute(snapshot);
        snapshot.WorldRevision = "ignored-existing-value";
        var second = WorldRevision.Compute(snapshot);
        snapshot.Tiles[0].Passable = false;
        var changed = WorldRevision.Compute(snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(first, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(changed, Is.Not.EqualTo(first));
        });
    }

    [Test]
    public void EmptyHoeDirtExposesWateredStateWithoutChangingCropFact()
    {
        var dry = HoeDirtProjectionPolicy.Create(0);
        var wet = HoeDirtProjectionPolicy.Create(1);
        var other = HoeDirtProjectionPolicy.Create(2);
        var crop = new CropFact { Watered = true };

        Assert.Multiple(() =>
        {
            Assert.That(dry.Watered, Is.False);
            Assert.That(wet.Watered, Is.True);
            Assert.That(other.Watered, Is.False);
            Assert.That(crop.Watered, Is.True);
            Assert.That(EntityKind.HoeDirt, Is.EqualTo((EntityKind)14));
        });
    }

    [Test]
    public void RefEnvelopeDistinguishesRestartFromUnknownCurrentToken()
    {
        const string currentInstance = "11111111-1111-4111-8111-111111111111";
        const string previousInstance = "22222222-2222-4222-8222-222222222222";
        var currentToken = OpaqueRefTokenCodec.NewToken(currentInstance);
        var previousToken = OpaqueRefTokenCodec.NewToken(previousInstance);

        Assert.Multiple(() =>
        {
            Assert.That(
                OpaqueRefTokenCodec.Decide(previousToken, currentInstance, issuedByCurrentInstance: false),
                Is.EqualTo(OpaqueRefLookupDecision.Stale)
            );
            Assert.That(
                OpaqueRefTokenCodec.Decide(currentToken, currentInstance, issuedByCurrentInstance: false),
                Is.EqualTo(OpaqueRefLookupDecision.NotFound)
            );
            Assert.That(
                OpaqueRefTokenCodec.Decide(currentToken, currentInstance, issuedByCurrentInstance: true),
                Is.EqualTo(OpaqueRefLookupDecision.Lookup)
            );
            Assert.That(
                OpaqueRefTokenCodec.Decide("not-a-ref", currentInstance, issuedByCurrentInstance: false),
                Is.EqualTo(OpaqueRefLookupDecision.NotFound)
            );
        });
    }

    [Test]
    public void LoadedLocationPolicyRejectsSameIdRebuildButAllowsRealCharacterMove()
    {
        var oldFarm = new object();
        var rebuiltFarm = new object();
        var town = new object();
        var loaded = new[]
        {
            (LocationId: "Farm", Instance: rebuiltFarm),
            (LocationId: "Town", Instance: town),
        };

        Assert.Multiple(() =>
        {
            Assert.That(LoadedLocationInstancePolicy.IsCurrent("Farm", oldFarm, loaded), Is.False);
            Assert.That(LoadedLocationInstancePolicy.IsCurrent("farm", rebuiltFarm, loaded), Is.True);
            Assert.That(
                LoadedLocationInstancePolicy.AllowsCharacterMove("Farm", oldFarm, "Farm", rebuiltFarm, loaded),
                Is.False
            );
            Assert.That(
                LoadedLocationInstancePolicy.AllowsCharacterMove("Farm", oldFarm, "Town", town, loaded),
                Is.True
            );
        });
    }

    [Test]
    public void UnresolvedDestinationFallsBackAndDoorAccessRemainsUnknown()
    {
        var door = WorldProjectionPolicy.CreateDoorWithUnknownAccess("FarmHouse");
        var warning = WorldProjectionPolicy.UnresolvedDestinationWarning(new Ref { Value = "opaque" });

        Assert.Multiple(() =>
        {
            Assert.That(WorldProjectionPolicy.HasResolvedDestination(""), Is.False);
            Assert.That(WorldProjectionPolicy.HasResolvedDestination("FarmHouse"), Is.True);
            Assert.That(
                WorldProjectionPolicy.HasResolvedDestination(string.Concat(Enumerable.Repeat("😀", 128))),
                Is.True
            );
            Assert.That(
                WorldProjectionPolicy.HasResolvedDestination(string.Concat(Enumerable.Repeat("😀", 129))),
                Is.False
            );
            Assert.That(WorldProjectionPolicy.HasResolvedDestination("Farm\0House"), Is.False);
            Assert.That(door.HasLocked, Is.False);
            Assert.That(warning.Code, Is.EqualTo("WORLD_DESTINATION_UNRESOLVED"));
            Assert.That(warning.Ref.Value, Is.EqualTo("opaque"));
        });
    }

    [Test]
    public void WarpAndDoorActionabilityPreservesKnownAndUnknownPresence()
    {
        var warnings = new List<QueryWarning>();
        var playerWarp = new WorldEntityFact { Ref = new Ref { Value = "player-warp" } };
        var npcWarp = new WorldEntityFact { Ref = new Ref { Value = "npc-warp" } };
        var unresolvedWarp = new WorldEntityFact { Ref = new Ref { Value = "unknown-warp" } };
        var door = new WorldEntityFact { Ref = new Ref { Value = "door" } };

        WorldProjectionPolicy.ApplyWarpActionability(playerWarp, npcOnly: false);
        WorldProjectionPolicy.ApplyWarpActionability(npcWarp, npcOnly: true);
        WorldProjectionPolicy.ApplyUnknownActionability(unresolvedWarp, warnings);
        WorldProjectionPolicy.ApplyUnknownActionability(door, warnings);
        WorldProjectionPolicy.ApplyUnknownActionability(door, warnings);

        Assert.Multiple(() =>
        {
            Assert.That(playerWarp.HasActionable, Is.True);
            Assert.That(playerWarp.Actionable, Is.True);
            Assert.That(npcWarp.HasActionable, Is.True);
            Assert.That(npcWarp.Actionable, Is.False);
            Assert.That(unresolvedWarp.HasActionable, Is.False);
            Assert.That(door.HasActionable, Is.False);
            Assert.That(
                warnings.Select(warning => warning.Ref.Value),
                Is.EqualTo(new[] { "unknown-warp", "door" })
            );
        });
    }

    [Test]
    public void EntityProjectionExceptionFallsBackWithoutRetryingOptionalGetters()
    {
        var warnings = new List<QueryWarning>();
        var fallback = new WorldEntityFact
        {
            Ref = new Ref { Value = "fallback-ref" },
            Kind = EntityKind.GenericObject,
            GenericObject = new GenericObjectFact { RuntimeType = "Test.Entity" },
        };

        var projected = WorldEntityProjectionGuard.ProjectOrFallback(
            () => throw new InvalidOperationException("third-party getter failed"),
            () =>
            {
                WorldEntityProjectionGuard.MarkActionableUnknown(fallback, warnings);
                return fallback;
            },
            warnings
        );

        Assert.Multiple(() =>
        {
            Assert.That(projected.Kind, Is.EqualTo(EntityKind.GenericObject));
            Assert.That(projected.HasActionable, Is.False);
            Assert.That(
                warnings.Select(warning => warning.Code),
                Is.EqualTo(new[] { "ENTITY_ACTIONABLE_UNKNOWN", "ENTITY_PROJECTION_FALLBACK" })
            );
            Assert.That(warnings.All(warning => warning.Ref.Value == "fallback-ref"), Is.True);
        });
    }

    [Test]
    public void ActionableExceptionLeavesOptionalFieldAbsentAndWarns()
    {
        var warnings = new List<QueryWarning>();
        var fact = new WorldEntityFact { Ref = new Ref { Value = "entity-ref" } };

        WorldEntityProjectionGuard.ApplyActionable(
            fact,
            () => throw new InvalidOperationException("third-party actionable failed"),
            warnings
        );

        Assert.Multiple(() =>
        {
            Assert.That(fact.HasActionable, Is.False);
            Assert.That(warnings.Single().Code, Is.EqualTo("ENTITY_ACTIONABLE_UNKNOWN"));
            Assert.That(warnings.Single().Ref.Value, Is.EqualTo("entity-ref"));
        });
    }

    [Test]
    public void ChestInventorySelectorUsesOnlyExistingBackingKinds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                ChestInventorySelection.Select(true, true, true, true),
                Is.EqualTo(ChestInventoryBacking.Global)
            );
            Assert.That(
                ChestInventorySelection.Select(false, true, true, false),
                Is.EqualTo(ChestInventoryBacking.SeparateWallet)
            );
            Assert.That(
                ChestInventorySelection.Select(false, false, false, true),
                Is.EqualTo(ChestInventoryBacking.Junimo)
            );
            Assert.That(
                ChestInventorySelection.Select(false, true, false, false),
                Is.EqualTo(ChestInventoryBacking.Local)
            );
        });
    }

    [Test]
    public void HandlerFinalizationSortsTruncatesAndFiltersWarningsByReturnedRefs()
    {
        var snapshot = new WorldSnapshot
        {
            Area = new TileArea { LocationId = "Farm", Width = 1, Height = 1 },
            Entities =
            {
                Entity("entity-b"),
                Entity("entity-a"),
            },
            Characters =
            {
                Character("character-d"),
                Character("character-c"),
            },
        };
        var warnings = new[]
        {
            Warning("entity-a"),
            Warning("entity-b"),
            Warning("character-c"),
            Warning("character-d"),
            new QueryWarning { Code = "FRIDGE_DISCOVERY_FAILED", Message = "no ref" },
        };

        var result = QueryWorldHandler.FinalizeResult(snapshot, warnings, 1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(result.Snapshot.Entities.Single().Ref.Value, Is.EqualTo("entity-a"));
            Assert.That(result.Snapshot.Characters.Single().Ref.Value, Is.EqualTo("character-c"));
            Assert.That(result.Snapshot.EntitiesTruncated, Is.True);
            Assert.That(result.Snapshot.CharactersTruncated, Is.True);
            Assert.That(result.Snapshot.WorldRevision, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(
                result.Warnings.Select(warning => warning.Ref?.Value ?? "no-ref"),
                Is.EqualTo(new[] { "entity-a", "character-c", "no-ref" })
            );
        });
    }

    private static WorldEntityFact Entity(string reference) => new()
    {
        Ref = new Ref { Value = reference },
        Kind = EntityKind.GenericObject,
        Position = new WorldPosition { LocationId = "Farm" },
        GenericObject = new GenericObjectFact { RuntimeType = "Test.Entity" },
    };

    private static CharacterFact Character(string reference) => new()
    {
        Ref = new Ref { Value = reference },
        Position = new WorldPosition { LocationId = "Farm" },
    };

    private static QueryWarning Warning(string reference) => new()
    {
        Code = "TEST_WARNING",
        Message = "test",
        Ref = new Ref { Value = reference },
    };

    private static CommandRequest Request(QueryWorldRequest query) =>
        new() { QueryWorld = query };
}
