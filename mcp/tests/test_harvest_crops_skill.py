from __future__ import annotations

import asyncio
from copy import deepcopy
from pathlib import Path

from mcp import types

from stardew_valley_mcp.skill_host import SkillHost
from stardew_valley_mcp.skill_loader import load_executable_skills


ROOT = Path(__file__).resolve().parents[2]
SKILL_DIR = ROOT / "skill" / "examples" / "stardew-harvest-crops"
AREA = {"locationId": "Farm", "x": 0, "y": 0, "width": 8, "height": 8}


def _run():
    return load_executable_skills([SKILL_DIR])[0].run


def _crop(x: int, y: int, action: str):
    return {
        "ref": {"value": f"crop-{x}-{y}"},
        "kind": "crop",
        "position": {"locationId": "Farm", "x": x, "y": y},
        "displayName": "Crop",
        "crop": {
            "cropId": "24",
            "harvestItemId": "24" if action == "interact" else "262",
            "growthPhase": 5,
            "readyForHarvest": True,
            "watered": True,
            "dead": False,
            "regrows": False,
            "harvestAction": action,
        },
    }


class HarvestContext:
    def __init__(self, crops=None, *, mutation_status="succeeded", with_scythe=True, mutation_changes=True):
        source = [_crop(2, 2, "interact")] if crops is None else crops
        self.crops = {item["ref"]["value"]: deepcopy(item) for item in source}
        self.calls: list[tuple[str, dict]] = []
        self.position = {"locationId": "Farm", "x": 1, "y": 1}
        self.mutation_status = mutation_status
        self.with_scythe = with_scythe
        self.mutation_changes = mutation_changes
        self.items: dict[tuple[str, int], int] = {}

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
            return {
                "status": "succeeded",
                "output": {"snapshot": {
                    "date": {"season": "fall", "dayOfMonth": 9, "year": 2},
                    "timeOfDay": 900,
                    "player": {"position": deepcopy(self.position), "energy": 180, "canMove": True},
                    "ui": {"menuOpen": False},
                }},
            }
        if name == "stardew_query_inventory":
            slots = []
            if self.with_scythe:
                slots.append({"index": 0, "item": {
                    "ref": {"value": "scythe"}, "qualifiedItemId": "(W)47", "displayName": "Scythe",
                    "stack": 1, "quality": 0, "category": "-99", "tool": True,
                    "toolLevel": 0, "toolKind": "scythe",
                }})
            for index, ((item_id, quality), stack) in enumerate(sorted(self.items.items()), start=1):
                slots.append({"index": index, "item": {
                    "ref": {"value": f"item-{index}"}, "qualifiedItemId": item_id,
                    "displayName": "Harvest", "stack": stack, "quality": quality,
                    "category": "-75", "tool": False, "toolLevel": 0,
                }})
            return {
                "status": "succeeded",
                "output": {"snapshot": {
                    "inventoryRevision": "c" * 64, "containerKind": "player",
                    "slotCount": 36, "slots": slots,
                }},
            }
        if name == "stardew_query_world":
            area = arguments["area"]
            entities = [
                deepcopy(item) for item in self.crops.values()
                if area["x"] <= item["position"]["x"] < area["x"] + area["width"]
                and area["y"] <= item["position"]["y"] < area["y"] + area["height"]
            ]
            return {
                "status": "succeeded",
                "output": {"snapshot": {
                    "worldRevision": "d" * 64, "area": deepcopy(area), "outdoors": True,
                    "tiles": [], "entities": entities, "characters": [],
                    "entitiesTruncated": False, "charactersTruncated": False,
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
        if name == "stardew_navigate":
            if "position" in arguments:
                self.position = deepcopy(arguments["position"])
            else:
                target = self.crops[arguments["targetRef"]["value"]]["position"]
                self.position = {"locationId": "Farm", "x": target["x"] - 1, "y": target["y"]}
            return {"status": "succeeded", "output": {}}
        if name == "stardew_equip":
            return {"status": "succeeded", "output": {"changed": True}}
        if name in {"stardew_interact", "stardew_use_tool"}:
            crop = self.crops.get(arguments["targetRef"]["value"])
            if self.mutation_changes and crop is not None:
                item_id = crop["crop"]["harvestItemId"]
                self.items[(f"(O){item_id}", 0)] = self.items.get((f"(O){item_id}", 0), 0) + 1
                del self.crops[arguments["targetRef"]["value"]]
            if self.mutation_status == "unknown":
                return {"status": "unknown", "error": {"code": "unknown_outcome", "message": "unknown", "retryable": False}}
            return {"status": self.mutation_status, "output": {}}
        raise AssertionError(f"unexpected Tool: {name}")


def test_harvest_skill_executes_interact_and_scythe_paths_and_reports_inventory_delta() -> None:
    context = HarvestContext([_crop(2, 2, "interact"), _crop(3, 2, "scythe")])

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "completed"
    assert result["output"]["succeededCount"] == 2
    assert result["output"]["interactCount"] == 1
    assert result["output"]["scytheCount"] == 1
    assert {item["qualifiedItemId"] for item in result["output"]["inventoryChanges"]} == {"(O)24", "(O)262"}
    mutations = [name for name, _ in context.calls if name in {"stardew_interact", "stardew_use_tool"}]
    assert mutations == ["stardew_interact", "stardew_use_tool"]


def test_harvest_moves_off_the_next_crop_before_interacting() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_navigate" and "targetRef" in arguments:
                self.calls.append((name, deepcopy(arguments)))
                target = self.crops[arguments["targetRef"]["value"]]["position"]
                self.position = {"locationId": "Farm", "x": target["x"] + 1, "y": target["y"]}
                return {"status": "succeeded", "output": {}}
            return await super().call_tool(name, arguments)

    context = Context([_crop(2, 2, "interact"), _crop(3, 2, "interact")])

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["output"]["finalStatus"] == "completed"
    assert result["output"]["succeededCount"] == 2
    navigations = [arguments for name, arguments in context.calls if name == "stardew_navigate"]
    assert navigations[1] == {
        "position": {"locationId": "Farm", "x": 2, "y": 2},
        "arrival": "exact",
        "faceOnArrival": "right",
    }


def test_harvest_navigation_failure_never_reports_completed_with_remaining_targets() -> None:
    class Context(HarvestContext):
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
    assert result["output"]["resumable"] is True


def test_harvest_skill_resolves_unknown_mutation_from_coordinate_postcondition_without_replay() -> None:
    context = HarvestContext(mutation_status="unknown", mutation_changes=True)

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["succeededCount"] == 1
    assert len([call for call in context.calls if call[0] == "stardew_interact"]) == 1


def test_harvest_skill_preserves_unknown_when_target_remains_mature() -> None:
    context = HarvestContext(mutation_status="unknown", mutation_changes=False)

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "harvest_unknown"
    assert result["output"]["finalStatus"] == "unknown"
    assert result["output"]["remainingTargets"] == [{"locationId": "Farm", "x": 2, "y": 2}]
    assert result["output"]["inventoryChanges"] == []
    assert len([call for call in context.calls if call[0] == "stardew_interact"]) == 1


def test_harvest_skill_skips_missing_scythe_target_but_continues_hand_harvest() -> None:
    context = HarvestContext([_crop(2, 2, "scythe"), _crop(3, 2, "interact")], with_scythe=False)

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "partial"
    assert result["output"]["stopReason"] == "scythe_missing"
    assert result["output"]["succeededCount"] == 1
    assert result["output"]["failedCount"] == 1
    assert [name for name, _ in context.calls if name == "stardew_interact"] == ["stardew_interact"]


def test_harvest_skill_stops_after_succeeded_action_without_world_progress() -> None:
    context = HarvestContext(mutation_status="succeeded", mutation_changes=False)

    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["stopReason"] == "no_progress"
    assert result["output"]["failedCount"] == 1
    assert result["output"]["resumable"] is True


def test_harvest_skill_action_limit_preserves_remaining_progress() -> None:
    context = HarvestContext()

    result = asyncio.run(_run()(context, {"area": AREA, "maxActions": 1}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "stopped"
    assert result["output"]["stopReason"] == "action_limit"
    assert result["output"]["actionsUsed"] == 1
    assert result["output"]["remainingCount"] == 1
    assert not any(name in {"stardew_interact", "stardew_use_tool"} for name, _ in context.calls)


def test_harvest_skill_stops_on_date_change_without_cross_day_cleanup() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            result = await super().call_tool(name, arguments)
            if name == "stardew_query_runtime" and self.items:
                result["output"]["snapshot"]["date"]["dayOfMonth"] = 10
            return result

    context = Context()
    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["stopReason"] == "date_changed"
    assert result["output"]["succeededCount"] == 1
    assert result["output"]["inventoryChanges"] == []
    assert len([name for name, _ in context.calls if name == "stardew_query_inventory"]) == 1
    assert len([name for name, _ in context.calls if name == "stardew_query_world"]) == 3


def test_harvest_cancellation_during_mutation_returns_unknown_progress() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_interact":
                self.calls.append((name, deepcopy(arguments)))
                await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    async def exercise():
        context = Context()
        task = asyncio.create_task(_run()(context, {"area": AREA}))
        while not any(name == "stardew_interact" for name, _ in context.calls):
            await asyncio.sleep(0)
        task.cancel()
        return await task

    result = asyncio.run(exercise())

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "skill_cancelled_unknown_outcome"
    assert result["output"]["stopReason"] == "cancelled"
    assert result["output"]["remainingCount"] == 1


def test_harvest_skill_is_hidden_without_complete_atomic_dependency_closure() -> None:
    skill = load_executable_skills([SKILL_DIR])[0]
    host = SkillHost(object(), [skill])
    tools = [types.Tool(name=name, inputSchema={"type": "object"}) for name in skill.allowed_tools]

    assert [tool.name for tool in host.available_tools(tools)] == ["stardew_skill_harvest_crops"]
    assert host.available_tools(tools[:-1]) == []


def test_harvest_skill_host_validates_no_target_result_against_public_schema() -> None:
    context = HarvestContext([])
    host = SkillHost(context, load_executable_skills([SKILL_DIR]))

    result = asyncio.run(host.invoke("stardew_skill_harvest_crops", {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "completed"
    assert result["output"]["targetTotal"] == 0


def test_harvest_stale_precondition_stops_instead_of_spinning() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_inspect":
                self.calls.append((name, deepcopy(arguments)))
                return {"status": "succeeded", "output": {"items": [{
                    "resolution": {"ref": arguments["refs"][0], "status": "stale", "kind": "world_entity"}
                }], "warnings": []}}
            return await super().call_tool(name, arguments)

    context = Context()
    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["stopReason"] == "target_changed"
    assert len([call for call in context.calls if call[0] == "stardew_inspect"]) == 1
    assert not any(name in {"stardew_interact", "stardew_use_tool"} for name, _ in context.calls)


def test_harvest_postcondition_query_unknown_is_not_downgraded_to_no_progress() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_query_world" and self.items:
                self.calls.append((name, deepcopy(arguments)))
                return {"status": "unknown", "error": {"code": "unknown_outcome", "message": "unknown", "retryable": False}}
            return await super().call_tool(name, arguments)

    result = asyncio.run(_run()(Context(), {"area": AREA}))

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "postcondition_unavailable"
    assert result["output"]["finalStatus"] == "unknown"


def test_harvest_scythe_aoe_counts_all_newly_completed_pending_targets() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            result = await super().call_tool(name, arguments)
            if name == "stardew_use_tool":
                for reference, crop in list(self.crops.items()):
                    item_id = crop["crop"]["harvestItemId"]
                    self.items[(f"(O){item_id}", 0)] = self.items.get((f"(O){item_id}", 0), 0) + 1
                    del self.crops[reference]
            return result

    context = Context([_crop(2, 2, "scythe"), _crop(3, 2, "scythe")])
    result = asyncio.run(_run()(context, {"area": AREA}))

    assert result["status"] == "succeeded"
    assert result["output"]["succeededCount"] == 2
    assert result["output"]["scytheCount"] == 2
    assert result["output"]["skippedCount"] == 0


def test_harvest_accepts_case_insensitive_current_location_id() -> None:
    context = HarvestContext([])
    result = asyncio.run(_run()(context, {"area": {**AREA, "locationId": "farm"}}))

    assert result["status"] == "succeeded"
    assert result["output"]["stopReason"] == "no_targets"


def test_harvest_postcondition_failure_after_mutation_is_unknown() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_query_world" and self.items:
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


def test_harvest_postcondition_deadline_after_mutation_is_unknown() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_inspect" and self.items:
                self.calls.append((name, deepcopy(arguments)))
                await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    result = asyncio.run(_run()(Context(), {"area": AREA, "timeoutSeconds": 0.05}))

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "postcondition_unavailable"
    assert result["output"]["finalStatus"] == "unknown"


def test_harvest_cancellation_during_postcondition_is_unknown() -> None:
    class Context(HarvestContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_inspect" and self.items:
                self.calls.append((name, deepcopy(arguments)))
                await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    async def exercise():
        context = Context()
        task = asyncio.create_task(_run()(context, {"area": AREA}))
        while not any(name == "stardew_inspect" and context.items for name, _ in context.calls):
            await asyncio.sleep(0)
        task.cancel()
        return await task

    result = asyncio.run(exercise())

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "skill_cancelled_unknown_outcome"
    assert result["output"]["stopReason"] == "cancelled"


def test_harvest_deadline_returns_without_cleanup_queries() -> None:
    class Context(HarvestContext):
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
