from __future__ import annotations

import asyncio
import sys
from copy import deepcopy
from pathlib import Path

from mcp import types

from stardew_valley_mcp.skill_host import SkillHost
from stardew_valley_mcp.skill_loader import _load_entrypoint, load_executable_skills


ROOT = Path(__file__).resolve().parents[2]
SKILL_DIR = ROOT / "skill" / "examples" / "stardew-refill-watering-can"
SCRIPT = SKILL_DIR / "scripts" / "run.py"


def _run():
    return load_executable_skills([SKILL_DIR])[0].run


def _module():
    run = _load_entrypoint(SCRIPT, "run")
    return sys.modules[run.__module__]


class RefillContext:
    def __init__(
        self,
        *,
        water=0,
        cans=1,
        source=(4, 2),
        blocked=(),
        navigate_failures=0,
        failed_navigation_position=None,
        use_status="succeeded",
        navigate_status="succeeded",
        equip_status="succeeded",
        refill=True,
    ):
        self.water = water
        self.cans = cans
        self.source = source
        self.blocked = set(blocked)
        self.navigate_failures = navigate_failures
        self.failed_navigation_position = failed_navigation_position
        self.use_status = use_status
        self.navigate_status = navigate_status
        self.equip_status = equip_status
        self.refill = refill
        self.position = {"locationId": "Farm", "x": 1, "y": 2}
        self.calls = []

    async def available_tools(self):
        skill = load_executable_skills([SKILL_DIR])[0]
        readonly = {"stardew_query_runtime", "stardew_query_inventory", "stardew_query_world"}
        return [
            types.Tool(
                name=name,
                inputSchema={"type": "object"},
                annotations=types.ToolAnnotations(readOnlyHint=name in readonly),
            )
            for name in skill.allowed_tools
        ]

    def resolve_unknown_mutation(self, tool_name):
        assert tool_name in {"stardew_navigate", "stardew_use_tool"}

    async def call_tool(self, name, arguments):
        self.calls.append((name, deepcopy(arguments)))
        if name == "stardew_query_runtime":
            return {"status": "succeeded", "output": {"snapshot": {
                "player": {"position": deepcopy(self.position), "canMove": True},
                "ui": {"menuOpen": False},
            }}}
        if name == "stardew_query_inventory":
            slots = []
            for index in range(self.cans):
                slots.append({"index": index, "item": {
                    "ref": {"value": f"watering-can-{index}"},
                    "qualifiedItemId": "(T)WateringCan",
                    "displayName": "Watering Can",
                    "stack": 1,
                    "quality": 0,
                    "category": "-99",
                    "tool": True,
                    "toolLevel": 0,
                    "toolKind": "watering_can",
                    "waterRemaining": self.water,
                    "waterCapacity": 40,
                    "bottomless": False,
                }})
            return {"status": "succeeded", "output": {"snapshot": {
                "inventoryRevision": "a" * 64,
                "containerKind": "player",
                "slotCount": 12,
                "slots": slots,
            }}}
        if name == "stardew_query_world":
            area = arguments["area"]
            width = min(area["width"], max(0, 8 - area["x"]))
            height = min(area["height"], max(0, 6 - area["y"]))
            if width <= 0 or height <= 0:
                return {"status": "failed", "error": {"code": "out_of_range"}}
            actual = {**area, "width": width, "height": height}
            tiles = []
            for y in range(actual["y"], actual["y"] + height):
                for x in range(actual["x"], actual["x"] + width):
                    refillable = self.source == (x, y)
                    blocked = (x, y) in self.blocked
                    tiles.append({
                        "position": {"locationId": "Farm", "x": x, "y": y},
                        "passable": not refillable and not blocked,
                        "occupied": blocked,
                        "diggable": False,
                        "water": refillable,
                        "terrainKind": "water" if refillable else "dirt",
                        "wateringCanRefillable": refillable,
                        "pathfindingBlocked": refillable or blocked,
                    })
            return {"status": "succeeded", "output": {"snapshot": {
                "worldRevision": "b" * 64,
                "area": actual,
                "outdoors": True,
                "tiles": tiles,
                "entities": [],
                "characters": [],
                "entitiesTruncated": False,
                "charactersTruncated": False,
            }}}
        if name == "stardew_equip":
            if self.equip_status == "failed":
                return {"status": "failed", "error": {"code": "command_cancelled"}}
            return {"status": self.equip_status, "output": {"changed": True}}
        if name == "stardew_navigate":
            if self.navigate_failures:
                self.navigate_failures -= 1
                if self.failed_navigation_position is not None:
                    self.position = {
                        "locationId": "Farm",
                        "x": self.failed_navigation_position[0],
                        "y": self.failed_navigation_position[1],
                    }
                return {
                    "status": "failed",
                    "error": {
                        "code": "execution_failed",
                        "details": {"navigation": {"lastConfirmedPosition": deepcopy(self.position)}},
                    },
                }
            self.position = deepcopy(arguments["position"])
            if self.navigate_status == "unknown":
                return {"status": "unknown", "error": {"code": "unknown_outcome"}}
            return {"status": self.navigate_status, "output": {}}
        if name == "stardew_use_tool":
            if self.refill:
                self.water = 40
            if self.use_status == "unknown":
                return {"status": "unknown", "error": {"code": "unknown_outcome"}}
            if self.use_status == "failed":
                return {"status": "failed", "error": {"code": "command_cancelled"}}
            return {"status": self.use_status, "output": {}}
        raise AssertionError(name)


