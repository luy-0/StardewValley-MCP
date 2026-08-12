"""在当前地图查找最近可达水源并装满唯一喷壶。"""

from __future__ import annotations

import asyncio
import time
from collections import deque
from dataclasses import dataclass
from typing import Any


DIRECTIONS = (("up", 0, -1), ("right", 1, 0), ("down", 0, 1), ("left", -1, 0))
CHUNK_SIZE = 32


@dataclass(frozen=True)
class Candidate:
    distance: int
    source: tuple[str, int, int]
    stand: tuple[str, int, int]
    facing: str


@dataclass
class SkillAbort(Exception):
    code: str
    message: str
    stop_reason: str
    outcome: str = "failed"
    retryable: bool = False


async def run(ctx, arguments: dict[str, Any]) -> dict[str, Any]:
    deadline = time.monotonic() + float(arguments.get("timeoutSeconds", 120))
    max_scan_tiles = int(arguments.get("maxScanTiles", 16384))
    state: dict[str, Any] = {
        "water_before": None,
        "water_after": None,
        "capacity": None,
        "selected_source": None,
        "selected_stand": None,
        "last_position": None,
        "candidate_count": 0,
        "unreachable_count": 0,
        "scanned_tile_count": 0,
        "navigation_attempts": 0,
        "actions_used": 0,
        "last_confirmed_state": "unavailable",
    }
    tracking = {"mutation": False, "mutation_uncertain": False, "postcondition_pending": False}

    try:
        runtime = await _runtime(ctx, deadline)
        player = runtime.get("player", {})
        position = _position(player.get("position"))
        state["last_position"] = position
        if runtime.get("ui", {}).get("menuOpen") or not player.get("canMove", False) or position is None:
            raise SkillAbort("not_ready", "玩家当前不可执行补水任务", "not_ready", retryable=True)

        inventory = await _inventory(ctx, deadline)
        watering_can = _unique_watering_can(inventory)
        if watering_can is None:
            raise SkillAbort("watering_can_missing", "背包中必须恰好存在一个公开喷壶", "watering_can_missing")
        state["water_before"] = int(watering_can["waterRemaining"])
        state["water_after"] = state["water_before"]
        state["capacity"] = int(watering_can["waterCapacity"])
        state["last_confirmed_state"] = _can_state(watering_can)
        if state["water_before"] == state["capacity"]:
            return _result("succeeded", "already_full", state)

        tiles = await _scan_current_location(ctx, position["locationId"], max_scan_tiles, deadline)
        state["scanned_tile_count"] = len(tiles)
        if any(
            "wateringCanRefillable" not in tile or "pathfindingBlocked" not in tile
            for tile in tiles.values()
        ):
            raise SkillAbort(
                "refillability_unavailable",
                "当前 Mod 尚未显式提供补水与原生寻路地块事实，请升级 Mod 后重试",
                "query_failed",
            )
        refillable = [key for key, tile in tiles.items() if tile.get("wateringCanRefillable", False)]
        if not refillable:
            raise SkillAbort("water_source_not_found", "当前地图没有公开可确认的喷壶补水地块", "water_source_not_found")

        candidates, unreachable = _rank_candidates(tiles, _key(position), refillable)
        state["candidate_count"] = len(candidates) + unreachable
        state["unreachable_count"] = unreachable
        if not candidates:
            raise SkillAbort("water_source_unreachable", "当前地图的补水岸边站位全部不可达", "water_source_unreachable", retryable=True)

        equip = await _mutate(
            ctx,
            "stardew_equip",
            {"itemRef": watering_can["ref"], "inventoryRevision": inventory["inventoryRevision"]},
            deadline,
            tracking,
            state,
        )
        if equip.get("status") == "unknown":
            raise SkillAbort("equip_unknown", "装备喷壶结果未知，禁止继续动作", "refill_not_confirmed", "unknown")
        if equip.get("status") != "succeeded":
            if _cancelled(equip):
                raise SkillAbort("cancelled", "装备喷壶被取消", "cancelled")
            raise SkillAbort("equip_failed", "无法装备喷壶", "refill_not_confirmed")

        selected: Candidate | None = None
        for candidate in candidates:
            state["selected_source"] = _from_key(candidate.source)
            state["selected_stand"] = _from_key(candidate.stand)
            navigation = await _mutate(
                ctx,
                "stardew_navigate",
                {
                    "position": _from_key(candidate.stand),
                    "arrival": "exact",
                    "faceOnArrival": candidate.facing,
                },
                deadline,
                tracking,
                state,
                navigation=True,
            )
            if navigation.get("status") == "unknown":
                tracking["postcondition_pending"] = True
                current = await _runtime_after_unknown_navigation(ctx, deadline)
                tracking["postcondition_pending"] = False
                current_position = _position(current.get("player", {}).get("position"))
                state["last_position"] = current_position
                if current_position is None or _key(current_position) != candidate.stand:
                    raise SkillAbort("navigation_unknown", "导航结果未知且未确认到达岸边站位", "navigation_failed", "unknown")
                ctx.resolve_unknown_mutation("stardew_navigate")
            elif navigation.get("status") != "succeeded":
                if _cancelled(navigation):
                    raise SkillAbort("cancelled", "导航被取消", "cancelled")
                state["unreachable_count"] += 1
                confirmed = _position(
                    navigation.get("error", {}).get("details", {}).get("navigation", {}).get("lastConfirmedPosition")
                )
                if confirmed is not None:
                    state["last_position"] = confirmed
                continue
            selected = candidate
            state["last_position"] = _from_key(candidate.stand)
            break

        if selected is None:
            raise SkillAbort("navigation_failed", "所有可达候选的实际导航均失败", "navigation_failed", retryable=True)

        tracking["postcondition_pending"] = True
        use = await _mutate(
            ctx,
            "stardew_use_tool",
            {"position": _from_key(selected.source), "chargeLevel": 0},
            deadline,
            tracking,
            state,
        )
        final_inventory = await _inventory(ctx, deadline, postcondition=True)
        final_can = _unique_watering_can(final_inventory)
        tracking["postcondition_pending"] = False
        if final_can is not None:
            state["water_after"] = int(final_can["waterRemaining"])
            state["capacity"] = int(final_can["waterCapacity"])
            state["last_confirmed_state"] = _can_state(final_can)
        full = final_can is not None and int(final_can["waterRemaining"]) == int(final_can["waterCapacity"])
        if full:
            if use.get("status") == "unknown":
                ctx.resolve_unknown_mutation("stardew_use_tool")
            return _result("succeeded", "completed", state)
        if use.get("status") == "unknown":
            raise SkillAbort("refill_unknown", "补水动作结果未知且喷壶未确认装满", "refill_not_confirmed", "unknown")
        if _cancelled(use):
            raise SkillAbort("cancelled", "补水动作被取消", "cancelled")
        raise SkillAbort("refill_not_confirmed", "动作后未确认喷壶水量等于容量", "refill_not_confirmed", retryable=True)
    except SkillAbort as error:
        return _result(error.outcome, error.stop_reason, state, error)
    except asyncio.CancelledError:
        outcome = "unknown" if tracking["mutation_uncertain"] or tracking["postcondition_pending"] else "failed"
        error = SkillAbort(
            "cancelled",
            "补水任务已取消，最后变更可能无法确认" if outcome == "unknown" else "补水任务已取消",
            "cancelled",
            outcome,
        )
        return _result(outcome, "cancelled", state, error)


