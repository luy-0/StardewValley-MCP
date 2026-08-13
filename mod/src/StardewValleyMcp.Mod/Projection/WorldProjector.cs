using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using StardewValleyMcp.Protocol.V1;
using SObject = StardewValley.Object;
using STree = StardewValley.TerrainFeatures.Tree;

namespace StardewValleyMcp.Mod;

internal static class WorldProjector
{
    public static List<TileFact> ProjectTiles(GameLocation location, ScanArea area)
    {
        var facts = new List<TileFact>(checked(area.Width * area.Height));
        for (var y = area.Y; y < area.Y + area.Height; y++)
        {
            for (var x = area.X; x < area.X + area.Width; x++)
            {
                var tile = new Vector2(x, y);
                facts.Add(new TileFact
                {
                    Position = Position(location, x, y),
                    Passable = location.isTilePassable(tile),
                    Occupied = location.IsTileOccupiedBy(tile),
                    Diggable = location.doesTileHaveProperty(x, y, "Diggable", "Back") is not null,
                    Water = location.isWaterTile(x, y),
                    TerrainKind = TerrainKind(location, tile, x, y),
                    WateringCanRefillable = location.CanRefillWateringCanOnTile(x, y),
                    PathfindingBlocked = location.isCollidingPosition(
                        new Rectangle(x * Game1.tileSize + 1, y * Game1.tileSize + 1, Game1.tileSize - 2, Game1.tileSize - 2),
                        Game1.viewport,
                        isFarmer: true,
                        damagesFarmer: 0,
                        glider: false,
                        character: Game1.player,
                        pathfinding: true,
                        skipCollisionEffects: true
                    ),
                });
            }
        }
        return facts;
    }

    public static List<WorldEntityFact> ProjectEntities(
        GameLocation location,
        ScanArea area,
        IReadOnlySet<EntityKind> kinds,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var facts = new List<WorldEntityFact>();
        foreach (var pair in location.terrainFeatures.Pairs)
        {
            var x = (int)pair.Key.X;
            var y = (int)pair.Key.Y;
            if (!area.Contains(x, y))
                continue;

            var fact = ProjectEntityAt(
                location,
                x,
                y,
                pair.Value,
                RefLocatorKind.TerrainFeature,
                guard: "",
                refs,
                warnings
            );
            AddIfIncluded(facts, fact, kinds);
        }

        foreach (var pair in location.Objects.Pairs)
        {
            var x = (int)pair.Key.X;
            var y = (int)pair.Key.Y;
            if (!area.Contains(x, y))
                continue;
            var fact = ProjectEntityAt(
                location,
                x,
                y,
                pair.Value,
                RefLocatorKind.Object,
                guard: "",
                refs,
                warnings
            );
            AddIfIncluded(facts, fact, kinds);
        }

        Point? fridgePosition = null;
        Chest? fridge = null;
        try
        {
            fridgePosition = location.GetFridgePosition();
            fridge = location.GetFridge();
        }
        catch
        {
            warnings.Add(new QueryWarning
            {
                Code = "FRIDGE_DISCOVERY_FAILED",
                Message = "Location 冰箱读取失败，已跳过该冰箱",
            });
        }
        if (fridgePosition is { } fridgeTile && fridge is not null
            && area.Contains(fridgeTile.X, fridgeTile.Y))
            AddIfIncluded(
                facts,
                ProjectEntityAt(
                    location,
                    fridgeTile.X,
                    fridgeTile.Y,
                    fridge,
                    RefLocatorKind.Fridge,
                    guard: "",
                    refs,
                    warnings
                ),
                kinds
            );

        foreach (var furniture in location.furniture)
        {
            var x = (int)furniture.TileLocation.X;
            var y = (int)furniture.TileLocation.Y;
            if (!area.Contains(x, y))
                continue;
            var fact = ProjectEntityAt(
                location,
                x,
                y,
                furniture,
                RefLocatorKind.Furniture,
                guard: "",
                refs,
                warnings
            );
            AddIfIncluded(facts, fact, kinds);
        }

        foreach (var clump in location.resourceClumps)
        {
            var x = (int)clump.Tile.X;
            var y = (int)clump.Tile.Y;
            if (area.Contains(x, y))
            {
                var fact = ProjectEntityAt(
                    location,
                    x,
                    y,
                    clump,
                    RefLocatorKind.ResourceClump,
                    guard: "",
                    refs,
                    warnings
                );
                AddIfIncluded(facts, fact, kinds);
            }
        }

        foreach (var warp in location.warps)
        {
            if (area.Contains(warp.X, warp.Y))
            {
                var fact = ProjectEntityAt(
                    location,
                    warp.X,
                    warp.Y,
                    warp,
                    RefLocatorKind.Warp,
                    guard: "",
                    refs,
                    warnings
                );
                AddIfIncluded(facts, fact, kinds);
            }
        }

        foreach (var pair in location.doors.Pairs)
        {
            if (area.Contains(pair.Key.X, pair.Key.Y))
            {
                var fact = ProjectEntityAt(
                    location,
                    pair.Key.X,
                    pair.Key.Y,
                    pair.Value,
                    RefLocatorKind.Door,
                    pair.Value,
                    refs,
                    warnings
                );
                AddIfIncluded(facts, fact, kinds);
            }
        }
        return facts;
    }

