using System.Diagnostics;
using Google.Protobuf;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class CommandCoordinator
{
    internal const uint ResultRetentionMs = 300_000;
    private const int MaximumSuccessfulEventBytes = 768 * 1024;

    private readonly CapabilityRegistry _registry;
    private readonly ICommandClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, CommandRecord> _records = new(StringComparer.Ordinal);
    private readonly List<CommandRecord> _queued = new();
    private CommandRecord? _activeMutation;

    public CommandCoordinator(CapabilityRegistry registry, ICommandClock? clock = null)
    {
        _registry = registry;
        _clock = clock ?? StopwatchCommandClock.Instance;
    }

    public event Action<CommandEvent>? EventPublished;

    public bool HasActiveMutation
    {
        get
        {
            lock (_gate)
                return _activeMutation is { IsTerminal: false };
        }
    }

    public CoordinatorResponse Submit(CommandRequest request)
    {
        lock (_gate)
        {
            if (!IsUuidV4(request.CommandId))
                return CoordinatorResponse.ProtocolFailure(ErrorCode.InvalidArgument, "command_id 必须是小写 UUIDv4");
            if (!_registry.TryResolve(request, out var capability))
                return CoordinatorResponse.ProtocolFailure(ErrorCode.UnsupportedCapability, "当前构建未提供该能力");
            var validation = capability.Handler.Validate(request);
            if (validation is not null)
                return CoordinatorResponse.ProtocolFailure(validation.Code, validation.Message);
            var timeout = request.TimeoutMs == 0
                ? capability.Descriptor.DefaultTimeoutMs
                : request.TimeoutMs;
            if (timeout > capability.Descriptor.MaxTimeoutMs)
                return CoordinatorResponse.ProtocolFailure(ErrorCode.InvalidArgument, "timeout_ms 超过能力上限");

            var canonical = Canonicalize(request);
            if (_records.TryGetValue(request.CommandId, out var existing))
            {
                if (!canonical.AsSpan().SequenceEqual(existing.CanonicalRequest))
                    return CoordinatorResponse.ProtocolFailure(ErrorCode.Conflict, "command_id 已用于不同请求");
                return existing.ResultExpired
                    ? CoordinatorResponse.ProtocolFailure(ErrorCode.IdempotencyRecordExpired, "命令结果保留期已过")
                    : CoordinatorResponse.FromEvent(existing.Current);
            }
            if (_queued.Count >= 64)
                return CoordinatorResponse.ProtocolFailure(ErrorCode.Busy, "主线程命令队列已满");
            if (capability.Descriptor.SideEffect == SideEffect.Mutating && _activeMutation is not null)
                return CoordinatorResponse.ProtocolFailure(ErrorCode.Busy, "已有变更命令正在执行");

            var record = new CommandRecord(
                request.CommandId,
                canonical,
                request.Clone(),
                capability,
                timeout,
                _clock.Milliseconds
            );
            _records.Add(record.CommandId, record);
            _queued.Add(record);
            if (capability.Descriptor.SideEffect == SideEffect.Mutating)
                _activeMutation = record;
            return CoordinatorResponse.FromEvent(record.Current);
        }
    }

    public CoordinatorResponse RequestCancel(string commandId)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(commandId, out var record))
                return CoordinatorResponse.Cancel(commandId, false, null, NewError(ErrorCode.NotFound, "command_id 不存在"));
            if (record.ResultExpired)
                return CoordinatorResponse.ProtocolFailure(ErrorCode.IdempotencyRecordExpired, "命令结果保留期已过");
            if (record.IsTerminal || !record.Descriptor.Cancellable || record.Continuation?.CanCancel == false)
                return CoordinatorResponse.Cancel(commandId, false, record.Current, NewError(ErrorCode.Conflict, "命令当前不可取消"));
            if (record.CancelRequestedAt is null)
            {
                record.CancelRequestedAt = _clock.Milliseconds;
                record.Current.Phase = "cancelling";
            }
            return CoordinatorResponse.Cancel(commandId, true, record.Current, null);
        }
    }

    public void ReleaseAccepted(string commandId)
    {
        lock (_gate)
        {
            if (_records.TryGetValue(commandId, out var record) && !record.IsTerminal)
                record.ReadyToRun = true;
        }
    }

    public CoordinatorResponse GetStatus(string commandId)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(commandId, out var record))
                return CoordinatorResponse.Status(commandId, false, null);
            return record.ResultExpired
                ? CoordinatorResponse.ProtocolFailure(ErrorCode.IdempotencyRecordExpired, "命令结果保留期已过")
                : CoordinatorResponse.Status(commandId, true, record.Current);
        }
    }

    public void Tick()
    {
        ExpireResults();
        CommandRecord? active;
        lock (_gate)
            active = _activeMutation is { IsTerminal: false } mutation ? mutation : DequeueNextLocked();
        if (active is not null)
            Advance(active);

        CommandRecord? readOnly = null;
        lock (_gate)
        {
            if (_activeMutation is { IsTerminal: false })
                readOnly = DequeueReadOnlyLocked();
        }
        if (readOnly is not null)
            Advance(readOnly);
    }

    private void Advance(CommandRecord record)
    {
        var events = new List<CommandEvent>();
        lock (_gate)
            AdvanceLocked(record, events);
        foreach (var commandEvent in events)
            Publish(commandEvent);
    }

    private void AdvanceLocked(CommandRecord record, List<CommandEvent> events)
    {
        if (record.IsTerminal)
            return;
        var stop = StopSignal(record);
        if (!record.Started)
        {
            if (stop != ContinuationStopSignal.None)
            {
                CompleteStop(record, stop, events);
                return;
            }
            record.Started = true;
            if (record.Handler is IImmediateCapabilityHandler immediate)
            {
                CommandEvent terminal;
                try
                {
                    terminal = immediate.Execute(record.CommandId, record.Request);
                }
                catch
                {
                    terminal = Failed(record.CommandId, ErrorCode.Internal, "能力执行失败", "failed");
                }
                Complete(record, NormalizeTerminal(record, terminal), events);
                return;
            }
            if (record.Handler is ILongRunningCapabilityHandler staged)
            {
                try
                {
                    record.Continuation = staged.Start(record.CommandId, record.Request);
                    record.Current = new CommandEvent
                    {
                        CommandId = record.CommandId,
                        State = CommandState.Running,
                        Phase = record.Continuation.Phase,
                    };
                    if (record.Continuation.ProgressPercent is > 100)
                    {
                        Complete(record, Failed(record.CommandId, ErrorCode.Internal, "能力返回无效进度", "failed"), events);
                        return;
                    }
                    if (record.Continuation.ProgressPercent is { } progress)
                        record.Current.ProgressPercent = progress;
                    events.Add(record.Current.Clone());
                }
                catch
                {
                    Complete(record, Failed(record.CommandId, ErrorCode.Internal, "能力启动失败", "failed"), events);
                }
                return;
            }
            Complete(record, Failed(record.CommandId, ErrorCode.Internal, "能力执行类型无效", "failed"), events);
            return;
        }

        var continuation = record.Continuation;
        if (continuation is null)
            return;
        ContinuationStep step;
        try
        {
            step = continuation.Tick(stop);
        }
        catch
        {
            Complete(record, Failed(record.CommandId, ErrorCode.Internal, "能力执行失败", "failed"), events);
            return;
        }
        switch (step)
        {
            case ContinuationStep.Pending:
                if (continuation.ProgressPercent is > 100)
                {
                    Complete(record, Failed(record.CommandId, ErrorCode.Internal, "能力返回无效进度", "failed"), events);
                    break;
                }
                record.Current.Phase = continuation.Phase;
                if (continuation.ProgressPercent is { } progress)
                    record.Current.ProgressPercent = progress;
                events.Add(record.Current.Clone());
                break;
            case ContinuationStep.Succeeded success when stop == ContinuationStopSignal.None:
                Complete(record, NormalizeTerminal(record, new CommandEvent
                {
                    CommandId = record.CommandId,
                    State = CommandState.Succeeded,
                    Phase = "completed",
                    ProgressPercent = 100,
                    Result = success.Result,
                }), events);
                break;
            case ContinuationStep.Failed failure when stop == ContinuationStopSignal.None:
                Complete(record, NormalizeTerminal(
                    record,
                    Failed(record.CommandId, failure.Error, "failed")
                ), events);
                break;
            case ContinuationStep.Stopped when stop != ContinuationStopSignal.None:
                CompleteStop(record, stop, events);
                break;
            default:
                Complete(record, Failed(record.CommandId, ErrorCode.Internal, "能力 continuation 返回无效步骤", "failed"), events);
                break;
        }
    }

    private ContinuationStopSignal StopSignal(CommandRecord record)
    {
        var now = _clock.Milliseconds;
        if (record.CancelRequestedAt is { } cancelled && cancelled <= record.DeadlineAt)
            return ContinuationStopSignal.CancelRequested;
        return now >= record.DeadlineAt
            ? ContinuationStopSignal.DeadlineExceeded
            : ContinuationStopSignal.None;
    }

    private void CompleteStop(CommandRecord record, ContinuationStopSignal stop, List<CommandEvent> events) =>
        Complete(record, stop == ContinuationStopSignal.CancelRequested
            ? new CommandEvent
            {
                CommandId = record.CommandId,
                State = CommandState.Cancelled,
                Phase = "cancelled",
                Error = NewError(ErrorCode.Cancelled, "命令已取消"),
            }
            : new CommandEvent
            {
                CommandId = record.CommandId,
                State = CommandState.TimedOut,
                Phase = "timed_out",
                Error = NewError(ErrorCode.DeadlineExceeded, "命令已超过期限"),
            }, events);

    private void Complete(CommandRecord record, CommandEvent terminal, List<CommandEvent> events)
    {
        if (record.IsTerminal)
            return;
        record.Current = terminal.Clone();
        record.TerminalAt = _clock.Milliseconds;
        if (ReferenceEquals(_activeMutation, record))
            _activeMutation = null;
        events.Add(terminal.Clone());
    }

    private CommandEvent NormalizeTerminal(CommandRecord record, CommandEvent terminal)
    {
        if (!IsTerminal(terminal.State) || terminal.CommandId != record.CommandId)
            return Failed(record.CommandId, ErrorCode.Internal, "能力返回无效终态", "failed");
        if (terminal.HasProgressPercent && terminal.ProgressPercent > 100)
            return Failed(record.CommandId, ErrorCode.Internal, "能力返回无效进度", "failed");
        if (terminal.State == CommandState.Succeeded)
        {
            if (terminal.OutcomeCase != CommandEvent.OutcomeOneofCase.Result
                || terminal.Result.ResultCase.ToString() != record.Request.OperationCase.ToString())
                return Failed(record.CommandId, ErrorCode.Internal, "能力成功结果与请求不匹配", "failed");
        }
        else if (terminal.OutcomeCase != CommandEvent.OutcomeOneofCase.Error || terminal.Error is null)
            return Failed(record.CommandId, ErrorCode.Internal, "能力失败终态缺少错误", "failed");
        else if (terminal.State is CommandState.Cancelled or CommandState.TimedOut
            || terminal.Error.Code is ErrorCode.Cancelled or ErrorCode.DeadlineExceeded)
            return Failed(record.CommandId, ErrorCode.Internal, "Handler 不得构造取消或超时终态", "failed");
        else if (terminal.Error.Code is not (ErrorCode.InvalidArgument
            or ErrorCode.NotReady
            or ErrorCode.NotFound
            or ErrorCode.StaleRef
            or ErrorCode.OutOfRange
            or ErrorCode.ExecutionFailed
            or ErrorCode.Internal))
            return Failed(record.CommandId, ErrorCode.Internal, "Handler 返回无效失败错误码", "failed");
        if (terminal.State == CommandState.Succeeded && terminal.CalculateSize() >= MaximumSuccessfulEventBytes)
            return Failed(record.CommandId, ErrorCode.ExecutionFailed, "命令结果达到或超过 768 KiB 限制", "result_too_large");
        return terminal;
    }

    private void ExpireResults()
    {
        lock (_gate)
        {
            foreach (var record in _records.Values)
            {
                if (record.TerminalAt is { } terminalAt
                    && !record.ResultExpired
                    && _clock.Milliseconds - terminalAt >= ResultRetentionMs)
                {
                    record.Current = new CommandEvent
                    {
                        CommandId = record.CommandId,
                        State = record.Current.State,
                        Phase = "result_expired",
                    };
                    record.ResultExpired = true;
                }
            }
        }
    }

    private CommandRecord? DequeueNextLocked()
    {
        var index = _queued.FindIndex(record => record.ReadyToRun);
        if (index < 0)
            return null;
        var next = _queued[index];
        _queued.RemoveAt(index);
        return next;
    }

    private CommandRecord? DequeueReadOnlyLocked()
    {
        var index = _queued.FindIndex(record =>
            record.ReadyToRun && record.Descriptor.SideEffect == SideEffect.ReadOnly
        );
        if (index < 0)
            return null;
        var next = _queued[index];
        _queued.RemoveAt(index);
        return next;
    }

    private void Publish(CommandEvent commandEvent) => EventPublished?.Invoke(commandEvent.Clone());

    private static byte[] Canonicalize(CommandRequest request) =>
        CommandRequest.Parser.WithDiscardUnknownFields(true).ParseFrom(request.ToByteArray()).ToByteArray();

    private static bool IsTerminal(CommandState state) => state is CommandState.Succeeded
        or CommandState.Failed or CommandState.Cancelled or CommandState.TimedOut;

    private static bool IsUuidV4(string value) => value.Length == 36
        && value == value.ToLowerInvariant()
        && value[14] == '4'
        && "89ab".Contains(value[19])
        && Guid.TryParseExact(value, "D", out _);

    private static CommandEvent Failed(string commandId, ErrorCode code, string message, string phase) => new()
    {
        CommandId = commandId,
        State = CommandState.Failed,
        Phase = phase,
        Error = NewError(code, message),
    };

    private static CommandEvent Failed(string commandId, Error error, string phase) => new()
    {
        CommandId = commandId,
        State = CommandState.Failed,
        Phase = phase,
        Error = error.Clone(),
    };

    private static Error NewError(ErrorCode code, string message) => new() { Code = code, Message = message };

    private sealed class CommandRecord
    {
        public CommandRecord(string commandId, byte[] canonicalRequest, CommandRequest request, RegisteredCapability capability, uint timeoutMs, long acceptedAt)
        {
            CommandId = commandId;
            CanonicalRequest = canonicalRequest;
            Request = request;
            Handler = capability.Handler;
            Descriptor = capability.Descriptor;
            DeadlineAt = checked(acceptedAt + timeoutMs);
            Current = new CommandEvent { CommandId = commandId, State = CommandState.Accepted, Phase = "queued" };
        }

        public string CommandId { get; }
        public byte[] CanonicalRequest { get; }
        public CommandRequest Request { get; }
        public ICapabilityHandler Handler { get; }
        public CapabilityDescriptor Descriptor { get; }
        public long DeadlineAt { get; }
        public CommandEvent Current { get; set; }
        public bool ResultExpired { get; set; }
        public bool ReadyToRun { get; set; }
        public long? TerminalAt { get; set; }
        public long? CancelRequestedAt { get; set; }
        public bool Started { get; set; }
        public ICommandContinuation? Continuation { get; set; }
        public bool IsTerminal => TerminalAt is not null;
    }
}

