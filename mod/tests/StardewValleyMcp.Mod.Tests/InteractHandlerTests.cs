using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class InteractHandlerTests
{
    private const string CommandId = "42000000-0000-4000-8000-000000000002";

    [Test]
    public void ValidateRequiresExactlyOneWellFormedTarget()
    {
        var handler = NewHandler(out _, out _);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Validate(PositionRequest("Farm", 5, 4)), Is.Null);
            Assert.That(handler.Validate(RefRequest()), Is.Null);
            Assert.That(
                handler.Validate(new CommandRequest { Interact = new InteractRequest() })?.Code,
                Is.EqualTo(ErrorCode.InvalidArgument)
            );
            Assert.That(
                handler.Validate(new CommandRequest
                {
                    Interact = new InteractRequest
                    {
                        Position = new WorldPosition { LocationId = "", X = 5, Y = 4 },
                    },
                })?.Code,
                Is.EqualTo(ErrorCode.InvalidArgument)
            );
            Assert.That(
                handler.Validate(new CommandRequest { QueryRuntime = new QueryRuntimeRequest() })?.Code,
                Is.EqualTo(ErrorCode.InvalidArgument)
            );
        });
    }

    [TestCase("Town", 5, 5, true, ErrorCode.InvalidArgument)]
    [TestCase("Farm", 5, 6, true, ErrorCode.OutOfRange)]
    [TestCase("Farm", 5, 5, false, ErrorCode.NotReady)]
    public void ResolveRejectsOtherLocationNonAdjacentAndHeldNonTool(
        string location,
        int playerX,
        int playerY,
        bool heldItemAllowed,
        ErrorCode expected
    )
    {
        var handler = NewHandler(out var resolver, out var interaction);
        resolver.Target = Target("Farm", 5, 4);
        interaction.State = Observation(location, playerX, playerY) with
        {
            HeldItemAllowed = heldItemAllowed,
        };

        var failed = (ContinuationStep.Failed)handler
            .Start(CommandId, PositionRequest("Farm", 5, 4))
            .Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(expected));
            Assert.That(interaction.SubmitCalls, Is.Zero);
        });
    }

    [Test]
    public void GrabTileAlignmentUsesBoundedMicroMoveWithoutLeavingStartTile()
    {
        var handler = NewHandler(out var resolver, out var interaction);
        resolver.Target = Target("Farm", 5, 4);
        interaction.State = Observation("Farm", 5, 5) with { GrabX = 4, GrabY = 4 };
        var continuation = handler.Start(CommandId, PositionRequest("Farm", 5, 4));

        continuation.Tick(ContinuationStopSignal.None);
        continuation.Tick(ContinuationStopSignal.None);
        interaction.State = interaction.State with { GrabX = 5, GrabY = 4 };
        continuation.Tick(ContinuationStopSignal.None);
        continuation.Tick(ContinuationStopSignal.None);
        interaction.State = interaction.State with { MenuState = "DialogueBox:1" };
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(interaction.FaceCalls, Is.EqualTo(new[] { 0 }));
            Assert.That(interaction.MicroMoveCalls, Is.EqualTo(new[] { 0 }));
            Assert.That(interaction.StopCalls, Is.EqualTo(1));
            Assert.That(interaction.SubmitCalls, Is.EqualTo(1));
            Assert.That(succeeded.Result.Interact.Execution.CompletionReason, Is.EqualTo("dialogue_opened"));
        });
    }

    [Test]
    public void MicroMoveOvershootStopsAndFails()
    {
        var handler = NewHandler(out var resolver, out var interaction);
        resolver.Target = Target("Farm", 5, 4);
        interaction.State = Observation("Farm", 5, 5) with { GrabX = 4, GrabY = 4 };
        var continuation = handler.Start(CommandId, PositionRequest("Farm", 5, 4));

        continuation.Tick(ContinuationStopSignal.None);
        continuation.Tick(ContinuationStopSignal.None);
        interaction.State = interaction.State with { PlayerY = 4 };
        var failed = (ContinuationStep.Failed)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(interaction.StopCalls, Is.EqualTo(1));
            Assert.That(interaction.SubmitCalls, Is.Zero);
        });
    }

    [TestCase("location", "location_changed")]
    [TestCase("dialogue", "dialogue_opened")]
    [TestCase("menu", "menu_opened")]
    [TestCase("inventory", "inventory_changed")]
    [TestCase("relationship", "relationship_changed")]
    [TestCase("target", "target_state_changed")]
    public void SucceedsOnlyAfterAConfiguredAssociatedEffect(string effect, string reason)
    {
        var handler = NewHandler(out var resolver, out var interaction);
        resolver.Target = Target("Farm", 5, 4);
        interaction.State = Observation("Farm", 5, 5) with { Energy = 270 };
        interaction.OnSubmit = () => interaction.State = ApplyEffect(interaction.State, effect);
        var continuation = handler.Start(CommandId, PositionRequest("Farm", 5, 4));

        TickThroughSubmit(continuation);
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);
        var result = succeeded.Result.Interact;

        Assert.Multiple(() =>
        {
            Assert.That(interaction.SubmitCalls, Is.EqualTo(1));
            Assert.That(result.Target, Is.EqualTo(Position("Farm", 5, 4)));
            Assert.That(result.Energy.Before, Is.EqualTo(270));
            Assert.That(result.Energy.After, Is.EqualTo(269.5));
            Assert.That(result.Energy.Delta, Is.EqualTo(-0.5));
            Assert.That(result.Execution.CompletionReason, Is.EqualTo(reason));
        });
    }

    [Test]
    public void BusyWithoutAssociatedEffectTimesOutAsExecutionFailure()
    {
        var handler = NewHandler(out var resolver, out var interaction);
        resolver.Target = Target("Farm", 5, 4);
        interaction.State = Observation("Farm", 5, 5);
        var continuation = handler.Start(CommandId, PositionRequest("Farm", 5, 4));
        TickThroughSubmit(continuation);
        interaction.State = interaction.State with { CanAct = false };

        ContinuationStep step = new ContinuationStep.Pending();
        for (var index = 0; index < 45; index++)
            step = continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(step, Is.TypeOf<ContinuationStep.Failed>());
            Assert.That(((ContinuationStep.Failed)step).Error.Code, Is.EqualTo(ErrorCode.ExecutionFailed));
            Assert.That(interaction.SubmitCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ObservedEffectAfterSubmissionWinsWhenConsumedRefDisappears()
    {
        var handler = NewHandler(out var resolver, out var interaction);
        resolver.Target = Target("Farm", 5, 4, hasRef: true);
        interaction.State = Observation("Farm", 5, 5);
        interaction.OnSubmit = () =>
        {
            interaction.State = interaction.State with { MenuState = "DialogueBox:1" };
            resolver.RevalidateError = new Error
            {
                Code = ErrorCode.ExecutionFailed,
                Message = "目标已经移动",
            };
        };
        var continuation = handler.Start(CommandId, RefRequest());

        TickThroughSubmit(continuation);
        var succeeded = (ContinuationStep.Succeeded)continuation.Tick(ContinuationStopSignal.None);

        Assert.Multiple(() =>
        {
            Assert.That(interaction.SubmitCalls, Is.EqualTo(1));
            Assert.That(
                succeeded.Result.Interact.Execution.CompletionReason,
                Is.EqualTo("dialogue_opened")
            );
        });
    }

    [Test]
    public void CancellationIsAllowedBeforeButNotAfterSingleSubmission()
    {
        var beforeHandler = NewHandler(out var beforeResolver, out var beforeInteraction);
        beforeResolver.Target = Target("Farm", 5, 4);
        beforeInteraction.State = Observation("Farm", 5, 5);
        var before = beforeHandler.Start(CommandId, PositionRequest("Farm", 5, 4));

        var stopped = before.Tick(ContinuationStopSignal.CancelRequested);

        var afterHandler = NewHandler(out var afterResolver, out var afterInteraction);
        afterResolver.Target = Target("Farm", 5, 4);
        afterInteraction.State = Observation("Farm", 5, 5);
        var after = afterHandler.Start(CommandId, PositionRequest("Farm", 5, 4));
        TickThroughSubmit(after);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.TypeOf<ContinuationStep.Stopped>());
            Assert.That(beforeInteraction.SubmitCalls, Is.Zero);
            Assert.That(beforeInteraction.StopCalls, Is.EqualTo(1));
            Assert.That(before.CanCancel, Is.True);
            Assert.That(after.CanCancel, Is.False);
            Assert.That(afterInteraction.SubmitCalls, Is.EqualTo(1));
        });
    }

    private static void TickThroughSubmit(ICommandContinuation continuation)
    {
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
        Assert.That(continuation.Tick(ContinuationStopSignal.None), Is.TypeOf<ContinuationStep.Pending>());
    }

    private static InteractionObservation ApplyEffect(
        InteractionObservation current,
        string effect
    ) => effect switch
    {
        "location" => current with { LocationId = "Town", Energy = 269.5 },
        "dialogue" => current with { MenuState = "DialogueBox:1", Energy = 269.5 },
        "menu" => current with { MenuState = "ItemGrabMenu:1", Energy = 269.5 },
        "inventory" => current with { InventoryState = "changed", Energy = 269.5 },
        "relationship" => current with { RelationshipState = "changed", Energy = 269.5 },
        "target" => current with { TargetState = "changed", Energy = 269.5 },
        _ => current,
    };

    private static InteractHandler NewHandler(
        out FakeTargetResolver resolver,
        out FakeInteractionDriver interaction
    )
    {
        resolver = new FakeTargetResolver();
        interaction = new FakeInteractionDriver();
        return new InteractHandler(resolver, interaction);
    }

    private static CommandRequest PositionRequest(string locationId, int x, int y) => new()
    {
        Interact = new InteractRequest
        {
            Position = Position(locationId, x, y),
        },
    };

    private static CommandRequest RefRequest() => new()
    {
        Interact = new InteractRequest
        {
            TargetRef = new Ref { Value = "world-ref" },
        },
    };

    private static WorldPosition Position(string locationId, int x, int y) => new()
    {
        LocationId = locationId,
        X = x,
        Y = y,
    };

    private static LockedActionTarget Target(
        string locationId,
        int x,
        int y,
        bool hasRef = false
    ) => new(
        locationId,
        x,
        y,
        hasRef ? new Ref { Value = "world-ref" } : null,
        hasRef ? new object() : null
    );

    private static InteractionObservation Observation(string locationId, int x, int y) => new(
        true,
        true,
        true,
        locationId,
        x,
        y,
        0,
        5,
        4,
        270,
        "none",
        "inventory",
        "relationship",
        "target"
    );

    private sealed class FakeTargetResolver : IActionTargetResolver
    {
        public LockedActionTarget? Target { get; set; }
        public Error? ResolveError { get; set; }
        public Error? RevalidateError { get; set; }
        public int RevalidateCalls { get; private set; }

        public ActionTargetResolution Resolve(WorldPosition? position, Ref? reference) =>
            new(Target, ResolveError);

        public Error? Revalidate(LockedActionTarget target)
        {
            RevalidateCalls++;
            return RevalidateError;
        }
    }

    private sealed class FakeInteractionDriver : IInteractionDriver
    {
        public InteractionObservation State { get; set; } = Observation("Farm", 5, 5);
        public bool FaceResult { get; set; } = true;
        public bool MicroMoveResult { get; set; } = true;
        public List<int> FaceCalls { get; } = new();
        public List<int> MicroMoveCalls { get; } = new();
        public int StopCalls { get; private set; }
        public int SubmitCalls { get; private set; }
        public Action? OnSubmit { get; set; }

        public InteractionObservation Observe(int targetX, int targetY) => State;

        public bool TryFace(int direction)
        {
            FaceCalls.Add(direction);
            if (FaceResult)
                State = State with { FacingDirection = direction };
            return FaceResult;
        }

        public bool BeginMicroMove(int direction)
        {
            MicroMoveCalls.Add(direction);
            return MicroMoveResult;
        }

        public void StopMicroMove() => StopCalls++;

        public void Submit(int targetX, int targetY)
        {
            SubmitCalls++;
            OnSubmit?.Invoke();
        }
    }
}