    public static List<CharacterFact> ProjectCharacters(
        GameLocation location,
        ScanArea area,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var facts = new List<CharacterFact>();
        foreach (var character in location.characters)
        {
            int x;
            int y;
            try
            {
                x = (int)character.Tile.X;
                y = (int)character.Tile.Y;
            }
            catch
            {
                warnings.Add(new QueryWarning
                {
                    Code = "CHARACTER_PROJECTION_SKIPPED",
                    Message = "角色位置不可读，已跳过该角色",
                });
                continue;
            }
            if (!area.Contains(x, y))
                continue;
            facts.Add(ProjectCharacterAt(location, x, y, character, refs, warnings));
        }
        foreach (var animal in location.Animals.Values)
        {
            int x;
            int y;
            try
            {
                x = (int)animal.Tile.X;
                y = (int)animal.Tile.Y;
            }
            catch
            {
                warnings.Add(new QueryWarning
                {
                    Code = "CHARACTER_PROJECTION_SKIPPED",
                    Message = "农场动物位置不可读，已跳过该角色",
                });
                continue;
            }
            if (area.Contains(x, y))
                facts.Add(ProjectCharacterAt(location, x, y, animal, refs, warnings));
        }
        return facts;
    }

    /// <summary>
    /// Reuses the same typed leaf projectors as query_world for one already-resolved entity.
    /// The caller-owned Ref remains authoritative; a changed identity guard is surfaced as
    /// stale instead of silently returning a newly signed Ref.
    /// </summary>
    public static WorldEntityFact ProjectResolvedEntity(
        ResolvedOpaqueRef resolved,
        Ref reference,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var location = resolved.Location;
        var (x, y) = CurrentEntityTile(resolved);
        // Query 已签发 fallback Ref 时不再重试已知失败的 typed getter；否则 typed
        // 投影可能在再次抛错前改写 binding guard，使调用方仍持有的 Ref 被误判 stale。
        var fact = ProjectEntityAt(
            location,
            x,
            y,
            resolved.Target,
            resolved.LocatorKind,
            resolved.Guard,
            refs,
            warnings,
            fallbackOnly: IsFallbackGuard(resolved.Guard)
        );
        PreserveInputRef(fact.Ref, reference);
        fact.Ref = reference.Clone();
        PreserveWarningRefs(warnings, reference);
        return fact;
    }

    public static CharacterFact ProjectResolvedCharacter(
        ResolvedOpaqueRef resolved,
        Ref reference,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var (x, y) = CurrentCharacterTile(resolved.Target);
        // Character fallback 与 World Entity 遵守相同规则：保留调用方 Ref，并重建
        // 已公开的最小事实，而不是重试 getter 或重新签发 Ref。
        var fact = ProjectCharacterAt(
            resolved.Location,
            x,
            y,
            resolved.Target,
            refs,
            warnings,
            fallbackOnly: IsFallbackGuard(resolved.Guard)
        );
        PreserveInputRef(fact.Ref, reference);
        fact.Ref = reference.Clone();
        PreserveWarningRefs(warnings, reference);
        return fact;
    }

    private static WorldEntityFact ProjectEntityAt(
        GameLocation location,
        int x,
        int y,
        object target,
        RefLocatorKind locatorKind,
        string guard,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings,
        bool fallbackOnly = false
    )
    {
        WorldEntityFact Fallback() => locatorKind == RefLocatorKind.Door
            ? ProjectDoorFallback(location, new Point(x, y), guard, refs, warnings)
            : ProjectGenericFallback(target, location, x, y, refs, locatorKind, warnings);

        return ProjectEntityOrFallback(
            () => locatorKind switch
            {
                RefLocatorKind.TerrainFeature => target switch
                {
                    STree tree => ProjectTree(location, x, y, tree, refs),
                    FruitTree fruitTree => ProjectFruitTree(location, x, y, fruitTree, refs),
                    HoeDirt { crop: not null } dirt => ProjectCrop(location, x, y, dirt, refs, warnings),
                    HoeDirt dirt => ProjectHoeDirt(location, x, y, dirt, refs, warnings),
                    TerrainFeature feature => ProjectGenericTerrainFeature(
                        location,
                        x,
                        y,
                        feature,
                        refs,
                        warnings
                    ),
                    _ => throw new InvalidOperationException("Terrain Ref 目标类型无效"),
                },
                RefLocatorKind.Object when target is SObject obj =>
                    ProjectObject(location, x, y, obj, refs, warnings),
                RefLocatorKind.Fridge when target is Chest fridge =>
                    ProjectContainer(
                        location,
                        x,
                        y,
                        fridge,
                        refs,
                        RefLocatorKind.Fridge,
                        warnings
                    ),
                RefLocatorKind.Furniture when target is BedFurniture bed =>
                    ProjectBed(location, x, y, bed, refs, warnings),
                RefLocatorKind.Furniture when target is Furniture furniture =>
                    ProjectFurniture(location, x, y, furniture, refs, warnings),
                RefLocatorKind.ResourceClump when target is ResourceClump clump =>
                    ProjectResourceClump(location, x, y, clump, refs),
                RefLocatorKind.Warp when target is Warp warp =>
                    ProjectWarp(location, warp, refs, warnings),
                RefLocatorKind.Door => ProjectDoor(
                    location,
                    new Point(x, y),
                    guard,
                    refs,
                    warnings
                ),
                _ => throw new InvalidOperationException("World Entity Ref 目标类型无效"),
            },
            Fallback,
            warnings,
            fallbackOnly
        );
    }

