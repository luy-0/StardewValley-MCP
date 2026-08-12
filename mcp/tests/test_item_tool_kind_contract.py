from stardew_valley_mcp.projection import project_message
from stardew_valley_mcp.protocol import facts_pb2


def test_item_tool_kind_is_optional_and_projects_language_neutral_values() -> None:
    field = facts_pb2.ItemFact.DESCRIPTOR.fields_by_name["tool_kind"]

    assert field.number == 12
    assert field.has_presence
    assert project_message(facts_pb2.ItemFact(tool=True)) == {
        "category": "",
        "displayName": "",
        "qualifiedItemId": "",
        "quality": 0,
        "stack": 0,
        "tool": True,
        "toolLevel": 0,
    }
    projected = project_message(
        facts_pb2.ItemFact(tool=True, tool_kind=facts_pb2.ITEM_TOOL_KIND_SCYTHE)
    )
    assert projected["toolKind"] == "scythe"