def test_already_full_does_not_mutate_or_move() -> None:
    context = RefillContext(water=40)
    result = asyncio.run(_run()(context, {}))
    assert result["status"] == "succeeded"
    assert result["output"]["stopReason"] == "already_full"
    assert not any(name in {"stardew_equip", "stardew_navigate", "stardew_use_tool"} for name, _ in context.calls)


def test_requires_exactly_one_public_watering_can() -> None:
    for count in (0, 2):
        result = asyncio.run(_run()(RefillContext(cans=count), {}))
        assert result["status"] == "failed"
        assert result["output"]["stopReason"] == "watering_can_missing"


def test_bottomless_flag_does_not_replace_the_capacity_postcondition() -> None:
    context = RefillContext(water=3)

    original_call = context.call_tool

    async def call_tool(name, arguments):
        result = await original_call(name, arguments)
        if name == "stardew_query_inventory":
            for slot in result["output"]["snapshot"]["slots"]:
                slot["item"]["bottomless"] = True
        return result

    context.call_tool = call_tool
    result = asyncio.run(_run()(context, {}))

    assert result["output"]["stopReason"] == "completed"
    assert result["output"]["waterBefore"] == 3
    assert result["output"]["waterAfter"] == 40


def test_equip_and_use_tool_cancellation_have_stable_reason() -> None:
    equip = asyncio.run(_run()(RefillContext(equip_status="failed"), {}))
    use = asyncio.run(_run()(RefillContext(use_status="failed", refill=False), {}))

    assert equip["output"]["stopReason"] == "cancelled"
    assert use["output"]["stopReason"] == "cancelled"


def test_missing_and_unreachable_water_sources_have_stable_reasons() -> None:
    missing = asyncio.run(_run()(RefillContext(source=None), {}))
    blocked = {(3, 2), (5, 2), (4, 1), (4, 3)}
    unreachable = asyncio.run(_run()(RefillContext(blocked=blocked), {}))
    assert missing["output"]["stopReason"] == "water_source_not_found"
    assert unreachable["output"]["stopReason"] == "water_source_unreachable"


def test_missing_refillability_presence_fails_closed_for_an_older_mod() -> None:
    class Context(RefillContext):
        async def call_tool(self, name, arguments):
            result = await super().call_tool(name, arguments)
            if name == "stardew_query_world" and result.get("status") == "succeeded":
                for tile in result["output"]["snapshot"]["tiles"]:
                    tile.pop("wateringCanRefillable", None)
            return result

    result = asyncio.run(_run()(Context(), {}))

    assert result["status"] == "failed"
    assert result["error"]["code"] == "refillability_unavailable"
    assert result["output"]["stopReason"] == "query_failed"
    assert result["output"]["actionsUsed"] == 0


