using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class UseToolHandlerTests
{
    private const string CommandId = "42000000-0000-4000-8000-000000000003";

    [Test]
    public void ValidateRequiresOneTargetAndPublicChargeRange()
    {
        var handler = NewHandler(out _, out _);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Validate(Request(0)), Is.Null);
            Assert.That(handler.Validate(RefRequest(5)), Is.Null);
            Assert.That(
                handler.Validate(new CommandRequest { UseTool = new UseToolRequest() })?.Code,
                Is.EqualTo(ErrorCode.InvalidArgument)
            );
            Assert.That(
                handler.Validate(Request(6))?.Code,
                Is.EqualTo(ErrorCode.InvalidArgument)
            );
            Assert.That(
                handler.Validate(new CommandRequest { QueryRuntime = new QueryRuntimeRequest() })?.Code,
                Is.EqualTo(ErrorCode.InvalidArgument)
            );
        });
    }

    [TestCase(0, true, 0, ErrorCode.InvalidArgument)]
    [TestCase(1, true, 1, ErrorCode.InvalidArgument)]
    [TestCase(3, true, 2, ErrorCode.InvalidArgument)]
    public void ResolveRejectsUnsupportedToolAndIllegalActualCharge(
        int kindValue,
        bool hasTool,
        int charge,
        ErrorCode expected
    )
    {
        var kind = (SupportedToolKind)kindValue;
        var handler = NewHandler(out _, out var driver);
        driver.State = Observation(kind) with
        {
            ToolIdentity = hasTool ? driver.Tool : null,
            MaxChargeLevel = kind == SupportedToolKind.Hoe ? 1 : 0,
        };

        var failed = (ContinuationStep.Failed)handler
            .Start(CommandId, Request((uint)charge))
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(expected));
            Assert.That(driver.BeginCalls, Is.Zero);
        });
    }

    [Test]
    public void MissingCurrentToolIsNotReady()
    {
        var handler = NewHandler(out _, out var driver);
        driver.State = Observation(SupportedToolKind.Unsupported) with
        {
            ToolIdentity = null,
            ToolQualifiedItemId = "",
        };

        var failed = (ContinuationStep.Failed)handler
            .Start(CommandId, Request(0))
            .Tick(ContinuationStopSignal.None);

        Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.NotReady));
    }

    [TestCase(1, "(T)CopperAxe")]
    [TestCase(2, "(T)SteelPickaxe")]
    public void AutoReleaseToolsRequireSwingEdgeAndStableIdle(
        int kindValue,
        string qualifiedItemId
    )
    {
        var kind = (SupportedToolKind)kindValue;
        var handler = NewHandler(out _, out var driver);
        driver.State = Observation(kind) with
        {
            ToolIdentity = driver.Tool,
            ToolQualifiedItemId = qualifiedItemId,
            ToolPower = 2,
        };
        driver.OnBegin = () => driver.State = driver.State with
        {
            SwingTicker = 11,
            CanSubmit = false,
        };
        var continuation = handler.Start(CommandId, Request(0));

        TickThroughSubmit(continuation);
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(driver.BeginCalls, Is.EqualTo(1));
            Assert.That(driver.ReleaseCalls, Is.Zero);
            Assert.That(succeeded.Result.UseTool.ToolQualifiedItemId, Is.EqualTo(qualifiedItemId));
            Assert.That(succeeded.Result.UseTool.ChargeLevel, Is.Zero);
            Assert.That(
                succeeded.Result.UseTool.Execution.CompletionReason,
                Is.EqualTo("tool_action_settled")
            );
        });
    }

    [Test]
    public void ScytheUsesSelfDrivenAnimationWithoutCallingRelease()
    {
        var handler = NewHandler(out _, out var driver);
        driver.State = Observation(SupportedToolKind.Scythe) with
        {
            ToolIdentity = driver.Tool,
            ToolQualifiedItemId = "(W)53",
        };
        driver.OnBegin = () => driver.State = driver.State with
        {
            UsingTool = true,
            CanReleaseTool = true,
            CanMove = false,
            CanSubmit = false,
            LastClickIsZero = false,
        };
        var continuation = handler.Start(CommandId, Request(0));

        TickThroughSubmit(continuation);
        continuation.Tick(ContinuationStopSignal.None);
        driver.State = Settled(driver.State) with { CanReleaseTool = true };
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        var succeeded = continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.TypeOf<ContinuationStep.Succeeded>());
            Assert.That(driver.ReleaseCalls, Is.Zero);
        });
    }

    [TestCase(3, "(T)CopperHoe", 0)]
    [TestCase(3, "(T)CopperHoe", 1)]
    [TestCase(4, "(T)SteelWateringCan", 0)]
    [TestCase(4, "(T)SteelWateringCan", 2)]
    public void ChargeableToolsReachRequestedPowerThenReleaseOnce(
        int kindValue,
        string qualifiedItemId,
        int charge
    )
    {
        var kind = (SupportedToolKind)kindValue;
        var handler = NewHandler(out _, out var driver);
        driver.State = Observation(kind) with
        {
            ToolIdentity = driver.Tool,
            ToolQualifiedItemId = qualifiedItemId,
            MaxChargeLevel = (int)charge,
        };
        driver.OnBegin = () => driver.State = driver.State with
        {
            UsingTool = true,
            CanReleaseTool = true,
            CanMove = false,
            CanSubmit = false,
            LastClickIsZero = false,
        };
        driver.OnRelease = () => driver.State = Settled(driver.State);
        var continuation = handler.Start(CommandId, Request((uint)charge));

        TickThroughSubmit(continuation);
        ContinuationStep step = new ContinuationStep.Pending();
        for (var index = 0; index < 12 && step is ContinuationStep.Pending; index++)
            step = continuation.Tick(ContinuationStopSignal.None);

        var succeeded = (ContinuationStep.Succeeded)step;
        Assert.Multiple(() =>
        {
            Assert.That(driver.IncreaseCalls, Is.EqualTo((int)charge));
            Assert.That(driver.ReleaseCalls, Is.EqualTo(1));
            Assert.That(succeeded.Result.UseTool.ChargeLevel, Is.EqualTo((uint)charge));
        });
    }

    [Test]
    public void PositionMustBeCardinalAdjacentInCurrentLocation()
    {
        var otherLocation = NewHandler(out var otherResolver, out var otherDriver);
        otherResolver.Target = Target("Town", 5, 4);
        otherDriver.State = Observation(SupportedToolKind.Axe);

        var far = NewHandler(out var farResolver, out var farDriver);
        farResolver.Target = Target("Farm", 5, 3);
        farDriver.State = Observation(SupportedToolKind.Axe);

        var locationFailure = (ContinuationStep.Failed)otherLocation
            .Start(CommandId, Request(0))
            .Tick(ContinuationStopSignal.None);
        var rangeFailure = (ContinuationStep.Failed)far
            .Start(CommandId, Request(0))
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(locationFailure.Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(rangeFailure.Error.Code, Is.EqualTo(ErrorCode.OutOfRange));
        });
    }

    [Test]
    public void ToolReplacementBeforeSubmissionFailsWithoutStartingAction()
    {
        var handler = NewHandler(out _, out var driver);
        driver.State = Observation(SupportedToolKind.Axe) with
        {
            ToolIdentity = driver.Tool,
        };
        var continuation = handler.Start(CommandId, Request(0));

        continuation.Tick(ContinuationStopSignal.None);
        driver.State = driver.State with { ToolIdentity = new object() };
        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(driver.BeginCalls, Is.Zero);
        });
    }

    [Test]
    public void DeadlineAfterSubmissionReleasesSafelyThenStopsAfterSettle()
    {
        var handler = NewHandler(out _, out var driver);
        driver.State = Observation(SupportedToolKind.Hoe) with
        {
            ToolIdentity = driver.Tool,
            ToolQualifiedItemId = "(T)CopperHoe",
            MaxChargeLevel = 1,
        };
        driver.OnBegin = () => driver.State = driver.State with
        {
            UsingTool = true,
            CanReleaseTool = true,
            CanMove = false,
            CanSubmit = false,
            LastClickIsZero = false,
        };
        driver.OnRelease = () => driver.State = Settled(driver.State);
        var continuation = handler.Start(CommandId, Request(1));
        TickThroughSubmit(continuation);

        var settling = continuation.Tick(ContinuationStopSignal.DeadlineExceeded);
        var stable = continuation.Tick(ContinuationStopSignal.DeadlineExceeded);
        var stopped = continuation.Tick(ContinuationStopSignal.DeadlineExceeded);

        Assert.Multiple(() =>
        {
            Assert.That(continuation.CanCancel, Is.False);
            Assert.That(settling, Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(stable, Is.TypeOf<ContinuationStep.Pending>());
            Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(driver.IncreaseCalls, Is.Zero, "Deadline 后不再增加蓄力");
            Assert.That(driver.ReleaseCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void CancellationBeforeSubmissionDoesNotTouchTool()
    {
        var handler = NewHandler(out _, out var driver);
        var continuation = handler.Start(CommandId, Request(0));

        var stopped = continuation.Tick(ContinuationStopSignal.CancelRequested);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(continuation.CanCancel, Is.True);
            Assert.That(driver.BeginCalls, Is.Zero);
            Assert.That(driver.ReleaseCalls, Is.Zero);
        });
    }

    private static void TickThroughSubmit(ICommandContinuation continuation)
    {
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        Assert.That(continuation.CanCancel, Is.False);
    }

    private static UseToolHandler NewHandler(
        out FakeTargetResolver resolver,
        out FakeToolUseDriver driver
    )
    {
        resolver = new FakeTargetResolver { Target = Target("Farm", 5, 4) };
        driver = new FakeToolUseDriver();
        return new UseToolHandler(resolver, driver);
    }

    private static CommandRequest Request(uint charge) => new()
    {
        UseTool = new UseToolRequest
        {
            Position = Position("Farm", 5, 4),
            ChargeLevel = charge,
        },
    };

    private static CommandRequest RefRequest(uint charge) => new()
    {
        UseTool = new UseToolRequest
        {
            TargetRef = new Ref { Value = "world-ref" },
            ChargeLevel = charge,
        },
    };

    private static WorldPosition Position(string locationId, int x, int y) => new()
    {
        LocationId = locationId,
        X = x,
        Y = y,
    };

    private static LockedActionTarget Target(string locationId, int x, int y) =>
        new(locationId, x, y, null, null);

    private static ToolUseObservation Observation(SupportedToolKind kind) => new(
        true,
        true,
        "Farm",
        5,
        5,
        0,
        null,
        kind,
        "(T)Axe",
        0,
        0,
        10,
        false,
        false,
        true,
        false,
        true,
        270
    );

    private static ToolUseObservation Settled(ToolUseObservation current) => current with
    {
        UsingTool = false,
        CanReleaseTool = false,
        CanMove = true,
        PauseForSingleAnimation = false,
        LastClickIsZero = true,
        CanSubmit = true,
    };

    private sealed class FakeTargetResolver : IActionTargetResolver
    {
        public LockedActionTarget? Target { get; set; }
        public Error? RevalidateError { get; set; }

        public ActionTargetResolution Resolve(WorldPosition? position, Ref? reference) =>
            new(Target, null);

        public Error? Revalidate(LockedActionTarget target) => RevalidateError;
    }

    private sealed class FakeToolUseDriver : IToolUseDriver
    {
        public object Tool { get; } = new();
        public ToolUseObservation State { get; set; }
        public int BeginCalls { get; private set; }
        public int IncreaseCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public Action? OnBegin { get; set; }
        public Action? OnRelease { get; set; }

        public FakeToolUseDriver() =>
            State = Observation(SupportedToolKind.Axe) with { ToolIdentity = Tool };

        public ToolUseObservation Observe() => State;

        public bool TryFace(int direction, object toolIdentity)
        {
            if (!ReferenceEquals(toolIdentity, State.ToolIdentity))
                return false;
            State = State with { FacingDirection = direction };
            return true;
        }

        public bool BeginUse(object toolIdentity, int targetX, int targetY)
        {
            if (!ReferenceEquals(toolIdentity, State.ToolIdentity))
                return false;
            BeginCalls++;
            OnBegin?.Invoke();
            return true;
        }

        public bool IncreaseCharge(object toolIdentity)
        {
            if (!ReferenceEquals(toolIdentity, State.ToolIdentity))
                return false;
            IncreaseCalls++;
            State = State with { ToolPower = State.ToolPower + 1 };
            return true;
        }

        public bool Release(object toolIdentity)
        {
            if (!ReferenceEquals(toolIdentity, State.ToolIdentity))
                return false;
            ReleaseCalls++;
            OnRelease?.Invoke();
            return true;
        }
    }
}