async def _runtime(ctx, deadline: float) -> dict[str, Any]:
    result = await _read(ctx, "stardew_query_runtime", {}, deadline)
    if result.get("status") != "succeeded":
        raise SkillAbort("runtime_query_failed", "无法读取游戏运行状态", "query_failed")
    return result["output"]["snapshot"]


async def _runtime_after_unknown_navigation(ctx, deadline: float) -> dict[str, Any]:
    try:
        return await _runtime(ctx, deadline)
    except SkillAbort as error:
        raise SkillAbort(
            "navigation_postcondition_unavailable",
            "导航结果未知，且无法通过独立只读查询确认最终位置",
            "navigation_failed",
            "unknown",
        ) from error


async def _inventory(ctx, deadline: float, *, postcondition: bool = False) -> dict[str, Any]:
    try:
        result = await _read(
            ctx,
            "stardew_query_inventory",
            {"playerInventory": {}, "includeEmptySlots": True},
            deadline,
        )
    except SkillAbort as error:
        if postcondition:
            raise SkillAbort("postcondition_unavailable", "补水后无法复查喷壶水量", "refill_not_confirmed", "unknown") from error
        raise
    if result.get("status") != "succeeded":
        outcome = "unknown" if postcondition or result.get("status") == "unknown" else "failed"
        raise SkillAbort("inventory_query_failed", "无法读取玩家背包", "query_failed" if not postcondition else "refill_not_confirmed", outcome)
    return result["output"]["snapshot"]


