"""在一个连续 Skill 调用中完成回家、上床、确认睡眠和换日收敛。"""

from __future__ import annotations

import asyncio
import time
from dataclasses import dataclass
from typing import Any


@dataclass
class SkillAbort(Exception):
    code: str
    message: str
    phase: str
    retryable: bool = False
    details: dict[str, Any] | None = None
    outcome: str = "failed"


async def run(ctx, arguments: dict[str, Any]) -> dict[str, Any]:
    timeout_seconds = float(arguments.get("timeoutSeconds", 180))
    deadline = time.monotonic() + timeout_seconds
    progress: list[str] = []
    before: dict[str, Any] = {}
    bed: dict[str, Any] | None = None
    sleep_prompt_seen = False
    sleep_confirmed = False
    post_sleep_ui_steps = 0

    try:
        before = await _runtime(ctx, "preflight")
        if before.get("ui", {}).get("menuOpen"):
            raise SkillAbort("menu_open", "开始睡眠前存在未处理菜单", "preflight")
        if not before.get("player", {}).get("canMove", False):
            raise SkillAbort("player_not_ready", "玩家当前不可操作", "preflight", True)
        home_location_id = before.get("player", {}).get("homeLocationId")
        if not home_location_id:
            raise SkillAbort("home_not_found", "当前玩家没有可查询的住宅地图", "find_bed")

        progress.append("preflight")
        bed = await _find_bed(ctx, home_location_id, before["player"]["position"])
        progress.append("bed_selected")

        navigation = await ctx.call_tool(
            "stardew_navigate",
            {"position": bed["bed"]["sleepPosition"], "arrival": "exact"},
        )
        progress.append("navigate_submitted")

        prompt = await _wait_for_sleep_prompt(ctx, bed, deadline, attempts=10)
        if prompt is None:
            if navigation.get("status") == "unknown":
                raise SkillAbort(
                    "navigation_unknown",
                    "到床导航结果未知，且未观察到睡眠问题；禁止重放",
                    "navigate",
                    details={"navigation": navigation},
                    outcome="unknown",
                )
            await _fallback_interact(ctx, bed, deadline)
            prompt = await _wait_for_sleep_prompt(ctx, bed, deadline, attempts=10)

        if prompt is None:
            current = await _runtime(ctx, "wait_sleep_prompt")
            if _date(current) != _date(before):
                raise SkillAbort(
                    "passed_out_before_sleep_confirmation",
                    "未确认睡眠问题前日期已经变化，本轮是昏倒换日",
                    "wait_sleep_prompt",
                )
            raise SkillAbort("sleep_prompt_missing", "到达床位后没有出现睡眠问题", "wait_sleep_prompt", True)

        if navigation.get("status") == "unknown":
            # 睡眠问题与玩家在床状态共同构成独立只读后置事实，足以收敛导航终态。
            ctx.resolve_unknown_mutation("stardew_navigate")
        sleep_prompt_seen = True
        progress.append("sleep_prompt_seen")
        activation = await ctx.call_tool(
            "stardew_activate_ui",
            {
                "elementRef": prompt["response"]["ref"],
                "uiRevision": prompt["uiRevision"],
            },
        )
        if activation.get("status") == "unknown":
            raise SkillAbort(
                "sleep_confirmation_unknown",
                "睡眠肯定响应结果未知，禁止重放",
                "confirm_sleep",
                details={"activation": activation},
                outcome="unknown",
            )
        if activation.get("status") != "succeeded":
            raise SkillAbort(
                "sleep_confirmation_failed",
                "睡眠肯定响应没有得到确定成功结果，禁止重放",
                "confirm_sleep",
                details={"activation": activation},
            )
        sleep_confirmed = True
        progress.append("sleep_confirmed")

        after, post_sleep_ui_steps = await _wait_for_new_day(ctx, before, deadline)
        progress.extend(("day_advanced", "post_sleep_ready"))
        return {
            "status": "succeeded",
            "output": {
                "finalStatus": "completed",
                "dateBefore": before["date"],
                "dateAfter": after["date"],
                "timeBefore": before["timeOfDay"],
                "timeAfter": after["timeOfDay"],
                "locationBefore": before["player"]["position"],
                "locationAfter": after["player"]["position"],
                "homeLocationId": home_location_id,
                "bedRef": bed["ref"],
                "sleepPosition": bed["bed"]["sleepPosition"],
                "sleepPromptSeen": sleep_prompt_seen,
                "sleepConfirmed": sleep_confirmed,
                "dayAdvanced": True,
                "postSleepUiSteps": post_sleep_ui_steps,
                "playerCanMoveAfter": after["player"]["canMove"],
                "progress": progress,
            },
        }
    except SkillAbort as error:
        return {
            "status": error.outcome,
            "error": {
                "code": error.code,
                "message": error.message,
                "retryable": error.retryable,
            },
            "output": {
                "finalStatus": error.outcome,
                "phase": error.phase,
                "dateBefore": before.get("date"),
                "bedRef": bed.get("ref") if bed else None,
                "sleepPromptSeen": sleep_prompt_seen,
                "sleepConfirmed": sleep_confirmed,
                "postSleepUiSteps": post_sleep_ui_steps,
                "progress": progress,
                "details": error.details or {},
            },
        }