internal abstract record CoordinatorResponse
{
    internal sealed record Event(CommandEvent Value) : CoordinatorResponse;
    internal sealed record ProtocolError(Error Value) : CoordinatorResponse;
    internal sealed record StatusResponse(string CommandId, bool Found, CommandEvent? Current) : CoordinatorResponse;
    internal sealed record CancelResponse(string CommandId, bool Accepted, CommandEvent? Current, Error? Error) : CoordinatorResponse;

    public static CoordinatorResponse ProtocolFailure(ErrorCode code, string message) => new ProtocolError(new Error { Code = code, Message = message });
    public static CoordinatorResponse FromEvent(CommandEvent value) => new Event(value.Clone());
    public static CoordinatorResponse Status(string commandId, bool found, CommandEvent? current) => new StatusResponse(commandId, found, current?.Clone());
    public static CoordinatorResponse Cancel(string commandId, bool accepted, CommandEvent? current, Error? error) => new CancelResponse(commandId, accepted, current?.Clone(), error?.Clone());
}

internal interface ICommandClock
{
    long Milliseconds { get; }
}

internal sealed class StopwatchCommandClock : ICommandClock
{
    public static StopwatchCommandClock Instance { get; } = new();
    public long Milliseconds => Stopwatch.GetTimestamp() * 1_000 / Stopwatch.Frequency;
}
