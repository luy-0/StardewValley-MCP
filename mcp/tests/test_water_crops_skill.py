from __future__ import annotations

import asyncio
import sys
from copy import deepcopy
from pathlib import Path

from mcp import types

from stardew_valley_mcp.skill_host import SkillHost
from stardew_valley_mcp.skill_loader import _load_entrypoint, load_executable_skills


ROOT = Path(__file__).resolve().parents[2]
SKILL_DIR = ROOT / "skill" / "examples" / "stardew-water-crops"
SCRIPT = SKILL_DIR / "scripts" / "run.py"
AREA = {"locationId": "Farm", "x": 0, "y": 0, "width": 8, "height": 8}


def _run():
    return load_executable_skills([SKILL_DIR])[0].run


def _module():
    run = _load_entrypoint(SCRIPT, "run")
    return sys.modules[run.__module__]


def _crop(x: int, y: int, *, watered: bool = False):
    return {
        "ref": {"value": f"crop-{x}-{y}"},
        "kind": "crop",
        "position": {"locationId": "Farm", "x": x, "y": y},
        "displayName": "Crop",
        "crop": {
            "cropId": "24",
            "harvestItemId": "24",
            "growthPhase": 1,
            "readyForHarvest": False,
            "watered": watered,
            "dead": False,
            "regrows": False,
            "harvestAction": "interact",
        },
    }


class WaterContext:
    def __init__(self, crops=None, *, use_status="succeeded", date_changes=False):
        source = [_crop(2, 2)] if crops is None else crops
        self.crops = {item["ref"]["value"]: deepcopy(item) for item in source}
        self.calls: list[tuple[str, dict]] = []
        self.water = 20
        self.position = {"locationId": "Farm", "x": 1, "y": 1}
        self.use_status = use_status
        self.date_changes = date_changes
        self.after_use = False

    async def available_tools(self):
        read_only = {
            "stardew_query_runtime", "stardew_query_inventory",
            "stardew_query_world", "stardew_inspect",
        }
        skill = load_executable_skills([SKILL_DIR])[0]
        return [
            types.Tool(
                name=name,
                inputSchema={"type": "object"},
                annotations=types.ToolAnnotations(readOnlyHint=name in read_only),
            )
            for name in skill.allowed_tools
        ]

    async def call_tool(self, name, arguments):
        self.calls.append((name, deepcopy(arguments)))
        if name == "stardew_query_runtime":
            day = 2 if self.date_changes and self.after_use else 1
            return {
                "status": "succeeded",
                "output": {"snapshot": {
                    "date": {"season": "spring", "dayOfMonth": day, "year": 1},
                    "timeOfDay": 700,
                    "player": {"position": deepcopy(self.position), "energy": 200, "canMove": True},
                    "ui": {"menuOpen": False},
                }},
            }
        if name == "stardew_query_inventory":
            return {
                "status": "succeeded",
                "output": {"snapshot": {
                    "inventoryRevision": "a" * 64,
                    "containerKind": "player",
                    "slotCount": 12,
                    "slots": [{"index": 0, "item": {
                        "ref": {"value": "watering-can"},
                        "qualifiedItemId": "(T)CopperWateringCan",
                        "displayName": "Watering Can",
                        "stack": 1,
                        "quality": 0,
                        "category": "-99",
                        "tool": True,
                        "toolLevel": 1,
                        "toolKind": "watering_can",
                        "waterRemaining": self.water,
                        "waterCapacity": 55,
                        "bottomless": False,
                    }}],
                }},
            }
        if name == "stardew_query_world":
            tiles = [
                {
                    "position": {"locationId": "Farm", "x": x, "y": y},
                    "passable": True,
                    "occupied": False,
                    "diggable": True,
                    "water": False,
                    "terrainKind": "dirt",
                }
                for y in range(8)
                for x in range(8)
            ]
            return {
                "status": "succeeded",
                "output": {"snapshot": {
                    "worldRevision": "b" * 64,
                    "area": deepcopy(arguments["area"]),
                    "outdoors": True,
                    "tiles": tiles if arguments.get("includeTiles") else [],
                    "entities": [deepcopy(item) for item in self.crops.values()],
                    "characters": [],
                    "entitiesTruncated": False,
                    "charactersTruncated": False,
                }},
            }
        if name == "stardew_inspect":
            items = []
            for reference in arguments["refs"]:
                crop = self.crops.get(reference["value"])
                if crop is None:
                    items.append({"resolution": {"ref": reference, "status": "stale", "kind": "world_entity"}})
                else:
                    items.append({
                        "resolution": {"ref": reference, "status": "resolved", "kind": "world_entity"},
                        "worldEntity": deepcopy(crop),
                    })
            return {"status": "succeeded", "output": {"items": items, "warnings": []}}
        if name == "stardew_equip":
            return {"status": "succeeded", "output": {"changed": True}}
        if name == "stardew_navigate":
            self.position = deepcopy(arguments["position"])
            return {"status": "succeeded", "output": {}}
        if name == "stardew_use_tool":
            module = _module()
            target = self.crops[arguments["targetRef"]["value"]]["position"]
            for key in module._affected(
                target["locationId"], target["x"], target["y"],
                _facing(self.position, target), arguments["chargeLevel"],
            ):
                crop = self.crops.get(f"crop-{key[1]}-{key[2]}")
                if crop is not None:
                    crop["crop"]["watered"] = True
            self.water -= arguments["chargeLevel"] + 1
            self.after_use = True
            if self.use_status == "unknown":
                return {"status": "unknown", "error": {"code": "unknown_outcome", "message": "unknown", "retryable": False}}
            return {"status": self.use_status, "output": {}}
        raise AssertionError(f"unexpected Tool: {name}")


