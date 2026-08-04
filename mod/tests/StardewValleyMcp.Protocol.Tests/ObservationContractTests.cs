using NUnit.Framework;
using StardewValleyMcp.Protocol.V1;

namespace StardewValleyMcp.Protocol.Tests;

public sealed class ObservationContractTests
{
    private const string Digest = "14664e95bbeb39b4c0ab235a5a7b3bf9df8fa2f702e66e1157b23dd4082f2994";

    [Test]
    public void GeneratedCatalogContainsAllPublicDescriptors()
    {
        var snapshot = CapabilityCatalog.CreateSnapshotFor(new[]
        {
            "say", "emote", "face", "navigate", "interact", "use_tool", "equip", "transfer_inventory_item", "set_equipment_slot", "move_inventory_item", "open_menu", "activate_ui", "close_menu",
            "query_runtime", "query_world", "query_inventory", "query_ui", "inspect",
        });
        Assert.That(snapshot.Capabilities, Has.Count.EqualTo(18));
        Assert.That(snapshot.Capabilities.Single(item => item.Id == "say").Execution, Is.EqualTo(ExecutionMode.Immediate));
        Assert.That(snapshot.Capabilities.Single(item => item.Id == "face").Execution, Is.EqualTo(ExecutionMode.LongRunning));
        Assert.That(snapshot.Capabilities.Single(item => item.Id == "query_runtime").RequiredScope, Is.EqualTo("game:read"));
    }

    [Test]
    public void RegisteredSubsetDoesNotPreannounceUnimplementedCapabilities()
    {
        var snapshot = CapabilityCatalog.CreateSnapshotFor(new[] { "query_runtime" });
        Assert.That(snapshot.Capabilities.Select(item => item.Id), Is.EqualTo(new[] { "query_runtime" }));
        Assert.That(snapshot.Digest, Is.EqualTo("6c9c9fc8002032a8b4191e3d4809f74ae9c20abcfb26fbf579d7a329d7daf199"));
    }

    [Test]
    public void CapabilityRegistrationMatchesIdOperationAndRequestType()
    {
        var descriptor = CapabilityCatalog.GetDescriptor("query_runtime");

        Assert.DoesNotThrow(() => CapabilityRegistrationContract.Validate(
            "query_runtime",
            CommandRequest.OperationOneofCase.QueryRuntime,
            descriptor
        ));
    }

    [Test]
    public void CapabilityRegistrationRejectsMismatchedIdOperationOrRequestType()
    {
        var descriptor = CapabilityCatalog.GetDescriptor("query_runtime");

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => CapabilityRegistrationContract.Validate(
                "query_world",
                CommandRequest.OperationOneofCase.QueryRuntime,
                descriptor
            ));
            Assert.Throws<InvalidOperationException>(() => CapabilityRegistrationContract.Validate(
                "query_runtime",
                CommandRequest.OperationOneofCase.QueryWorld,
                descriptor
            ));

            var wrongRequestType = descriptor.Clone();
            wrongRequestType.RequestType = nameof(QueryWorldRequest);
            Assert.Throws<InvalidOperationException>(() => CapabilityRegistrationContract.Validate(
                "query_runtime",
                CommandRequest.OperationOneofCase.QueryRuntime,
                wrongRequestType
            ));
        });
    }

    [Test]
    public void UiEquipmentSlotsKeepStableEnumAndOptionalFieldNumbers()
    {
        var field = UiElementFact.Descriptor.FindFieldByNumber(13);
        var fact = new UiElementFact
        {
            Kind = UiElementKind.EquipmentSlot,
            EquipmentSlotKind = UiEquipmentSlotKind.Hat,
        };

        Assert.Multiple(() =>
        {
            Assert.That((int)UiElementKind.EquipmentSlot, Is.EqualTo(7));
            Assert.That((int)UiEquipmentSlotKind.Hat, Is.EqualTo(1));
            Assert.That((int)UiEquipmentSlotKind.LeftRing, Is.EqualTo(2));
            Assert.That((int)UiEquipmentSlotKind.RightRing, Is.EqualTo(3));
            Assert.That((int)UiEquipmentSlotKind.Boots, Is.EqualTo(4));
            Assert.That((int)UiEquipmentSlotKind.Shirt, Is.EqualTo(5));
            Assert.That((int)UiEquipmentSlotKind.Pants, Is.EqualTo(6));
            Assert.That((int)UiEquipmentSlotKind.Trinket, Is.EqualTo(7));
            Assert.That(field?.Name, Is.EqualTo("equipment_slot_kind"));
            Assert.That(field?.HasPresence, Is.True);
            Assert.That(fact.HasEquipmentSlotKind, Is.True);
            fact.ClearEquipmentSlotKind();
            Assert.That(fact.HasEquipmentSlotKind, Is.False);
        });
    }
}
