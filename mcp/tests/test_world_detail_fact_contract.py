from __future__ import annotations

import copy
from pathlib import Path

from google.protobuf import json_format
from jsonschema import Draft202012Validator

from stardew_valley_mcp.catalog import Catalog
from stardew_valley_mcp.projection import project_message
from stardew_valley_mcp.protocol import facts_pb2, transport_pb2


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "spec" / "fixtures" / "v1" / "observation"
COMPLETE_REFS = (
    "entity-crop-complete",
    "entity-machine-complete",
    "entity-furniture-complete",
    "character-animal-complete",
)


def _load(name: str) -> transport_pb2.TransportFrame:
    frame = transport_pb2.TransportFrame()
    json_format.Parse((FIXTURES / name).read_text(), frame)
    return frame


def _world_facts(frame: transport_pb2.TransportFrame) -> dict[str, object]:
    snapshot = frame.command_event.result.query_world.snapshot
    return {
        **{entity.ref.value: entity for entity in snapshot.entities},
        **{character.ref.value: character for character in snapshot.characters},
    }


def test_new_detail_scalars_and_enums_are_additive_with_presence() -> None:
    optional_fields = {
        facts_pb2.CropFact: (
            "has_fertilizer", "fertilizer_item_id", "growth_phase_day",
            "growth_phase_duration", "growth_phase_count",
            "growth_days_remaining_if_watered", "regrow_days",
            "regrow_days_remaining", "mature", "needs_watering",
        ),
        facts_pb2.MachineFact: ("held_item", "state", "input_item"),
        facts_pb2.FurnitureFact: (
            "qualified_item_id", "rotation_count", "can_rotate", "seat_capacity",
            "occupied_seats", "has_surface_item", "surface_item", "is_on",
            "interaction_profile_complete", "storage_item_count",
        ),
        facts_pb2.FarmAnimalFact: (
            "fullness", "fed_today", "auto_petted_today", "produce_item_id",
            "produce_quality", "produce_harvest_method", "age_days", "adult",
            "days_until_mature", "days_since_last_produce", "base_days_to_produce",
            "has_home_building", "home_building_id", "home_building_type",
            "in_home_building",
        ),
    }
    for message, names in optional_fields.items():
        for name in names:
            assert message.DESCRIPTOR.fields_by_name[name].has_presence, (message.__name__, name)

    assert facts_pb2.MachineFact.DESCRIPTOR.fields_by_name["state"].number == 5
    assert facts_pb2.FarmAnimalFact.DESCRIPTOR.fields_by_name["home_building_id"].number == 18
    assert facts_pb2.FurnitureFact.DESCRIPTOR.fields_by_name["interaction_kinds"].number == 12


def test_complete_world_fixture_covers_four_detail_classes_and_json_schema() -> None:
    frame = _load("query-world.success-complete.json")
    facts = _world_facts(frame)

    crop = facts["entity-crop-complete"].crop
    assert crop.has_fertilizer is True
    assert crop.fertilizer_item_id == "(O)368"
    assert crop.growth_phase_day == 1
    assert crop.growth_phase_duration == 2
    assert crop.growth_phase_count == 5
    assert crop.growth_days_remaining_if_watered == 4
    assert crop.HasField("mature") and crop.mature is False
    assert crop.HasField("needs_watering") and crop.needs_watering is False

    machine = facts["entity-machine-complete"].machine
    assert machine.state == facts_pb2.MACHINE_STATE_PROCESSING
    assert machine.input_item.qualified_item_id == "(O)24"
    assert machine.held_item.qualified_item_id == "(O)344"

    furniture = facts["entity-furniture-complete"].furniture
    assert furniture.qualified_item_id == "(F)1120"
    assert furniture.HasField("seat_capacity") and furniture.seat_capacity == 0
    assert furniture.HasField("occupied_seats") and furniture.occupied_seats == 0
    assert list(furniture.interaction_kinds) == [facts_pb2.FURNITURE_INTERACTION_KIND_SURFACE]
    assert furniture.interaction_profile_complete is True

    animal = facts["character-animal-complete"].farm_animal
    assert (animal.fullness, animal.fed_today) == (255, True)
    assert animal.HasField("auto_petted_today") and animal.auto_petted_today is False
    assert animal.produce_harvest_method == facts_pb2.FARM_ANIMAL_PRODUCE_HARVEST_METHOD_HARVEST_WITH_TOOL
    assert animal.HasField("days_until_mature") and animal.days_until_mature == 0
    assert animal.home_building_id == "2f46f213-1b91-4b8a-8f6e-e08e97ab4e91"

    output = project_message(frame.command_event.result.query_world)
    result = {
        "status": "succeeded",
        "commandId": frame.command_event.command_id,
        "output": output,
    }
    Draft202012Validator(Catalog.load().tool("query_world").outputSchema).validate(result)


