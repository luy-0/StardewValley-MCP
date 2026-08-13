using StardewValley;
using StardewValleyMcp.Protocol.V1;
using SObject = StardewValley.Object;

namespace StardewValleyMcp.Mod;

internal static class MachineFactProjector
{
    public static MachineFact Project(
        SObject machine,
        Ref reference,
        ICollection<QueryWarning> warnings
    )
    {
        var ready = machine.readyForHarvest.Value;
        var minutes = machine.MinutesUntilReady;
        var held = machine.heldObject.Value;
        var qualifiedItemId = machine.QualifiedItemId ?? "";
        if (!PublicStringPolicy.IsNonEmptyValid(qualifiedItemId))
            throw new InvalidOperationException("机器 QID 无法安全公开");
        var detail = new MachineFact
        {
            QualifiedItemId = qualifiedItemId,
            ReadyForHarvest = ready,
            MinutesUntilReady = minutes,
            State = ClassifyState(ready, minutes, held is not null),
        };

        if (held is not null)
        {
            TryApplyHeldItem(detail, reference, warnings, () => ItemFactProjector.Project(held));
        }
        if (machine.lastInputItem.Value is { } input)
        {
            TryApplyInputItem(detail, reference, warnings, () => ItemFactProjector.Project(input));
        }
        return detail;
    }

    internal static bool TryApplyHeldItem(
        MachineFact detail,
        Ref reference,
        ICollection<QueryWarning> warnings,
        Func<ItemFact> project
    ) => WorldFactProjectionGuard.TryApplyEntity(
        reference,
        warnings,
        () => detail.HeldItem = project()
    );

    internal static bool TryApplyInputItem(
        MachineFact detail,
        Ref reference,
        ICollection<QueryWarning> warnings,
        Func<ItemFact> project
    ) => WorldFactProjectionGuard.TryApplyEntity(
        reference,
        warnings,
        () => detail.InputItem = project()
    );

    internal static MachineState ClassifyState(bool ready, int minutesUntilReady, bool hasHeldItem)
    {
        if (ready && hasHeldItem && minutesUntilReady <= 0)
            return MachineState.Ready;
        if (!ready && hasHeldItem && minutesUntilReady > 0)
            return MachineState.Processing;
        if (!ready && !hasHeldItem && minutesUntilReady <= 0)
            return MachineState.Idle;
        return MachineState.Unknown;
    }
}
