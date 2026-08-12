from stardew_valley_mcp.catalog import Catalog
from stardew_valley_mcp.projection import project_message
from stardew_valley_mcp.protocol import common_pb2, facts_pb2


def test_tile_refillability_is_additive_field_seven_with_presence() -> None:
    field = facts_pb2.TileFact.DESCRIPTOR.fields_by_name["watering_can_refillable"]
    assert field.number == 7
    assert field.has_presence

    false_tile = facts_pb2.TileFact(
        position=common_pb2.WorldPosition(location_id="Farm", x=1, y=2),
        passable=True,
        watering_can_refillable=False,
    )
    true_tile = facts_pb2.TileFact(
        position=common_pb2.WorldPosition(location_id="Farm", x=3, y=4),
        watering_can_refillable=True,
    )
    assert project_message(false_tile)["wateringCanRefillable"] is False
    assert project_message(true_tile)["wateringCanRefillable"] is True
    assert project_message(facts_pb2.TileFact(position=common_pb2.WorldPosition(location_id="Farm"))) == {
        "position": {"locationId": "Farm", "x": 0, "y": 0},
        "passable": False,
        "occupied": False,
        "diggable": False,
        "water": False,
        "terrainKind": "",
    }


def test_query_world_schema_keeps_refillability_optional_for_old_mod_compatibility() -> None:
    schema = Catalog.load().tool("query_world").outputSchema
    tile = schema["$defs"]["TileFact"]
    assert "wateringCanRefillable" not in tile["required"]
    assert tile["properties"]["wateringCanRefillable"] == {"type": "boolean"}


def test_tile_pathfinding_blockage_is_additive_field_eight_with_presence() -> None:
    field = facts_pb2.TileFact.DESCRIPTOR.fields_by_name["pathfinding_blocked"]
    assert field.number == 8
    assert field.has_presence

    tile = facts_pb2.TileFact(
        position=common_pb2.WorldPosition(location_id="Farm"),
        pathfinding_blocked=False,
    )
    assert project_message(tile)["pathfindingBlocked"] is False
    schema = Catalog.load().tool("query_world").outputSchema["$defs"]["TileFact"]
    assert "pathfindingBlocked" not in schema["required"]
