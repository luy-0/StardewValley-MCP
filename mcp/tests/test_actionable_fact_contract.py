from __future__ import annotations

from pathlib import Path

from google.protobuf import json_format
from jsonschema import Draft202012Validator

from stardew_valley_mcp.catalog import Catalog
from stardew_valley_mcp.projection import project_message
from stardew_valley_mcp.protocol import facts_pb2, transport_pb2


ROOT = Path(__file__).resolve().parents[2]
QUERY_WORLD_FIXTURE = (
    ROOT / "spec" / "fixtures" / "v1" / "observation" / "query-world.success-complete.json"
)
INSPECT_FIXTURE = (
    ROOT / "spec" / "fixtures" / "v1" / "observation" / "inspect.success-complete.json"
)


def _load_frame(path: Path) -> transport_pb2.TransportFrame:
    frame = transport_pb2.TransportFrame()
    json_format.Parse(path.read_text(), frame)
    return frame


def test_actionable_presence_distinguishes_false_from_unknown() -> None:
    field = facts_pb2.WorldEntityFact.DESCRIPTOR.fields_by_name["actionable"]
    assert field.number == 5
    assert field.has_presence

    frame = _load_frame(QUERY_WORLD_FIXTURE)
    query_world = frame.command_event.result.query_world
    entities = {entity.ref.value: entity for entity in query_world.snapshot.entities}

    assert entities["entity-a"].HasField("actionable")
    assert entities["entity-a"].actionable is False
    assert not entities["entity-b"].HasField("actionable")
    assert [
        (item.code, item.ref.value, item.message)
        for item in query_world.warnings
        if item.code == "ENTITY_ACTIONABLE_UNKNOWN"
    ] == [
        (
            "ENTITY_ACTIONABLE_UNKNOWN",
            "entity-b",
            "无法在无副作用的前提下确定该实体对当前玩家是否可操作。",
        )
    ]


def test_query_world_actionable_presence_matches_generated_output_schema() -> None:
    frame = _load_frame(QUERY_WORLD_FIXTURE)
    output = project_message(frame.command_event.result.query_world)
    entities = {entity["ref"]["value"]: entity for entity in output["snapshot"]["entities"]}

    assert entities["entity-a"]["actionable"] is False
    assert "actionable" not in entities["entity-b"]

    result = {
        "status": "succeeded",
        "commandId": frame.command_event.command_id,
        "output": output,
    }
    Draft202012Validator(Catalog.load().tool("query_world").outputSchema).validate(result)


def test_inspect_unknown_actionable_has_warning_and_matches_output_schema() -> None:
    frame = _load_frame(INSPECT_FIXTURE)
    inspect = frame.command_event.result.inspect
    entity = inspect.items[0].world_entity

    assert entity.ref.value == "entity-a"
    assert not entity.HasField("actionable")
    assert [(item.code, item.ref.value) for item in inspect.warnings] == [
        ("ENTITY_ACTIONABLE_UNKNOWN", "entity-a")
    ]

    output = project_message(inspect)
    assert "actionable" not in output["items"][0]["worldEntity"]
    result = {
        "status": "succeeded",
        "commandId": frame.command_event.command_id,
        "output": output,
    }
    Draft202012Validator(Catalog.load().tool("inspect").outputSchema).validate(result)
