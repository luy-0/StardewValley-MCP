using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class MenuActionHandlerTests
{
    [Test]
    public void Validate_IsPureAndRejectsOnlyMalformedRequests()
    {
        var runtime = new FakeRuntime();
        var open = new OpenMenuHandler(runtime);
        var close = new CloseMenuHandler(runtime);
        var activate = new ActivateUiHandler(runtime);

        Assert.Multiple(() =>
        {
            Assert.That(open.Validate(new CommandRequest { OpenMenu = new OpenMenuRequest { Menu = MenuKind.Inventory } }), Is.Null);
            Assert.That(open.Validate(new CommandRequest { OpenMenu = new OpenMenuRequest() })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(close.Validate(new CommandRequest { CloseMenu = new CloseMenuRequest() }), Is.Null);
            Assert.That(activate.Validate(new CommandRequest
            {
                ActivateUi = new ActivateUiRequest { ElementRef = new Ref { Value = "r" }, UiRevision = Revision("a") },
            }), Is.Null);
            Assert.That(activate.Validate(new CommandRequest { ActivateUi = new ActivateUiRequest { ElementRef = new Ref { Value = "r" }, UiRevision = "stale" } })!.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(runtime.Calls, Is.Zero, "Validate 不得读取 Game 或 Ref Store");
        });
    }

    [Test]
    public void OpenMenu_ObservesRequestedTabAndSupportsIdempotency()
    {
        var runtime = new FakeRuntime(Menu(MenuKind.Inventory, "before"));
        var continuation = new OpenMenuContinuation(runtime, MenuKind.Inventory);

        var step = continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(step, Is.TypeOf<ContinuationStep.Succeeded>());
            Assert.That(((ContinuationStep.Succeeded)step).Result.OpenMenu.Transition.MenuTypeBefore, Is.EqualTo("GameMenu"));
            Assert.That(runtime.Opened, Is.EqualTo(MenuKind.Inventory));
            Assert.That(continuation.CanCancel, Is.False);
        });
    }

    [Test]
    public void OpenMenu_RejectsModalAndCancellationBeforeSubmission()
    {
        var modal = new FakeRuntime(Menu(MenuKind.Inventory, "before") with { Modal = true });
        var rejected = new OpenMenuContinuation(modal, MenuKind.Options).Tick(ContinuationStopSignal.None);
        var cancelled = new OpenMenuContinuation(new FakeRuntime(), MenuKind.Options).Tick(ContinuationStopSignal.CancelRequested);

        Assert.Multiple(() =>
        {
            Assert.That(((ContinuationStep.Failed)rejected).Error.Code, Is.EqualTo(ErrorCode.NotReady));
            Assert.That(modal.Opened, Is.Null);
            Assert.That(cancelled, Is.TypeOf<ContinuationStep.Stopped>());
        });
    }

    [Test]
    public void CloseMenu_IsIdempotentAndDoesNotSubmitWhenAlreadyClosed()
    {
        var runtime = new FakeRuntime(NoMenu("before"));
        var continuation = new CloseMenuContinuation(runtime);

        var step = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(step.Result.CloseMenu.AlreadyClosed, Is.True);
            Assert.That(runtime.Closed, Is.False);
            Assert.That(step.Result.CloseMenu.Transition.MenuTypeAfter, Is.Empty);
        });
    }

    [Test]
    public void CloseMenu_RejectsModalAndOnlySucceedsAfterNoMenuObservation()
    {
        var modal = new FakeRuntime(Menu(MenuKind.Inventory, "before") with { Modal = true });
        var rejected = new CloseMenuContinuation(modal).Tick(ContinuationStopSignal.None);
        var runtime = new FakeRuntime(Menu(MenuKind.Inventory, "before")) { DelayCloseObservation = true };
        var continuation = new CloseMenuContinuation(runtime);

        Assert.Multiple(() =>
        {
            Assert.That(((ContinuationStep.Failed)rejected).Error.Code, Is.EqualTo(ErrorCode.NotReady));
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(continuation.CanCancel, Is.False);
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Succeeded>());
        });
    }

    [Test]
    public void DialogueClosePolicy_AllowsOnlyReadyFinalNonEventDialogue()
    {
        var ready = new DialogueCloseFacts(
            IsQuestion: false,
            EventUp: false,
            Transitioning: false,
            SafetyReady: true,
            TextReadable: true,
            CharacterIndex: 4,
            TextLength: 5,
            HasCharacterDialogue: false,
            ContinuedOnNextScreen: false,
            BrokenUpPageCount: 0,
            PlainDialogueCount: 1,
            CharacterDialogueIsFinal: true,
            ObjectDialogueCount: 1
        );

        Assert.That(DialogueClosePolicy.CanClose(ready), Is.True);
        var blockers = new[]
        {
            ready with { IsQuestion = true },
            ready with { EventUp = true },
            ready with { Transitioning = true },
            ready with { SafetyReady = false },
            ready with { TextReadable = false },
            ready with { CharacterIndex = 3 },
            ready with { PlainDialogueCount = 2 },
            ready with { ObjectDialogueCount = 2 },
            ready with
            {
                HasCharacterDialogue = true,
                ContinuedOnNextScreen = true,
            },
            ready with
            {
                HasCharacterDialogue = true,
                BrokenUpPageCount = 2,
            },
            ready with
            {
                HasCharacterDialogue = true,
                BrokenUpPageCount = 1,
                CharacterDialogueIsFinal = false,
            },
        };
        Assert.That(blockers.All(facts => !DialogueClosePolicy.CanClose(facts)), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(DialogueClosePolicy.CanClose(ready with
            {
                HasCharacterDialogue = true,
                BrokenUpPageCount = 1,
                CharacterDialogueIsFinal = true,
            }), Is.True);
            Assert.That(DialogueClosePolicy.CanClose(ready with
            {
                ObjectDialogueCount = 0,
            }), Is.True);
        });
    }

    [Test]
    public void CloseMenu_DoesNotSucceedWhileNativeFlowKeepsMenuOpen()
    {
        var runtime = new FakeRuntime(Menu(MenuKind.Unspecified, "dialogue-before"))
        {
            KeepMenuOpenAfterClose = true,
        };
        var continuation = new CloseMenuContinuation(runtime);

        Assert.Multiple(() =>
        {
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(runtime.Closed, Is.True);
            Assert.That(continuation.CanCancel, Is.False);
        });
    }

    [Test]
    public void ActivateUi_RequiresCurrentRevisionVisibleEnabledAndObservesNewRevision()
    {
        var runtime = new FakeRuntime(Menu(MenuKind.Inventory, Revision("a")));
        var continuation = new ActivateUiContinuation(runtime, new Ref { Value = "ui-ref" }, Revision("a"));

        Assert.Multiple(() =>
        {
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(runtime.ActivatedRef, Is.EqualTo("ui-ref"));
            Assert.That(continuation.CanCancel, Is.False, "提交后不可取消");
            Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Succeeded>());
        });
    }

    [TestCase(ErrorCode.StaleRef)]
    [TestCase(ErrorCode.InvalidArgument)]
    [TestCase(ErrorCode.NotFound)]
    public void ActivateUi_PropagatesRefAndRevisionFailures(ErrorCode code)
    {
        var runtime = new FakeRuntime(Menu(MenuKind.Inventory, Revision("a")))
        {
            ActivationError = new Error { Code = code, Message = "blocked" },
        };
        var continuation = new ActivateUiContinuation(runtime, new Ref { Value = "ui-ref" }, Revision("a"));

        var step = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.That(step.Error.Code, Is.EqualTo(code));
    }

    [Test]
    public void ActivateUi_UsesNoScaleCoordinatesInContinuationContract()
    {
        var runtime = new FakeRuntime(Menu(MenuKind.Inventory, Revision("a")));
        var continuation = new ActivateUiContinuation(runtime, new Ref { Value = "ui-ref" }, Revision("a"));

        continuation.Tick(ContinuationStopSignal.None);

        Assert.That(runtime.ActivationCalls, Is.EqualTo(1), "continuation 只请求一次由 runtime 已验证组件中心完成的 Primary Activation");
    }

    private static MenuObservation Menu(MenuKind kind, string revision) => new(true, true, "GameMenu", kind, revision, false);
    private static MenuObservation NoMenu(string revision) => new(true, false, "", MenuKind.Unspecified, revision, false);
    private static string Revision(string value) => value.PadRight(64, value[0]);

    private sealed class FakeRuntime : IMenuActionRuntime
    {
        private MenuObservation _current;
        private bool _activationObservationPending;

        public FakeRuntime(MenuObservation? current = null) => _current = current ?? NoMenu(Revision("0"));
        public int Calls { get; private set; }
        public int ActivationCalls { get; private set; }
        public MenuKind? Opened { get; private set; }
        public bool Closed { get; private set; }
        public bool DelayCloseObservation { get; init; }
        public bool KeepMenuOpenAfterClose { get; init; }
        public Error? ActivationError { get; init; }
        public string? ActivatedRef { get; private set; }

        public MenuObservation Observe()
        {
            Calls++;
            if (_activationObservationPending)
            {
                _activationObservationPending = false;
                var beforeActivation = _current;
                _current = _current with { UiRevision = Revision("d") };
                return beforeActivation;
            }
            if (DelayCloseObservation && Closed && _current.MenuOpen)
            {
                _current = NoMenu(Revision("c"));
                return Menu(MenuKind.Inventory, Revision("b"));
            }
            return _current;
        }

        public MenuActionAttempt Open(MenuKind menu)
        {
            Calls++;
            var before = _current;
            if (before.Modal)
                return new MenuActionAttempt(before, new Error { Code = ErrorCode.NotReady });
            Opened = menu;
            _current = Menu(menu, before.MenuKind == menu ? before.UiRevision : Revision("o"));
            return new MenuActionAttempt(before, Submitted: true);
        }

        public MenuActionAttempt Close()
        {
            Calls++;
            var before = _current;
            if (before.Modal)
                return new MenuActionAttempt(before, new Error { Code = ErrorCode.NotReady });
            if (!before.MenuOpen)
                return new MenuActionAttempt(before);
            Closed = true;
            if (!DelayCloseObservation && !KeepMenuOpenAfterClose)
                _current = NoMenu(Revision("c"));
            return new MenuActionAttempt(before, Submitted: true);
        }

        public MenuActionAttempt Activate(Ref elementRef, string uiRevision)
        {
            Calls++;
            var before = _current;
            if (ActivationError is not null)
                return new MenuActionAttempt(before, ActivationError);
            if (before.UiRevision != uiRevision)
                return new MenuActionAttempt(before, new Error { Code = ErrorCode.StaleRef });
            ActivationCalls++;
            ActivatedRef = elementRef.Value;
            _activationObservationPending = true;
            return new MenuActionAttempt(before, Submitted: true);
        }
    }
}
