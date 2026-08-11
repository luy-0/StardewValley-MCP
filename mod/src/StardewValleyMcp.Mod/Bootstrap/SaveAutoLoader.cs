using System.Diagnostics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace StardewValleyMcp.Mod;

/// <summary>在当前 SMAPI 进程内自动选择一个明确指定的存档。</summary>
internal sealed class SaveAutoLoader
{
    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private readonly GameAdvancePolicy _gameAdvancePolicy;
    private readonly string _saveFolderName = "";
    private readonly TimeSpan _timeout;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private LoadGameMenu? _loadMenu;
    private State _state;

    public SaveAutoLoader(
        IModHelper helper,
        IMonitor monitor,
        ModConfig config,
        GameAdvancePolicy gameAdvancePolicy
    )
    {
        _helper = helper;
        _monitor = monitor;
        _gameAdvancePolicy = gameAdvancePolicy;

        if (!config.AutoLoadSave)
        {
            _state = State.Disabled;
            return;
        }

        _saveFolderName = config.AutoLoadSaveName.Trim();
        if (_saveFolderName.Length == 0)
        {
            _state = State.Failed;
            monitor.Log("[AutoLoad] 已启用自动加载，但 AutoLoadSaveName 为空。", LogLevel.Error);
            return;
        }

        var timeoutSeconds = Math.Clamp(config.AutoLoadTimeoutSeconds, 30, 600);
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        _state = State.WaitingForTitle;
        helper.Events.GameLoop.UpdateTicking += OnUpdateTicking;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        monitor.Log($"[AutoLoad] 等待标题菜单，将加载存档目录 '{_saveFolderName}'。", LogLevel.Info);
    }

    private void OnUpdateTicking(object? sender, UpdateTickingEventArgs eventArgs)
    {
        if (_state is State.Disabled or State.Completed or State.Failed)
            return;

        EnsureGameCanAdvanceWhileUnfocused();
        if (_stopwatch.Elapsed > _timeout)
        {
            Fail($"等待自动加载完成超时（{_timeout.TotalSeconds:0} 秒）。");
            return;
        }

        if (_state == State.WaitingForTitle)
        {
            if (Game1.activeClickableMenu is not TitleMenu titleMenu)
                return;

            _loadMenu = new LoadGameMenu();
            titleMenu.ForceSubmenu(_loadMenu);
            _state = State.ScanningSaves;
            _monitor.Log("[AutoLoad] 已打开原生存档扫描菜单。", LogLevel.Info);
            return;
        }

        if (_state != State.ScanningSaves || _loadMenu is null || _loadMenu.IsDoingTask())
            return;

        var slot = _loadMenu.MenuSlots
            .OfType<LoadGameMenu.SaveFileSlot>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Farmer.slotName, _saveFolderName, StringComparison.Ordinal));

        if (slot is null)
        {
            Fail($"原生存档扫描完成，但未找到目录 '{_saveFolderName}'。");
            return;
        }
        if (slot.versionComparison < 0)
        {
            Fail($"存档 '{_saveFolderName}' 来自更高版本的游戏，拒绝自动加载。");
            return;
        }

        _state = State.Loading;
        _monitor.Log($"[AutoLoad] 已找到角色 '{slot.Farmer.Name}'，开始加载 '{_saveFolderName}'。", LogLevel.Info);
        slot.Activate();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs eventArgs)
    {
        if (_state != State.Loading)
            return;

        if (!string.Equals(Constants.SaveFolderName, _saveFolderName, StringComparison.Ordinal))
        {
            Fail($"收到 SaveLoaded，但实际目录为 '{Constants.SaveFolderName}'，预期为 '{_saveFolderName}'。");
            return;
        }

        _state = State.Completed;
        Unsubscribe();
        _monitor.Log($"[AutoLoad] 自动加载完成：'{Constants.SaveFolderName}'，玩家 '{Game1.player.Name}'，位置 '{Game1.currentLocation?.NameOrUniqueName}'。", LogLevel.Info);
    }

    private void EnsureGameCanAdvanceWhileUnfocused()
    {
        _gameAdvancePolicy.EnsureGameCanAdvance();
    }

    private void Fail(string message)
    {
        _state = State.Failed;
        _gameAdvancePolicy.RestoreIfWorldNotReady();
        Unsubscribe();
        _monitor.Log($"[AutoLoad] {message}", LogLevel.Error);
    }

    private void Unsubscribe()
    {
        _helper.Events.GameLoop.UpdateTicking -= OnUpdateTicking;
        _helper.Events.GameLoop.SaveLoaded -= OnSaveLoaded;
    }

    private enum State
    {
        Disabled,
        WaitingForTitle,
        ScanningSaves,
        Loading,
        Completed,
        Failed
    }
}