def test_bfs_path_cost_beats_manhattan_distance_and_counts_unreachable_stands() -> None:
    module = _module()
    tiles = {}
    for y in range(8):
        for x in range(7):
            source = (x, y) in {(3, 2), (1, 6), (6, 0)}
            blocked = (
                (x == 2 and y < 6)
                or (x, y) in {(5, 0), (5, 1), (6, 2)}
            )
            tiles[("Farm", x, y)] = {
                "position": {"locationId": "Farm", "x": x, "y": y},
                "passable": not source and not blocked,
                "occupied": blocked,
                "wateringCanRefillable": source,
                "pathfindingBlocked": source or blocked,
            }
    candidates, unreachable = module._rank_candidates(
        tiles, ("Farm", 1, 2), [("Farm", 3, 2), ("Farm", 1, 6), ("Farm", 6, 0)]
    )
    assert candidates
    assert candidates[0].source == ("Farm", 1, 6)
    assert unreachable > 0


def test_native_pathfinding_fact_distinguishes_crop_from_hard_obstruction() -> None:
    module = _module()
    tiles = {}
    for x in range(6):
        source = x == 5
        tiles[("Farm", x, 0)] = {
            "position": {"locationId": "Farm", "x": x, "y": 0},
            "passable": not source,
            "occupied": x in {2, 4},
            "wateringCanRefillable": source,
            "pathfindingBlocked": source or x == 4,
        }

    candidates, _ = module._rank_candidates(
        tiles, ("Farm", 0, 0), [("Farm", 5, 0)]
    )

    assert candidates == []
    tiles[("Farm", 4, 0)]["pathfindingBlocked"] = False
    candidates, _ = module._rank_candidates(
        tiles, ("Farm", 0, 0), [("Farm", 5, 0)]
    )
    assert candidates[0].stand == ("Farm", 4, 0)
    assert candidates[0].distance == 4


def test_navigation_failure_falls_through_to_next_candidate() -> None:
    context = RefillContext(navigate_failures=1)
    result = asyncio.run(_run()(context, {}))
    assert result["status"] == "succeeded"
    assert result["output"]["stopReason"] == "completed"
    assert result["output"]["navigationAttempts"] == 2


