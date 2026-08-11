"""在一次受限 Skill 调用中完成指定范围的成熟作物收获。"""

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


async def run(ctx, arguments: dict[str, Any]) -> dict[str, Any]:
    area = dict(arguments["area"])
    max_targets = int(arguments.get("maxTargets", 128))
    max_actions = int(arguments.get("maxActions", 384))
    min_energy = float(arguments.get("minEnergy", 5))
    stop_time = int(arguments.get("stopTime", 2500))
    deadline = time.monotonic() + float(arguments.get("timeoutSeconds", 300))

    before: dict[str, Any] | None = None
    current_runtime: dict[str, Any] | None = None
    initial_inventory: dict[str, Any] | None = None
    current_inventory: dict[str, Any] | None = None
    target_total = succeeded = interacted = scythed = skipped = failed = actions_used = 0
    initial_positions: set[tuple[str, int, int]] = set()
    finished_positions: set[tuple[str, int, int]] = set()
    pending: set[tuple[str, int, int]] = set()
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
        initial_world = await _world(ctx, area, deadline)
        initial_targets = _mature_crops(initial_world, area)
        target_total = len(initial_targets)
        initial_positions = {_key(entity["position"]) for entity in initial_targets}
        pending = set(
            sorted(initial_positions, key=_position_sort)[:max_targets]
        )
        if not pending:
            stop_reason = "no_targets"

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

            world = await _world(ctx, area, deadline)
            current = {_key(entity["position"]): entity for entity in _mature_crops(world, area)}
            externally_completed = pending - current.keys()
            if externally_completed:
                skipped += len(externally_completed)
                finished_positions.update(externally_completed)
                pending -= externally_completed
            if not pending:
                break

            key = min(pending, key=_position_sort)
            entity = current[key]
            last_target = _normalized_position(entity["position"])
            action = entity.get("crop", {}).get("harvestAction")
            if action not in {"interact", "scythe"}:
                failed += 1
                pending.remove(key)
                deferred_reason = deferred_reason or "unsupported_harvest_action"
                continue

            inspected = await _inspect_crop(ctx, entity, deadline)
            if inspected is None:
                stop_reason = "target_changed"
                break
            if not _is_mature(inspected):
                skipped += 1
                finished_positions.add(key)
                pending.remove(key)
                continue

            scythe = None
            if action == "scythe":
                current_inventory = await _inventory(ctx, deadline)
                scythe = _scythe(current_inventory)
                if scythe is None:
                    failed += 1
                    pending.remove(key)
                    deferred_reason = deferred_reason or "scythe_missing"
                    continue

            current_position = current_runtime["player"]["position"]
            if _manhattan(_key(current_position), key) != 1:
                navigation_arguments = {"targetRef": entity["ref"], "arrival": "adjacent"}
                if _manhattan(_key(current_position), key) == 0:
                    crop_positions = {
                        _key(item["position"])
                        for item in world.get("entities", [])
                        if item.get("kind") == "crop" and item.get("position")
                    }
                    known_stands = [
                        stand
                        for stand in crop_positions | finished_positions
                        if _manhattan(stand, key) == 1
                    ]
                    if known_stands:
                        stand = min(known_stands, key=_position_sort)
                        navigation_arguments = {
                            "position": _position(stand),
                            "arrival": "exact",
                            "faceOnArrival": _facing(stand, key),
                        }
                navigation = await _mutate(
                    ctx,
                    "stardew_navigate",
                    navigation_arguments,
                    actions_used,
                    max_actions,
                    deadline,
                    tracking,
                )
                actions_used += 1
                if navigation.get("status") == "unknown":
                    position = (await _runtime(ctx, deadline))["player"]["position"]
                    if _manhattan(_key(position), key) != 1:
                        raise SkillAbort("navigation_unknown", "导航结果未知，禁止提交收获动作", "unknown_outcome", "unknown")
                elif navigation.get("status") != "succeeded":
                    if _tool_stop_reason(navigation) == "cancelled":
                        stop_reason = "cancelled"
                        break
                    failed += 1
                    pending.remove(key)
                    deferred_reason = deferred_reason or "navigation_failed"
                    continue

            current_runtime = await _runtime(ctx, deadline)
            if _date(current_runtime) != _date(before):
                stop_reason = "date_changed"
                break
            inspected = await _inspect_crop(ctx, entity, deadline)
            if inspected is None or not _is_mature(inspected):
                stop_reason = "target_changed"
                break

            if action == "scythe":
                equip = await _mutate(
                    ctx,
                    "stardew_equip",
                    {
                        "itemRef": scythe["ref"],
                        "inventoryRevision": current_inventory["inventoryRevision"],
                    },
                    actions_used,
                    max_actions,
                    deadline,
                    tracking,
                )
                actions_used += 1
                if equip.get("status") == "unknown":
                    raise SkillAbort("equip_unknown", "装备镰刀结果未知，禁止继续动作", "unknown_outcome", "unknown")
                if equip.get("status") != "succeeded":
                    if _tool_stop_reason(equip) == "cancelled":
                        stop_reason = "cancelled"
                        break
                    failed += 1
                    pending.remove(key)
                    continue
                tracking["postconditionPending"] = True
                mutation = await _mutate(
                    ctx,
                    "stardew_use_tool",
                    {"targetRef": entity["ref"], "chargeLevel": 0},
                    actions_used,
                    max_actions,
                    deadline,
                    tracking,
                )
                actions_used += 1
            else:
                tracking["postconditionPending"] = True
                mutation = await _mutate(
                    ctx,
                    "stardew_interact",
                    {"targetRef": entity["ref"]},
                    actions_used,
                    max_actions,
                    deadline,
                    tracking,
                )
                actions_used += 1
            current_inventory = None

            await _inspect_crop_after(ctx, entity, deadline)
            completed_positions = await _completed_positions(ctx, area, pending, deadline)
            tracking["postconditionPending"] = False
            if key in completed_positions:
                succeeded += len(completed_positions)
                if action == "interact":
                    interacted += len(completed_positions)
                else:
                    scythed += len(completed_positions)
                finished_positions.update(completed_positions)
                pending -= completed_positions
            elif mutation.get("status") == "unknown":
                raise SkillAbort("harvest_unknown", "收获结果未知且目标仍成熟，禁止重放", "unknown_outcome", "unknown")
            elif mutation.get("status") != "succeeded":
                if _tool_stop_reason(mutation) == "cancelled":
                    stop_reason = "cancelled"
                    break
                failed += 1
                pending.remove(key)
            else:
                failed += 1
                stop_reason = "no_progress"
                break

            current_runtime = await _runtime(ctx, deadline)
            if _date(current_runtime) != _date(before):
                stop_reason = "date_changed"
                break
            current_inventory = await _inventory(ctx, deadline)

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
        remaining = _mature_crops(final_world, area) if final_world else []
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
        interacted=interacted,
        scythed=scythed,
        skipped=skipped,
        failed=failed,
        actions_used=actions_used,
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
    if not snapshot.get("entitiesTruncated"):
        return snapshot
    if area["width"] == 1 and area["height"] == 1:
        raise SkillAbort("scope_truncated", "单格查询仍被截断，无法取得完整成熟作物集合", "scope_truncated")
    first_area, second_area = _split_area(area)
    first = await _world(ctx, first_area, deadline)
    second = await _world(ctx, second_area, deadline)
    entities = {
        item.get("ref", {}).get("value", f"{item.get('kind')}:{_key(item['position'])}"): item
        for item in [*first.get("entities", []), *second.get("entities", [])]
    }
    return {
        "worldRevision": second.get("worldRevision", first.get("worldRevision", "")),
        "area": area,
        "outdoors": bool(first.get("outdoors") and second.get("outdoors")),
        "tiles": [],
        "entities": list(entities.values()),
        "characters": [],
        "entitiesTruncated": False,
        "charactersTruncated": False,
    }


