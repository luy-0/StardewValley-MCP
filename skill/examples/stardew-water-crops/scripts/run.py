"""在一次受限 Skill 调用中完成指定范围的作物浇水。"""

from __future__ import annotations

import asyncio
import time
from dataclasses import dataclass
from typing import Any


@dataclass
class SkillAbort(Exception):
    code: str
    message: str
    stop_reason: str
    outcome: str = "failed"
    retryable: bool = False


@dataclass(frozen=True)
class WaterPlan:
    stand: dict[str, Any]
    target: dict[str, Any]
    direction: str
    charge_level: int
    entities: tuple[dict[str, Any], ...]


DIRECTIONS = (
    ("up", 0, -1),
    ("right", 1, 0),
    ("down", 0, 1),
    ("left", -1, 0),
)


async def run(ctx, arguments: dict[str, Any]) -> dict[str, Any]:
    area = dict(arguments["area"])
    max_targets = int(arguments.get("maxTargets", 64))
    max_actions = int(arguments.get("maxActions", 192))
    max_charge = int(arguments.get("maxChargeLevel", 0))
    min_energy = float(arguments.get("minEnergy", 10))
    stop_time = int(arguments.get("stopTime", 2500))
    deadline = time.monotonic() + float(arguments.get("timeoutSeconds", 300))

    before: dict[str, Any] | None = None
    current_runtime: dict[str, Any] | None = None
    initial_inventory: dict[str, Any] | None = None
    current_inventory: dict[str, Any] | None = None
    target_total = succeeded = skipped = failed = actions_used = charged_actions = 0
    initial_positions: set[tuple[str, int, int]] = set()
    pending: set[tuple[str, int, int]] = set()
    finished_positions: set[tuple[str, int, int]] = set()
    attempted: set[tuple[str, int, int]] = set()
    last_target: dict[str, Any] | None = None
    stop_reason = "completed"
    deferred_reason: str | None = None
    outcome = "succeeded"
    error: SkillAbort | None = None
    cancelled = False
    tracking = {"cancelledDuringMutation": False, "postconditionPending": False}

    try:
        before = current_runtime = await _runtime(ctx, deadline)
        _ensure_ready(before, area)
        initial_inventory = current_inventory = await _inventory(ctx, deadline)
        watering_can = _watering_can(current_inventory)
        if watering_can is None:
            raise SkillAbort("watering_can_missing", "背包中没有唯一可用的喷壶", "watering_can_missing")

        world = await _world(ctx, area, deadline)
        initial_entities = _unwatered_crops(world, area)
        initial_positions = {_key(entity["position"]) for entity in initial_entities}
        target_total = len(initial_positions)
        pending = set(sorted(initial_positions, key=_position_sort)[:max_targets])
        if not pending:
            stop_reason = "no_targets"
        else:
            equip = await _mutate(
                ctx,
                "stardew_equip",
                {
                    "itemRef": watering_can["ref"],
                    "inventoryRevision": current_inventory["inventoryRevision"],
                },
                actions_used,
                max_actions,
                deadline,
                tracking,
            )
            actions_used += 1
            if equip.get("status") == "unknown":
                raise SkillAbort("equip_unknown", "装备喷壶结果未知，禁止继续动作", "unknown_outcome", "unknown")
            if equip.get("status") != "succeeded":
                raise SkillAbort("equip_failed", "无法装备喷壶", _tool_stop_reason(equip))

        while pending and stop_reason == "completed":
            _ensure_deadline(deadline)
            current_runtime = await _runtime(ctx, deadline)
            if _date(current_runtime) != _date(before):
                stop_reason = "date_changed"
                break
            if current_runtime.get("timeOfDay", 0) >= stop_time:
                stop_reason = "time_limit"
                break
            if float(current_runtime.get("player", {}).get("energy", 0)) < min_energy:
                stop_reason = "low_energy"
                break
            if actions_used >= max_actions:
                stop_reason = "action_limit"
                break

            current_inventory = await _inventory(ctx, deadline)
            watering_can = _watering_can(current_inventory)
            if watering_can is None:
                stop_reason = "watering_can_missing"
                break
            water_left = watering_can.get("waterRemaining")
            if water_left is not None and not watering_can.get("bottomless", False) and int(water_left) <= 0:
                stop_reason = "watering_can_empty"
                break

            world = await _world_with_stand_margin(ctx, area, deadline)
            current = {_key(entity["position"]): entity for entity in _unwatered_crops(world, area)}
            externally_completed = pending - current.keys()
            if externally_completed:
                skipped += len(externally_completed)
                finished_positions.update(externally_completed)
                pending -= externally_completed
            if not pending:
                break

            repeat = sorted(pending & attempted, key=_position_sort)
            if repeat:
                pending.remove(repeat[0])
                failed += 1
                stop_reason = "no_progress"
                break

            plan = _choose_plan(
                {key: current[key] for key in pending if key in current},
                world,
                current_runtime["player"]["position"],
                min(max_charge, int(watering_can.get("toolLevel", 0))),
                watering_can,
            )
            if plan is None:
                failed += len(pending)
                stop_reason = "no_safe_stand"
                break
            planned_keys = {_key(entity["position"]) for entity in plan.entities}
            last_target = dict(plan.target)

            inspected = await _inspect_crops(ctx, plan.entities, deadline)
            if inspected != planned_keys:
                stop_reason = "target_changed"
                break

            navigation = await _mutate(
                ctx,
                "stardew_navigate",
                {"position": plan.stand, "arrival": "exact", "faceOnArrival": plan.direction},
                actions_used,
                max_actions,
                deadline,
                tracking,
            )
            actions_used += 1
            if navigation.get("status") == "unknown":
                position = (await _runtime(ctx, deadline))["player"]["position"]
                if not _same_key(_key(position), _key(plan.stand)):
                    raise SkillAbort("navigation_unknown", "导航结果未知，禁止提交工具动作", "unknown_outcome", "unknown")
            elif navigation.get("status") != "succeeded":
                if _tool_stop_reason(navigation) == "cancelled":
                    stop_reason = "cancelled"
                    break
                failed += len(planned_keys)
                pending -= planned_keys
                deferred_reason = deferred_reason or "navigation_failed"
                continue

            current_runtime = await _runtime(ctx, deadline)
            if _date(current_runtime) != _date(before):
                stop_reason = "date_changed"
                break
            inspected = await _inspect_crops(ctx, plan.entities, deadline)
            if inspected != planned_keys:
                stop_reason = "target_changed"
                break

            attempted.update(planned_keys)

            tracking["postconditionPending"] = True
            use = await _mutate(
                ctx,
                "stardew_use_tool",
                {"targetRef": plan.entities[0]["ref"], "chargeLevel": plan.charge_level},
                actions_used,
                max_actions,
                deadline,
                tracking,
            )
            actions_used += 1
            current_inventory = None
            if plan.charge_level > 0:
                charged_actions += 1

            watered_after = await _inspect_watered(ctx, plan.entities, deadline)
            tracking["postconditionPending"] = False
            confirmed = planned_keys & watered_after
            if confirmed:
                succeeded += len(confirmed)
                finished_positions.update(confirmed)
                pending -= confirmed
            if use.get("status") == "unknown" and confirmed != planned_keys:
                raise SkillAbort("watering_unknown", "浇水动作结果未知且后置条件未完全确认", "unknown_outcome", "unknown")
            if use.get("status") != "succeeded" and use.get("status") != "unknown":
                if _tool_stop_reason(use) == "cancelled":
                    stop_reason = "cancelled"
                    break
                failed += len(planned_keys - confirmed)
                pending -= planned_keys - confirmed
                deferred_reason = deferred_reason or "action_failed"
            elif not confirmed:
                failed += len(planned_keys)
                stop_reason = "no_progress"
                break

            current_runtime = await _runtime(ctx, deadline)
            if _date(current_runtime) != _date(before):
                stop_reason = "date_changed"
                break

        if stop_reason == "completed" and target_total > max_targets:
            stop_reason = "target_limit"
        elif stop_reason == "completed" and deferred_reason:
            stop_reason = deferred_reason
    except SkillAbort as caught:
        if caught.stop_reason in {"deadline", "action_limit"}:
            stop_reason = caught.stop_reason
        else:
            error = caught
            outcome = caught.outcome
            stop_reason = caught.stop_reason
    except asyncio.CancelledError:
        cancelled = True
        stop_reason = "cancelled"
        if tracking["cancelledDuringMutation"] or tracking["postconditionPending"]:
            outcome = "unknown"
            error = SkillAbort(
                "skill_cancelled_unknown_outcome",
                "取消发生在变更 Tool 执行期间，最后动作结果未知",
                "cancelled",
                "unknown",
            )

    if cancelled or stop_reason in {"date_changed", "unknown_outcome", "deadline"}:
        unresolved = initial_positions - finished_positions
        remaining = [{"position": _position(key)} for key in sorted(unresolved, key=_position_sort)]
    else:
        final_world = await _safe_world(ctx, area)
        remaining = _unwatered_crops(final_world, area) if final_world else []
        current_runtime = await _safe_runtime(ctx) or current_runtime or before
        current_inventory = await _safe_inventory(ctx) or current_inventory or initial_inventory
        if remaining and stop_reason in {"completed", "no_targets"}:
            stop_reason = deferred_reason or "targets_remain"
    output = _output(
        area=area,
        before=before,
        after=current_runtime,
        initial_inventory=initial_inventory,
        current_inventory=current_inventory,
        target_total=target_total,
        planned_target_count=min(target_total, max_targets),
        succeeded=succeeded,
        skipped=skipped,
        failed=failed,
        actions_used=actions_used,
        charged_actions=charged_actions,
        last_target=last_target,
        stop_reason=stop_reason,
        remaining=remaining,
        outcome=outcome,
    )
    if error is not None:
        return {
            "status": outcome,
            "error": {"code": error.code, "message": error.message, "retryable": error.retryable},
            "output": output,
        }
    return {"status": "succeeded", "output": output}


