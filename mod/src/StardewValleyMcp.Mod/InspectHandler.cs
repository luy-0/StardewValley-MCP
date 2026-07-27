using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Mod;

internal sealed class InspectHandler : ICapabilityHandler
{
    private readonly OpaqueRefStore _refs;

    public InspectHandler(OpaqueRefStore refs)
    {
        _refs = refs;
    }

    public string Id => "inspect";
    public CommandRequest.OperationOneofCase Operation =>
        CommandRequest.OperationOneofCase.Inspect;
    public Error? Validate(CommandRequest request) => InspectRequestValidator.Validate(request);

    public CommandEvent Execute(string commandId, CommandRequest request)
    {
        var context = new InspectProjectionContext(_refs);
        var result = Assemble(
            request.Inspect,
            _refs.ResolveForInspect,
            (reference, target) => InspectFactProjector.Project(reference, target, context)
        );
        return new CommandEvent
        {
            CommandId = commandId,
            State = CommandState.Succeeded,
            Phase = "completed",
            ProgressPercent = 100,
            Result = new CapabilityResult { Inspect = result },
        };
    }

    internal static InspectResult Assemble(
        InspectRequest request,
        Func<Ref, InspectRefLookup> resolve,
        Func<Ref, InspectableRefTarget, InspectProjectionResult> project
    )
    {
        var result = new InspectResult();
        foreach (var reference in request.Refs)
        {
            var lookup = resolve(reference);
            if (lookup.Resolution.Status != RefStatus.Resolved || lookup.Target is null)
            {
                result.Items.Add(new InspectedRef { Resolution = lookup.Resolution.Clone() });
                continue;
            }
            if (lookup.Target.Kind != lookup.Resolution.Kind)
            {
                result.Items.Add(new InspectedRef
                {
                    Resolution = Failure(
                        reference,
                        RefStatus.Unsupported,
                        lookup.Resolution.Kind,
                        ErrorCode.InvalidArgument,
                        "当前 Ref 类型不支持检查"
                    ),
                });
                continue;
            }

            try
            {
                var projection = project(reference, lookup.Target);
                projection.Item.Resolution = lookup.Resolution.Clone();
                result.Items.Add(projection.Item);
                result.Warnings.AddRange(projection.Warnings.Select(item => item.Clone()));
            }
            catch (InspectRefStaleException)
            {
                result.Items.Add(new InspectedRef
                {
                    Resolution = Failure(
                        reference,
                        RefStatus.Stale,
                        lookup.Resolution.Kind,
                        ErrorCode.StaleRef,
                        "Ref 已失效"
                    ),
                });
            }
            catch
            {
                result.Items.Add(new InspectedRef
                {
                    Resolution = Failure(
                        reference,
                        RefStatus.FactUnavailable,
                        lookup.Resolution.Kind,
                        ErrorCode.Internal,
                        "当前 Ref 事实不可用"
                    ),
                });
            }
        }
        return result;
    }

    private static RefResolution Failure(
        Ref reference,
        RefStatus status,
        RefKind kind,
        ErrorCode code,
        string message
    ) => new()
    {
        Ref = reference.Clone(),
        Status = status,
        Kind = kind,
        Error = new Error { Code = code, Message = message },
    };
}
