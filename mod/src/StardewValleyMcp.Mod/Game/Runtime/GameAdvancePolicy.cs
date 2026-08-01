using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewValleyMcp.Mod;

/// <summary>
/// 本地控制服务运行时保持单机世界在窗口失焦后继续更新。
/// 这里只管理游戏暂停偏好，不模拟输入，也不改变窗口激活状态。
/// </summary>
internal sealed class GameAdvancePolicy
{
    private bool? _originalPauseWhenOutOfFocus;

    public GameAdvancePolicy(IModHelper helper)
    {
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.UpdateTicking += OnUpdateTicking;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs eventArgs)
    {
        EnsureGameCanAdvance();
    }

    private void OnUpdateTicking(object? sender, UpdateTickingEventArgs eventArgs)
    {
        if (!Context.IsWorldReady)
            return;

        EnsureGameCanAdvance();
    }

    private void EnsureGameCanAdvance()
    {
        if (Game1.options is null)
            return;

        _originalPauseWhenOutOfFocus ??= Game1.options.pauseWhenOutOfFocus;
        Game1.options.pauseWhenOutOfFocus = false;
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs eventArgs)
    {
        Restore();
    }

    private void Restore()
    {
        if (_originalPauseWhenOutOfFocus.HasValue && Game1.options is not null)
            Game1.options.pauseWhenOutOfFocus = _originalPauseWhenOutOfFocus.Value;
        _originalPauseWhenOutOfFocus = null;
    }
}