async def _runtime(ctx, deadline: float | None = None) -> dict[str, Any]:
    result = await _read(ctx, "stardew_query_runtime", {}, deadline)
    if result.get("status") != "succeeded":
        outcome = "unknown" if result.get("status") == "unknown" else "failed"
        raise SkillAbort("runtime_query_failed", "无法取得游戏运行状态", "query_failed", outcome)
    return result["output"]["snapshot"]


async def _inventory(ctx, deadline: float | None = None) -> dict[str, Any]:
    result = await _read(
        ctx,
        "stardew_query_inventory",
        {"playerInventory": {}, "includeEmptySlots": True},
        deadline,
    )
    if result.get("status") != "succeeded":
        outcome = "unknown" if result.get("status") == "unknown" else "failed"
        raise SkillAbort("inventory_query_failed", "无法取得玩家背包", "query_failed", outcome)
    return result["output"]["snapshot"]


async def _world(ctx, area: dict[str, Any], deadline: float | None = None) -> dict[str, Any]:
    snapshot = await _world_piece(ctx, area, deadline)
    if not snapshot.get("entitiesTruncated") and not snapshot.get("charactersTruncated"):
        return snapshot
    if area["width"] == 1 and area["height"] == 1:
        raise SkillAbort("scope_truncated", "单格查询仍被截断，无法取得完整安全事实", "scope_truncated")
    first_area, second_area = _split_area(area)
    first = await _world(ctx, first_area, deadline)
    second = await _world(ctx, second_area, deadline)
    return _merge_world(area, [first, second])