def test_query_world_and_inspect_reuse_identical_four_class_facts() -> None:
    world = _world_facts(_load("query-world.success-complete.json"))
    inspect = _load("inspect.success-complete.json").command_event.result.inspect
    inspected = {
        item.resolution.ref.value: item
        for item in inspect.items
        if item.resolution.status == 1
    }
    for reference in COMPLETE_REFS[:3]:
        assert world[reference] == inspected[reference].world_entity
        assert world[reference].SerializeToString(deterministic=True) == inspected[
            reference
        ].world_entity.SerializeToString(deterministic=True)
    reference = COMPLETE_REFS[3]
    assert world[reference] == inspected[reference].character
    assert world[reference].SerializeToString(deterministic=True) == inspected[
        reference
    ].character.SerializeToString(deterministic=True)


def test_old_sender_minimal_facts_remain_valid_and_do_not_invent_zero_values() -> None:
    frame = _load("query-world.success-minimal.json")
    facts = _world_facts(frame)

    crop = facts["legacy-crop"].crop
    assert not crop.HasField("has_fertilizer")
    assert not crop.HasField("growth_phase_day")
    assert not crop.HasField("mature")
    assert "hasFertilizer" not in project_message(crop)

    machine = facts["legacy-machine"].machine
    assert not machine.HasField("state")
    assert not machine.HasField("input_item")
    assert "state" not in project_message(machine)

    furniture = facts["legacy-furniture"].furniture
    assert not furniture.HasField("qualified_item_id")
    assert not furniture.HasField("interaction_profile_complete")
    assert furniture.interaction_kinds == []

    animal = facts["legacy-animal"].farm_animal
    assert not animal.HasField("fullness")
    assert not animal.HasField("fed_today")
    assert not animal.HasField("has_home_building")

    output = project_message(frame.command_event.result.query_world)
    result = {
        "status": "succeeded",
        "commandId": frame.command_event.command_id,
        "output": output,
    }
    Draft202012Validator(Catalog.load().tool("query_world").outputSchema).validate(result)


def test_partial_detail_uses_warning_and_omits_unavailable_optional_fields() -> None:
    frame = _load("query-world.success-complete.json")
    result = frame.command_event.result.query_world
    facts = _world_facts(frame)

    crop = facts["entity-crop-partial"].crop
    assert not crop.HasField("has_fertilizer")
    assert not crop.HasField("growth_phase_count")
    assert not crop.HasField("needs_watering")
    animal = facts["character-animal-partial"].farm_animal
    assert not animal.HasField("fullness")
    assert not animal.HasField("fed_today")
    assert not animal.HasField("has_home_building")
    machine = facts["entity-machine-partial"].machine
    assert machine.qualified_item_id == "(BC)custom.machine"
    assert machine.minutes_until_ready == 30
    assert not machine.HasField("state")
    assert not machine.HasField("input_item")
    assert not machine.HasField("held_item")
    furniture = facts["entity-furniture-partial"].furniture
    assert furniture.furniture_kind == "other"
    assert len(furniture.occupied_tiles) == 1
    assert not furniture.HasField("qualified_item_id")
    assert not furniture.HasField("rotation_count")
    assert not furniture.HasField("interaction_profile_complete")
    assert not furniture.HasField("has_surface_item")
    assert not facts["entity-furniture-complete"].HasField("actionable")
    assert not facts["entity-furniture-partial"].HasField("actionable")

    assert {
        (warning.code, warning.ref.value)
        for warning in result.warnings
    } >= {
        ("ENTITY_FACT_PARTIAL", "entity-crop-partial"),
        ("ENTITY_FACT_PARTIAL", "entity-machine-partial"),
        ("ENTITY_FACT_PARTIAL", "entity-furniture-partial"),
        ("CHARACTER_FACT_PARTIAL", "character-animal-partial"),
        ("ENTITY_ACTIONABLE_UNKNOWN", "entity-furniture-complete"),
        ("ENTITY_ACTIONABLE_UNKNOWN", "entity-furniture-partial"),
    }


def test_partial_machine_and_furniture_match_inspect_byte_for_byte() -> None:
    world = _world_facts(_load("query-world.success-complete.json"))
    inspect = _load("inspect.success-complete.json").command_event.result.inspect
    inspected = {item.resolution.ref.value: item for item in inspect.items}
    for reference in ("entity-machine-partial", "entity-furniture-partial"):
        query_fact = world[reference]
        inspect_fact = inspected[reference].world_entity
        assert query_fact == inspect_fact
        assert query_fact.SerializeToString(deterministic=True) == inspect_fact.SerializeToString(
            deterministic=True
        )
    warning_pairs = {(warning.code, warning.ref.value) for warning in inspect.warnings}
    assert warning_pairs >= {
        ("ENTITY_FACT_PARTIAL", "entity-machine-partial"),
        ("ENTITY_FACT_PARTIAL", "entity-furniture-partial"),
    }


