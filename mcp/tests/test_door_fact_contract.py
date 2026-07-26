from __future__ import annotations

from pathlib import Path

from google.protobuf import json_format
from jsonschema import Draft202012Validator

from stardew_valley_mcp.catalog import Catalog
from stardew_valley_mcp.projection import project_message
from stardew_valley_mcp.protocol import facts_pb2, transport_pb2


ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "spec" / "fixtures" / "v1" / "observation" / "query-world.success-complete.json"


def test_door_locked_presence_distinguishes_known_from_unknown() -> None:
    field = facts_pb2.DoorFact.DESCRIPTOR.fields_by_name["locked"]
    assert field.number == 1
    assert field.has_presence

    frame = transport_pb2.TransportFrame()
    json_format.Parse(FIXTURE.read_text(), frame)
    query_world = frame.command_event.result.query_world
    doors = {
        entity.ref.value: entity.door
        for entity in query_world.snapshot.entities
        if entity.HasField("door")
    }

    assert doors["entity-c-door-known"].HasField("locked")
    assert doors["entity-c-door-known"].locked is False
    assert not doors["entity-d-door-unknown"].HasField("locked")
    assert [
        (item.code, item.ref.value)
        for item in query_world.warnings
        if item.code == "DOOR_ACCESS_UNKNOWN"
    ] == [
        ("DOOR_ACCESS_UNKNOWN", "entity-d-door-unknown")
    ]


def test_known_and_unknown_doors_match_generated_output_schema() -> None:
    frame = transport_pb2.TransportFrame()
    json_format.Parse(FIXTURE.read_text(), frame)
    output = project_message(frame.command_event.result.query_world)
    entities = {entity["ref"]["value"]: entity for entity in output["snapshot"]["entities"]}

    assert entities["entity-c-door-known"]["door"]["locked"] is False
    assert "locked" not in entities["entity-d-door-unknown"]["door"]

    result = {
        "status": "succeeded",
        "commandId": frame.command_event.command_id,
        "output": output,
    }
    Draft202012Validator(Catalog.load().tool("query_world").outputSchema).validate(result)