async def _world_with_stand_margin(
    ctx,
    area: dict[str, Any],
    deadline: float | None = None,
) -> dict[str, Any]:
    pieces = [area]
    if area["y"] > 0:
        pieces.append({**area, "y": area["y"] - 1, "height": 1})
    pieces.append({**area, "y": area["y"] + area["height"], "height": 1})
    if area["x"] > 0:
        pieces.append({**area, "x": area["x"] - 1, "width": 1})
    pieces.append({**area, "x": area["x"] + area["width"], "width": 1})
    return _merge_world(
        area,
        [await _world(ctx, piece, deadline) for piece in pieces],
    )


def _merge_world(area: dict[str, Any], snapshots: list[dict[str, Any]]) -> dict[str, Any]:
    tiles = {
        _key(item["position"]): item
        for snapshot in snapshots
        for item in snapshot.get("tiles", [])
    }
    entities = {
        item.get("ref", {}).get("value", f"{item.get('kind')}:{_key(item['position'])}"): item
        for snapshot in snapshots
        for item in snapshot.get("entities", [])
    }
    characters = {
        item.get("ref", {}).get("value", f"character:{_key(item['position'])}"): item
        for snapshot in snapshots
        for item in snapshot.get("characters", [])
    }
    first = snapshots[0]
    last = snapshots[-1]
    return {
        "worldRevision": last.get("worldRevision", first.get("worldRevision", "")),
        "area": area,
        "outdoors": bool(first.get("outdoors")),
        "tiles": list(tiles.values()),
        "entities": list(entities.values()),
        "characters": list(characters.values()),
        "entitiesTruncated": False,
        "charactersTruncated": False,
    }