    private static CharacterFact ProjectCharacterAt(
        GameLocation location,
        int x,
        int y,
        object target,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings,
        bool fallbackOnly = false
    )
    {
        CharacterFact Fallback() => ProjectCharacterFallback(location, x, y, target, refs);
        return ProjectCharacterOrFallback(
            () => target switch
            {
                FarmAnimal animal => ProjectFarmAnimal(location, x, y, animal, refs, warnings),
                NPC character => ProjectCharacter(location, x, y, character, refs),
                _ => throw new InvalidOperationException("Character Ref 目标类型无效"),
            },
            Fallback,
            warnings,
            fallbackOnly
        );
    }

    internal static WorldEntityFact ProjectEntityOrFallback(
        Func<WorldEntityFact> project,
        Func<WorldEntityFact> fallback,
        ICollection<QueryWarning> warnings,
        bool fallbackOnly
    ) => fallbackOnly
        ? WorldEntityProjectionGuard.ProjectFallback(fallback, warnings)
        : WorldEntityProjectionGuard.ProjectOrFallback(project, fallback, warnings);

    internal static CharacterFact ProjectCharacterOrFallback(
        Func<CharacterFact> project,
        Func<CharacterFact> fallback,
        ICollection<QueryWarning> warnings,
        bool fallbackOnly
    ) => fallbackOnly
        ? CharacterProjectionGuard.ProjectFallback(fallback, warnings)
        : CharacterProjectionGuard.ProjectOrFallback(project, fallback, warnings);

    private static bool IsFallbackGuard(string guard) =>
        guard.StartsWith("generic_fallback:", StringComparison.Ordinal)
        || guard.StartsWith("character_fallback:", StringComparison.Ordinal);

    private static (int X, int Y) CurrentEntityTile(ResolvedOpaqueRef resolved)
    {
        if (resolved.LocatorKind == RefLocatorKind.Object)
        {
            foreach (var pair in resolved.Location.Objects.Pairs)
            {
                if (ReferenceEquals(pair.Value, resolved.Target))
                    return ((int)pair.Key.X, (int)pair.Key.Y);
            }
            throw new InspectRefStaleException();
        }
        return resolved.LocatorKind switch
        {
            RefLocatorKind.TerrainFeature => FindTerrainTile(resolved),
            RefLocatorKind.Fridge => FindFridgeTile(resolved),
            RefLocatorKind.Furniture when resolved.Target is Furniture furniture =>
                ((int)furniture.TileLocation.X, (int)furniture.TileLocation.Y),
            RefLocatorKind.ResourceClump when resolved.Target is ResourceClump clump =>
                ((int)clump.Tile.X, (int)clump.Tile.Y),
            RefLocatorKind.Warp when resolved.Target is Warp warp => (warp.X, warp.Y),
            RefLocatorKind.Door => (resolved.X, resolved.Y),
            _ => throw new InvalidOperationException("World Entity Ref Locator 不支持检查"),
        };
    }

    private static (int X, int Y) FindTerrainTile(ResolvedOpaqueRef resolved)
    {
        foreach (var pair in resolved.Location.terrainFeatures.Pairs)
        {
            if (ReferenceEquals(pair.Value, resolved.Target))
                return ((int)pair.Key.X, (int)pair.Key.Y);
        }
        throw new InspectRefStaleException();
    }

    private static (int X, int Y) FindFridgeTile(ResolvedOpaqueRef resolved)
    {
        if (resolved.Location.GetFridgePosition() is not { } tile)
            throw new InspectRefStaleException();
        return (tile.X, tile.Y);
    }

    private static (int X, int Y) CurrentCharacterTile(object target) => target switch
    {
        NPC character => ((int)character.Tile.X, (int)character.Tile.Y),
        FarmAnimal animal => ((int)animal.Tile.X, (int)animal.Tile.Y),
        _ => throw new InvalidOperationException("Character Ref 目标类型无效"),
    };

    private static void PreserveInputRef(Ref projected, Ref input)
    {
        if (!string.Equals(projected.Value, input.Value, StringComparison.Ordinal))
            throw new InspectRefStaleException();
    }

    private static void PreserveWarningRefs(ICollection<QueryWarning> warnings, Ref input)
    {
        foreach (var warning in warnings)
        {
            if (warning.Ref is not null)
                warning.Ref = input.Clone();
        }
    }

    private static WorldEntityFact ProjectTree(
        GameLocation location,
        int x,
        int y,
        STree tree,
        OpaqueRefStore refs
    ) => new()
    {
        Ref = refs.GetOrCreate(
            tree,
            location,
            RefKind.WorldEntity,
            RefLocatorKind.TerrainFeature,
            x,
            y,
            $"tree:{tree.treeType.Value}"
        ),
        Kind = EntityKind.Tree,
        Position = Position(location, x, y),
        DisplayName = TreeDisplayName(tree.treeType.Value),
        Actionable = true,
        Tree = new TreeFact
        {
            GrowthStage = UInt(tree.growthStage.Value),
            Stump = tree.stump.Value,
            Tapped = tree.tapped.Value,
            Mossy = tree.hasMoss.Value,
            Health = tree.health.Value,
        },
    };

    private static WorldEntityFact ProjectFruitTree(
        GameLocation location,
        int x,
        int y,
        FruitTree tree,
        OpaqueRefStore refs
    )
    {
        var treeId = tree.treeId.Value ?? "";
        var fruitItemId = tree.fruit.FirstOrDefault()?.QualifiedItemId ?? "";
        return new WorldEntityFact
        {
            Ref = refs.GetOrCreate(
                tree,
                location,
                RefKind.WorldEntity,
                RefLocatorKind.TerrainFeature,
                x,
                y,
                $"fruit_tree:{treeId}"
            ),
            Kind = EntityKind.FruitTree,
            Position = Position(location, x, y),
            DisplayName = tree.GetDisplayName() ?? "",
            Actionable = true,
            FruitTree = new FruitTreeFact
            {
                FruitItemId = fruitItemId,
                GrowthStage = UInt(tree.growthStage.Value),
                DaysUntilMature = UInt(tree.daysUntilMature.Value),
                FruitCount = UInt(tree.fruit.Count),
                Stump = tree.stump.Value,
            },
        };
    }

