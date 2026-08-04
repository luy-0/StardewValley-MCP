using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal interface ICapabilityHandler
{
    string Id { get; }
    CommandRequest.OperationOneofCase Operation { get; }
    Error? Validate(CommandRequest request);
}

internal interface IImmediateCapabilityHandler : ICapabilityHandler
{
    CommandEvent Execute(string commandId, CommandRequest request);
}

internal interface ILongRunningCapabilityHandler : ICapabilityHandler
{
    ICommandContinuation Start(string commandId, CommandRequest request);
}

internal interface ICommandContinuation
{
    string Phase { get; }
    uint? ProgressPercent { get; }
    bool CanCancel { get; }
    ContinuationStep Tick(ContinuationStopSignal signal);
}

// Coordinator 保持取消和 Deadline 终态的唯一所有者；长时能力只能在终态写入前
// 补充已确认的停止上下文，不能自行构造取消或超时错误码。
internal interface IStopErrorContextProvider
{
    void EnrichStopError(ContinuationStopSignal signal, Error error);
}

internal enum ContinuationStopSignal
{
    None,
    CancelRequested,
    DeadlineExceeded,
}

internal abstract record ContinuationStep
{
    internal sealed record Pending : ContinuationStep;
    internal sealed record Succeeded(CapabilityResult Result) : ContinuationStep;
    internal sealed record Failed(Error Error) : ContinuationStep;
    internal sealed record Stopped : ContinuationStep;
}