async def _world_piece(ctx, area: dict[str, Any], deadline: float | None) -> dict[str, Any]:
    result = await _read(
        ctx,
        "stardew_query_world",
        {
            "area": area,
            "includeTiles": True,
            "includeEntities": True,
            "includeCharacters": True,
            "maxEntities": 512,
            "maxCharacters": 512,
        },
        deadline,
    )
    if result.get("status") != "succeeded":
        outcome = "unknown" if result.get("status") == "unknown" else "failed"
        raise SkillAbort("world_query_failed", "无法取得浇水范围事实", "query_failed", outcome)
    return result["output"]["snapshot"]


def _split_area(area: dict[str, Any]) -> tuple[dict[str, Any], dict[str, Any]]:
    if area["width"] >= area["height"] and area["width"] > 1:
        first_width = area["width"] // 2
        return (
            {**area, "width": first_width},
            {**area, "x": area["x"] + first_width, "width": area["width"] - first_width},
        )
    first_height = area["height"] // 2
    return (
        {**area, "height": first_height},
        {**area, "y": area["y"] + first_height, "height": area["height"] - first_height},
    )


async def _inspect_crops(ctx, entities: tuple[dict[str, Any], ...], deadline: float) -> set[tuple[str, int, int]]:
    result = await _read(ctx, "stardew_inspect", {"refs": [entity["ref"] for entity in entities]}, deadline)
    if result.get("status") != "succeeded":
        if result.get("status") == "unknown":
            raise SkillAbort("inspect_unknown", "动作前目标事实未知", "unknown_outcome", "unknown")
        return set()
    resolved: set[tuple[str, int, int]] = set()
    for item in result["output"].get("items", []):
        entity = item.get("worldEntity")
        crop = entity.get("crop") if entity else None
        if item.get("resolution", {}).get("status") == "resolved" and crop and not crop.get("dead") and not crop.get("watered"):
            resolved.add(_key(entity["position"]))
    return resolved


async def _inspect_watered(ctx, entities: tuple[dict[str, Any], ...], deadline: float) -> set[tuple[str, int, int]]:
    try:
        result = await _read(ctx, "stardew_inspect", {"refs": [entity["ref"] for entity in entities]}, deadline)
    except SkillAbort as error:
        raise SkillAbort(
            "postcondition_unavailable",
            "浇水后的目标事实不可用，最后动作结果无法确认",
            "unknown_outcome",
            "unknown",
        ) from error
    if result.get("status") != "succeeded":
        raise SkillAbort(
            "postcondition_unavailable",
            "浇水后的目标事实不可用，最后动作结果无法确认",
            "unknown_outcome",
            "unknown",
        )
    watered: set[tuple[str, int, int]] = set()
    for item in result["output"].get("items", []):
        entity = item.get("worldEntity")
        crop = entity.get("crop") if entity else None
        if item.get("resolution", {}).get("status") == "resolved" and crop and crop.get("watered"):
            watered.add(_key(entity["position"]))
    return watered


async def _mutate(
    ctx, name: str, arguments: dict[str, Any], used: int, maximum: int,
    deadline: float, tracking: dict[str, bool],
):
    _ensure_deadline(deadline)
    if used >= maximum:
        raise SkillAbort("action_limit", "达到最大动作次数", "action_limit")
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise SkillAbort("skill_deadline", "达到本次 Skill Deadline", "deadline")
    try:
        async with asyncio.timeout(remaining):
            return await ctx.call_tool(name, arguments)
    except TimeoutError as caught:
        raise SkillAbort(
            "skill_deadline_unknown_outcome",
            "变更 Tool 在本次 Deadline 内没有返回，最后动作结果未知",
            "unknown_outcome",
            "unknown",
        ) from caught
    except asyncio.CancelledError:
        tracking["cancelledDuringMutation"] = True
        raise


async def _read(ctx, name: str, arguments: dict[str, Any], deadline: float | None):
    if deadline is None:
        return await ctx.call_tool(name, arguments)
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise SkillAbort("skill_deadline", "达到本次 Skill Deadline", "deadline")
    try:
        async with asyncio.timeout(remaining):
            return await ctx.call_tool(name, arguments)
    except TimeoutError as caught:
        raise SkillAbort("skill_deadline", "达到本次 Skill Deadline", "deadline") from caught


