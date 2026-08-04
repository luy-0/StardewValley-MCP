using NUnit.Framework;
using Google.Protobuf;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod.Tests;

public sealed class CommandCoordinatorTests
{
    [Test]
    public void ImmediateCommandPublishesAcceptedThenTerminalAndRetainsTombstone()
    {
        var clock = new FakeClock();
        var coordinator = NewCoordinator(clock, new ImmediateHandler());
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;
        var request = RuntimeRequest("11111111-1111-4111-8111-111111111111");

        var accepted = coordinator.Submit(request);
        var current = coordinator.GetStatus(request.CommandId);
        var replay = coordinator.Submit(request);
        coordinator.ReleaseAccepted(request.CommandId);
        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(((CoordinatorResponse.Event)accepted).Value.State, Is.EqualTo(CommandState.Accepted));
            Assert.That(((CoordinatorResponse.StatusResponse)current).Current!.State, Is.EqualTo(CommandState.Accepted));
            Assert.That(((CoordinatorResponse.Event)replay).Value.State, Is.EqualTo(CommandState.Accepted));
            Assert.That(events.Single().State, Is.EqualTo(CommandState.Succeeded));
            Assert.That(coordinator.GetStatus(request.CommandId), Is.TypeOf<CoordinatorResponse.StatusResponse>());
        });

        clock.Milliseconds += CommandCoordinator.ResultRetentionMs;
        coordinator.Tick();
        var expiredStatus = (CoordinatorResponse.ProtocolError)coordinator.GetStatus(request.CommandId);
        var expiredReplay = (CoordinatorResponse.ProtocolError)coordinator.Submit(request);
        var expiredCancel = (CoordinatorResponse.ProtocolError)coordinator.RequestCancel(request.CommandId);
        Assert.Multiple(() =>
        {
            Assert.That(expiredStatus.Value.Code, Is.EqualTo(ErrorCode.IdempotencyRecordExpired));
            Assert.That(expiredReplay.Value.Code, Is.EqualTo(ErrorCode.IdempotencyRecordExpired));
            Assert.That(expiredCancel.Value.Code, Is.EqualTo(ErrorCode.IdempotencyRecordExpired));
        });
    }

    [Test]
    public void CanonicalReplayDiscardsUnknownFieldsAndDoesNotExecuteTwice()
    {
        var clock = new FakeClock();
        var handler = new ImmediateHandler();
        var coordinator = NewCoordinator(clock, handler);
        var request = RuntimeRequest("66666666-6666-4666-8666-666666666666");

        var accepted = coordinator.Submit(WithUnknownField(request));
        var replay = coordinator.Submit(request);
        coordinator.ReleaseAccepted(request.CommandId);
        coordinator.Tick();
        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.TypeOf<CoordinatorResponse.Event>());
            Assert.That(replay, Is.TypeOf<CoordinatorResponse.Event>());
            Assert.That(handler.Executions, Is.EqualTo(1));
        });
    }

    [Test]
    public void SameIdWithDifferentKnownFieldsConflictsAndLeavesOriginalRecordUntouched()
    {
        var clock = new FakeClock();
        var handler = new ImmediateHandler();
        var coordinator = NewCoordinator(clock, handler);
        var original = RuntimeRequest("77777777-7777-4777-8777-777777777777");
        var conflicting = original.Clone();
        conflicting.TimeoutMs = 1;

        coordinator.Submit(original);
        coordinator.ReleaseAccepted(original.CommandId);
        var conflict = coordinator.Submit(conflicting);
        clock.Milliseconds = 1;
        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(conflict, Is.TypeOf<CoordinatorResponse.ProtocolError>());
            Assert.That(((CoordinatorResponse.ProtocolError)conflict).Value.Code, Is.EqualTo(ErrorCode.Conflict));
            Assert.That(handler.Executions, Is.EqualTo(1), "冲突请求不得替换原始 timeout");
        });
    }

    [Test]
    public void CancelRejectsUnknownNoncancellableAndTerminalCommands()
    {
        var clock = new FakeClock();
        var coordinator = NewCoordinator(clock, new ImmediateHandler());
        var request = RuntimeRequest("88888888-8888-4888-8888-888888888888");

        var unknown = (CoordinatorResponse.CancelResponse)coordinator.RequestCancel("99999999-9999-4999-8999-999999999999");
        coordinator.Submit(request);
        var noncancellable = (CoordinatorResponse.CancelResponse)coordinator.RequestCancel(request.CommandId);
        coordinator.ReleaseAccepted(request.CommandId);
        coordinator.Tick();
        var terminal = (CoordinatorResponse.CancelResponse)coordinator.RequestCancel(request.CommandId);

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Accepted, Is.False);
            Assert.That(unknown.Error!.Code, Is.EqualTo(ErrorCode.NotFound));
            Assert.That(noncancellable.Accepted, Is.False);
            Assert.That(noncancellable.Error!.Code, Is.EqualTo(ErrorCode.Conflict));
            Assert.That(terminal.Accepted, Is.False);
            Assert.That(terminal.Error!.Code, Is.EqualTo(ErrorCode.Conflict));
            Assert.That(terminal.Current!.State, Is.EqualTo(CommandState.Succeeded));
        });
    }

    [Test]
    public void StagedCommandRunsCancelsAndRejectsSecondMutationBeforeAcceptance()
    {
        var clock = new FakeClock();
        var staged = new StagedHandler();
        var coordinator = NewCoordinator(clock, staged);
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;
        var first = FaceRequest("22222222-2222-4222-8222-222222222222");
        var second = FaceRequest("33333333-3333-4333-8333-333333333333");

        coordinator.Submit(first);
        coordinator.ReleaseAccepted(first.CommandId);
        Assert.That(coordinator.Submit(second), Is.TypeOf<CoordinatorResponse.ProtocolError>());
        coordinator.Tick();
        coordinator.Tick();
        var cancel = coordinator.RequestCancel(first.CommandId);
        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(events.First(item => item.State == CommandState.Running).Phase, Is.EqualTo("running"));
            Assert.That(events.Count(item => item.State == CommandState.Running), Is.EqualTo(2));
            Assert.That(((CoordinatorResponse.CancelResponse)cancel).Accepted, Is.True);
            Assert.That(events.Last().State, Is.EqualTo(CommandState.Cancelled));
            Assert.That(events.Last().Error.Code, Is.EqualTo(ErrorCode.Cancelled));
            Assert.That(staged.Continuation.SawCancel, Is.True);
        });
    }

    [Test]
    public void QueuedDeadlineSkipsHandlerAndWritesTimedOut()
    {
        var clock = new FakeClock();
        var handler = new ImmediateHandler();
        var coordinator = NewCoordinator(clock, handler);
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;
        coordinator.Submit(RuntimeRequest("44444444-4444-4444-8444-444444444444", 1));
        coordinator.ReleaseAccepted("44444444-4444-4444-8444-444444444444");
        clock.Milliseconds = 1;

        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(handler.Executions, Is.Zero);
            Assert.That(events.Single().State, Is.EqualTo(CommandState.TimedOut));
            Assert.That(events.Single().Error.Code, Is.EqualTo(ErrorCode.DeadlineExceeded));
        });
    }

    [Test]
    public void StagedDeadlineIsDeliveredBeforeStoppedContinuationBecomesTimedOut()
    {
        var clock = new FakeClock();
        var staged = new StagedHandler();
        var coordinator = NewCoordinator(clock, staged);
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;
        coordinator.Submit(FaceRequest("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", timeout: 1));
        coordinator.ReleaseAccepted("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

        coordinator.Tick();
        clock.Milliseconds = 1;
        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(staged.Continuation.LastSignal, Is.EqualTo(ContinuationStopSignal.DeadlineExceeded));
            Assert.That(events.Last().State, Is.EqualTo(CommandState.TimedOut));
            Assert.That(events.Last().Error.Code, Is.EqualTo(ErrorCode.DeadlineExceeded));
        });
    }

    [Test]
    public void DeadlineUsesContinuationContextWithoutLettingHandlerConstructTimeout()
    {
        var clock = new FakeClock();
        var coordinator = NewCoordinator(clock, new NavigationTimeoutHandler());
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;
        var request = new CommandRequest
        {
            CommandId = "aeaeaeae-aeae-4eae-8eae-aeaeaeaeaeae",
            TimeoutMs = 1,
            Navigate = new NavigateRequest
            {
                Position = new WorldPosition { LocationId = "Hospital", X = 10, Y = 15 },
                Arrival = ArrivalMode.Exact,
            },
        };

        coordinator.Submit(request);
        coordinator.ReleaseAccepted(request.CommandId);
        coordinator.Tick();
        clock.Milliseconds = 1;
        coordinator.Tick();

        var terminal = events.Last();
        Assert.Multiple(() =>
        {
            Assert.That(terminal.State, Is.EqualTo(CommandState.TimedOut));
            Assert.That(terminal.Error.Code, Is.EqualTo(ErrorCode.DeadlineExceeded));
            Assert.That(terminal.Error.Navigation.LastConfirmedPosition, Is.EqualTo(new WorldPosition { LocationId = "Town", X = 36, Y = 57 }));
            Assert.That(terminal.Error.Navigation.RouteSegmentsTotal, Is.EqualTo(3));
            Assert.That(terminal.Error.Navigation.RouteSegmentsCompleted, Is.EqualTo(2));
            Assert.That(terminal.Error.Navigation.InterruptionReason, Is.EqualTo("deadline_exceeded"));
        });
    }

    [TestCase(InvalidTerminalKind.WrongResultBranch)]
    [TestCase(InvalidTerminalKind.Cancelled)]
    [TestCase(InvalidTerminalKind.TimedOut)]
    [TestCase(InvalidTerminalKind.NonTerminal)]
    [TestCase(InvalidTerminalKind.InvalidFailedCode)]
    [TestCase(InvalidTerminalKind.InvalidProgress)]
    public void InvalidHandlerTerminalsNormalizeToInternalFailed(InvalidTerminalKind kind)
    {
        var coordinator = NewCoordinator(new FakeClock(), new InvalidTerminalHandler(kind));
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;
        coordinator.Submit(RuntimeRequest("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"));
        coordinator.ReleaseAccepted("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].State, Is.EqualTo(CommandState.Failed));
            Assert.That(events[0].Error.Code, Is.EqualTo(ErrorCode.Internal));
        });
    }

    [Test]
    public void ContextualInvalidArgumentRemainsACommandFailure()
    {
        var coordinator = NewCoordinator(new FakeClock(), new ContextualInvalidArgumentHandler());
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;
        var request = RuntimeRequest("12121212-1212-4212-8212-121212121212");

        coordinator.Submit(request);
        coordinator.ReleaseAccepted(request.CommandId);
        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(events.Single().State, Is.EqualTo(CommandState.Failed));
            Assert.That(events.Single().Error.Code, Is.EqualTo(ErrorCode.InvalidArgument));
            Assert.That(events.Single().Error.Navigation, Is.Not.Null);
            Assert.That(
                events.Single().Error.Navigation.LastConfirmedPosition,
                Is.EqualTo(new WorldPosition { LocationId = "Farm", X = 3, Y = 4 })
            );
        });
    }

    [Test]
    public void ActiveMutationTickAlsoAdvancesOneQueuedReadOnlyCommand()
    {
        var clock = new FakeClock();
        var staged = new StagedHandler();
        var immediate = new ImmediateHandler();
        var coordinator = NewCoordinator(clock, staged, immediate);
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;

        coordinator.Submit(FaceRequest("cccccccc-cccc-4ccc-8ccc-cccccccccccc"));
        coordinator.Submit(RuntimeRequest("dddddddd-dddd-4ddd-8ddd-dddddddddddd"));
        coordinator.ReleaseAccepted("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        coordinator.ReleaseAccepted("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        coordinator.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(events.Select(item => item.State), Is.EqualTo(new[] { CommandState.Running, CommandState.Succeeded }));
            Assert.That(immediate.Executions, Is.EqualTo(1));
        });
    }

    [Test]
    public void StatusReadsAnAtomicSnapshotWhileCancellationIsPending()
    {
        var clock = new FakeClock();
        var coordinator = NewCoordinator(clock, new StagedHandler());
        var request = FaceRequest("55555555-5555-4555-8555-555555555555");

        coordinator.Submit(request);
        coordinator.ReleaseAccepted(request.CommandId);
        coordinator.Tick();
        coordinator.RequestCancel(request.CommandId);
        var status = (CoordinatorResponse.StatusResponse)coordinator.GetStatus(request.CommandId);

        Assert.Multiple(() =>
        {
            Assert.That(status.Found, Is.True);
            Assert.That(status.Current!.State, Is.EqualTo(CommandState.Running));
            Assert.That(status.Current.Phase, Is.EqualTo("cancelling"));
            Assert.That(status.Current.OutcomeCase, Is.EqualTo(CommandEvent.OutcomeOneofCase.None));
        });
    }

    [Test]
    public void CommandCannotRunBeforeAcceptedResponseIsQueued()
    {
        var handler = new ImmediateHandler();
        var coordinator = NewCoordinator(new FakeClock(), handler);
        var events = new List<CommandEvent>();
        coordinator.EventPublished += events.Add;
        var request = RuntimeRequest("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");

        coordinator.Submit(request);
        coordinator.Tick();
        Assert.Multiple(() =>
        {
            Assert.That(handler.Executions, Is.Zero);
            Assert.That(events, Is.Empty);
        });

        coordinator.ReleaseAccepted(request.CommandId);
        coordinator.Tick();
        Assert.Multiple(() =>
        {
            Assert.That(handler.Executions, Is.EqualTo(1));
            Assert.That(events.Single().State, Is.EqualTo(CommandState.Succeeded));
        });
    }

    private static CommandCoordinator NewCoordinator(FakeClock clock, params ICapabilityHandler[] handlers) =>
        new(new CapabilityRegistry(handlers), clock);

    private static CommandRequest RuntimeRequest(string commandId, uint timeout = 0) => new()
    {
        CommandId = commandId,
        TimeoutMs = timeout,
        QueryRuntime = new QueryRuntimeRequest(),
    };

    private static CommandRequest FaceRequest(string commandId, uint timeout = 0) => new()
    {
        CommandId = commandId,
        TimeoutMs = timeout,
        Face = new FaceRequest { Direction = Direction.Up },
    };

    private static CommandRequest WithUnknownField(CommandRequest request) =>
        CommandRequest.Parser.ParseFrom(request.ToByteArray().Concat(new byte[] { 0xa0, 0x06, 0x01 }).ToArray());

    private sealed class FakeClock : ICommandClock
    {
        public long Milliseconds { get; set; }
    }

    private sealed class ImmediateHandler : IImmediateCapabilityHandler
    {
        public string Id => "query_runtime";
        public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.QueryRuntime;
        public int Executions { get; private set; }
        public Error? Validate(CommandRequest request) => request.OperationCase == Operation ? null : new Error { Code = ErrorCode.InvalidArgument };
        public CommandEvent Execute(string commandId, CommandRequest request)
        {
            Executions++;
            return new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.Succeeded,
                Result = new CapabilityResult { QueryRuntime = new QueryRuntimeResult { Snapshot = new RuntimeSnapshot() } },
            };
        }
    }

    private sealed class StagedHandler : ILongRunningCapabilityHandler
    {
        public string Id => "face";
        public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.Face;
        public FakeContinuation Continuation { get; } = new();
        public Error? Validate(CommandRequest request) => request.OperationCase == Operation ? null : new Error { Code = ErrorCode.InvalidArgument };
        public ICommandContinuation Start(string commandId, CommandRequest request) => Continuation;
    }

    private sealed class ContextualInvalidArgumentHandler : IImmediateCapabilityHandler
    {
        public string Id => "query_runtime";
        public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.QueryRuntime;
        public Error? Validate(CommandRequest request) => null;
        public CommandEvent Execute(string commandId, CommandRequest request) => new()
        {
            CommandId = commandId,
            State = CommandState.Failed,
            Error = new Error
            {
                Code = ErrorCode.InvalidArgument,
                Message = "引用类型与能力不匹配",
                Navigation = new NavigationFailureContext
                {
                    LastConfirmedPosition = new WorldPosition { LocationId = "Farm", X = 3, Y = 4 },
                },
            },
        };
    }

    private sealed class FakeContinuation : ICommandContinuation
    {
        public string Phase => "running";
        public uint? ProgressPercent => null;
        public bool CanCancel => true;
        public bool SawCancel { get; private set; }
        public ContinuationStopSignal LastSignal { get; private set; }
        public ContinuationStep Tick(ContinuationStopSignal signal)
        {
            LastSignal = signal;
            if (signal == ContinuationStopSignal.CancelRequested)
            {
                SawCancel = true;
                return new ContinuationStep.Stopped();
            }
            if (signal == ContinuationStopSignal.DeadlineExceeded)
                return new ContinuationStep.Stopped();
            return new ContinuationStep.Pending();
        }
    }

    private sealed class NavigationTimeoutHandler : ILongRunningCapabilityHandler
    {
        public string Id => "navigate";
        public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.Navigate;
        public Error? Validate(CommandRequest request) => null;
        public ICommandContinuation Start(string commandId, CommandRequest request) => new NavigationTimeoutContinuation();
    }

    private sealed class NavigationTimeoutContinuation : ICommandContinuation, IStopErrorContextProvider
    {
        public string Phase => "walking";
        public uint? ProgressPercent => null;
        public bool CanCancel => true;
        public ContinuationStep Tick(ContinuationStopSignal signal) =>
            signal == ContinuationStopSignal.DeadlineExceeded
                ? new ContinuationStep.Stopped()
                : new ContinuationStep.Pending();

        public void EnrichStopError(ContinuationStopSignal signal, Error error)
        {
            if (signal != ContinuationStopSignal.DeadlineExceeded)
                return;
            error.Navigation = new NavigationFailureContext
            {
                LastConfirmedPosition = new WorldPosition { LocationId = "Town", X = 36, Y = 57 },
                RouteSegmentsTotal = 3,
                RouteSegmentsCompleted = 2,
                InterruptionReason = "deadline_exceeded",
                ResumeHint = "可按原目标重新调用 navigate 继续执行。",
            };
        }
    }

    public enum InvalidTerminalKind
    {
        WrongResultBranch,
        Cancelled,
        TimedOut,
        NonTerminal,
        InvalidFailedCode,
        InvalidProgress,
    }

    private sealed class InvalidTerminalHandler : IImmediateCapabilityHandler
    {
        private readonly InvalidTerminalKind _kind;

        public InvalidTerminalHandler(InvalidTerminalKind kind) => _kind = kind;
        public string Id => "query_runtime";
        public CommandRequest.OperationOneofCase Operation => CommandRequest.OperationOneofCase.QueryRuntime;
        public Error? Validate(CommandRequest request) => null;

        public CommandEvent Execute(string commandId, CommandRequest request) => _kind switch
        {
            InvalidTerminalKind.WrongResultBranch => new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.Succeeded,
                Result = new CapabilityResult { QueryInventory = new QueryInventoryResult() },
            },
            InvalidTerminalKind.Cancelled => new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.Cancelled,
                Error = new Error { Code = ErrorCode.Cancelled, Message = "handler cancellation" },
            },
            InvalidTerminalKind.TimedOut => new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.TimedOut,
                Error = new Error { Code = ErrorCode.DeadlineExceeded, Message = "handler timeout" },
            },
            InvalidTerminalKind.InvalidFailedCode => new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.Failed,
                Error = new Error { Code = ErrorCode.Busy, Message = "invalid terminal error code" },
            },
            InvalidTerminalKind.InvalidProgress => new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.Succeeded,
                ProgressPercent = 101,
                Result = new CapabilityResult { QueryRuntime = new QueryRuntimeResult() },
            },
            _ => new CommandEvent { CommandId = commandId, State = CommandState.Running, Phase = "invalid" },
        };
    }
}
