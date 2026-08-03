using System.Reflection;
using StardewModdingAPI;
using StardewValley;

namespace StardewValleyMcp.Mod;

internal interface IButtonOverride
{
    void SetActionButton(bool pressed);
}

/// <summary>
/// 在游戏原生交互检查期间建立一次动作键按下边沿，并保证随后释放。
/// 这里只负责单次输入状态，不持有跨命令队列。
/// </summary>
internal sealed class ActionButtonInput
{
    private readonly IButtonOverride _buttons;

    internal ActionButtonInput()
        : this(new SmapiButtonOverride())
    {
    }

    internal ActionButtonInput(IButtonOverride buttons)
    {
        _buttons = buttons;
    }

    internal void Submit(Action action)
    {
        try
        {
            _buttons.SetActionButton(true);
            action();
        }
        finally
        {
            _buttons.SetActionButton(false);
        }
    }
}

/// <summary>
/// 通过 SMAPI 的输入状态覆盖建立按下与释放语义。
/// ApplyOverrides 会立即同步状态，因此失焦时也不会遗留持续按键。
/// </summary>
internal sealed class SmapiButtonOverride : IButtonOverride
{
    private object? _inputState;
    private MethodInfo? _overrideButton;
    private MethodInfo? _applyOverrides;

    public void SetActionButton(bool pressed)
    {
        EnsureInitialized();
        try
        {
            _overrideButton!.Invoke(_inputState, new object[] { SButton.X, pressed });
            _applyOverrides!.Invoke(_inputState, null);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"无法{(pressed ? "按下" : "释放")}交互键",
                exception.InnerException ?? exception
            );
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"无法{(pressed ? "按下" : "释放")}交互键",
                exception
            );
        }
    }

    private void EnsureInitialized()
    {
        if (_inputState is not null && _overrideButton is not null && _applyOverrides is not null)
            return;

        var inputField = typeof(Game1).GetField(
            "input",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        );
        _inputState = inputField?.GetValue(null);
        if (_inputState is null)
            throw new InvalidOperationException("无法访问 SMAPI 输入状态");

        var inputType = _inputState.GetType();
        _overrideButton = inputType.GetMethod(
            "OverrideButton",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(SButton), typeof(bool) },
            modifiers: null
        );
        _applyOverrides = inputType.GetMethod(
            "ApplyOverrides",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null
        );
        if (_overrideButton is null || _applyOverrides is null)
            throw new InvalidOperationException("当前 SMAPI 不支持输入状态覆盖");
    }
}