async def _runtime(ctx, phase: str) -> dict[str, Any]:
    result = await ctx.call_tool("stardew_query_runtime", {})
    if result.get("status") == "unknown":
        raise SkillAbort(
            "runtime_query_unknown",
            "无法确认游戏运行状态",
            phase,
            details={"result": result},
            outcome="unknown",
        )
    if result.get("status") != "succeeded":
        raise SkillAbort(
            "runtime_query_failed",
            "无法取得游戏运行状态",
            phase,
            result.get("status") == "failed",
            {"result": result},
        )
    return result["output"]["snapshot"]


async def _find_bed(ctx, home_location_id: str, player_position: dict[str, Any]) -> dict[str, Any]:
    candidates: list[dict[str, Any]] = []
    for x, y in ((0, 0), (32, 0), (0, 32), (32, 32)):
        result = await ctx.call_tool(
            "stardew_query_world",
            {
                "area": {"locationId": home_location_id, "x": x, "y": y, "width": 32, "height": 32},
                "includeTiles": False,
                "includeCharacters": False,
                "entityKinds": ["bed"],
                "maxEntities": 32,
            },
        )
        if result.get("status") == "unknown":
            raise SkillAbort(
                "bed_query_unknown",
                "床位查询结果未知",
                "find_bed",
                details={"result": result},
                outcome="unknown",
            )
        if result.get("status") != "succeeded":
            continue
        for entity in result["output"]["snapshot"].get("entities", []):
            facts = entity.get("bed") or {}
            if entity.get("kind") != "bed" or not facts.get("sleepPosition"):
                continue
            occupied_by_self = any(_same_position(tile, player_position) for tile in facts.get("occupiedTiles", []))
            if facts.get("canSleep") or occupied_by_self:
                candidates.append(entity)

    if not candidates:
        raise SkillAbort("sleepable_bed_not_found", "住宅内没有可安全选择的成人床位", "find_bed")

    def distance(entity: dict[str, Any]) -> int:
        target = entity["bed"]["sleepPosition"]
        if target.get("locationId") != player_position.get("locationId"):
            return 1_000_000
        return abs(target["x"] - player_position["x"]) + abs(target["y"] - player_position["y"])

    return min(candidates, key=lambda entity: (not entity["bed"].get("canSleep", False), distance(entity)))