def test_navigation_failure_does_not_require_runtime_refresh_before_next_candidate() -> None:
    class Context(RefillContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_query_runtime" and self.navigate_failures == 0:
                raise AssertionError("候选导航失败后不应依赖额外 runtime 查询")
            return await super().call_tool(name, arguments)

    result = asyncio.run(_run()(Context(navigate_failures=1), {}))

    assert result["status"] == "succeeded"
    assert result["output"]["navigationAttempts"] == 2


def test_all_navigation_failures_do_not_report_completion() -> None:
    result = asyncio.run(_run()(RefillContext(
        navigate_failures=99,
        failed_navigation_position=(2, 3),
    ), {}))
    assert result["status"] == "failed"
    assert result["output"]["stopReason"] == "navigation_failed"
    assert result["output"]["lastConfirmedState"] == "not_full"
    assert result["output"]["lastPosition"] == {"locationId": "Farm", "x": 2, "y": 3}
    assert result["output"]["selectedSource"] is not None
    assert result["output"]["selectedStand"] is not None


def test_success_and_unknown_use_tool_both_require_full_postcondition() -> None:
    succeeded = asyncio.run(_run()(RefillContext(), {}))
    resolved_unknown = asyncio.run(_run()(RefillContext(use_status="unknown"), {}))
    unresolved = asyncio.run(_run()(RefillContext(use_status="unknown", refill=False), {}))
    assert succeeded["output"]["waterAfter"] == 40
    assert resolved_unknown["status"] == "succeeded"
    assert resolved_unknown["output"]["lastConfirmedState"] == "full"
    assert unresolved["status"] == "unknown"
    assert unresolved["output"]["stopReason"] == "refill_not_confirmed"


def test_confirmed_full_is_not_negated_by_an_unneeded_runtime_refresh() -> None:
    class Context(RefillContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_query_runtime" and self.water == 40:
                return {"status": "failed", "error": {"code": "transport_closed"}}
            return await super().call_tool(name, arguments)

    skill = load_executable_skills([SKILL_DIR])[0]
    result = asyncio.run(SkillHost(Context(use_status="unknown"), [skill]).invoke(skill.name, {}))

    assert result["status"] == "succeeded"
    assert result["output"]["stopReason"] == "completed"


def test_refill_not_confirmed_is_stable_when_action_returns_success() -> None:
    result = asyncio.run(_run()(RefillContext(refill=False), {}))
    assert result["status"] == "failed"
    assert result["output"]["stopReason"] == "refill_not_confirmed"
    assert result["output"]["waterAfter"] == 0


def test_deadline_is_bounded_and_stable() -> None:
    class Context(RefillContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_query_world":
                await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    result = asyncio.run(_run()(Context(), {"timeoutSeconds": 0.01}))
    assert result["status"] == "failed"
    assert result["output"]["stopReason"] == "deadline"


def test_expired_deadline_before_first_mutation_does_not_count_an_action() -> None:
    module = _module()
    context = RefillContext()
    state = {"actions_used": 0, "navigation_attempts": 0}
    tracking = {"mutation": False, "mutation_uncertain": False, "postcondition_pending": False}

    async def exercise():
        try:
            await module._mutate(
                context,
                "stardew_equip",
                {},
                module.time.monotonic() - 1,
                tracking,
                state,
            )
        except module.SkillAbort as error:
            return error
        raise AssertionError("expired deadline must abort")

    error = asyncio.run(exercise())

    assert error.stop_reason == "deadline"
    assert error.retryable is True
    assert state["actions_used"] == 0
    assert context.calls == []


def test_map_exactly_at_scan_limit_is_not_rejected_by_boundary_probe() -> None:
    module = _module()

    class Context:
        async def call_tool(self, name, arguments):
            assert name == "stardew_query_world"
            area = arguments["area"]
            if area["x"] >= 32 or area["y"] >= 32:
                return {"status": "failed", "error": {"code": "out_of_range"}}
            tiles = [
                {
                    "position": {"locationId": "Farm", "x": x, "y": y},
                    "passable": True,
                    "occupied": False,
                    "wateringCanRefillable": False,
                    "pathfindingBlocked": False,
                }
                for y in range(32)
                for x in range(32)
            ]
            return {"status": "succeeded", "output": {"snapshot": {
                "area": {"locationId": "Farm", "x": 0, "y": 0, "width": 32, "height": 32},
                "tiles": tiles,
            }}}

    tiles = asyncio.run(
        module._scan_current_location(Context(), "Farm", 1024, float("inf"))
    )
    assert len(tiles) == 1024


def test_cancellation_during_navigation_is_unknown() -> None:
    class Context(RefillContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_navigate":
                self.calls.append((name, deepcopy(arguments)))
                await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    async def exercise():
        context = Context()
        task = asyncio.create_task(_run()(context, {}))
        while not any(name == "stardew_navigate" for name, _ in context.calls):
            await asyncio.sleep(0)
        task.cancel()
        return await task

    result = asyncio.run(exercise())
    assert result["status"] == "unknown"
    assert result["output"]["stopReason"] == "cancelled"
    assert result["output"]["actionsUsed"] == 2
    assert result["output"]["navigationAttempts"] == 1


def test_cancellation_during_unknown_navigation_postcondition_preserves_progress() -> None:
    class Context(RefillContext):
        def __init__(self):
            super().__init__(navigate_status="unknown")
            self.runtime_queries = 0

        async def call_tool(self, name, arguments):
            if name == "stardew_query_runtime":
                self.runtime_queries += 1
                if self.runtime_queries > 1:
                    await asyncio.sleep(60)
            return await super().call_tool(name, arguments)

    async def exercise():
        context = Context()
        skill = load_executable_skills([SKILL_DIR])[0]
        task = asyncio.create_task(SkillHost(context, [skill]).invoke(skill.name, {}))
        while context.runtime_queries < 2:
            await asyncio.sleep(0)
        task.cancel()
        return await task

    result = asyncio.run(exercise())

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "cancelled"
    assert result["output"]["stopReason"] == "cancelled"
    assert result["output"]["waterBefore"] == 0
    assert result["output"]["selectedSource"] is not None
    assert result["output"]["selectedStand"] is not None
    assert result["output"]["actionsUsed"] == 2


def test_skill_declares_only_contract_atomic_tools_and_host_validates_result() -> None:
    skill = load_executable_skills([SKILL_DIR])[0]
    assert skill.allowed_tools == {
        "stardew_query_runtime", "stardew_query_inventory", "stardew_query_world",
        "stardew_navigate", "stardew_equip", "stardew_use_tool",
    }
    context = RefillContext(water=40)
    host = SkillHost(context, [skill])
    result = asyncio.run(host.invoke("stardew_skill_refill_watering_can", {}))
    assert result["status"] == "succeeded"


def test_host_preserves_navigation_failure_progress_after_known_mutations() -> None:
    context = RefillContext(
        navigate_failures=99,
        failed_navigation_position=(2, 3),
    )
    skill = load_executable_skills([SKILL_DIR])[0]

    result = asyncio.run(SkillHost(context, [skill]).invoke(skill.name, {}))

    assert result["status"] == "failed"
    assert result["error"]["retryable"] is False
    assert result["output"]["stopReason"] == "navigation_failed"
    assert result["output"]["lastPosition"] == {"locationId": "Farm", "x": 2, "y": 3}
    assert result["output"]["selectedSource"] is not None
    assert result["output"]["candidateCount"] > 0


def test_host_preserves_refill_postcondition_failure_after_known_mutations() -> None:
    context = RefillContext(refill=False)
    skill = load_executable_skills([SKILL_DIR])[0]

    result = asyncio.run(SkillHost(context, [skill]).invoke(skill.name, {}))

    assert result["status"] == "failed"
    assert result["error"]["retryable"] is False
    assert result["output"]["stopReason"] == "refill_not_confirmed"
    assert result["output"]["waterAfter"] == 0
    assert result["output"]["lastConfirmedState"] == "not_full"


def test_host_preserves_failure_after_unknown_navigation_is_resolved_by_position() -> None:
    context = RefillContext(navigate_status="unknown", refill=False)
    skill = load_executable_skills([SKILL_DIR])[0]

    result = asyncio.run(SkillHost(context, [skill]).invoke(skill.name, {}))

    assert result["status"] == "failed"
    assert result["error"]["retryable"] is False
    assert result["output"]["stopReason"] == "refill_not_confirmed"
    assert result["output"]["lastConfirmedState"] == "not_full"


def test_host_preserves_structured_progress_when_unknown_navigation_cannot_be_queried() -> None:
    class Context(RefillContext):
        def __init__(self):
            super().__init__(navigate_status="unknown")
            self.runtime_queries = 0

        async def call_tool(self, name, arguments):
            if name == "stardew_query_runtime":
                self.runtime_queries += 1
                if self.runtime_queries > 1:
                    return {"status": "failed", "error": {"code": "transport_closed"}}
            return await super().call_tool(name, arguments)

    skill = load_executable_skills([SKILL_DIR])[0]
    result = asyncio.run(SkillHost(Context(), [skill]).invoke(skill.name, {}))

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "navigation_postcondition_unavailable"
    assert result["output"]["stopReason"] == "navigation_failed"
    assert result["output"]["waterBefore"] == 0
    assert result["output"]["selectedSource"] is not None
    assert result["output"]["selectedStand"] is not None
    assert result["output"]["actionsUsed"] == 2
