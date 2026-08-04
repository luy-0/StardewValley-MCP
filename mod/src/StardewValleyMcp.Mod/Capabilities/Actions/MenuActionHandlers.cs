using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class OpenMenuHandler : ILongRunningCapabilityHandler
{
    private readonly IMenuActionRuntime _runtime;

    public OpenMenuHandler(OpaqueRefStore refs) : this(new RuntimeMenuActionAdapter(refs)) { }
    internal OpenMenuHandler(IMenuActionRuntime runtime) => _runtime = runtime;

    public string Id => "open_menu";
    public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.OpenMenu;
    public Error? Validate(CommandRequest request) => MenuActionValidation.Open(request);
    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new OpenMenuContinuation(_runtime, request.OpenMenu.Menu);
}

internal sealed class CloseMenuHandler : ILongRunningCapabilityHandler
{
    private readonly IMenuActionRuntime _runtime;

    public CloseMenuHandler(OpaqueRefStore refs) : this(new RuntimeMenuActionAdapter(refs)) { }
    internal CloseMenuHandler(IMenuActionRuntime runtime) => _runtime = runtime;

    public string Id => "close_menu";
    public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.CloseMenu;
    public Error? Validate(CommandRequest request) => MenuActionValidation.Close(request);
    public ICommandContinuation Start(string commandId, CommandRequest request) => new CloseMenuContinuation(_runtime);
}

internal sealed class ActivateUiHandler : ILongRunningCapabilityHandler
{
    private readonly IMenuActionRuntime _runtime;

    public ActivateUiHandler(OpaqueRefStore refs) : this(new RuntimeMenuActionAdapter(refs)) { }
    internal ActivateUiHandler(IMenuActionRuntime runtime) => _runtime = runtime;

    public string Id => "activate_ui";
    public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.ActivateUi;
    public Error? Validate(CommandRequest request) => MenuActionValidation.Activate(request);
    public ICommandContinuation Start(string commandId, CommandRequest request) =>
        new ActivateUiContinuation(_runtime, request.ActivateUi.ElementRef.Clone(), request.ActivateUi.UiRevision);
}

internal static class MenuActionValidation
{
    public static Error? Open(CommandRequest request) => request.OperationCase != CommandRequest.OperationOneofCase.OpenMenu
        ? Invalid("open_menu 请求类型无效")
        : !Enum.IsDefined(request.OpenMenu.Menu) || request.OpenMenu.Menu == MenuKind.Unspecified
            ? Invalid("menu 必须是受支持的非空枚举")
            : null;

    public static Error? Close(CommandRequest request) => request.OperationCase == CommandRequest.OperationOneofCase.CloseMenu
        ? null
        : Invalid("close_menu 请求类型无效");

    public static Error? Activate(CommandRequest request)
    {
        if (request.OperationCase != CommandRequest.OperationOneofCase.ActivateUi)
            return Invalid("activate_ui 请求类型无效");
        var value = request.ActivateUi.ElementRef?.Value ?? "";
        if (string.IsNullOrEmpty(value) || value.Length > 512 || value.Contains('\0'))
            return Invalid("element_ref 无效");
        var revision = request.ActivateUi.UiRevision;
        return revision.Length != 64 || revision.Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            ? Invalid("ui_revision 无效")
            : null;
    }

    private static Error Invalid(string message) => new() { Code = ErrorCode.InvalidArgument, Message = message };
}

internal interface IMenuActionRuntime
{
    MenuObservation Observe();
    MenuActionAttempt Open(MenuKind menu);
    MenuActionAttempt Close();
    MenuActionAttempt Activate(Ref elementRef, string uiRevision);
}

internal sealed record MenuObservation(
    bool Ready,
    bool MenuOpen,
    string MenuType,
    MenuKind MenuKind,
    string UiRevision,
    bool Modal
);

internal sealed record MenuActionAttempt(MenuObservation Before, Error? Error = null, bool Submitted = false);

internal static class DialogueClosePolicy
{
    public static bool CanClose(DialogueCloseFacts facts) =>
        !facts.IsQuestion
        && !facts.EventUp
        && UiProjectionPolicy.DialogueEnabled(
            true,
            facts.Transitioning,
            facts.SafetyReady,
            facts.TextReadable,
            facts.CharacterIndex,
            facts.TextLength
        )
        && !UiProjectionPolicy.DialogueHasNextPage(
            facts.HasCharacterDialogue,
            facts.ContinuedOnNextScreen,
            facts.BrokenUpPageCount,
            facts.PlainDialogueCount
        )
        && (!facts.HasCharacterDialogue || facts.CharacterDialogueIsFinal)
        && facts.ObjectDialogueCount <= 1;
}

