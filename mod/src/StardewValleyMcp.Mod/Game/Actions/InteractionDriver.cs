using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace StardewValleyMcp.Mod;

/// <summary>
/// 一次交互命令可观察的游戏事实。这里只暴露判断交互效果所需的窄事实，
/// 不签发 Ref，也不持有跨命令输入队列。
/// </summary>
internal sealed record InteractionObservation(
    bool IsReady,
    bool CanAct,
    bool HeldItemAllowed,
    string LocationId,
    int PlayerX,
    int PlayerY,
    int FacingDirection,
    int GrabX,
    int GrabY,
    double Energy,
    string MenuState,
    string InventoryState,
    string RelationshipState,
    string TargetState
);

internal interface IInteractionDriver
{
    InteractionObservation Observe(int targetX, int targetY);
    bool TryFace(int direction);
    bool BeginMicroMove(int direction);
    void StopMicroMove();
    void Submit(int targetX, int targetY);
}

/// <summary>
/// 使用游戏公开动作语义执行交互。微移仅写 Farmer 自身方向状态，
/// 并由调用方在对齐或离开起始 Tile 时立即停止。
/// </summary>
internal sealed class StardewInteractionDriver : IInteractionDriver
{
    public InteractionObservation Observe(int targetX, int targetY)
    {
        if (!Context.IsWorldReady
            || Game1.player is not { } player
            || Game1.currentLocation is not { } location)
            return Unavailable();

        var grab = player.GetGrabTile();
        return new InteractionObservation(
            true,
            Context.CanPlayerMove
                && Game1.activeClickableMenu is null
                && !Game1.eventUp
                && player.CanMove
                && !player.UsingTool,
            player.CurrentItem is null or Tool,
            location.NameOrUniqueName,
            (int)player.Tile.X,
            (int)player.Tile.Y,
            player.FacingDirection,
            (int)grab.X,
            (int)grab.Y,
            player.Stamina,
            MenuState(),
            InventoryState(player),
            RelationshipState(player, location, targetX, targetY),
            TargetState(location, targetX, targetY)
        );
    }

    public bool TryFace(int direction)
    {
        if (!CanControlPlayer(direction, out var player))
            return false;
        player.faceDirection(direction);
        return player.FacingDirection == direction;
    }

    public bool BeginMicroMove(int direction)
    {
        if (!CanControlPlayer(direction, out var player))
            return false;
        StopMicroMove();
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
        if (!player.movementDirections.Contains(direction))
            return false;
        player.MovePosition(Game1.currentGameTime, Game1.viewport, Game1.currentLocation);
        player.movementDirections.Clear();
        player.Halt();
        player.faceDirection(direction);
        return true;
    }

    public void StopMicroMove()
    {
        if (Game1.player is not { } player)
            return;
        player.movementDirections.Clear();
        player.Halt();
    }

    public void Submit(int targetX, int targetY)
    {
        if (!Context.IsWorldReady
            || Game1.player is not { } player
            || Game1.currentLocation is null)
            throw new InvalidOperationException("游戏世界尚未就绪");
        Game1.tryToCheckAt(new Vector2(targetX, targetY), player);
    }

    private static bool CanControlPlayer(int direction, out Farmer player)
    {
        player = Game1.player!;
        return direction is >= 0 and <= 3
            && Context.IsWorldReady
            && Game1.player is not null
            && Game1.currentLocation is not null
            && Game1.activeClickableMenu is null
            && !Game1.eventUp
            && Game1.player.CanMove
            && !Game1.player.UsingTool;
    }

    private static InteractionObservation Unavailable() => new(
        false,
        false,
        false,
        "",
        0,
        0,
        -1,
        0,
        0,
        0,
        "",
        "",
        "",
        ""
    );

    private static string MenuState()
    {
        var menu = Game1.activeClickableMenu;
        return menu is null
            ? "none"
            : $"{menu.GetType().FullName}:{RuntimeHelpers.GetHashCode(menu)}";
    }

    private static string InventoryState(Farmer player)
    {
        var value = new StringBuilder().Append(player.CurrentToolIndex).Append('|');
        for (var index = 0; index < player.Items.Count; index++)
        {
            var item = player.Items[index];
            value.Append(index).Append(':');
            if (item is not null)
            {
                value.Append(item.QualifiedItemId)
                    .Append(':')
                    .Append(item.Stack)
                    .Append(':')
                    .Append(item is StardewValley.Object obj ? obj.Quality : 0);
            }
            value.Append('|');
        }
        return value.ToString();
    }

    private static string RelationshipState(
        Farmer player,
        GameLocation location,
        int targetX,
        int targetY
    )
    {
        var character = location.isCharacterAtTile(new Vector2(targetX, targetY));
        return character is not null
            && player.friendshipData.TryGetValue(character.Name, out var friendship)
                ? $"{character.Name}:{friendship.Points}"
                : "none";
    }

    private static string TargetState(GameLocation location, int x, int y)
    {
        var tile = new Vector2(x, y);
        var value = new StringBuilder();
        if (location.Objects.TryGetValue(tile, out var obj))
        {
            value.Append("object:")
                .Append(RuntimeHelpers.GetHashCode(obj)).Append(':')
                .Append(obj.QualifiedItemId).Append(':')
                .Append(obj.Stack).Append(':')
                .Append(obj.readyForHarvest.Value).Append(':')
                .Append(obj.MinutesUntilReady);
            if (obj.heldObject.Value is { } held)
                value.Append(':').Append(held.QualifiedItemId).Append(':').Append(held.Stack);
        }
        else if (location.terrainFeatures.TryGetValue(tile, out var feature))
        {
            value.Append("terrain:")
                .Append(feature.GetType().FullName).Append(':')
                .Append(RuntimeHelpers.GetHashCode(feature));
        }
        else if (location.isCharacterAtTile(tile) is { } character)
        {
            value.Append("character:")
                .Append(character.GetType().FullName).Append(':')
                .Append(RuntimeHelpers.GetHashCode(character)).Append(':')
                .Append(character.Name).Append(':')
                .Append(character.TemporaryDialogue?.Count ?? 0);
        }
        else
        {
            value.Append("empty");
        }
        return value.ToString();
    }
}