    private static WorldEntityFact ProjectCrop(
        GameLocation location,
        int x,
        int y,
        HoeDirt dirt,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var crop = dirt.crop;
        var cropId = crop.netSeedIndex.Value ?? "";
        var harvestId = crop.indexOfHarvest.Value ?? "";
        var fact = new WorldEntityFact
        {
            Ref = refs.GetOrCreate(
                dirt,
                location,
                RefKind.WorldEntity,
                RefLocatorKind.TerrainFeature,
                x,
                y,
                $"crop:{cropId}:{harvestId}"
            ),
            Kind = EntityKind.Crop,
            Position = Position(location, x, y),
            DisplayName = CropDisplayName(harvestId),
            Actionable = true,
        };
        fact.Crop = CropFactProjector.Project(dirt, fact.Ref, warnings);
        return fact;
    }

    private static WorldEntityFact ProjectHoeDirt(
        GameLocation location,
        int x,
        int y,
        HoeDirt dirt,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var fact = new WorldEntityFact
        {
            Ref = refs.GetOrCreate(
                dirt,
                location,
                RefKind.WorldEntity,
                RefLocatorKind.TerrainFeature,
                x,
                y,
                "hoe_dirt"
            ),
            Kind = EntityKind.HoeDirt,
            Position = Position(location, x, y),
            DisplayName = "HoeDirt",
            HoeDirt = HoeDirtProjectionPolicy.Create(dirt.state.Value),
        };
        WorldEntityProjectionGuard.ApplyActionable(fact, dirt.isActionable, warnings);
        return fact;
    }

    private static WorldEntityFact ProjectObject(
        GameLocation location,
        int x,
        int y,
        SObject obj,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        if (obj is Chest chest)
            return ProjectContainer(location, x, y, chest, refs, RefLocatorKind.Object, warnings);
        if (obj.GetMachineData() is not null)
            return ProjectMachine(location, x, y, obj, refs, warnings);
        if (IsResourceNode(obj))
            return ProjectResourceNode(location, x, y, obj, refs);
        if (obj.IsSpawnedObject)
            return ProjectLooseItem(location, x, y, obj, refs);
        return ProjectGenericObject(location, x, y, obj, refs, warnings);
    }

    private static WorldEntityFact ProjectMachine(
        GameLocation location,
        int x,
        int y,
        SObject obj,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var fact = Entity(
            obj,
            location,
            x,
            y,
            refs,
            EntityKind.Machine,
            obj.DisplayName,
            null,
            $"machine:{obj.GetType().FullName}:{obj.QualifiedItemId}",
            fact => fact.Machine = MachineFactProjector.Project(obj, fact.Ref, warnings)
        );
        WorldEntityProjectionGuard.ApplyActionable(fact, () => obj.isActionable(Game1.player), warnings);
        return fact;
    }

    private static WorldEntityFact ProjectContainer(
        GameLocation location,
        int x,
        int y,
        Chest chest,
        OpaqueRefStore refs,
        RefLocatorKind locatorKind,
        ICollection<QueryWarning> warnings
    )
    {
        var itemCount = ChestInventoryReader.EnumerateSlots(chest, Game1.player)
            .Count(item => item is not null);
        var containerKind = ContainerKindClassifier.Classify(chest, locatorKind);
        var fact = Entity(
            chest,
            location,
            x,
            y,
            refs,
            EntityKind.Container,
            chest.DisplayName,
            null,
            ContainerKindClassifier.IdentityGuard(chest, locatorKind),
            entity => entity.Container = new ContainerFact
            {
                ContainerKind = containerKind,
                Capacity = UInt(chest.GetActualCapacity()),
                ItemCount = UInt(itemCount),
            },
            locatorKind
        );
        WorldEntityProjectionGuard.ApplyActionable(
            fact,
            () => chest.isActionable(Game1.player),
            warnings
        );
        return fact;
    }

    private static WorldEntityFact ProjectResourceNode(
        GameLocation location,
        int x,
        int y,
        SObject obj,
        OpaqueRefStore refs
    ) => Entity(
        obj,
        location,
        x,
        y,
        refs,
        EntityKind.ResourceNode,
        obj.DisplayName,
        true,
        $"resource_node:{obj.GetType().FullName}:{obj.QualifiedItemId}",
        fact => fact.ResourceNode = new ResourceNodeFact
        {
            NodeKind = ResourceNodeKind(obj),
            HitsToDestroy = UInt(obj.MinutesUntilReady),
            RequiredTool = obj.Name == "Twig" ? "axe" : obj.Name == "Weeds" ? "scythe" : "pickaxe",
        }
    );

    private static WorldEntityFact ProjectLooseItem(
        GameLocation location,
        int x,
        int y,
        SObject obj,
        OpaqueRefStore refs
    ) => Entity(
        obj,
        location,
        x,
        y,
        refs,
        EntityKind.LooseItem,
        obj.DisplayName,
        obj.CanBeGrabbed,
        $"loose_item:{obj.GetType().FullName}:{obj.QualifiedItemId}",
        fact => fact.LooseItem = new LooseItemFact
        {
            Item = ProjectItem(obj),
            CanPickUp = obj.CanBeGrabbed,
        }
    );