async def _world_piece(ctx, area: dict[str, Any], deadline: float | None) -> dict[str, Any]:
    result = await _read(
        ctx,
        "stardew_query_world",
        {
            "area": area,
            "entityKinds": ["crop"],
            "includeTiles": False,
            "includeEntities": True,
            "includeCharacters": False,
            "maxEntities": 512,
        },
        deadline,
    )
    if result.get("status") != "succeeded":
        outcome = "unknown" if result.get("status") == "unknown" else "failed"
        raise SkillAbort("world_query_failed", "无法取得收获范围事实", "query_failed", outcome)
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


async def _inspect_crop(ctx, entity: dict[str, Any], deadline: float) -> dict[str, Any] | None:
    result = await _read(ctx, "stardew_inspect", {"refs": [entity["ref"]]}, deadline)
    if result.get("status") == "unknown":
        raise SkillAbort("inspect_unknown", "动作前目标事实未知", "unknown_outcome", "unknown")
    if result.get("status") != "succeeded":
        return None
    items = result["output"].get("items", [])
    if len(items) != 1 or items[0].get("resolution", {}).get("status") != "resolved":
        return None
    return items[0].get("worldEntity")


async def _inspect_crop_after(ctx, entity: dict[str, Any], deadline: float) -> None:
    try:
        result = await _read(ctx, "stardew_inspect", {"refs": [entity["ref"]]}, deadline)
    except SkillAbort as error:
        raise SkillAbort(
            "postcondition_unavailable",
            "收获后的 Ref 事实不可用，最后动作结果无法确认",
            "unknown_outcome",
            "unknown",
        ) from error
    if result.get("status") != "succeeded":
        raise SkillAbort(
            "postcondition_unavailable",
            "收获后的 Ref 事实不可用，最后动作结果无法确认",
            "unknown_outcome",
            "unknown",
        )


