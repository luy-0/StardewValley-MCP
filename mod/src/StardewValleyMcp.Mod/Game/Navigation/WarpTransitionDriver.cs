using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace StardewValleyMcp.Mod;

internal interface IWarpTransitionDriver
{
    bool IsTransitionPending { get; }
    bool BeginWalkThrough(int direction);
    bool SubmitDoor(int x, int y);
    void Stop();
}

internal sealed class StardewWarpTransitionDriver : IWarpTransitionDriver
{
    public bool IsTransitionPending => Game1.isWarping || Game1.locationRequest is not null;

    public bool BeginWalkThrough(int direction)
    {
        if (!Context.IsWorldReady
            || Game1.player is not { } player
            || Game1.currentLocation is null
            || Game1.activeClickableMenu is not null
            || player.UsingTool
            || direction is < 0 or > 3)
            return false;

        Stop();
        player.faceDirection(direction);
        switch (direction)
        {
            case 0:
                player.SetMovingUp(true);
                break;
            case 1:
                player.SetMovingRight(true);
                break;
            case 2:
                player.SetMovingDown(true);
                break;
            case 3:
                player.SetMovingLeft(true);
                break;
        }
        return player.movementDirections.Contains(direction);
    }

    public bool SubmitDoor(int x, int y)
    {
        if (!Context.IsWorldReady
            || Game1.player is not { } player
            || Game1.currentLocation is null
            || Game1.activeClickableMenu is not null
            || player.UsingTool)
            return false;
        return Game1.tryToCheckAt(new Vector2(x, y), player);
    }

    public void Stop()
    {
        if (Game1.player is not { } player)
            return;
        player.movementDirections.Clear();
        player.Halt();
    }
}