internal readonly record struct DialogueCloseFacts(
    bool IsQuestion,
    bool EventUp,
    bool Transitioning,
    bool SafetyReady,
    bool TextReadable,
    int CharacterIndex,
    int TextLength,
    bool HasCharacterDialogue,
    bool ContinuedOnNextScreen,
    int BrokenUpPageCount,
    int PlainDialogueCount,
    bool CharacterDialogueIsFinal,
    int ObjectDialogueCount
);

internal sealed class OpenMenuContinuation : ICommandContinuation
{
    private readonly IMenuActionRuntime _runtime;
    private readonly MenuKind _menu;
    private MenuActionAttempt? _attempt;

    public OpenMenuContinuation(IMenuActionRuntime runtime, MenuKind menu)
    {
        _runtime = runtime;
        _menu = menu;
    }

    public string Phase => _attempt is null ? "preparing" : "observing";
    public uint? ProgressPercent => null;
    public bool CanCancel => _attempt is null;

    public ContinuationStep Tick(ContinuationStopSignal signal)
    {
        if (signal != ContinuationStopSignal.None)
            return new ContinuationStep.Stopped();
        _attempt ??= _runtime.Open(_menu);
        if (_attempt.Error is not null)
            return new ContinuationStep.Failed(_attempt.Error);
        var after = _runtime.Observe();
        if (!after.Ready || !after.MenuOpen || after.MenuKind != _menu)
            return new ContinuationStep.Pending();
        return new ContinuationStep.Succeeded(new CapabilityResult
        {
            OpenMenu = new OpenMenuResult { Transition = Transition(_attempt.Before, after) },
        });
    }

    private static MenuTransition Transition(MenuObservation before, MenuObservation after) => new()
    {
        MenuTypeBefore = before.MenuType,
        MenuTypeAfter = after.MenuType,
        UiRevisionBefore = before.UiRevision,
        UiRevisionAfter = after.UiRevision,
    };
}

internal sealed class CloseMenuContinuation : ICommandContinuation
{
    private readonly IMenuActionRuntime _runtime;
    private MenuActionAttempt? _attempt;

    public CloseMenuContinuation(IMenuActionRuntime runtime) => _runtime = runtime;
    public string Phase => _attempt is null ? "preparing" : "observing";
    public uint? ProgressPercent => null;
    public bool CanCancel => _attempt is null;

    public ContinuationStep Tick(ContinuationStopSignal signal)
    {
        if (signal != ContinuationStopSignal.None)
            return new ContinuationStep.Stopped();
        _attempt ??= _runtime.Close();
        if (_attempt.Error is not null)
            return new ContinuationStep.Failed(_attempt.Error);
        var after = _runtime.Observe();
        if (!after.Ready || after.MenuOpen)
            return new ContinuationStep.Pending();
        return new ContinuationStep.Succeeded(new CapabilityResult
        {
            CloseMenu = new CloseMenuResult
            {
                AlreadyClosed = !_attempt.Submitted,
                Transition = new MenuTransition
                {
                    MenuTypeBefore = _attempt.Before.MenuType,
                    MenuTypeAfter = after.MenuType,
                    UiRevisionBefore = _attempt.Before.UiRevision,
                    UiRevisionAfter = after.UiRevision,
                },
            },
        });
    }
}

internal sealed class ActivateUiContinuation : ICommandContinuation
{
    private readonly IMenuActionRuntime _runtime;
    private readonly Ref _elementRef;
    private readonly string _revision;
    private MenuActionAttempt? _attempt;

    public ActivateUiContinuation(IMenuActionRuntime runtime, Ref elementRef, string revision)
    {
        _runtime = runtime;
        _elementRef = elementRef;
        _revision = revision;
    }

    public string Phase => _attempt is null ? "preparing" : "observing";
    public uint? ProgressPercent => null;
    public bool CanCancel => _attempt is null;

