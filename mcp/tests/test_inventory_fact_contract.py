from __future__ import annotations

from pathlib import Path

from google.protobuf import descriptor, json_format

from stardew_valley_mcp.protocol import facts_pb2, transport_pb2


ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "spec" / "fixtures" / "v1" / "observation" / "query-inventory.success-complete.json"


def test_container_inventory_fixture_uses_opaque_inventory_item_refs() -> None:
    frame = transport_pb2.TransportFrame()
    json_format.Parse(FIXTURE.read_text(encoding="utf-8"), frame)
    snapshot = frame.command_event.result.query_inventory.snapshot

    assert snapshot.container_kind == "chest"
    assert snapshot.HasField("container_ref")
    assert [slot.index for slot in snapshot.slots] == [0, 1, 2]
    assert [slot.item.ref.value for slot in snapshot.slots if slot.HasField("item")] == ["item-a", "item-b"]


def test_item_fact_keeps_wire_shape_and_public_permission_boundary() -> None:
    fields = facts_pb2.ItemFact.DESCRIPTOR.fields_by_name
    assert fields["ref"].number == 1
    assert fields["category"].number == 6
    assert fields["category"].type == descriptor.FieldDescriptor.TYPE_STRING

    behavior = (ROOT / "spec" / "capabilities" / "behavior.md").read_text(encoding="utf-8")
    facts = (ROOT / "spec" / "proto" / "facts.proto").read_text(encoding="utf-8")

    assert "所有 `QueryInventoryResult.snapshot.slots[].item.ref` 都是可用于 `inspect`" in behavior
    assert "容器库存 Item Ref 不得用于 `equip`" in behavior
    assert "容器库存 Item Ref 即使可被 `inspect` 解析也必须以 `INVALID_ARGUMENT` 拒绝" in behavior
    assert "Item.Category` 使用 invariant culture 格式化得到的十进制整数字符串" in behavior
    assert "可用于 inspect 的 INVENTORY_ITEM Ref" in facts
    assert "不是本地化分类名称" in facts