def test_generated_schemas_keep_optional_fields_optional_and_reject_unspecified_interaction() -> None:
    catalog = Catalog.load()
    for tool_name in ("query_world", "inspect"):
        schema = catalog.tool(tool_name).outputSchema
        for fact_name, optional_names in {
            "CropFact": {"hasFertilizer", "growthPhaseDay", "mature"},
            "MachineFact": {"state", "inputItem"},
            "FurnitureFact": {"qualifiedItemId", "interactionProfileComplete"},
            "FarmAnimalFact": {"fullness", "fedToday", "homeBuildingId"},
        }.items():
            assert optional_names.isdisjoint(schema["$defs"][fact_name].get("required", []))
        interaction_items = schema["$defs"]["FurnitureFact"]["properties"][
            "interactionKinds"
        ]["items"]
        assert interaction_items["enum"] == ["seat", "surface", "storage", "toggle"]
        assert "unspecified" not in interaction_items["enum"]
        machine_states = schema["$defs"]["MachineFact"]["properties"]["state"]["enum"]
        assert machine_states == ["unknown", "idle", "processing", "ready"]
        assert "unspecified" not in machine_states
        harvest_methods = schema["$defs"]["FarmAnimalFact"]["properties"][
            "produceHarvestMethod"
        ]["enum"]
        assert harvest_methods == ["drop_overnight", "harvest_with_tool", "dig_up"]
        assert "unspecified" not in harvest_methods


def test_generated_schemas_reject_dependents_when_presence_flag_is_false() -> None:
    invalid_mutations = (
        ("cropId", {"hasFertilizer": False, "fertilizerItemId": "(O)368"}),
        ("furnitureKind", {
            "hasSurfaceItem": False,
            "surfaceItem": {"qualifiedItemId": "(O)390"},
        }),
        ("animalType", {
            "hasHomeBuilding": False,
            "homeBuildingId": "2f46f213-1b91-4b8a-8f6e-e08e97ab4e91",
        }),
        ("animalType", {"hasHomeBuilding": False, "homeBuildingType": "Coop"}),
        ("animalType", {"hasHomeBuilding": False, "inHomeBuilding": False}),
        ("animalType", {"produceReady": False, "produceItemId": "(O)442"}),
        ("animalType", {"produceReady": False, "produceQuality": 2}),
        ("animalType", {
            "produceReady": False,
            "produceHarvestMethod": "harvest_with_tool",
        }),
    )
    fixtures = (
        ("query_world", "query-world.success-complete.json", "query_world"),
        ("inspect", "inspect.success-complete.json", "inspect"),
    )
    for tool_name, fixture_name, result_field in fixtures:
        frame = _load(fixture_name)
        result_message = getattr(frame.command_event.result, result_field)
        base = {
            "status": "succeeded",
            "commandId": frame.command_event.command_id,
            "output": project_message(result_message),
        }
        validator = Draft202012Validator(Catalog.load().tool(tool_name).outputSchema)
        validator.validate(base)
        for marker, mutation in invalid_mutations:
            candidate = copy.deepcopy(base)
            candidate_target = _find_dict_with_key(candidate, marker)
            assert candidate_target is not None, (tool_name, marker)
            candidate_target.update(mutation)
            assert not validator.is_valid(candidate), (tool_name, mutation)


def test_generated_schemas_reject_dependents_when_presence_flag_is_missing() -> None:
    invalid_mutations = (
        ("cropId", "hasFertilizer", {"fertilizerItemId": "(O)368"}),
        ("furnitureKind", "hasSurfaceItem", {
            "surfaceItem": {"qualifiedItemId": "(O)390"},
        }),
        ("animalType", "hasHomeBuilding", {
            "homeBuildingId": "2f46f213-1b91-4b8a-8f6e-e08e97ab4e91",
        }),
        ("animalType", "hasHomeBuilding", {"homeBuildingType": "Coop"}),
        ("animalType", "hasHomeBuilding", {"inHomeBuilding": False}),
        ("animalType", "produceReady", {"produceItemId": "(O)442"}),
        ("animalType", "produceReady", {"produceQuality": 2}),
        ("animalType", "produceReady", {
            "produceHarvestMethod": "harvest_with_tool",
        }),
    )
    fixtures = (
        ("query_world", "query-world.success-complete.json", "query_world"),
        ("inspect", "inspect.success-complete.json", "inspect"),
    )
    for tool_name, fixture_name, result_field in fixtures:
        frame = _load(fixture_name)
        result_message = getattr(frame.command_event.result, result_field)
        base = {
            "status": "succeeded",
            "commandId": frame.command_event.command_id,
            "output": project_message(result_message),
        }
        validator = Draft202012Validator(Catalog.load().tool(tool_name).outputSchema)
        validator.validate(base)
        for marker, presence_flag, mutation in invalid_mutations:
            candidate = copy.deepcopy(base)
            candidate_target = _find_dict_with_key(candidate, marker)
            assert candidate_target is not None, (tool_name, marker)
            candidate_target.pop(presence_flag, None)
            candidate_target.update(mutation)
            assert not validator.is_valid(candidate), (tool_name, mutation)


def _find_dict_with_key(value: object, key: str) -> dict[str, object] | None:
    if isinstance(value, dict):
        if key in value:
            return value
        for child in value.values():
            found = _find_dict_with_key(child, key)
            if found is not None:
                return found
    elif isinstance(value, list):
        for child in value:
            found = _find_dict_with_key(child, key)
            if found is not None:
                return found
    return None