async def _scan_current_location(ctx, location_id: str, maximum: int, deadline: float) -> dict[tuple[str, int, int], dict[str, Any]]:
    tiles: dict[tuple[str, int, int], dict[str, Any]] = {}
    y = 0
    while True:
        row_height = CHUNK_SIZE
        x = 0
        while True:
            area = {"locationId": location_id, "x": x, "y": y, "width": CHUNK_SIZE, "height": CHUNK_SIZE}
            result = await _read(
                ctx,
                "stardew_query_world",
                {"area": area, "includeTiles": True, "includeEntities": False, "includeCharacters": False},
                deadline,
            )
            if result.get("status") != "succeeded":
                if _out_of_range(result):
                    if x == 0:
                        return tiles
                    break
                raise SkillAbort("world_query_failed", "无法完整扫描当前地图", "query_failed")
            snapshot = result["output"]["snapshot"]
            actual = snapshot["area"]
            row_height = min(row_height, int(actual["height"]))
            chunk_tiles = snapshot.get("tiles", [])
            if len(tiles) + len(chunk_tiles) > maximum:
                raise SkillAbort("scan_limit", "当前地图超过本次最大扫描 Tile 数", "scan_limit", retryable=True)
            for tile in chunk_tiles:
                tiles[_key(tile["position"])] = tile
            if int(actual["width"]) < CHUNK_SIZE:
                break
            x += CHUNK_SIZE
        if row_height < CHUNK_SIZE:
            return tiles
        y += CHUNK_SIZE


def _rank_candidates(
    tiles: dict[tuple[str, int, int], dict[str, Any]],
    start: tuple[str, int, int],
    refillable: list[tuple[str, int, int]],
) -> tuple[list[Candidate], int]:
    traversable = {
        key for key, tile in tiles.items()
        if not tile.get("pathfindingBlocked", True)
        and not tile.get("wateringCanRefillable", False)
    }
    standable = set(traversable)
    if start in tiles:
        traversable.add(start)
    distances: dict[tuple[str, int, int], int] = {}
    if start in traversable:
        distances[start] = 0
        queue = deque([start])
        while queue:
            current = queue.popleft()
            for _, dx, dy in DIRECTIONS:
                neighbor = (current[0], current[1] + dx, current[2] + dy)
                if neighbor in traversable and neighbor not in distances:
                    distances[neighbor] = distances[current] + 1
                    queue.append(neighbor)

    candidates: list[Candidate] = []
    unreachable = 0
    for source in sorted(refillable, key=lambda item: (item[2], item[1])):
        for facing, dx, dy in DIRECTIONS:
            stand = (source[0], source[1] - dx, source[2] - dy)
            tile = tiles.get(stand)
            if not tile or stand not in standable:
                continue
            if stand not in distances:
                unreachable += 1
                continue
            candidates.append(Candidate(distances[stand], source, stand, facing))
    candidates.sort(key=lambda item: (item.distance, item.source[2], item.source[1], item.stand[2], item.stand[1]))
    return candidates, unreachable


async def _read(ctx, name: str, arguments: dict[str, Any], deadline: float):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise SkillAbort("deadline", "达到本次 Skill Deadline", "deadline", retryable=True)
    try:
        async with asyncio.timeout(remaining):
            return await ctx.call_tool(name, arguments)
    except TimeoutError as error:
        raise SkillAbort("deadline", "达到本次 Skill Deadline", "deadline", retryable=True) from error