    private static WorldEntityFact ProjectGenericObject(
        GameLocation location,
        int x,
        int y,
        SObject obj,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var fact = Entity(
            obj,
            location,
            x,
            y,
            refs,
            EntityKind.GenericObject,
            obj.DisplayName,
            null,
            $"generic:{obj.GetType().FullName}:{obj.QualifiedItemId}",
            entity => entity.GenericObject = new GenericObjectFact
            {
                RuntimeType = obj.GetType().FullName ?? obj.GetType().Name,
                QualifiedItemId = obj.QualifiedItemId ?? "",
            }
        );
        WorldEntityProjectionGuard.ApplyActionable(fact, () => obj.isActionable(Game1.player), warnings);
        return fact;
    }

    private static WorldEntityFact ProjectGenericTerrainFeature(
        GameLocation location,
        int x,
        int y,
        TerrainFeature feature,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var fact = Entity(
            feature,
            location,
            x,
            y,
            refs,
            EntityKind.GenericObject,
            feature.GetType().Name,
            null,
            $"generic_terrain:{feature.GetType().FullName}",
            entity => entity.GenericObject = new GenericObjectFact
            {
                RuntimeType = feature.GetType().FullName ?? feature.GetType().Name,
                QualifiedItemId = "",
            },
            RefLocatorKind.TerrainFeature
        );
        WorldEntityProjectionGuard.ApplyActionable(fact, feature.isActionable, warnings);
        return fact;
    }

    private static WorldEntityFact ProjectGenericFallback(
        object target,
        GameLocation location,
        int x,
        int y,
        OpaqueRefStore refs,
        RefLocatorKind locatorKind,
        ICollection<QueryWarning> warnings
    )
    {
        var runtimeType = target.GetType().FullName ?? target.GetType().Name;
        var fact = Entity(
            target,
            location,
            x,
            y,
            refs,
            EntityKind.GenericObject,
            target.GetType().Name,
            null,
            $"generic_fallback:{runtimeType}",
            entity => entity.GenericObject = new GenericObjectFact
            {
                RuntimeType = runtimeType,
                QualifiedItemId = "",
            },
            locatorKind
        );
        // A typed projection may have failed in arbitrary third-party code. Keep fallback
        // strictly minimal: don't retry DisplayName, QualifiedItemId, or isActionable getters.
        WorldEntityProjectionGuard.MarkActionableUnknown(fact, warnings);
        return fact;
    }

    private static WorldEntityFact ProjectResourceClump(
        GameLocation location,
        int x,
        int y,
        ResourceClump clump,
        OpaqueRefStore refs
    )
    {
        var index = clump.parentSheetIndex.Value;
        var kind = ResourceClumpKind(index);
        var fact = Entity(
            clump,
            location,
            x,
            y,
            refs,
            EntityKind.ResourceClump,
            kind,
            true,
            $"resource_clump:{index}",
            fact => fact.ResourceClump = new ResourceClumpFact
            {
                ClumpKind = kind,
                Width = UInt(clump.width.Value),
                Height = UInt(clump.height.Value),
                Health = UInt((int)clump.health.Value),
                RequiredTool = index is 600 or 602 ? "axe" : "pickaxe",
                RequiredToolLevel = index switch
                {
                    600 => 1,
                    602 => 2,
                    622 or 148 => 3,
                    672 => 2,
                    _ => 0,
                },
            },
            RefLocatorKind.ResourceClump
        );
        return fact;
    }

    private static WorldEntityFact ProjectBed(
        GameLocation location,
        int x,
        int y,
        BedFurniture bed,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var occupied = OccupiedTiles(location, x, y, bed.GetBoundingBox());
        var fact = Entity(
            bed,
            location,
            x,
            y,
            refs,
            EntityKind.Bed,
            bed.DisplayName,
            null,
            $"bed:{bed.GetType().FullName}:{bed.QualifiedItemId}",
            fact =>
            {
                var bedSpot = bed.GetBedSpot();
                fact.Bed = new BedFact
                {
                    CanSleep = bed.bedType != BedFurniture.BedType.Child
                        && !bed.IsBeingSleptIn(),
                    SleepPosition = WorldProjectionPolicy.SleepPosition(
                        location.NameOrUniqueName,
                        bedSpot.X,
                        bedSpot.Y
                    ),
                };
                fact.Bed.OccupiedTiles.AddRange(occupied);
            },
            RefLocatorKind.Furniture
        );
        WorldEntityProjectionGuard.ApplyActionable(fact, () => bed.isActionable(Game1.player), warnings);
        return fact;
    }

    private static WorldEntityFact ProjectFurniture(
        GameLocation location,
        int x,
        int y,
        Furniture furniture,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var occupied = OccupiedTiles(location, x, y, furniture.GetBoundingBox());
        var fact = Entity(
            furniture,
            location,
            x,
            y,
            refs,
            EntityKind.Furniture,
            furniture.DisplayName,
            null,
            $"furniture:{furniture.GetType().FullName}:{furniture.QualifiedItemId}",
            fact =>
            {
                fact.Furniture = FurnitureFactProjector.Project(
                    furniture,
                    occupied,
                    fact.Ref,
                    warnings
                );
            },
            RefLocatorKind.Furniture
        );
        FurnitureFactProjector.ApplyActionability(fact, warnings);
        return fact;
    }

