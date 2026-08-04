from __future__ import annotations

from pathlib import Path

from google.protobuf import json_format
from jsonschema import Draft202012Validator

from stardew_valley_mcp.catalog import Catalog
from stardew_valley_mcp.projection import project_message
from stardew_valley_mcp.protocol import facts_pb2, transport_pb2


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "spec" / "fixtures" / "v1" / "observation"


def _load(name: str) -> transport_pb2.TransportFrame:
    frame = transport_pb2.TransportFrame()
    json_format.Parse((FIXTURES / name).read_text(), frame)
    return frame


def test_hoe_dirt_is_a_dedicated_additive_fact() -> None:
    assert facts_pb2.ENTITY_KIND_HOE_DIRT == 14
    field = facts_pb2.WorldEntityFact.DESCRIPTOR.fields_by_name["hoe_dirt"]
    assert field.number == 33
    assert field.message_type.full_name == "stardew_valley.mcp.v1.HoeDirtFact"
    assert facts_pb2.HoeDirtFact.DESCRIPTOR.fields_by_name["watered"].number == 1
    assert project_message(facts_pb2.HoeDirtFact(watered=False)) == {"watered": False}


def test_query_world_and_inspect_project_the_same_watered_hoe_dirt_fact() -> None:
    world_frame = _load("query-world.success-complete.json")
    inspect_frame = _load("inspect.success-complete.json")
    world = next(
        entity
        for entity in world_frame.command_event.result.query_world.snapshot.entities
        if entity.ref.value == "entity-a"
    )
    inspected = inspect_frame.command_event.result.inspect.items[0].world_entity

    assert world == inspected
    assert world.kind == facts_pb2.ENTITY_KIND_HOE_DIRT
    assert world.WhichOneof("details") == "hoe_dirt"
    assert world.hoe_dirt.watered is True

    world_result = {
        "status": "succeeded",
        "commandId": world_frame.command_event.command_id,
        "output": project_message(world_frame.command_event.result.query_world),
    }
    inspect_result = {
        "status": "succeeded",
        "commandId": inspect_frame.command_event.command_id,
        "output": project_message(inspect_frame.command_event.result.inspect),
    }
    Draft202012Validator(Catalog.load().tool("query_world").outputSchema).validate(world_result)
    Draft202012Validator(Catalog.load().tool("inspect").outputSchema).validate(inspect_result)
    projected = next(
        entity
        for entity in world_result["output"]["snapshot"]["entities"]
        if entity["ref"]["value"] == "entity-a"
    )
    inspected_projection = inspect_result["output"]["items"][0]["worldEntity"]
    assert projected == inspected_projection
    assert projected["kind"] == "hoe_dirt"
    assert projected["hoeDirt"] == {"watered": True}
