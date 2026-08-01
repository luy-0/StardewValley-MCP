using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace StardewValleyMcp.Mod;

/// <summary>
/// 同一 Location 内的窄导航端口。它只拥有当前命令创建的
/// <see cref="PathFindController"/>，不建立全局移动队列。
/// </summary>
internal interface ILocalNavigationDriver
{
    NavigationPlayerState Capture();
    LocalNavigationStart Start(int x, int y);
    bool TryFace(int direction);
    void Stop();
}

internal sealed record NavigationPlayerState(
    bool IsReady,
    bool CanMove,
    string LocationId,
    int X,
    int Y,
    int FacingDirection,
    bool OwnedPathActive
);

internal enum LocalNavigationStart
{
    Started,
    AlreadyThere,
    NotReady,
    NoPath,
}

internal sealed class StardewLocalNavigationDriver : ILocalNavigationDriver
{
    private PathFindController? _ownedController;

    public NavigationPlayerState Capture()
    {
        if (!Context.IsWorldReady
            || Game1.player is not { } player
            || Game1.currentLocation is not { } location)
            return new NavigationPlayerState(false, false, "", 0, 0, -1, false);

        return new NavigationPlayerState(
            true,
            Context.CanPlayerMove
                && Game1.activeClickableMenu is null
                && !player.UsingTool,
            location.NameOrUniqueName,
            (int)player.Tile.X,
            (int)player.Tile.Y,
            player.FacingDirection,
            _ownedController is not null
                && ReferenceEquals(player.controller, _ownedController)
        );
    }

    public LocalNavigationStart Start(int x, int y)
    {
        var state = Capture();
        if (!state.IsReady || !state.CanMove)
            return LocalNavigationStart.NotReady;
        if (state.X == x && state.Y == y)
            return LocalNavigationStart.AlreadyThere;

        var player = Game1.player;
        var location = Game1.currentLocation;
        if (player is null || location is null)
            return LocalNavigationStart.NotReady;

        Stop();
        var controller = new PathFindController(
            player,
            location,
            new Point(x, y),
            -1,
            static (_, _) => { }
        );
        if (controller.pathToEndPoint is null || controller.pathToEndPoint.Count == 0)
        {
            if (ReferenceEquals(player.controller, controller))
                player.controller = null;
            player.movementDirections.Clear();
            player.Halt();
            return LocalNavigationStart.NoPath;
        }

        _ownedController = controller;
        player.controller = controller;
        return LocalNavigationStart.Started;
    }

    public bool TryFace(int direction)
    {
        var state = Capture();
        if (!state.IsReady || state.OwnedPathActive || direction is < 0 or > 3)
            return false;
        Game1.player.faceDirection(direction);
        return Game1.player.FacingDirection == direction;
    }

    public void Stop()
    {
        if (Game1.player is not { } player)
        {
            _ownedController = null;
            return;
        }
        if (_ownedController is not null && ReferenceEquals(player.controller, _ownedController))
            player.controller = null;
        _ownedController = null;
        player.movementDirections.Clear();
        player.Halt();
    }
}