def _facing(stand, target):
    dx, dy = target["x"] - stand["x"], target["y"] - stand["y"]
    return {(0, -1): "up", (1, 0): "right", (0, 1): "down", (-1, 0): "left"}[(dx, dy)]


def test_water_skill_uses_safe_charge_and_verifies_every_affected_crop() -> None:
    context = WaterContext([_crop(2, 2), _crop(2, 3), _crop(2, 4)])

    result = asyncio.run(_run()(context, {"area": AREA, "maxChargeLevel": 1}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "completed"
    assert result["output"]["targetTotal"] == 3
    assert result["output"]["succeededCount"] == 3
    assert result["output"]["chargedActions"] == 1
    assert result["output"]["waterBefore"] == 20
    assert result["output"]["waterAfter"] == 18
    uses = [arguments for name, arguments in context.calls if name == "stardew_use_tool"]
    assert uses == [{"targetRef": {"value": "crop-2-2"}, "chargeLevel": 1}]


def test_water_skill_downgrades_to_single_tile_when_charge_footprint_is_incomplete() -> None:
    context = WaterContext([_crop(2, 2), _crop(2, 3)])

    result = asyncio.run(_run()(context, {"area": AREA, "maxChargeLevel": 1}))

    assert result["status"] == "succeeded"
    assert result["output"]["succeededCount"] == 2
    assert result["output"]["chargedActions"] == 0
    assert all(arguments["chargeLevel"] == 0 for name, arguments in context.calls if name == "stardew_use_tool")


def test_water_skill_never_charges_across_max_target_boundary() -> None:
    context = WaterContext([_crop(2, 2), _crop(2, 3), _crop(2, 4)])

    result = asyncio.run(_run()(context, {"area": AREA, "maxTargets": 1, "maxChargeLevel": 1}))

    assert result["status"] == "succeeded"
    assert result["output"]["plannedTargetCount"] == 1
    assert result["output"]["succeededCount"] == 1
    assert result["output"]["stopReason"] == "target_limit"
    uses = [arguments for name, arguments in context.calls if name == "stardew_use_tool"]
    assert uses == [{"targetRef": {"value": "crop-2-2"}, "chargeLevel": 0}]


def test_water_skill_resolves_unknown_use_tool_only_from_watered_postcondition() -> None:
    context = WaterContext(use_status="unknown")

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["succeededCount"] == 1
    assert len([call for call in context.calls if call[0] == "stardew_use_tool"]) == 1


def test_water_skill_ignores_already_watered_crop() -> None:
    context = WaterContext([_crop(2, 2, watered=True), _crop(3, 2)])

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["targetTotal"] == 1
    assert result["output"]["succeededCount"] == 1
    uses = [arguments for name, arguments in context.calls if name == "stardew_use_tool"]
    assert uses == [{"targetRef": {"value": "crop-3-2"}, "chargeLevel": 0}]


def test_water_skill_stops_when_date_changes_without_claiming_full_completion() -> None:
    context = WaterContext(date_changes=True)

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "partial"
    assert result["output"]["stopReason"] == "date_changed"
    assert result["output"]["resumable"] is False
    assert result["output"]["waterAfter"] is None


def test_water_skill_action_limit_preserves_remaining_progress() -> None:
    context = WaterContext()

    result = asyncio.run(_run()(context, {"area": AREA, "maxActions": 1}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "stopped"
    assert result["output"]["stopReason"] == "action_limit"
    assert result["output"]["actionsUsed"] == 1
    assert result["output"]["remainingCount"] == 1
    assert not any(name == "stardew_use_tool" for name, _ in context.calls)


def test_water_navigation_failure_never_reports_completed_with_remaining_targets() -> None:
    class Context(WaterContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_navigate":
                self.calls.append((name, deepcopy(arguments)))
                return {
                    "status": "failed",
                    "error": {"code": "execution_failed", "message": "blocked", "retryable": False},
                }
            return await super().call_tool(name, arguments)

    result = asyncio.run(_run()(Context(), {"area": AREA}))

    assert result["output"]["finalStatus"] == "stopped"
    assert result["output"]["stopReason"] == "navigation_failed"
    assert result["output"]["remainingCount"] == 1


def test_water_plan_allows_passable_crop_stand_but_rejects_real_blockers() -> None:
    module = _module()
    crop = _crop(2, 2)
    stand_crop = _crop(1, 2, watered=True)
    world = {
        "tiles": [
            {"position": {"locationId": "Farm", "x": 2, "y": 1}, "passable": True, "occupied": True},
            {"position": {"locationId": "Farm", "x": 3, "y": 2}, "passable": True, "occupied": True},
            {"position": {"locationId": "Farm", "x": 2, "y": 3}, "passable": True, "occupied": True},
            {"position": {"locationId": "Farm", "x": 1, "y": 2}, "passable": True, "occupied": True},
        ],
        "entities": [
            crop,
            stand_crop,
            {"ref": {"value": "machine-up"}, "kind": "machine", "position": {"locationId": "Farm", "x": 2, "y": 3}},
            {"ref": {"value": "machine-down"}, "kind": "machine", "position": {"locationId": "Farm", "x": 2, "y": 1}},
        ],
        "characters": [{"ref": {"value": "npc"}, "position": {"locationId": "Farm", "x": 3, "y": 2}}],
    }
    plan = module._choose_plan(
        {("Farm", 2, 2): crop}, world,
        {"locationId": "Farm", "x": 0, "y": 0}, 0,
        {"waterRemaining": 5, "toolLevel": 0},
    )

    assert plan is not None
    assert plan.stand == {"locationId": "Farm", "x": 1, "y": 2}
    assert plan.direction == "right"


def test_water_planning_queries_one_tile_beyond_explicit_target_area() -> None:
    module = _module()

    class Context:
        def __init__(self):
            self.areas = []

        async def call_tool(self, name, arguments):
            assert name == "stardew_query_world"
            area = deepcopy(arguments["area"])
            self.areas.append(area)
            return {
                "status": "succeeded",
                "output": {"snapshot": {
                    "worldRevision": "f" * 64,
                    "area": area,
                    "outdoors": True,
                    "tiles": [],
                    "entities": [],
                    "characters": [],
                    "entitiesTruncated": False,
                    "charactersTruncated": False,
                }},
            }

    context = Context()
    area = {"locationId": "Farm", "x": 4, "y": 15, "width": 7, "height": 1}

    asyncio.run(module._world_with_stand_margin(context, area))

    assert context.areas == [
        area,
        {**area, "y": 14, "height": 1},
        {**area, "y": 16, "height": 1},
        {**area, "x": 3, "width": 1},
        {**area, "x": 11, "width": 1},
    ]


def test_water_skill_is_hidden_without_complete_atomic_dependency_closure() -> None:
    skill = load_executable_skills([SKILL_DIR])[0]
    host = SkillHost(object(), [skill])
    tools = [types.Tool(name=name, inputSchema={"type": "object"}) for name in skill.allowed_tools]

    assert [tool.name for tool in host.available_tools(tools)] == ["stardew_skill_water_crops"]
    assert host.available_tools(tools[:-1]) == []


def test_water_skill_host_validates_no_target_result_against_public_schema() -> None:
    context = WaterContext([])
    host = SkillHost(context, load_executable_skills([SKILL_DIR]))

    result = asyncio.run(host.invoke("stardew_skill_water_crops", {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "completed"
    assert result["output"]["targetTotal"] == 0


def test_water_skill_cancellation_during_mutation_returns_unknown_progress() -> None:
    class Context(WaterContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_navigate":
                self.calls.append((name, deepcopy(arguments)))
                await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    async def exercise():
        context = Context()
        task = asyncio.create_task(_run()(context, {"area": AREA}))
        while not any(name == "stardew_navigate" for name, _ in context.calls):
            await asyncio.sleep(0)
        task.cancel()
        return await task

    result = asyncio.run(exercise())

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "skill_cancelled_unknown_outcome"
    assert result["output"]["stopReason"] == "cancelled"
    assert result["output"]["remainingCount"] == 1


def test_water_world_recursively_splits_truncated_area() -> None:
    module = _module()

    class Context:
        def __init__(self):
            self.calls = []

        async def call_tool(self, name, arguments):
            assert name == "stardew_query_world"
            area = arguments["area"]
            self.calls.append(deepcopy(area))
            truncated = area["width"] > 4
            crop = _crop(area["x"], area["y"])
            return {
                "status": "succeeded",
                "output": {"snapshot": {
                    "worldRevision": "e" * 64, "area": area, "outdoors": True,
                    "tiles": [], "entities": [] if truncated else [crop], "characters": [],
                    "entitiesTruncated": truncated, "charactersTruncated": False,
                }},
            }

    context = Context()
    result = asyncio.run(module._world(context, AREA))

    assert result["entitiesTruncated"] is False
    assert len(result["entities"]) == 2
    assert len(context.calls) == 3


def test_water_postcondition_failure_after_mutation_is_unknown() -> None:
    class Context(WaterContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_inspect" and self.after_use:
                self.calls.append((name, deepcopy(arguments)))
                return {
                    "status": "failed",
                    "error": {"code": "internal", "message": "failed", "retryable": False},
                }
            return await super().call_tool(name, arguments)

    result = asyncio.run(_run()(Context(), {"area": AREA}))

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "postcondition_unavailable"
    assert result["output"]["finalStatus"] == "unknown"


def test_water_postcondition_deadline_after_mutation_is_unknown() -> None:
    class Context(WaterContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_inspect" and self.after_use:
                self.calls.append((name, deepcopy(arguments)))
                await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    result = asyncio.run(_run()(Context(), {"area": AREA, "timeoutSeconds": 0.05}))

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "postcondition_unavailable"
    assert result["output"]["finalStatus"] == "unknown"


def test_water_cancellation_during_postcondition_is_unknown() -> None:
    class Context(WaterContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_inspect" and self.after_use:
                self.calls.append((name, deepcopy(arguments)))
                await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    async def exercise():
        context = Context()
        task = asyncio.create_task(_run()(context, {"area": AREA}))
        while not any(name == "stardew_inspect" and context.after_use for name, _ in context.calls):
            await asyncio.sleep(0)
        task.cancel()
        return await task

    result = asyncio.run(exercise())

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "skill_cancelled_unknown_outcome"
    assert result["output"]["stopReason"] == "cancelled"


def test_water_deadline_returns_without_cleanup_queries() -> None:
    class Context(WaterContext):
        def __init__(self):
            super().__init__()
            self.runtime_queries = 0

        async def call_tool(self, name, arguments):
            if name == "stardew_query_runtime":
                self.runtime_queries += 1
                if self.runtime_queries == 2:
                    self.calls.append((name, deepcopy(arguments)))
                    await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    context = Context()
    result = asyncio.run(_run()(context, {"area": AREA, "timeoutSeconds": 0.01}))

    assert result["status"] == "succeeded"
    assert result["output"]["stopReason"] == "deadline"
    assert result["output"]["remainingCount"] == 1
    assert context.runtime_queries == 2
    assert len([name for name, _ in context.calls if name == "stardew_query_inventory"]) == 1
    assert len([name for name, _ in context.calls if name == "stardew_query_world"]) == 1