    private static WorldEntityFact ProjectWarp(
        GameLocation location,
        Warp warp,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var target = warp.TargetName ?? "";
        var targetLocationId = ResolveDestinationLocationId(target);
        var guard = $"warp:{target}:{warp.TargetX}:{warp.TargetY}:{warp.npcOnly.Value}";
        if (!WorldProjectionPolicy.HasResolvedDestination(targetLocationId))
        {
            var generic = Entity(
                warp,
                location,
                warp.X,
                warp.Y,
                refs,
                EntityKind.GenericObject,
                "Warp",
                null,
                guard,
                fact => fact.GenericObject = new GenericObjectFact
                {
                    RuntimeType = warp.GetType().FullName ?? warp.GetType().Name,
                    QualifiedItemId = "",
                },
                RefLocatorKind.Warp
            );
            WorldProjectionPolicy.ApplyUnknownActionability(generic, warnings);
            warnings.Add(WorldProjectionPolicy.UnresolvedDestinationWarning(generic.Ref));
            return generic;
        }
        var fact = Entity(
            warp,
            location,
            warp.X,
            warp.Y,
            refs,
            EntityKind.Warp,
            "Warp",
            null,
            guard,
            fact => fact.Warp = new WarpFact
            {
                Destination = new WorldPosition
                {
                    LocationId = targetLocationId,
                    X = warp.TargetX,
                    Y = warp.TargetY,
                },
                NpcOnly = warp.npcOnly.Value,
            },
            RefLocatorKind.Warp
        );
        WorldProjectionPolicy.ApplyWarpActionability(fact, warp.npcOnly.Value);
        return fact;
    }

    private static WorldEntityFact ProjectDoor(
        GameLocation location,
        Point tile,
        string targetLocation,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var identity = refs.GetLogicalIdentity(location, RefLocatorKind.Door, tile.X, tile.Y);
        var targetLocationId = ResolveDestinationLocationId(targetLocation);
        if (!WorldProjectionPolicy.HasResolvedDestination(targetLocationId))
        {
            var generic = Entity(
                identity,
                location,
                tile.X,
                tile.Y,
                refs,
                EntityKind.GenericObject,
                "Door",
                null,
                targetLocation,
                entity => entity.GenericObject = new GenericObjectFact
                {
                    RuntimeType = "stardew_valley.map_door",
                    QualifiedItemId = "",
                },
                RefLocatorKind.Door
            );
            WorldProjectionPolicy.ApplyUnknownActionability(generic, warnings);
            warnings.Add(WorldProjectionPolicy.UnresolvedDestinationWarning(generic.Ref));
            return generic;
        }
        var doorDetail = WorldProjectionPolicy.CreateDoorWithUnknownAccess(targetLocationId);
        var fact = Entity(
            identity,
            location,
            tile.X,
            tile.Y,
            refs,
            EntityKind.Door,
            "Door",
            null,
            targetLocation,
            entity => entity.Door = doorDetail,
            RefLocatorKind.Door
        );
        WorldProjectionPolicy.ApplyUnknownActionability(fact, warnings);
        try
        {
            if (location.getWarpFromDoor(tile) is { } warp)
                fact.Door.TargetTile = new TilePoint { X = warp.TargetX, Y = warp.TargetY };
        }
        catch
        {
            // Door metadata is optional; the door itself remains visible.
        }
        return fact;
    }

    private static WorldEntityFact ProjectDoorFallback(
        GameLocation location,
        Point tile,
        string targetLocation,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var identity = refs.GetLogicalIdentity(location, RefLocatorKind.Door, tile.X, tile.Y);
        var fact = Entity(
            identity,
            location,
            tile.X,
            tile.Y,
            refs,
            EntityKind.GenericObject,
            "Door",
            null,
            targetLocation,
            entity => entity.GenericObject = new GenericObjectFact
            {
                RuntimeType = "stardew_valley.map_door",
                QualifiedItemId = "",
            },
            RefLocatorKind.Door
        );
        WorldProjectionPolicy.ApplyUnknownActionability(fact, warnings);
        return fact;
    }

    private static CharacterFact ProjectCharacter(
        GameLocation location,
        int x,
        int y,
        NPC character,
        OpaqueRefStore refs
    )
    {
        var fact = CharacterBase(location, x, y, character, refs);
        switch (character)
        {
            case Monster monster:
                fact.Kind = CharacterKind.Monster;
                fact.Monster = new MonsterFact
                {
                    Health = monster.Health,
                    MaxHealth = monster.MaxHealth,
                    ContactDamage = monster.DamageToFarmer,
                };
                break;
            case Horse horse:
                fact.Kind = CharacterKind.Horse;
                fact.Horse = new HorseFact { HasRider = horse.rider is not null };
                break;
            case Pet pet:
                var pettedToday = pet.lastPetDay.TryGetValue(Game1.player.UniqueMultiplayerID, out var day)
                    && day >= Game1.Date.TotalDays;
                fact.Kind = CharacterKind.Pet;
                fact.Pet = new PetFact
                {
                    PetType = pet.petType.Value ?? "",
                    PettedToday = pettedToday,
                    Friendship = pet.friendshipTowardFarmer.Value,
                };
                break;
            default:
                var friendship = Game1.player.friendshipData.TryGetValue(character.Name, out var data)
                    ? data.Points
                    : 0;
                fact.Kind = CharacterKind.Npc;
                fact.Npc = new NpcFact
                {
                    CanSocialize = character.CanSocialize,
                    FriendshipPoints = friendship,
                    HasDialogue = character.TemporaryDialogue is { Count: > 0 },
                };
                break;
        }
        return fact;
    }