async def _wait_for_sleep_prompt(
    ctx,
    bed: dict[str, Any],
    deadline: float,
    *,
    attempts: int,
) -> dict[str, Any] | None:
    dialogue_seen = False
    for _ in range(attempts):
        _ensure_time(deadline, "wait_sleep_prompt")
        result = await ctx.call_tool("stardew_query_ui", {})
        if result.get("status") == "unknown":
            raise SkillAbort(
                "ui_query_unknown",
                "睡眠问题查询结果未知",
                "wait_sleep_prompt",
                details={"result": result},
                outcome="unknown",
            )
        if result.get("status") == "succeeded":
            snapshot = result["output"]["snapshot"]
            prompt = await _sleep_prompt(ctx, snapshot, bed)
            if prompt is not None:
                return prompt
            if snapshot.get("menuOpen"):
                menu_type = snapshot.get("menu", {}).get("menuType", "unknown")
                if menu_type == "DialogueBox":
                    dialogue_seen = True
                else:
                    raise SkillAbort("unexpected_menu", f"等待睡眠问题时出现未知菜单: {menu_type}", "wait_sleep_prompt")
        await asyncio.sleep(0.25)
    if dialogue_seen:
        raise SkillAbort(
            "sleep_prompt_unrecognized",
            "睡眠对话框在等待窗口内没有形成可激活的肯定响应",
            "wait_sleep_prompt",
            True,
        )
    return None


async def _sleep_prompt(ctx, snapshot: dict[str, Any], bed: dict[str, Any]) -> dict[str, Any] | None:
    menu = snapshot.get("menu") or {}
    if (
        not snapshot.get("menuOpen")
        or menu.get("menuType") != "DialogueBox"
        or menu.get("dialogueKind") != "sleep_confirmation"
    ):
        return None
    responses = sorted(
        (
            item
            for item in snapshot.get("elements", [])
            if item.get("kind") == "dialogue_response" and item.get("visible") and item.get("enabled")
        ),
        key=lambda item: item.get("index", 0),
    )
    if len(responses) != 2 or [response.get("index") for response in responses] != [0, 1]:
        return None
    players_result = await ctx.call_tool("stardew_query_players", {})
    if players_result.get("status") == "unknown":
        raise SkillAbort(
            "player_query_unknown",
            "睡眠问题出现时无法确认当前玩家状态，禁止选择对话响应",
            "wait_sleep_prompt",
            details={"result": players_result},
            outcome="unknown",
        )
    if players_result.get("status") != "succeeded":
        raise SkillAbort(
            "player_query_failed",
            "睡眠问题出现时无法确认当前玩家状态",
            "wait_sleep_prompt",
            players_result.get("status") == "failed",
            {"result": players_result},
        )
    players = players_result.get("output", {}).get("snapshot", {}).get("players", [])
    myself = next((player for player in players if player.get("relation") == "myself"), None)
    sleep_position = bed.get("bed", {}).get("sleepPosition", {})
    if (
        myself is None
        or not myself.get("online")
        or not myself.get("isInBed")
        or not _same_position(myself.get("position", {}), sleep_position)
    ):
        return None
    return {"uiRevision": snapshot["uiRevision"], "response": responses[0]}


async def _fallback_interact(ctx, bed: dict[str, Any], deadline: float) -> None:
    _ensure_time(deadline, "fallback_interact")
    navigation = await ctx.call_tool(
        "stardew_navigate",
        {"targetRef": bed["ref"], "arrival": "adjacent"},
    )
    if navigation.get("status") == "unknown":
        raise SkillAbort(
            "fallback_navigation_unknown",
            "床旁站位结果未知，禁止继续提交交互",
            "fallback_interact",
            details={"navigation": navigation},
            outcome="unknown",
        )
    if navigation.get("status") != "succeeded":
        raise SkillAbort(
            "fallback_navigation_failed",
            "无法到达床旁交互位置",
            "fallback_interact",
            navigation.get("error", {}).get("retryable", False),
            {"navigation": navigation},
        )
    interaction = await ctx.call_tool("stardew_interact", {"targetRef": bed["ref"]})
    if interaction.get("status") == "unknown":
        raise SkillAbort(
            "fallback_interaction_unknown",
            "床交互结果未知，禁止重放",
            "fallback_interact",
            details={"interaction": interaction},
            outcome="unknown",
        )
    if interaction.get("status") != "succeeded":
        raise SkillAbort(
            "fallback_interaction_failed",
            "床位交互没有成功",
            "fallback_interact",
            interaction.get("error", {}).get("retryable", False),
            {"interaction": interaction},
        )