async def _mutate(
    ctx,
    name: str,
    arguments: dict[str, Any],
    deadline: float,
    tracking: dict[str, bool],
    state: dict[str, Any],
    *,
    navigation: bool = False,
):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise SkillAbort("deadline", "达到本次 Skill Deadline", "deadline", retryable=True)
    state["actions_used"] += 1
    if navigation:
        state["navigation_attempts"] += 1
    tracking["mutation"] = True
    try:
        async with asyncio.timeout(remaining):
            return await ctx.call_tool(name, arguments)
    except TimeoutError as error:
        raise SkillAbort("mutation_deadline", "变更 Tool 在 Deadline 内没有返回", "deadline", "unknown") from error
    except asyncio.CancelledError:
        tracking["mutation_uncertain"] = True
        raise
    finally:
        tracking["mutation"] = False


def _unique_watering_can(inventory: dict[str, Any]) -> dict[str, Any] | None:
    candidates = [
        slot["item"] for slot in inventory.get("slots", [])
        if slot.get("item", {}).get("toolKind") == "watering_can"
        and "ref" in slot["item"]
        and "waterRemaining" in slot["item"]
        and "waterCapacity" in slot["item"]
    ]
    return candidates[0] if len(candidates) == 1 else None


def _result(status: str, reason: str, state: dict[str, Any], error: SkillAbort | None = None) -> dict[str, Any]:
    completed = reason in {"completed", "already_full"}
    output = {
        "finalStatus": "completed" if completed else ("unknown" if status == "unknown" else "stopped"),
        "stopReason": reason,
        "resumable": reason in {"water_source_not_found", "water_source_unreachable", "navigation_failed", "refill_not_confirmed", "deadline", "scan_limit"} and status != "unknown",
        "resumeHint": _resume_hint(reason, status),
        "waterBefore": state["water_before"],
        "waterAfter": state["water_after"],
        "waterCapacity": state["capacity"],
        "selectedSource": state["selected_source"],
        "selectedStand": state["selected_stand"],
        "lastPosition": state["last_position"],
        "candidateCount": state["candidate_count"],
        "unreachableCount": state["unreachable_count"],
        "scannedTileCount": state["scanned_tile_count"],
        "navigationAttempts": state["navigation_attempts"],
        "actionsUsed": state["actions_used"],
        "lastConfirmedState": state["last_confirmed_state"],
    }
    if error is None:
        return {"status": "succeeded", "output": output}
    return {
        "status": status,
        "error": {
            "code": error.code,
            "message": error.message,
            # 发生过变更后只允许 Agent 根据结构化进度决定人工续跑，禁止 Host 自动重放。
            "retryable": error.retryable and state["actions_used"] == 0,
        },
        "output": output,
    }


def _resume_hint(reason: str, status: str) -> str:
    if reason in {"completed", "already_full"}:
        return "喷壶已经确认装满，无需续跑。"
    if status == "unknown":
        return "最后变更结果未知；先重新查询喷壶与玩家状态，禁止自动重放最后动作。"
    if reason == "water_source_not_found":
        return "当前地图没有可确认水源；切换地图后重新调用。"
    return "解决停止原因后可重新调用；Skill 会重新扫描并按当前事实规划。"


def _can_state(item: dict[str, Any]) -> str:
    return "full" if int(item["waterRemaining"]) == int(item["waterCapacity"]) else "not_full"


def _position(value: Any) -> dict[str, Any] | None:
    if not isinstance(value, dict) or not value.get("locationId"):
        return None
    return {"locationId": value["locationId"], "x": int(value.get("x", 0)), "y": int(value.get("y", 0))}


def _key(position: dict[str, Any]) -> tuple[str, int, int]:
    return position["locationId"], int(position["x"]), int(position["y"])


def _from_key(key: tuple[str, int, int]) -> dict[str, Any]:
    return {"locationId": key[0], "x": key[1], "y": key[2]}


def _out_of_range(result: dict[str, Any]) -> bool:
    code = str(result.get("error", {}).get("code", "")).lower()
    return code in {"out_of_range", "error_code_out_of_range"}


def _cancelled(result: dict[str, Any]) -> bool:
    return str(result.get("error", {}).get("code", "")).lower() in {"cancelled", "command_cancelled", "error_code_cancelled"}