def _ensure_ready(runtime: dict[str, Any], area: dict[str, Any]) -> None:
    if runtime.get("ui", {}).get("menuOpen"):
        raise SkillAbort("menu_open", "开始浇水前存在未处理菜单", "not_ready")
    if not runtime.get("player", {}).get("canMove", False):
        raise SkillAbort("player_not_ready", "玩家当前不可操作", "not_ready", retryable=True)
    if not _same_location(runtime.get("player", {}).get("position", {}).get("locationId"), area["locationId"]):
        raise SkillAbort("scope_not_current", "浇水范围必须位于玩家当前地图", "not_ready")


def _watering_can(inventory: dict[str, Any]) -> dict[str, Any] | None:
    candidates = []
    for slot in inventory.get("slots", []):
        item = slot.get("item")
        if not item or "ref" not in item:
            continue
        if item.get("toolKind") == "watering_can" or (
            "waterRemaining" in item and "waterCapacity" in item
        ):
            candidates.append(item)
    return candidates[0] if len(candidates) == 1 else None


def _unwatered_crops(snapshot: dict[str, Any] | None, area: dict[str, Any]) -> list[dict[str, Any]]:
    if not snapshot:
        return []
    result = []
    for entity in snapshot.get("entities", []):
        crop = entity.get("crop")
        position = entity.get("position", {})
        if (
            entity.get("kind") == "crop"
            and crop
            and not crop.get("dead", False)
            and not crop.get("watered", False)
            and _in_area(position, area)
        ):
            result.append(entity)
    return sorted(result, key=lambda item: _position_sort(_key(item["position"])))


def _choose_plan(
    entities: dict[tuple[str, int, int], dict[str, Any]],
    world: dict[str, Any],
    player_position: dict[str, Any],
    max_charge: int,
    watering_can: dict[str, Any],
) -> WaterPlan | None:
    tiles = {_key(tile["position"]): tile for tile in world.get("tiles", [])}
    blocked = {
        _key(entity["position"])
        for entity in world.get("entities", [])
        if entity.get("kind") != "crop" and entity.get("position")
    }
    blocked.update(
        _key(character["position"])
        for character in world.get("characters", [])
        if character.get("position")
    )
    water_left = watering_can.get("waterRemaining")
    bottomless = watering_can.get("bottomless", False)
    candidates: list[WaterPlan] = []
    for key in sorted(entities, key=_position_sort):
        location, x, y = key
        for direction, dx, dy in DIRECTIONS:
            stand_key = (location, x - dx, y - dy)
            tile = tiles.get(stand_key)
            if not tile or not tile.get("passable", False):
                continue
            if stand_key in blocked and not _same_key(stand_key, _key(player_position)):
                continue
            for charge in range(max_charge, -1, -1):
                affected = _affected(location, x, y, direction, charge)
                if not all(position in entities for position in affected):
                    continue
                if any(position in blocked for position in affected):
                    continue
                cost = charge + 1
                if water_left is not None and not bottomless and int(water_left) < cost:
                    continue
                plan_entities = tuple(entities[position] for position in affected)
                candidates.append(
                    WaterPlan(
                        stand=_position(stand_key),
                        target={"locationId": location, "x": x, "y": y},
                        direction=direction,
                        charge_level=charge,
                        entities=plan_entities,
                    )
                )
                break
    if not candidates:
        return None
    return min(
        candidates,
        key=lambda plan: (
            -len(plan.entities),
            -plan.charge_level,
            plan.target["y"],
            plan.target["x"],
            next(index for index, item in enumerate(DIRECTIONS) if item[0] == plan.direction),
        ),
    )


def _affected(location: str, x: int, y: int, direction: str, charge: int) -> tuple[tuple[str, int, int], ...]:
    _, dx, dy = next(item for item in DIRECTIONS if item[0] == direction)
    perpendicular = (-dy, dx)
    if charge == 0:
        offsets = [(0, 0)]
    elif charge == 1:
        offsets = [(distance, 0) for distance in range(3)]
    elif charge == 2:
        offsets = [(distance, 0) for distance in range(5)]
    elif charge == 3:
        offsets = [(forward, side) for forward in range(3) for side in (-1, 0, 1)]
    elif charge == 4:
        offsets = [(forward, side) for forward in range(6) for side in (-1, 0, 1)]
    else:
        offsets = [(forward, side) for forward in range(5) for side in range(-2, 3)]
    return tuple(
        (location, x + dx * forward + perpendicular[0] * side, y + dy * forward + perpendicular[1] * side)
        for forward, side in offsets
    )