    private static CharacterFact ProjectFarmAnimal(
        GameLocation location,
        int x,
        int y,
        FarmAnimal animal,
        OpaqueRefStore refs,
        ICollection<QueryWarning> warnings
    )
    {
        var fact = new CharacterFact
        {
            Ref = refs.GetOrCreate(
                animal,
                location,
                RefKind.Character,
                RefLocatorKind.Character,
                x,
                y,
                $"farm_animal:{animal.myID.Value}"
            ),
            Kind = CharacterKind.FarmAnimal,
            Name = animal.Name ?? "",
            DisplayName = animal.displayName ?? animal.Name ?? "",
            Position = Position(location, x, y),
            Facing = DirectionOf(animal.FacingDirection),
        };
        fact.FarmAnimal = FarmAnimalFactProjector.Project(animal, fact.Ref, warnings);
        return fact;
    }

    private static CharacterFact ProjectCharacterFallback(
        GameLocation location,
        int x,
        int y,
        object character,
        OpaqueRefStore refs
    ) => new()
    {
        Ref = refs.GetOrCreate(
            character,
            location,
            RefKind.Character,
            RefLocatorKind.Character,
            x,
            y,
            $"character_fallback:{character.GetType().FullName}"
        ),
        Kind = character switch
        {
            Monster => CharacterKind.Monster,
            Horse => CharacterKind.Horse,
            Pet => CharacterKind.Pet,
            FarmAnimal => CharacterKind.FarmAnimal,
            NPC => CharacterKind.Npc,
            _ => CharacterKind.Unspecified,
        },
        Position = Position(location, x, y),
    };

    private static CharacterFact CharacterBase(
        GameLocation location,
        int x,
        int y,
        NPC character,
        OpaqueRefStore refs
    ) => new()
    {
        Ref = refs.GetOrCreate(
            character,
            location,
            RefKind.Character,
            RefLocatorKind.Character,
            x,
            y,
            $"character:{character.GetType().FullName}:{CharacterIdentity(character)}"
        ),
        Name = character.Name ?? "",
        DisplayName = character.displayName ?? character.Name ?? "",
        Position = Position(location, x, y),
        Facing = DirectionOf(character.FacingDirection),
    };

    private static WorldEntityFact Entity(
        object identity,
        GameLocation location,
        int x,
        int y,
        OpaqueRefStore refs,
        EntityKind kind,
        string? displayName,
        bool? actionable,
        string guard,
        Action<WorldEntityFact> setDetail,
        RefLocatorKind locatorKind = RefLocatorKind.Object,
        RefKind refKind = RefKind.WorldEntity
    )
    {
        var fact = new WorldEntityFact
        {
            Ref = refs.GetOrCreate(identity, location, refKind, locatorKind, x, y, guard),
            Kind = kind,
            Position = Position(location, x, y),
            DisplayName = displayName ?? "",
        };
        if (actionable.HasValue)
            fact.Actionable = actionable.Value;
        setDetail(fact);
        return fact;
    }

    private static ItemFact ProjectItem(Item item) => ItemFactProjector.Project(item);

    private static List<WorldPosition> OccupiedTiles(
        GameLocation location,
        int x,
        int y,
        Rectangle bounds
    )
    {
        var width = Math.Max(1, (bounds.Width + Game1.tileSize - 1) / Game1.tileSize);
        var height = Math.Max(1, (bounds.Height + Game1.tileSize - 1) / Game1.tileSize);
        var positions = new List<WorldPosition>(width * height);
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
                positions.Add(Position(location, x + dx, y + dy));
        }
        return positions;
    }

    private static void AddIfIncluded(
        ICollection<WorldEntityFact> facts,
        WorldEntityFact? fact,
        IReadOnlySet<EntityKind> kinds
    )
    {
        if (fact is not null && (kinds.Count == 0 || kinds.Contains(fact.Kind)))
            facts.Add(fact);
    }

    private static WorldPosition Position(GameLocation location, int x, int y) => new()
    {
        LocationId = location.NameOrUniqueName,
        X = x,
        Y = y,
    };

    private static string TerrainKind(GameLocation location, Vector2 tile, int x, int y)
    {
        if (location.terrainFeatures.TryGetValue(tile, out var feature))
            return feature switch
            {
                HoeDirt => "hoe_dirt",
                STree => "tree",
                FruitTree => "fruit_tree",
                Grass => "grass",
                Flooring => "flooring",
                _ => feature.GetType().Name,
            };
        return location.doesTileHaveProperty(x, y, "Type", "Back") ?? "";
    }

    private static bool IsResourceNode(SObject obj)
    {
        return obj.ItemId is
            "751" or "849" or "290" or "850" or "764" or "851" or "765" or "852"
            or "2" or "4" or "6" or "8" or "10" or "12" or "14" or "343" or "450";
    }

    private static string ResourceNodeKind(SObject obj) => obj.ItemId switch
    {
        "751" or "849" => "copper",
        "290" or "850" => "iron",
        "764" or "851" => "gold",
        "765" or "852" => "iridium",
        "2" or "4" or "6" or "8" or "10" or "12" or "14" => "gem",
        "343" or "450" => "stone",
        _ => obj.ItemId ?? "",
    };

    private static string ResourceClumpKind(int index) => index switch
    {
        148 => "quarry_boulder",
        600 => "large_stump",
        602 => "hollow_log",
        622 => "meteorite",
        672 => "large_rock",
        752 => "mine_rock_0",
        754 => "mine_rock_1",
        756 => "mine_rock_2",
        758 => "mine_rock_3",
        _ => $"clump_{index}",
    };

    private static string TreeDisplayName(string treeType) => treeType switch
    {
        "1" => "Oak Tree",
        "2" => "Maple Tree",
        "3" => "Pine Tree",
        "6" => "Palm Tree",
        "7" => "Mushroom Tree",
        "8" => "Mahogany Tree",
        _ => "Tree",
    };

    private static string CropDisplayName(string harvestId)
    {
        if (string.IsNullOrEmpty(harvestId))
            return "Crop";
        return ItemRegistry.GetData($"(O){harvestId}")?.DisplayName ?? "Crop";
    }

    private static string ResolveDestinationLocationId(string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
            return "";
        var resolved = GameLocationIdentity.FindExact(targetName)?.NameOrUniqueName ?? "";
        return WorldProjectionPolicy.HasResolvedDestination(resolved) ? resolved : "";
    }

    private static string CharacterIdentity(NPC character) => character switch
    {
        Horse horse => horse.HorseId.ToString("N"),
        Pet pet => pet.petId.Value.ToString("N"),
        _ => character.Name ?? "",
    };

    private static Direction DirectionOf(int value) => value switch
    {
        0 => Direction.Up,
        1 => Direction.Right,
        2 => Direction.Down,
        3 => Direction.Left,
        _ => Direction.Unspecified,
    };

    private static uint UInt(int value) => value <= 0 ? 0u : checked((uint)value);

}

