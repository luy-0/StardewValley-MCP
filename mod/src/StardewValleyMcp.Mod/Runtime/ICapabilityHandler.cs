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