    public ContinuationStep Tick(ContinuationStopSignal signal)
    {
        if (signal != ContinuationStopSignal.None)
            return new ContinuationStep.Stopped();
        _attempt ??= _runtime.Activate(_elementRef, _revision);
        if (_attempt.Error is not null)
            return new ContinuationStep.Failed(_attempt.Error);
        var after = _runtime.Observe();
        if (!after.Ready)
            return new ContinuationStep.Failed(Failed(ErrorCode.ExecutionFailed, "激活后 UI 不可观察"));
        if (after.UiRevision == _attempt.Before.UiRevision)
            return new ContinuationStep.Pending();
        return new ContinuationStep.Succeeded(new CapabilityResult
        {
            ActivateUi = new ActivateUiResult
            {
                ElementRef = _elementRef.Clone(),
                Transition = new MenuTransition
                {
                    MenuTypeBefore = _attempt.Before.MenuType,
                    MenuTypeAfter = after.MenuType,
                    UiRevisionBefore = _attempt.Before.UiRevision,
                    UiRevisionAfter = after.UiRevision,
                },
            },
        });
    }

    private static Error Failed(ErrorCode code, string message) => new() { Code = code, Message = message };
}

internal sealed class RuntimeMenuActionAdapter : IMenuActionRuntime
{
    private readonly OpaqueRefStore _refs;

    public RuntimeMenuActionAdapter(OpaqueRefStore refs) => _refs = refs;

    public MenuObservation Observe()
    {
        if (!Context.IsWorldReady || Game1.player is null)
            return new MenuObservation(false, false, "", MenuKind.Unspecified, "", false);
        var menu = Game1.activeClickableMenu;
        var snapshot = menu is null
            ? UiProjector.ProjectNoMenu(_refs).Snapshot
            : UiRuntimeProjector.Project(menu, Game1.player, _refs).Snapshot;
        return new MenuObservation(
            true,
            snapshot.MenuOpen,
            snapshot.Menu?.MenuType ?? "",
            snapshot.Menu?.MenuKind ?? MenuKind.Unspecified,
            snapshot.UiRevision,
            snapshot.Menu?.Modal ?? false
        );
    }

    public MenuActionAttempt Open(MenuKind menu)
    {
        var before = Observe();
        if (!before.Ready || Game1.eventUp || before.Modal)
            return Rejected(before, "当前游戏状态不能安全切换菜单");
        if (!TryGetTab(menu, out var tab))
            return Rejected(before, "请求菜单不受支持", ErrorCode.InvalidArgument);
        if (Game1.activeClickableMenu is null)
            Game1.activeClickableMenu = new GameMenu(tab);
        else if (Game1.activeClickableMenu.GetType() == typeof(GameMenu))
            ((GameMenu)Game1.activeClickableMenu).changeTab(tab);
        else
            return Rejected(before, "当前菜单不能安全切换");
        return new MenuActionAttempt(before, Submitted: true);
    }

    public MenuActionAttempt Close()
    {
        var before = Observe();
        if (!before.Ready)
            return Rejected(before, "世界尚未就绪");
        if (!before.MenuOpen)
            return new MenuActionAttempt(before);
        if (Game1.activeClickableMenu is not { } menu)
            return Rejected(before, "当前菜单不能安全关闭");
        if (menu is DialogueBox dialogue)
        {
            if (dialogue.GetType() != typeof(DialogueBox)
                || !CanSafelyCloseDialogue(dialogue))
                return Rejected(before, "当前对话不能安全结束");
            dialogue.receiveLeftClick(0, 0);
            return new MenuActionAttempt(before, Submitted: true);
        }
        if (before.Modal || !menu.readyToClose())
            return Rejected(before, "当前菜单不能安全关闭");
        menu.exitThisMenu();
        return new MenuActionAttempt(before, Submitted: true);
    }

    private static bool CanSafelyCloseDialogue(DialogueBox dialogue)
    {
        try
        {
            var text = dialogue.getCurrentString();
            var hasCharacterDialogue = dialogue.characterDialogue is not null;
            var characterDialogueIsFinal = !hasCharacterDialogue
                || dialogue.characterDialogue!.isOnFinalDialogue();
            return DialogueClosePolicy.CanClose(new DialogueCloseFacts(
                dialogue.isQuestion,
                Game1.eventUp,
                dialogue.transitioning,
                dialogue.safetyTimer <= 0,
                PublicStringPolicy.IsValid(text),
                dialogue.characterIndexInDialogue,
                text?.Length ?? 0,
                hasCharacterDialogue,
                dialogue.characterDialogue?.isCurrentStringContinuedOnNextScreen ?? false,
                dialogue.characterDialoguesBrokenUp.Count,
                dialogue.dialogues.Count,
                characterDialogueIsFinal,
                Game1.currentObjectDialogue.Count
            ));
        }
        catch
        {
            return false;
        }
    }

