using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

/// <summary>
/// 把公共位置或进程内 Ref 解析为一次命令私有的目标锁。
/// Ref 的对象身份和动作 Tile 在启动时固定，后续只做同一对象重验。
/// </summary>
internal interface IActionTargetResolver
{
    ActionTargetResolution Resolve(WorldPosition? position, Ref? reference);
    Error? Revalidate(LockedActionTarget target);
}

internal sealed record LockedActionTarget(
    string LocationId,
    int X,
    int Y,
    Ref? SourceRef,
    object? RuntimeIdentity
);

internal sealed record ActionTargetResolution(
    LockedActionTarget? Target,
    Error? Error
);

internal sealed class ActionTargetResolver : IActionTargetResolver
{
    private static readonly IReadOnlySet<RefKind> AllowedKinds =
        new HashSet<RefKind> { RefKind.WorldEntity, RefKind.Character };

    private readonly OpaqueRefStore _refs;

    public ActionTargetResolver(OpaqueRefStore refs) => _refs = refs;

    public ActionTargetResolution Resolve(WorldPosition? position, Ref? reference)
    {
        if (position is not null && reference is null)
        {
            var location = GameLocationIdentity.FindExact(position.LocationId);
            if (location is null)
                return Failed(ErrorCode.NotFound, "目标 Location 不存在");
            return Succeeded(new LockedActionTarget(
                location.NameOrUniqueName,
                position.X,
                position.Y,
                null,
                null
            ));
        }

        if (reference is null || position is not null)
            return Failed(ErrorCode.InvalidArgument, "动作必须提供唯一目标");

        var resolution = _refs.Resolve(reference, AllowedKinds, out var resolved);
        if (resolution.Status != RefStatus.Resolved || resolved is null)
            return new ActionTargetResolution(null, resolution.Error ?? Error(
                ErrorCode.Internal,
                "目标 Ref 无法解析"
            ));
        if (!TryCurrentTile(resolved, out var tile))
            return Failed(ErrorCode.StaleRef, "目标 Ref 已失效");

        return Succeeded(new LockedActionTarget(
            resolved.Location.NameOrUniqueName,
            tile.X,
            tile.Y,
            reference.Clone(),
            resolved.Target
        ));
    }

    public Error? Revalidate(LockedActionTarget target)
    {
        if (target.SourceRef is null)
            return GameLocationIdentity.FindExact(target.LocationId) is null
                ? Error(ErrorCode.ExecutionFailed, "目标 Location 在执行中失效")
                : null;

        var resolution = _refs.Resolve(target.SourceRef, AllowedKinds, out var resolved);
        if (resolution.Status != RefStatus.Resolved
            || resolved is null
            || !ReferenceEquals(resolved.Target, target.RuntimeIdentity)
            || !string.Equals(
                resolved.Location.NameOrUniqueName,
                target.LocationId,
                StringComparison.OrdinalIgnoreCase
            )
            || !TryCurrentTile(resolved, out var tile)
            || tile.X != target.X
            || tile.Y != target.Y)
            return Error(ErrorCode.ExecutionFailed, "目标在命令执行中移动或失效");
        return null;
    }

    private static bool TryCurrentTile(ResolvedOpaqueRef resolved, out Point tile)
    {
        tile = default;
        switch (resolved.LocatorKind)
        {
            case RefLocatorKind.Character:
                tile = resolved.Target switch
                {
                    NPC npc => new Point((int)npc.Tile.X, (int)npc.Tile.Y),
                    FarmAnimal animal => new Point((int)animal.Tile.X, (int)animal.Tile.Y),
                    _ => default,
                };
                return resolved.Target is NPC or FarmAnimal;
            case RefLocatorKind.Object:
                foreach (var pair in resolved.Location.Objects.Pairs)
                {
                    if (ReferenceEquals(pair.Value, resolved.Target))
                    {
                        tile = new Point((int)pair.Key.X, (int)pair.Key.Y);
                        return true;
                    }
                }
                return false;
            case RefLocatorKind.TerrainFeature:
                foreach (var pair in resolved.Location.terrainFeatures.Pairs)
                {
                    if (ReferenceEquals(pair.Value, resolved.Target))
                    {
                        tile = new Point((int)pair.Key.X, (int)pair.Key.Y);
                        return true;
                    }
                }
                return false;
            case RefLocatorKind.Fridge:
                if (resolved.Location.GetFridgePosition() is { } fridge)
                {
                    tile = fridge;
                    return true;
                }
                return false;
            case RefLocatorKind.Furniture when resolved.Target is Furniture furniture:
                tile = new Point((int)furniture.TileLocation.X, (int)furniture.TileLocation.Y);
                return true;
            case RefLocatorKind.ResourceClump when resolved.Target is ResourceClump clump:
                tile = new Point((int)clump.Tile.X, (int)clump.Tile.Y);
                return true;
            case RefLocatorKind.Warp when resolved.Target is Warp warp:
                tile = new Point(warp.X, warp.Y);
                return true;
            case RefLocatorKind.Door:
                tile = new Point(resolved.X, resolved.Y);
                return true;
            default:
                return false;
        }
    }

    private static ActionTargetResolution Succeeded(LockedActionTarget target) =>
        new(target, null);

    private static ActionTargetResolution Failed(ErrorCode code, string message) =>
        new(null, Error(code, message));

    private static Error Error(ErrorCode code, string message) =>
        new() { Code = code, Message = message };
}