async def _completed_positions(
    ctx, area: dict[str, Any], pending: set[tuple[str, int, int]], deadline: float,
) -> set[tuple[str, int, int]]:
    try:
        world = await _world(ctx, area, deadline)
    except SkillAbort as error:
        raise SkillAbort(
            "postcondition_unavailable",
            "收获后的范围事实不可用，最后动作结果无法确认",
            "unknown_outcome",
            "unknown",
        ) from error
    remaining = {_key(entity["position"]) for entity in _mature_crops(world, area)}
    return pending - remaining


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
        raise SkillAbort("menu_open", "开始收获前存在未处理菜单", "not_ready")
    if not runtime.get("player", {}).get("canMove", False):
        raise SkillAbort("player_not_ready", "玩家当前不可操作", "not_ready", retryable=True)
    if not _same_location(runtime.get("player", {}).get("position", {}).get("locationId"), area["locationId"]):
        raise SkillAbort("scope_not_current", "收获范围必须位于玩家当前地图", "not_ready")


def _mature_crops(snapshot: dict[str, Any] | None, area: dict[str, Any]) -> list[dict[str, Any]]:
    if not snapshot:
        return []
    return sorted(
        (
            entity
            for entity in snapshot.get("entities", [])
            if _is_mature(entity) and _in_area(entity.get("position", {}), area)
        ),
        key=lambda item: _position_sort(_key(item["position"])),
    )


def _is_mature(entity: dict[str, Any] | None) -> bool:
    crop = entity.get("crop") if entity else None
    return bool(entity and entity.get("kind") == "crop" and crop and crop.get("readyForHarvest") and not crop.get("dead"))


def _scythe(inventory: dict[str, Any]) -> dict[str, Any] | None:
    candidates = [
        (int(slot.get("index", 0)), slot["item"])
        for slot in inventory.get("slots", [])
        if slot.get("item", {}).get("toolKind") == "scythe" and "ref" in slot["item"]
    ]
    if not candidates:
        return None
    return min(candidates, key=lambda candidate: (candidate[0], candidate[1].get("qualifiedItemId", "")))[1]