    public MenuActionAttempt Activate(Ref elementRef, string uiRevision)
    {
        var resolved = _refs.ResolveUiElement(elementRef);
        if (resolved.Status != UiElementResolveStatus.Resolved || resolved.Target is null)
            return new MenuActionAttempt(Observe(), ResolveError(resolved));
        var before = Observe();
        if (!before.Ready || !before.MenuOpen || before.UiRevision != uiRevision)
            return new MenuActionAttempt(before, Failed(ErrorCode.StaleRef, "UI Revision 已失效"));
        var fact = CurrentElement(elementRef, before.UiRevision);
        if (fact is null || !fact.Visible || !fact.Enabled)
            return new MenuActionAttempt(before, Failed(ErrorCode.StaleRef, "UI Element 已失效或不可用"));
        if (Game1.activeClickableMenu is not { } menu)
            return new MenuActionAttempt(before, Failed(ErrorCode.Internal, "当前 UI 菜单不可用"));
        if (resolved.Target.Extractor == UiExtractorKind.GameMenu
            && !UiProjectionPolicy.CanActivateGameMenuElement(
                resolved.Target.Extractor,
                resolved.Target.PublicKind,
                fact.Kind,
                menu.GetType(),
                typeof(GameMenu)
            ))
            return new MenuActionAttempt(before, Failed(ErrorCode.InvalidArgument, "GameMenu 仅允许激活顶部页签"));

        if (resolved.Target.Extractor == UiExtractorKind.DialogueAdvance
            && menu.GetType() == typeof(DialogueBox))
        {
            ((DialogueBox)menu).receiveLeftClick(0, 0);
            return new MenuActionAttempt(before, Submitted: true);
        }

        if (resolved.Target.Component is not ClickableComponent component)
            return new MenuActionAttempt(before, Failed(ErrorCode.Internal, "当前 UI 组件不可用"));
        var activated = resolved.Target.Extractor switch
        {
            UiExtractorKind.GameMenu when UiProjectionPolicy.CanActivateGameMenuElement(
                resolved.Target.Extractor,
                resolved.Target.PublicKind,
                fact.Kind,
                menu.GetType(),
                typeof(GameMenu)
            ) => true,
            UiExtractorKind.DialogueResponse when menu.GetType() == typeof(DialogueBox) => true,
            _ => false,
        };
        if (!activated)
            return new MenuActionAttempt(before, Failed(ErrorCode.InvalidArgument, "UI Element 类型不支持激活"));

        var center = component.bounds.Center;
        menu.receiveLeftClick(center.X, center.Y);
        return new MenuActionAttempt(before, Submitted: true);
    }

    private UiElementFact? CurrentElement(Ref elementRef, string revision)
    {
        var menu = Game1.activeClickableMenu;
        if (menu is null || Game1.player is null)
            return null;
        var snapshot = UiRuntimeProjector.Project(menu, Game1.player, _refs).Snapshot;
        return snapshot.UiRevision == revision
            ? snapshot.Elements.SingleOrDefault(item => item.Ref.Value == elementRef.Value)
            : null;
    }

    private static bool TryGetTab(MenuKind menu, out int tab)
    {
        tab = menu switch
        {
            MenuKind.Inventory => GameMenu.inventoryTab,
            MenuKind.Skills => GameMenu.skillsTab,
            MenuKind.Social => GameMenu.socialTab,
            MenuKind.Map => GameMenu.mapTab,
            MenuKind.Crafting => GameMenu.craftingTab,
            MenuKind.Collections => GameMenu.collectionsTab,
            MenuKind.Options => GameMenu.optionsTab,
            _ => -1,
        };
        return tab >= 0;
    }

    private static MenuActionAttempt Rejected(MenuObservation before, string message, ErrorCode code = ErrorCode.NotReady) =>
        new(before, Failed(code, message));

    private static Error ResolveError(UiElementResolveResult result) => result.Status switch
    {
        UiElementResolveStatus.Stale => Failed(ErrorCode.StaleRef, "UI Element Ref 已失效"),
        UiElementResolveStatus.NotFound => Failed(ErrorCode.NotFound, "UI Element Ref 不存在"),
        UiElementResolveStatus.Unsupported => Failed(ErrorCode.InvalidArgument, "Ref 不是 UI Element"),
        _ => result.Error?.Clone() ?? Failed(ErrorCode.Internal, "当前 UI Ref 不可用"),
    };

    private static Error Failed(ErrorCode code, string message) => new() { Code = code, Message = message };
}
