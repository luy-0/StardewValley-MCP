using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Google.Protobuf;
using StardewModdingAPI;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class LocalServer
{
    private const uint ResultRetentionMs = 300_000;
    private const uint ReconnectGraceMs = 10_000;
    private const int MaximumSuccessfulEventBytes = 768 * 1024;
    private static readonly ProtocolVersion ProtocolVersion = new() { Major = 1, Minor = 0 };

    private readonly TcpListener _listener;
    private readonly byte[] _secret;
    private readonly IMonitor _monitor;
    private readonly CapabilityRegistry _registry;
    private readonly CapabilitySnapshot _snapshot;
    private readonly ConcurrentQueue<CommandRecord> _pending = new();
    private readonly ConcurrentDictionary<string, CommandRecord> _commands = new();
    private readonly object _ownerLock = new();
    private readonly string _modInstanceId;

    private Owner? _owner;
    private bool _ownerConnected;
    private ulong _lastLeaseEpoch;
    private long _messageSequence;

    public LocalServer(
        IPAddress address,
        int port,
        byte[] secret,
        IMonitor monitor,
        CapabilityRegistry registry,
        string modInstanceId
    )
    {
        _listener = new TcpListener(address, port);
        _secret = secret.ToArray();
        _monitor = monitor;
        _registry = registry;
        _snapshot = registry.Snapshot.Clone();
        _modInstanceId = modInstanceId;
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _monitor.Log($"本地协议监听已启动：{endpoint.Address}:{endpoint.Port}", LogLevel.Info);
    }

    public void ProcessOne()
    {
        if (!_pending.TryDequeue(out var command) || command.IsTerminal)
            return;

        CommandEvent terminal;
        if (command.ElapsedMilliseconds >= command.TimeoutMs)
        {
            terminal = new CommandEvent
            {
                CommandId = command.CommandId,
                State = CommandState.TimedOut,
                Phase = "timed_out",
                Error = new Error
                {
                    Code = ErrorCode.DeadlineExceeded,
                    Message = "命令在执行前已超过期限",
                },
            };
        }
        else
        {
            var handlerStartedAt = Stopwatch.GetTimestamp();
            try
            {
                terminal = command.Handler.Execute(command.CommandId, command.Request);
            }
            catch (Exception exception)
            {
                _monitor.Log(
                    $"capability_execute_failed capability_id={command.Handler.Id} "
                    + $"exception_type={exception.GetType().Name}",
                    LogLevel.Error
                );
                terminal = new CommandEvent
                {
                    CommandId = command.CommandId,
                    State = CommandState.Failed,
                    Phase = "failed",
                    Error = new Error
                    {
                        Code = ErrorCode.Internal,
                        Message = "能力执行失败",
                    },
                };
            }
            var handlerElapsedMs = ElapsedMilliseconds(handlerStartedAt);
            var producedBytes = terminal.CalculateSize();
            if (terminal.State == CommandState.Succeeded && producedBytes >= MaximumSuccessfulEventBytes)
            {
                terminal = new CommandEvent
                {
                    CommandId = command.CommandId,
                    State = CommandState.Failed,
                    Phase = "result_too_large",
                    Error = new Error
                    {
                        Code = ErrorCode.ExecutionFailed,
                        Message = "命令结果达到或超过 768 KiB 限制",
                    },
                };
            }

            _monitor.Log(
                $"capability_execute capability_id={command.Handler.Id} "
                + $"elapsed_ms={handlerElapsedMs} serialized_bytes={terminal.CalculateSize()} "
                + $"produced_bytes={producedBytes} state={terminal.State}",
                LogLevel.Debug
            );
        }
        command.Complete(terminal);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var ownsConnection = false;
        try
        {
            if (client.Client.RemoteEndPoint is not IPEndPoint peer || !IPAddress.IsLoopback(peer.Address))
                return;

            client.NoDelay = true;
            var stream = client.GetStream();
            using var handshakeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var serverNonce = RandomNumberGenerator.GetBytes(32);
            var serverHelloId = NextMessageId();
            await FrameCodec.WriteAsync(
                stream,
                new TransportFrame
                {
                    MessageId = serverHelloId,
                    ServerHello = new ServerHello
                    {
                        Version = ProtocolVersion.Clone(),
                        ModInstanceId = _modInstanceId,
                        ServerNonce = ByteString.CopyFrom(serverNonce),
                    },
                },
                handshakeTimeout.Token
            ).ConfigureAwait(false);

            var clientFrame = await FrameCodec
                .ReadAsync(stream, handshakeTimeout.Token)
                .ConfigureAwait(false);
            var handshakeError = ValidateClientHello(clientFrame, serverHelloId, serverNonce);
            if (handshakeError is not null)
            {
                await RejectAsync(stream, clientFrame.MessageId, handshakeError, handshakeTimeout.Token)
                    .ConfigureAwait(false);
                return;
            }

            Owner? owner;
            Error? ownerError;
            lock (_ownerLock)
            {
                owner = AcquireOwner(clientFrame.ClientHello, out ownerError);
                ownsConnection = owner is not null;
            }
            if (owner is null)
            {
                await RejectAsync(stream, clientFrame.MessageId, ownerError!, handshakeTimeout.Token)
                    .ConfigureAwait(false);
                return;
            }

            var clientHello = clientFrame.ClientHello;
            var ready = new ServerReady
            {
                SelectedVersion = ProtocolVersion.Clone(),
                SessionId = owner.SessionId,
                LeaseEpoch = owner.LeaseEpoch,
                CapabilitySnapshot = _snapshot.Clone(),
                ResultRetentionMs = ResultRetentionMs,
                ReconnectGraceMs = ReconnectGraceMs,
            };
            ready.AuthTag = ByteString.CopyFrom(
                Authentication.ComputeServerTag(
                    _secret,
                    _modInstanceId,
                    clientHello.ClientInstanceId,
                    serverNonce,
                    clientHello.ClientNonce.ToByteArray(),
                    ProtocolVersion,
                    owner.SessionId,
                    owner.LeaseEpoch,
                    _snapshot.Digest,
                    ResultRetentionMs,
                    ReconnectGraceMs
                )
            );
            await FrameCodec.WriteAsync(
                stream,
                new TransportFrame
                {
                    MessageId = NextMessageId(),
                    ReplyTo = clientFrame.MessageId,
                    ServerReady = ready,
                },
                handshakeTimeout.Token
            ).ConfigureAwait(false);

            var fence = owner.CreateFence(_snapshot.Digest);
            var seenMessageIds = new HashSet<string>(StringComparer.Ordinal) { clientFrame.MessageId };
            while (true)
            {
                var frame = await FrameCodec.ReadAsync(stream, CancellationToken.None).ConfigureAwait(false);
                if (!IsMessageId(frame.MessageId) || !seenMessageIds.Add(frame.MessageId))
                    throw new InvalidDataException("message_id 无效或重复");
                if (!FenceMatches(frame, fence))
                {
                    await SendErrorAsync(
                        stream,
                        fence,
                        frame.MessageId,
                        ErrorCode.StaleLease,
                        "Session Fence 已失效"
                    ).ConfigureAwait(false);
                    throw new InvalidDataException("Session Fence 已失效");
                }

                switch (frame.BodyCase)
                {
                    case TransportFrame.BodyOneofCase.CommandRequest:
                        await HandleCommandAsync(stream, fence, frame).ConfigureAwait(false);
                        break;
                    case TransportFrame.BodyOneofCase.GetCommandStatusRequest:
                        await HandleStatusAsync(stream, fence, frame).ConfigureAwait(false);
                        break;
                    case TransportFrame.BodyOneofCase.Ping:
                        await FrameCodec.WriteAsync(
                            stream,
                            new TransportFrame
                            {
                                MessageId = NextMessageId(),
                                ReplyTo = frame.MessageId,
                                Fence = fence.Clone(),
                                Pong = new Pong { Sequence = frame.Ping.Sequence },
                            },
                            CancellationToken.None
                        ).ConfigureAwait(false);
                        break;
                    default:
                        throw new InvalidDataException("认证后消息方向无效");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _monitor.Log("本地协议握手超时", LogLevel.Trace);
        }
        catch (EndOfStreamException)
        {
            _monitor.Log("MCP 本地连接已断开", LogLevel.Trace);
        }
        catch (Exception exception)
        {
            _monitor.Log($"本地协议连接关闭：{exception.GetType().Name}", LogLevel.Trace);
        }
        finally
        {
            if (ownsConnection)
            {
                lock (_ownerLock)
                {
                    _ownerConnected = false;
                    if (_owner is not null)
                        _owner.DisconnectedAt = Stopwatch.GetTimestamp();
                }
            }
            client.Dispose();
        }
    }

    private async Task HandleCommandAsync(
        Stream stream,
        SessionFence fence,
        TransportFrame frame
    )
    {
        var request = frame.CommandRequest;
        if (!IsUuidV4(request.CommandId))
        {
            await SendErrorAsync(stream, fence, frame.MessageId, ErrorCode.InvalidArgument, "command_id 必须是小写 UUIDv4");
            return;
        }
        if (!_registry.TryResolve(request, out var capability))
        {
            await SendErrorAsync(stream, fence, frame.MessageId, ErrorCode.UnsupportedCapability, "当前构建未提供该能力");
            return;
        }
        var validationError = capability.Handler.Validate(request);
        if (validationError is not null)
        {
            await SendErrorAsync(
                stream,
                fence,
                frame.MessageId,
                validationError.Code,
                validationError.Message
            );
            return;
        }

        var timeoutMs = request.TimeoutMs == 0
            ? capability.Descriptor.DefaultTimeoutMs
            : request.TimeoutMs;
        if (timeoutMs > capability.Descriptor.MaxTimeoutMs)
        {
            await SendErrorAsync(stream, fence, frame.MessageId, ErrorCode.InvalidArgument, "timeout_ms 超过能力上限");
            return;
        }

        var requestBytes = request.ToByteArray();
        if (_commands.TryGetValue(request.CommandId, out var existing))
        {
            if (!requestBytes.AsSpan().SequenceEqual(existing.RequestBytes))
            {
                await SendErrorAsync(stream, fence, frame.MessageId, ErrorCode.Conflict, "command_id 已用于不同请求");
                return;
            }
            await ReplayCommandAsync(stream, fence, frame.MessageId, existing).ConfigureAwait(false);
            return;
        }
        if (_pending.Count >= 64)
        {
            await SendErrorAsync(stream, fence, frame.MessageId, ErrorCode.Busy, "主线程命令队列已满");
            return;
        }

        var command = new CommandRecord(
            request.CommandId,
            requestBytes,
            timeoutMs,
            capability.Handler,
            request.Clone()
        );
        if (!_commands.TryAdd(request.CommandId, command))
        {
            await HandleCommandAsync(stream, fence, frame).ConfigureAwait(false);
            return;
        }

        await SendEventAsync(stream, fence, frame.MessageId, command.Current).ConfigureAwait(false);
        _pending.Enqueue(command);
        await SendTerminalAsync(stream, fence, command).ConfigureAwait(false);
    }

    private async Task ReplayCommandAsync(
        Stream stream,
        SessionFence fence,
        string replyTo,
        CommandRecord command
    )
    {
        var current = command.Current;
        await SendEventAsync(stream, fence, replyTo, current).ConfigureAwait(false);
        if (!IsTerminal(current.State))
            await SendTerminalAsync(stream, fence, command).ConfigureAwait(false);
    }

    private async Task SendTerminalAsync(Stream stream, SessionFence fence, CommandRecord command)
    {
        var terminal = await command.Completion.Task.ConfigureAwait(false);
        await SendEventAsync(stream, fence, "", terminal).ConfigureAwait(false);
    }

    private async Task HandleStatusAsync(
        Stream stream,
        SessionFence fence,
        TransportFrame frame
    )
    {
        var commandId = frame.GetCommandStatusRequest.CommandId;
        var response = new GetCommandStatusResponse { CommandId = commandId };
        if (_commands.TryGetValue(commandId, out var command))
        {
            response.Found = true;
            response.Current = command.Current;
        }
        await FrameCodec.WriteAsync(
            stream,
            new TransportFrame
            {
                MessageId = NextMessageId(),
                ReplyTo = frame.MessageId,
                Fence = fence.Clone(),
                GetCommandStatusResponse = response,
            },
            CancellationToken.None
        ).ConfigureAwait(false);
    }

    private Error? ValidateClientHello(TransportFrame frame, string replyTo, byte[] serverNonce)
    {
        if (
            frame.BodyCase != TransportFrame.BodyOneofCase.ClientHello
            || frame.Fence is not null
            || frame.ReplyTo != replyTo
            || !IsMessageId(frame.MessageId)
        )
            return NewError(ErrorCode.Unauthenticated, "握手消息无效");

        var hello = frame.ClientHello;
        if (hello.RequestedVersion.Major != 1 || hello.RequestedVersion.Minor != 0)
            return NewError(ErrorCode.UnsupportedVersion, "不支持请求的协议版本");
        if (!IsUuidV4(hello.ClientInstanceId) || hello.ClientNonce.Length != 32)
            return NewError(ErrorCode.Unauthenticated, "客户端身份无效");

        var resumeId = hello.HasResumeSessionId ? hello.ResumeSessionId : "";
        var expected = Authentication.ComputeClientTag(
            _secret,
            _modInstanceId,
            hello.ClientInstanceId,
            serverNonce,
            hello.ClientNonce.ToByteArray(),
            hello.RequestedVersion,
            resumeId
        );
        return Authentication.FixedTimeEquals(expected, hello.AuthTag)
            ? null
            : NewError(ErrorCode.Unauthenticated, "本地认证失败");
    }

    private Owner? AcquireOwner(ClientHello hello, out Error? error)
    {
        error = null;
        if (_ownerConnected)
        {
            error = NewError(ErrorCode.Busy, "已有 MCP Owner 连接");
            return null;
        }

        var resumeId = hello.HasResumeSessionId ? hello.ResumeSessionId : "";
        if (_owner?.InsideGracePeriod(ReconnectGraceMs) == true)
        {
            if (resumeId == _owner.SessionId && hello.ClientInstanceId == _owner.ClientInstanceId)
            {
                _ownerConnected = true;
                _owner.DisconnectedAt = null;
                return _owner;
            }
            error = NewError(ErrorCode.Busy, "Owner 会话仍在重连宽限期");
            return null;
        }
        if (resumeId.Length > 0)
        {
            error = NewError(ErrorCode.Unauthenticated, "无法恢复指定会话");
            return null;
        }

        _owner = new Owner(
            Guid.NewGuid().ToString("D"),
            hello.ClientInstanceId,
            checked(++_lastLeaseEpoch)
        );
        _ownerConnected = true;
        return _owner;
    }

    private async Task SendEventAsync(
        Stream stream,
        SessionFence fence,
        string replyTo,
        CommandEvent commandEvent
    )
    {
        await FrameCodec.WriteAsync(
            stream,
            new TransportFrame
            {
                MessageId = NextMessageId(),
                ReplyTo = replyTo,
                Fence = fence.Clone(),
                CommandEvent = commandEvent.Clone(),
            },
            CancellationToken.None
        ).ConfigureAwait(false);
    }

    private async Task SendErrorAsync(
        Stream stream,
        SessionFence fence,
        string replyTo,
        ErrorCode code,
        string message
    )
    {
        await FrameCodec.WriteAsync(
            stream,
            new TransportFrame
            {
                MessageId = NextMessageId(),
                ReplyTo = replyTo,
                Fence = fence.Clone(),
                ProtocolError = new ProtocolError { Error = NewError(code, message) },
            },
            CancellationToken.None
        ).ConfigureAwait(false);
    }

    private async Task RejectAsync(
        Stream stream,
        string replyTo,
        Error error,
        CancellationToken cancellationToken
    )
    {
        await FrameCodec.WriteAsync(
            stream,
            new TransportFrame
            {
                MessageId = NextMessageId(),
                ReplyTo = replyTo,
                HandshakeRejected = new HandshakeRejected { Error = error },
            },
            cancellationToken
        ).ConfigureAwait(false);
    }

    private string NextMessageId() => $"s-{Interlocked.Increment(ref _messageSequence)}";

    private static bool FenceMatches(TransportFrame frame, SessionFence fence) =>
        frame.Fence is not null
        && frame.Fence.SessionId == fence.SessionId
        && frame.Fence.LeaseEpoch == fence.LeaseEpoch
        && frame.Fence.CapabilityDigest == fence.CapabilityDigest;

    private static bool IsMessageId(string value) =>
        value.Length is >= 1 and <= 64 && value.All(character => character is >= '!' and <= '~');

    private static bool IsUuidV4(string value) =>
        value.Length == 36
        && value == value.ToLowerInvariant()
        && value[14] == '4'
        && "89ab".Contains(value[19])
        && Guid.TryParseExact(value, "D", out _);

    private static bool IsTerminal(CommandState state) => state is
        CommandState.Succeeded
        or CommandState.Failed
        or CommandState.Cancelled
        or CommandState.TimedOut;

    private static Error NewError(ErrorCode code, string message) => new() { Code = code, Message = message };

    private sealed class Owner
    {
        public Owner(string sessionId, string clientInstanceId, ulong leaseEpoch)
        {
            SessionId = sessionId;
            ClientInstanceId = clientInstanceId;
            LeaseEpoch = leaseEpoch;
        }

        public string SessionId { get; }
        public string ClientInstanceId { get; }
        public ulong LeaseEpoch { get; }
        public long? DisconnectedAt { get; set; }

        public bool InsideGracePeriod(uint graceMs) =>
            DisconnectedAt is not null
            && ElapsedMilliseconds(DisconnectedAt.Value) < graceMs;

        public SessionFence CreateFence(string digest) =>
            new() { SessionId = SessionId, LeaseEpoch = LeaseEpoch, CapabilityDigest = digest };
    }

    private sealed class CommandRecord
    {
        private CommandEvent _current;

        public CommandRecord(
            string commandId,
            byte[] requestBytes,
            uint timeoutMs,
            ICapabilityHandler handler,
            CommandRequest request
        )
        {
            CommandId = commandId;
            RequestBytes = requestBytes;
            TimeoutMs = timeoutMs;
            Handler = handler;
            Request = request;
            AcceptedAt = Stopwatch.GetTimestamp();
            _current = new CommandEvent
            {
                CommandId = commandId,
                State = CommandState.Accepted,
                Phase = "queued",
            };
        }

        public string CommandId { get; }
        public byte[] RequestBytes { get; }
        public uint TimeoutMs { get; }
        public ICapabilityHandler Handler { get; }
        public CommandRequest Request { get; }
        public long AcceptedAt { get; }
        public TaskCompletionSource<CommandEvent> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long ElapsedMilliseconds => LocalServer.ElapsedMilliseconds(AcceptedAt);
        public CommandEvent Current => _current.Clone();
        public bool IsTerminal => Completion.Task.IsCompleted;

        public void Complete(CommandEvent terminal)
        {
            _current = terminal.Clone();
            Completion.TrySetResult(_current.Clone());
        }
    }

    private static long ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1_000 / Stopwatch.Frequency;
}