async def _wait_for_new_day(
    ctx,
    before: dict[str, Any],
    deadline: float,
) -> tuple[dict[str, Any], int]:
    ui_steps = 0
    while True:
        _ensure_time(deadline, "wait_new_day")
        runtime_result = await ctx.call_tool("stardew_query_runtime", {})
        if runtime_result.get("status") == "unknown":
            raise SkillAbort(
                "runtime_query_unknown",
                "换日期间运行状态未知",
                "wait_new_day",
                outcome="unknown",
            )
        if runtime_result.get("status") != "succeeded":
            await asyncio.sleep(0.5)
            continue

        current = runtime_result["output"]["snapshot"]
        if _date(current) == _date(before):
            await asyncio.sleep(0.5)
            continue
        if not current.get("ui", {}).get("menuOpen") and current.get("player", {}).get("canMove"):
            return current, ui_steps

        ui_result = await ctx.call_tool("stardew_query_ui", {})
        if ui_result.get("status") == "unknown":
            raise SkillAbort(
                "post_sleep_ui_unknown",
                "换日菜单状态未知",
                "drain_post_sleep_ui",
                outcome="unknown",
            )
        if ui_result.get("status") != "succeeded":
            await asyncio.sleep(0.5)
            continue

        snapshot = ui_result["output"]["snapshot"]
        if not snapshot.get("menuOpen"):
            await asyncio.sleep(0.5)
            continue
        menu_type = (snapshot.get("menu") or {}).get("menuType", "unknown")

        if menu_type == "SaveGameMenu":
            await asyncio.sleep(0.5)
            continue
        if menu_type == "DialogueBox":
            if any(item.get("kind") == "dialogue_response" for item in snapshot.get("elements", [])):
                raise SkillAbort("decision_required", "换日过程出现需要选择的问题", "drain_post_sleep_ui")
            advances = [
                item
                for item in snapshot.get("elements", [])
                if item.get("kind") == "dialogue_advance" and item.get("visible") and item.get("enabled")
            ]
            if len(advances) != 1:
                raise SkillAbort("post_sleep_dialogue_unsupported", "换日普通对话无法安全推进", "drain_post_sleep_ui")
            result = await ctx.call_tool(
                "stardew_activate_ui",
                {"elementRef": advances[0]["ref"], "uiRevision": snapshot["uiRevision"]},
            )
            if result.get("status") != "succeeded":
                raise SkillAbort("post_sleep_dialogue_failed", "换日普通对话推进失败", "drain_post_sleep_ui")
            ui_steps += 1
            continue
        if menu_type == "LevelUpMenu" and any(
            item.get("kind") in {"option", "dialogue_response"} and item.get("enabled")
            for item in snapshot.get("elements", [])
        ):
            raise SkillAbort("decision_required", "升级页面需要玩家选择", "drain_post_sleep_ui")
        if menu_type not in {"ShippingMenu", "LevelUpMenu"}:
            raise SkillAbort("unknown_post_sleep_menu", f"未知换日菜单: {menu_type}", "drain_post_sleep_ui")

        close_result = await ctx.call_tool("stardew_close_menu", {})
        if close_result.get("status") == "unknown":
            raise SkillAbort(
                "post_sleep_close_unknown",
                "换日菜单关闭结果未知",
                "drain_post_sleep_ui",
                outcome="unknown",
            )
        if close_result.get("status") == "succeeded":
            ui_steps += 1
        await asyncio.sleep(0.5)


def _date(snapshot: dict[str, Any]) -> tuple[Any, Any, Any]:
    value = snapshot.get("date") or {}
    return value.get("year"), value.get("season"), value.get("dayOfMonth")


def _same_position(left: dict[str, Any], right: dict[str, Any]) -> bool:
    return (
        left.get("locationId") == right.get("locationId")
        and left.get("x") == right.get("x")
        and left.get("y") == right.get("y")
    )


def _ensure_time(deadline: float, phase: str) -> None:
    if time.monotonic() >= deadline:
        raise SkillAbort("skill_deadline", "睡眠流程超过允许时间，可重新查询状态后决定是否续跑", phase, True)
