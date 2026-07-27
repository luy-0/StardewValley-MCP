using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;
# 目前阶段, 没有把 capability 抽象成更复杂的 class hierarchy，比如 ReadOnlyCapability、LongRunningCapability、MutationCapability。原因是当前阶段只实现观察能力，过早拆层会让接口为了未来猜测变重。长期动作、瞬时动作、查询动作的差异现在放在 CapabilityDescriptor 里，也就是 side_effect、execution、cancellable、timeout、scope、risk、destructive 这些字段。等后续 做 mutating 和 long-running 时，再让运行时根据 descriptor 扩展状态处理，而不是直接按照当前设计的路径继续依赖, 需要在运行时里做 capability 的分类。
internal sealed class CapabilityRegistry
{
    private readonly IReadOnlyDictionary<CommandRequest.OperationOneofCase, RegisteredCapability> _byOperation;

    public CapabilityRegistry(string modInstanceId)
        : this(new OpaqueRefStore(modInstanceId))
    {
    }

    private CapabilityRegistry(OpaqueRefStore refs)
        : this(new ICapabilityHandler[]
        {
            new QueryRuntimeHandler(),
            new QueryWorldHandler(refs),
            new QueryInventoryHandler(refs),
            new QueryUiHandler(refs),
            new InspectHandler(refs),
        })
    {
    }

    internal CapabilityRegistry(IEnumerable<ICapabilityHandler> handlers)
    {
        var byId = new Dictionary<string, RegisteredCapability>(StringComparer.Ordinal);
        var byOperation = new Dictionary<CommandRequest.OperationOneofCase, RegisteredCapability>();
        foreach (var handler in handlers)
        {
            var descriptor = CapabilityCatalog.GetObservationDescriptor(handler.Id);
            CapabilityRegistrationContract.Validate(handler.Id, handler.Operation, descriptor);
            var registration = new RegisteredCapability(handler, descriptor);
            if (!byId.TryAdd(handler.Id, registration))
                throw new InvalidOperationException($"重复 capability id: {handler.Id}");
            if (!byOperation.TryAdd(handler.Operation, registration))
                throw new InvalidOperationException($"重复 capability operation: {handler.Operation}");
        }

        _byOperation = byOperation;
        Snapshot = CapabilityCatalog.CreateSnapshot(byId.Values.Select(item => item.Descriptor));
    }

    public CapabilitySnapshot Snapshot { get; }

    public bool TryResolve(
        CommandRequest request,
        out RegisteredCapability registration
    ) => _byOperation.TryGetValue(request.OperationCase, out registration!);
}

internal sealed record RegisteredCapability(ICapabilityHandler Handler, CapabilityDescriptor Descriptor);
