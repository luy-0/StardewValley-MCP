using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Google.Protobuf;
using StardewModdingAPI;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class LocalServer
{
    private const uint ReconnectGraceMs = 10_000;
    private static readonly ProtocolVersion ProtocolVersion = new() { Major = 1, Minor = 0 };
    private readonly TcpListener _listener;
    private readonly byte[] _secret;
    private readonly IMonitor _monitor;
    private readonly CapabilitySnapshot _snapshot;
    private readonly CommandCoordinator _coordinator;
    private readonly object _ownerLock = new();
    private readonly string _modInstanceId;
    private Owner? _owner;
    private bool _ownerConnected;
    private ulong _lastLeaseEpoch;
    private long _messageSequence;

    public LocalServer(IPAddress address, int port, byte[] secret, IMonitor monitor, CapabilityRegistry registry, string modInstanceId)
    {
        _listener = new TcpListener(address, port);
        _secret = secret.ToArray();
        _monitor = monitor;
        _snapshot = registry.Snapshot.Clone();
        _coordinator = new CommandCoordinator(registry);
        _modInstanceId = modInstanceId;
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _monitor.Log($"本地协议监听已启动：{endpoint.Address}:{endpoint.Port}", LogLevel.Info);
    }

    public void Tick() => _coordinator.Tick();

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
        Action<CommandEvent>? eventSink = null;
        Channel<TransportFrame>? outgoing = null;
        Task? writerTask = null;
        Task? writerMonitorTask = null;
        using var writerStop = new CancellationTokenSource();
        try
        {
            if (client.Client.RemoteEndPoint is not IPEndPoint peer || !IPAddress.IsLoopback(peer.Address))
                return;
            client.NoDelay = true;
            var stream = client.GetStream();
            using var handshakeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var serverNonce = RandomNumberGenerator.GetBytes(32);
            var serverHelloId = NextMessageId();
            await FrameCodec.WriteAsync(stream, new TransportFrame
            {
                MessageId = serverHelloId,
                ServerHello = new ServerHello { Version = ProtocolVersion.Clone(), ModInstanceId = _modInstanceId, ServerNonce = ByteString.CopyFrom(serverNonce) },
            }, handshakeTimeout.Token).ConfigureAwait(false);

            var clientFrame = await FrameCodec.ReadAsync(stream, handshakeTimeout.Token).ConfigureAwait(false);
            var handshakeError = ValidateClientHello(clientFrame, serverHelloId, serverNonce);
            if (handshakeError is not null)
            {
                await RejectAsync(stream, clientFrame.MessageId, handshakeError, handshakeTimeout.Token).ConfigureAwait(false);
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
                await RejectAsync(stream, clientFrame.MessageId, ownerError!, handshakeTimeout.Token).ConfigureAwait(false);
                return;
            }
            var ready = NewReady(owner, clientFrame.ClientHello, serverNonce);
            await FrameCodec.WriteAsync(stream, new TransportFrame
            {
                MessageId = NextMessageId(), ReplyTo = clientFrame.MessageId, ServerReady = ready,
            }, handshakeTimeout.Token).ConfigureAwait(false);

            var fence = owner.CreateFence(_snapshot.Digest);
            var seenMessageIds = new HashSet<string>(StringComparer.Ordinal) { clientFrame.MessageId };
            outgoing = Channel.CreateBounded<TransportFrame>(new BoundedChannelOptions(128)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
            writerTask = WriteOutboundAsync(stream, outgoing.Reader, writerStop.Token);
            writerMonitorTask = CancelReadLoopWhenWriterStopsAsync(writerTask, writerStop);
            eventSink = commandEvent =>
            {
                if (TryQueueEvent(outgoing.Writer, new TransportFrame
                    {
                        MessageId = NextMessageId(), Fence = fence.Clone(), CommandEvent = commandEvent.Clone(),
                    }, writerStop))
                    return;
                _monitor.Log("本地协议写出队列已满，关闭连接以避免丢失命令事件", LogLevel.Error);
            };
            _coordinator.EventPublished += eventSink;
            while (true)
            {
                var frame = await FrameCodec.ReadAsync(stream, writerStop.Token).ConfigureAwait(false);
                if (!IsMessageId(frame.MessageId) || !seenMessageIds.Add(frame.MessageId))
                    throw new InvalidDataException("message_id 无效或重复");
                if (!FenceMatches(frame, fence))
                {
                    EnsureQueued(QueueProtocolError(outgoing.Writer, fence, frame.MessageId, ErrorCode.StaleLease, "Session Fence 已失效"));
                    throw new InvalidDataException("Session Fence 已失效");
                }
                switch (frame.BodyCase)
                {
                    case TransportFrame.BodyOneofCase.CommandRequest:
                        EnsureQueued(QueueSubmittedCommand(outgoing.Writer, fence, frame.MessageId, frame.CommandRequest));
                        break;
                    case TransportFrame.BodyOneofCase.CancelCommandRequest:
                        EnsureQueued(QueueCoordinatorResponse(outgoing.Writer, fence, frame.MessageId, _coordinator.RequestCancel(frame.CancelCommandRequest.CommandId)));
                        break;
                    case TransportFrame.BodyOneofCase.GetCommandStatusRequest:
                        EnsureQueued(QueueCoordinatorResponse(outgoing.Writer, fence, frame.MessageId, _coordinator.GetStatus(frame.GetCommandStatusRequest.CommandId)));
                        break;
                    case TransportFrame.BodyOneofCase.Ping:
                        EnsureQueued(outgoing.Writer.TryWrite(new TransportFrame
                        {
                            MessageId = NextMessageId(), ReplyTo = frame.MessageId, Fence = fence.Clone(), Pong = new Pong { Sequence = frame.Ping.Sequence },
                        }));
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
            if (eventSink is not null)
                _coordinator.EventPublished -= eventSink;
            if (outgoing is not null)
                outgoing.Writer.TryComplete();
            if (writerTask is not null)
            {
                try
                {
                    await writerTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
                catch
                {
                    writerStop.Cancel();
                }
            }
            if (writerMonitorTask is not null)
                await writerMonitorTask.ConfigureAwait(false);
            if (ownsConnection)
            {
                lock (_ownerLock)
                {
                    _ownerConnected = false;
                    if (_owner is not null)
                        _owner.DisconnectedAt = StopwatchCommandClock.Instance.Milliseconds;
                }
            }
            client.Dispose();
        }
    }

    private async Task WriteOutboundAsync(Stream stream, ChannelReader<TransportFrame> reader, CancellationToken cancellationToken)
    {
        await foreach (var frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            await FrameCodec.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
    }

    internal static bool TryQueueEvent(
        ChannelWriter<TransportFrame> writer,
        TransportFrame frame,
        CancellationTokenSource readLoopStop
    )
    {
        if (writer.TryWrite(frame))
            return true;
        writer.TryComplete(new IOException("本地协议写出队列已满"));
        readLoopStop.Cancel();
        return false;
    }

    internal static async Task CancelReadLoopWhenWriterStopsAsync(
        Task writerTask,
        CancellationTokenSource readLoopStop
    )
    {
        try
        {
            await writerTask.ConfigureAwait(false);
        }
        catch
        {
            // HandleClientAsync 会在清理阶段观察写入任务；此任务仅负责把停止信号
            // 传递给可能仍阻塞在帧读取上的循环。
        }
        finally
        {
            readLoopStop.Cancel();
        }
    }

    private ServerReady NewReady(Owner owner, ClientHello hello, byte[] serverNonce)
    {
        var ready = new ServerReady
        {
            SelectedVersion = ProtocolVersion.Clone(), SessionId = owner.SessionId, LeaseEpoch = owner.LeaseEpoch,
            CapabilitySnapshot = _snapshot.Clone(), ResultRetentionMs = CommandCoordinator.ResultRetentionMs, ReconnectGraceMs = ReconnectGraceMs,
        };
        ready.AuthTag = ByteString.CopyFrom(Authentication.ComputeServerTag(_secret, _modInstanceId, hello.ClientInstanceId, serverNonce, hello.ClientNonce.ToByteArray(), ProtocolVersion, owner.SessionId, owner.LeaseEpoch, _snapshot.Digest, CommandCoordinator.ResultRetentionMs, ReconnectGraceMs));
        return ready;
    }

    private static bool QueueCoordinatorResponse(ChannelWriter<TransportFrame> writer, SessionFence fence, string replyTo, CoordinatorResponse response)
    {
        TransportFrame frame = response switch
        {
            CoordinatorResponse.Event command => new TransportFrame { ReplyTo = replyTo, Fence = fence.Clone(), CommandEvent = command.Value.Clone() },
            CoordinatorResponse.ProtocolError error => new TransportFrame { ReplyTo = replyTo, Fence = fence.Clone(), ProtocolError = new ProtocolError { Error = error.Value.Clone() } },
            CoordinatorResponse.StatusResponse status => StatusFrame(replyTo, fence, status),
            CoordinatorResponse.CancelResponse cancel => CancelFrame(replyTo, fence, cancel),
            _ => throw new InvalidOperationException("未知 Coordinator 响应"),
        };
        frame.MessageId = $"s-{Guid.NewGuid():N}";
        return writer.TryWrite(frame);
    }

    private bool QueueSubmittedCommand(
        ChannelWriter<TransportFrame> writer,
        SessionFence fence,
        string replyTo,
        CommandRequest request
    )
    {
        var response = _coordinator.Submit(request);
        var queued = QueueCoordinatorResponse(writer, fence, replyTo, response);
        if (response is CoordinatorResponse.Event { Value.State: CommandState.Accepted } accepted)
            _coordinator.ReleaseAccepted(accepted.Value.CommandId);
        return queued;
    }

    private static TransportFrame StatusFrame(string replyTo, SessionFence fence, CoordinatorResponse.StatusResponse status)
    {
        var response = new GetCommandStatusResponse { CommandId = status.CommandId, Found = status.Found };
        if (status.Current is not null)
            response.Current = status.Current.Clone();
        return new TransportFrame { ReplyTo = replyTo, Fence = fence.Clone(), GetCommandStatusResponse = response };
    }

    private static TransportFrame CancelFrame(string replyTo, SessionFence fence, CoordinatorResponse.CancelResponse cancel)
    {
        var response = new CancelCommandResponse { CommandId = cancel.CommandId, Accepted = cancel.Accepted };
        if (cancel.Current is not null)
            response.Current = cancel.Current.Clone();
        if (cancel.Error is not null)
            response.Error = cancel.Error.Clone();
        return new TransportFrame { ReplyTo = replyTo, Fence = fence.Clone(), CancelCommandResponse = response };
    }

    private static bool QueueProtocolError(ChannelWriter<TransportFrame> writer, SessionFence fence, string replyTo, ErrorCode code, string message) =>
        QueueCoordinatorResponse(writer, fence, replyTo, CoordinatorResponse.ProtocolFailure(code, message));

    private static void EnsureQueued(bool queued)
    {
        if (!queued)
            throw new IOException("本地协议写出队列已满或已关闭");
    }

    private Error? ValidateClientHello(TransportFrame frame, string replyTo, byte[] serverNonce)
    {
        if (frame.BodyCase != TransportFrame.BodyOneofCase.ClientHello || frame.Fence is not null || frame.ReplyTo != replyTo || !IsMessageId(frame.MessageId))
            return NewError(ErrorCode.Unauthenticated, "握手消息无效");
        var hello = frame.ClientHello;
        if (hello.RequestedVersion.Major != 1 || hello.RequestedVersion.Minor != 0)
            return NewError(ErrorCode.UnsupportedVersion, "不支持请求的协议版本");
        if (!IsUuidV4(hello.ClientInstanceId) || hello.ClientNonce.Length != 32)
            return NewError(ErrorCode.Unauthenticated, "客户端身份无效");
        var resumeId = hello.HasResumeSessionId ? hello.ResumeSessionId : "";
        var expected = Authentication.ComputeClientTag(_secret, _modInstanceId, hello.ClientInstanceId, serverNonce, hello.ClientNonce.ToByteArray(), hello.RequestedVersion, resumeId);
        return Authentication.FixedTimeEquals(expected, hello.AuthTag) ? null : NewError(ErrorCode.Unauthenticated, "本地认证失败");
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
        if (_owner is not null && resumeId == _owner.SessionId && hello.ClientInstanceId == _owner.ClientInstanceId)
        {
            _ownerConnected = true;
            _owner.DisconnectedAt = null;
            return _owner;
        }
        if (resumeId.Length > 0 || _coordinator.HasActiveMutation)
        {
            error = NewError(resumeId.Length > 0 ? ErrorCode.Unauthenticated : ErrorCode.Busy, "无法恢复或转移当前 Owner 会话");
            return null;
        }
        _owner = new Owner(Guid.NewGuid().ToString("D"), hello.ClientInstanceId, checked(++_lastLeaseEpoch));
        _ownerConnected = true;
        return _owner;
    }

    private async Task RejectAsync(Stream stream, string replyTo, Error error, CancellationToken cancellationToken) =>
        await FrameCodec.WriteAsync(stream, new TransportFrame { MessageId = NextMessageId(), ReplyTo = replyTo, HandshakeRejected = new HandshakeRejected { Error = error } }, cancellationToken).ConfigureAwait(false);

    private string NextMessageId() => $"s-{Interlocked.Increment(ref _messageSequence)}";
    private static bool FenceMatches(TransportFrame frame, SessionFence fence) => frame.Fence is not null && frame.Fence.SessionId == fence.SessionId && frame.Fence.LeaseEpoch == fence.LeaseEpoch && frame.Fence.CapabilityDigest == fence.CapabilityDigest;
    private static bool IsMessageId(string value) => value.Length is >= 1 and <= 64 && value.All(character => character is >= '!' and <= '~');
    private static bool IsUuidV4(string value) => value.Length == 36 && value == value.ToLowerInvariant() && value[14] == '4' && "89ab".Contains(value[19]) && Guid.TryParseExact(value, "D", out _);
    private static Error NewError(ErrorCode code, string message) => new() { Code = code, Message = message };

    private sealed class Owner
    {
        public Owner(string sessionId, string clientInstanceId, ulong leaseEpoch) { SessionId = sessionId; ClientInstanceId = clientInstanceId; LeaseEpoch = leaseEpoch; }
        public string SessionId { get; }
        public string ClientInstanceId { get; }
        public ulong LeaseEpoch { get; }
        public long? DisconnectedAt { get; set; }
        public bool InsideGracePeriod(uint graceMs) => DisconnectedAt is not null && StopwatchCommandClock.Instance.Milliseconds - DisconnectedAt.Value < graceMs;
        public SessionFence CreateFence(string digest) => new() { SessionId = SessionId, LeaseEpoch = LeaseEpoch, CapabilityDigest = digest };
    }
}
