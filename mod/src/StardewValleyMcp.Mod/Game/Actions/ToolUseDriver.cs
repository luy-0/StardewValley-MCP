using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Tools;

namespace StardewValleyMcp.Mod;

internal enum SupportedToolKind
{
    Unsupported,
    Axe,
    Pickaxe,
    Hoe,
    WateringCan,
    Scythe,
}

/// <summary>
/// 一次工具动作所需的窄游戏事实。Handler 只观察公开工具生命周期，
/// 不维护共享输入队列，也不模拟全局键盘状态。
/// </summary>
internal sealed record ToolUseObservation(
    bool IsReady,
    bool CanSubmit,
    string LocationId,
    int PlayerX,
    int PlayerY,
    int FacingDirection,
    object? ToolIdentity,
    SupportedToolKind ToolKind,
    string ToolQualifiedItemId,
    int MaxChargeLevel,
    int ToolPower,
    int SwingTicker,
    bool UsingTool,
    bool CanReleaseTool,
    bool CanMove,
    bool PauseForSingleAnimation,
    bool LastClickIsZero,
    double Energy
);

internal interface IToolUseDriver
{
    ToolUseObservation Observe();
    bool TryFace(int direction, object toolIdentity);
    bool BeginUse(object toolIdentity, int targetX, int targetY);
    bool IncreaseCharge(object toolIdentity);
    bool Release(object toolIdentity);
}

/// <summary>
/// 通过 Farmer 与 Tool 的公开语义驱动一次动作。失焦时由运行期保证游戏
/// Update 继续推进；这里不安装 InputSimulator，也不写 SMAPI 私有按键状态。
/// </summary>
internal sealed class StardewToolUseDriver : IToolUseDriver
{
    public ToolUseObservation Observe()
    {
        if (!Context.IsWorldReady
            || Game1.player is not { } player
            || Game1.currentLocation is not { } location)
            return Unavailable();

        var tool = player.CurrentTool;
        var kind = Classify(tool);
        return new ToolUseObservation(
            true,
            Context.CanPlayerMove
                && Game1.activeClickableMenu is null
                && !Game1.eventUp
                && player.CanMove
                && !player.UsingTool,
            location.NameOrUniqueName,
            (int)player.Tile.X,
            (int)player.Tile.Y,
            player.FacingDirection,
            tool,
            kind,
            tool?.QualifiedItemId ?? "",
            MaxCharge(tool, kind),
            player.toolPower.Value,
            tool?.swingTicker ?? 0,
            player.UsingTool,
            player.canReleaseTool,
            player.CanMove,
            player.FarmerSprite.PauseForSingleAnimation,
            player.lastClick == Vector2.Zero,
            player.Stamina
        );
    }

    public bool TryFace(int direction, object toolIdentity)
    {
        if (!CanControl(toolIdentity, requireIdle: true, out var player))
            return false;
        player.faceDirection(direction);
        return player.FacingDirection == direction;
    }

    public bool BeginUse(object toolIdentity, int targetX, int targetY)
    {
        if (!CanControl(toolIdentity, requireIdle: true, out var player))
            return false;
        // 对齐 Game1.pressUseToolButton 的单次按下初始化，避免上一项蓄力工具
        // 遗留的 power/hold 污染新动作；不触碰动画或强制移动状态。
        player.toolPower.Value = 0;
        player.toolHold.Value = 0;
        player.lastClick = new Vector2(targetX * Game1.tileSize + Game1.tileSize / 2, targetY * Game1.tileSize + Game1.tileSize / 2);
        player.BeginUsingTool();
        return true;
    }

    public bool IncreaseCharge(object toolIdentity)
    {
        if (!CanControl(toolIdentity, requireIdle: false, out var player)
            || !player.UsingTool
            || !player.canReleaseTool)
            return false;
        player.toolPowerIncrease();
        return true;
    }

    public bool Release(object toolIdentity)
    {
        if (!CanControl(toolIdentity, requireIdle: false, out var player)
            || !player.UsingTool
            || !player.canReleaseTool)
            return false;
        player.EndUsingTool();
        return true;
    }

    private static bool CanControl(
        object toolIdentity,
        bool requireIdle,
        out Farmer player
    )
    {
        player = Game1.player!;
        return Context.IsWorldReady
            && Game1.player is not null
            && Game1.currentLocation is not null
            && Game1.activeClickableMenu is null
            && !Game1.eventUp
            && ReferenceEquals(Game1.player.CurrentTool, toolIdentity)
            && (!requireIdle || (Game1.player.CanMove && !Game1.player.UsingTool));
    }

    private static SupportedToolKind Classify(Tool? tool) => tool switch
    {
        Axe => SupportedToolKind.Axe,
        Pickaxe => SupportedToolKind.Pickaxe,
        Hoe => SupportedToolKind.Hoe,
        WateringCan => SupportedToolKind.WateringCan,
        MeleeWeapon weapon when weapon.isScythe() => SupportedToolKind.Scythe,
        _ => SupportedToolKind.Unsupported,
    };

    private static int MaxCharge(Tool? tool, SupportedToolKind kind)
    {
        if (tool is null || kind is not (SupportedToolKind.Hoe or SupportedToolKind.WateringCan))
            return 0;
        var reaching = tool.hasEnchantmentOfType<ReachingToolEnchantment>() ? 1 : 0;
        return Math.Min(5, Math.Max(0, tool.UpgradeLevel + reaching));
    }

    private static ToolUseObservation Unavailable() => new(
        false,
        false,
        "",
        0,
        0,
        -1,
        null,
        SupportedToolKind.Unsupported,
        "",
        0,
        0,
        0,
        false,
        false,
        false,
        false,
        true,
        0
    );
}