def _output(**state: Any) -> dict[str, Any]:
    before = state["before"] or {}
    after = state["after"] or before
    initial_can = _watering_can(state["initial_inventory"] or {})
    final_can = _watering_can(state["current_inventory"] or {})
    remaining = [_normalized_position(entity["position"]) for entity in state["remaining"]]
    final_status = _final_status(state["outcome"], state["stop_reason"], remaining, state["succeeded"])
    resumable, hint = _resume(state["stop_reason"], len(remaining))
    if state["outcome"] == "unknown":
        resumable = False
        hint = "最后一次变更结果未知；必须先重新查询事实，且不得自动重放最后动作。"
    return {
        "finalStatus": final_status,
        "area": state["area"],
        "dateBefore": before.get("date"),
        "dateAfter": after.get("date"),
        "targetTotal": state["target_total"],
        "plannedTargetCount": state["planned_target_count"],
        "succeededCount": state["succeeded"],
        "skippedCount": state["skipped"],
        "failedCount": state["failed"],
        "actionsUsed": state["actions_used"],
        "chargedActions": state["charged_actions"],
        "waterBefore": initial_can.get("waterRemaining") if initial_can else None,
        "waterAfter": final_can.get("waterRemaining") if final_can else None,
        "energyBefore": float(before.get("player", {}).get("energy", 0)),
        "energyAfter": float(after.get("player", {}).get("energy", 0)),
        "lastPosition": _normalized_position(after.get("player", {}).get("position")) if after else None,
        "lastTarget": _normalized_position(state["last_target"]) if state["last_target"] else None,
        "stopReason": state["stop_reason"],
        "resumable": resumable,
        "resumeHint": hint,
        "remainingCount": len(remaining),
        "remainingTargets": remaining[:128],
        "remainingTargetsTruncated": len(remaining) > 128,
    }


def _final_status(outcome: str, reason: str, remaining: list[Any], succeeded: int) -> str:
    if outcome == "unknown":
        return "unknown"
    if reason in {"completed", "no_targets"} and not remaining:
        return "completed"
    return "partial" if succeeded else "stopped"


def _resume(reason: str, remaining: int) -> tuple[bool, str]:
    if reason in {"completed", "no_targets"}:
        return False, "指定范围内没有剩余未浇水作物。"
    if reason in {"date_changed", "unknown_outcome"}:
        return False, "日期或副作用边界已经变化；请先重新查询，再开始新的任务。"
    return True, f"仍观察到 {remaining} 个未浇水目标；解决停止原因后可对同一范围重新调用。"


def _tool_stop_reason(result: dict[str, Any]) -> str:
    return "cancelled" if result.get("error", {}).get("code") == "command_cancelled" else "action_failed"


def _ensure_deadline(deadline: float) -> None:
    if time.monotonic() >= deadline:
        raise SkillAbort("skill_deadline", "达到本次 Skill Deadline", "deadline")


def _date(runtime: dict[str, Any]) -> tuple[Any, Any, Any]:
    date = runtime.get("date", {})
    return date.get("year"), date.get("season"), date.get("dayOfMonth")


def _key(position: dict[str, Any]) -> tuple[str, int, int]:
    return position.get("locationId", ""), int(position.get("x", 0)), int(position.get("y", 0))


def _position(key: tuple[str, int, int]) -> dict[str, Any]:
    return {"locationId": key[0], "x": key[1], "y": key[2]}


def _normalized_position(position: dict[str, Any] | None) -> dict[str, Any] | None:
    return _position(_key(position)) if position else None


def _position_sort(key: tuple[str, int, int]) -> tuple[str, int, int]:
    return key[0], key[2], key[1]


def _in_area(position: dict[str, Any], area: dict[str, Any]) -> bool:
    return (
        _same_location(position.get("locationId"), area["locationId"])
        and area["x"] <= int(position.get("x", 0)) < area["x"] + area["width"]
        and area["y"] <= int(position.get("y", 0)) < area["y"] + area["height"]
    )


def _same_location(left: Any, right: Any) -> bool:
    return isinstance(left, str) and isinstance(right, str) and left.casefold() == right.casefold()


def _same_key(left: tuple[str, int, int], right: tuple[str, int, int]) -> bool:
    return _same_location(left[0], right[0]) and left[1:] == right[1:]


async def _safe_runtime(ctx):
    try:
        return await _runtime(ctx)
    except SkillAbort:
        return None


async def _safe_inventory(ctx):
    try:
        return await _inventory(ctx)
    except SkillAbort:
        return None


async def _safe_world(ctx, area):
    try:
        return await _world(ctx, area)
    except SkillAbort:
        return None