internal readonly record struct ScanArea(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y) =>
        x >= X && x < X + Width && y >= Y && y < Y + Height;
}

internal static class HoeDirtProjectionPolicy
{
    public static HoeDirtFact Create(int state) => new() { Watered = state == 1 };
}

internal static class WorldProjectionPolicy
{
    public static bool HasResolvedDestination(string locationId) => LocationIdPolicy.IsValid(locationId);

    public static bool CropReady(bool dead, bool gameReady) => !dead && gameReady;

    public static CropHarvestAction HarvestActionFor(bool usesScythe) =>
        usesScythe ? Protocol.V1.CropHarvestAction.Scythe : Protocol.V1.CropHarvestAction.Interact;

    public static WorldPosition SleepPosition(string locationId, int x, int y) => new()
    {
        LocationId = locationId,
        X = x,
        Y = y,
    };

    public static void ApplyWarpActionability(WorldEntityFact fact, bool npcOnly) =>
        fact.Actionable = !npcOnly;

    public static void ApplyUnknownActionability(
        WorldEntityFact fact,
        ICollection<QueryWarning> warnings
    )
    {
        fact.ClearActionable();
        WorldEntityProjectionGuard.MarkActionableUnknown(fact, warnings);
    }

    public static DoorFact CreateDoorWithUnknownAccess(string targetLocationId) =>
        new() { TargetLocationId = targetLocationId };

    public static QueryWarning UnresolvedDestinationWarning(Ref reference) => new()
    {
        Code = "WORLD_DESTINATION_UNRESOLVED",
        Message = "目标不是当前已加载 Location 的 NameOrUniqueName；实体已降级为 GenericObjectFact",
        Ref = reference.Clone(),
    };
}

internal static class WorldEntityProjectionGuard
{
    public static WorldEntityFact ProjectOrFallback(
        Func<WorldEntityFact> project,
        Func<WorldEntityFact> fallback,
        ICollection<QueryWarning> warnings
    )
    {
        try
        {
            return project();
        }
        catch
        {
            return ProjectFallback(fallback, warnings);
        }
    }

    public static WorldEntityFact ProjectFallback(
        Func<WorldEntityFact> fallback,
        ICollection<QueryWarning> warnings
    )
    {
        var fact = fallback();
        warnings.Add(new QueryWarning
        {
            Code = "ENTITY_PROJECTION_FALLBACK",
            Message = "实体类型化投影失败，已降级为 GenericObjectFact",
            Ref = fact.Ref.Clone(),
        });
        return fact;
    }

    public static void ApplyActionable(
        WorldEntityFact fact,
        Func<bool> read,
        ICollection<QueryWarning> warnings
    )
    {
        try
        {
            fact.Actionable = read();
        }
        catch
        {
            fact.ClearActionable();
            MarkActionableUnknown(fact, warnings);
        }
    }

    public static void MarkActionableUnknown(
        WorldEntityFact fact,
        ICollection<QueryWarning> warnings
    )
    {
        if (warnings.Any(warning =>
            warning.Code == "ENTITY_ACTIONABLE_UNKNOWN"
            && string.Equals(warning.Ref?.Value, fact.Ref.Value, StringComparison.Ordinal)))
            return;
        warnings.Add(new QueryWarning
        {
            Code = "ENTITY_ACTIONABLE_UNKNOWN",
            Message = "实体可交互性无法通过无副作用只读 API 可靠判断",
            Ref = fact.Ref.Clone(),
        });
    }
}

internal static class CharacterProjectionGuard
{
    public static CharacterFact ProjectOrFallback(
        Func<CharacterFact> project,
        Func<CharacterFact> fallback,
        ICollection<QueryWarning> warnings
    )
    {
        try
        {
            return project();
        }
        catch
        {
            return ProjectFallback(fallback, warnings);
        }
    }

    public static CharacterFact ProjectFallback(
        Func<CharacterFact> fallback,
        ICollection<QueryWarning> warnings
    )
    {
        var fact = fallback();
        warnings.Add(new QueryWarning
        {
            Code = "CHARACTER_PROJECTION_FALLBACK",
            Message = "角色类型化投影失败，已保留最小 CharacterFact",
            Ref = fact.Ref.Clone(),
        });
        return fact;
    }
}