def _inventory_totals(snapshot: dict[str, Any] | None) -> dict[tuple[str, int], int]:
    totals: dict[tuple[str, int], int] = {}
    for slot in (snapshot or {}).get("slots", []):
        item = slot.get("item")
        if not item or item.get("tool"):
            continue
        key = (item.get("qualifiedItemId", ""), int(item.get("quality", 0)))
        totals[key] = totals.get(key, 0) + int(item.get("stack", 0))
    return totals


def _inventory_changes(before: dict[str, Any] | None, after: dict[str, Any] | None) -> list[dict[str, Any]]:
    if before is None or after is None:
        return []
    before_totals = _inventory_totals(before)
    after_totals = _inventory_totals(after)
    changes = []
    for key in sorted(before_totals.keys() | after_totals.keys()):
        old = before_totals.get(key, 0)
        new = after_totals.get(key, 0)
        if old == new:
            continue
        changes.append({
            "qualifiedItemId": key[0],
            "quality": key[1],
            "before": old,
            "after": new,
            "delta": new - old,
        })
    return changes[:128]


def _output(**state: Any) -> dict[str, Any]:
    before = state["before"] or {}
    after = state["after"] or before
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
        "interactCount": state["interacted"],
        "scytheCount": state["scythed"],
        "skippedCount": state["skipped"],
        "failedCount": state["failed"],
        "actionsUsed": state["actions_used"],
        "energyBefore": float(before.get("player", {}).get("energy", 0)),
        "energyAfter": float(after.get("player", {}).get("energy", 0)),
        "lastPosition": _normalized_position(after.get("player", {}).get("position")) if after else None,
        "lastTarget": state["last_target"],
        "stopReason": state["stop_reason"],
        "resumable": resumable,
        "resumeHint": hint,
        "remainingCount": len(remaining),
        "remainingTargets": remaining[:256],
        "remainingTargetsTruncated": len(remaining) > 256,
        "inventoryChanges": _inventory_changes(state["initial_inventory"], state["current_inventory"]),
    }


def _final_status(outcome: str, reason: str, remaining: list[Any], succeeded: int) -> str:
    if outcome == "unknown":
        return "unknown"
    if reason in {"completed", "no_targets"} and not remaining:
        return "completed"
    return "partial" if succeeded else "stopped"


def _resume(reason: str, remaining: int) -> tuple[bool, str]:
    if reason in {"completed", "no_targets"}:
        return False, "指定范围内没有剩余成熟作物。"
    if reason in {"date_changed", "unknown_outcome"}:
        return False, "日期或副作用边界已经变化；请先重新查询，再开始新的任务。"
    return True, f"仍观察到 {remaining} 个成熟目标；解决停止原因后可对同一范围重新调用。"


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


def _normalized_position(position: dict[str, Any] | None) -> dict[str, Any] | None:
    if not position:
        return None
    location, x, y = _key(position)
    return {"locationId": location, "x": x, "y": y}


def _position_sort(key: tuple[str, int, int]) -> tuple[str, int, int]:
    return key[0], key[2], key[1]


def _manhattan(left: tuple[str, int, int], right: tuple[str, int, int]) -> int:
    return 1_000_000 if not _same_location(left[0], right[0]) else abs(left[1] - right[1]) + abs(left[2] - right[2])


def _facing(stand: tuple[str, int, int], target: tuple[str, int, int]) -> str:
    delta = target[1] - stand[1], target[2] - stand[2]
    return {(0, -1): "up", (1, 0): "right", (0, 1): "down", (-1, 0): "left"}[delta]


def _in_area(position: dict[str, Any], area: dict[str, Any]) -> bool:
    return (
        _same_location(position.get("locationId"), area["locationId"])
        and area["x"] <= int(position.get("x", 0)) < area["x"] + area["width"]
        and area["y"] <= int(position.get("y", 0)) < area["y"] + area["height"]
    )


def _same_location(left: Any, right: Any) -> bool:
    return isinstance(left, str) and isinstance(right, str) and left.casefold() == right.casefold()


def _position(key: tuple[str, int, int]) -> dict[str, Any]:
    return {"locationId": key[0], "x": key[1], "y": key[2]}


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
