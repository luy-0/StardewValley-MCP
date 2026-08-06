from __future__ import annotations

import asyncio
from pathlib import Path

from mcp import types

from stardew_valley_mcp.builtin_skills import _load_run, create_builtin_skill_host


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "skill" / "examples" / "stardew-sleep-until-next-day" / "scripts" / "run.py"


def _runtime(day: int, *, menu_open: bool = False, can_move: bool = True):
    return {
        "status": "succeeded",
        "output": {
            "snapshot": {
                "date": {"season": "fall", "dayOfMonth": day, "year": 3},
                "timeOfDay": 900 if day == 26 else 600,
                "player": {
                    "position": {"locationId": "FarmHouse", "x": 29, "y": 28},
                    "canMove": can_move,
                    "homeLocationId": "FarmHouse",
                },
                "ui": {"menuOpen": menu_open, "menuType": "DialogueBox" if menu_open else ""},
            }
        },
    }


def _bed_query():
    return {
        "status": "succeeded",
        "output": {
            "snapshot": {
                "entities": [
                    {
                        "ref": {"value": "bed-ref"},
                        "kind": "bed",
                        "position": {"locationId": "FarmHouse", "x": 29, "y": 27},
                        "bed": {
                            "canSleep": False,
                            "occupiedTiles": [{"locationId": "FarmHouse", "x": 29, "y": 28}],
                            "sleepPosition": {"locationId": "FarmHouse", "x": 30, "y": 28},
                        },
                    }
                ]
            }
        },
    }


def _sleep_prompt():
    return {
        "status": "succeeded",
        "output": {
            "snapshot": {
                "menuOpen": True,
                "uiRevision": "a" * 64,
                "menu": {"menuType": "DialogueBox", "dialogueText": "Go to sleep for the night?"},
                "elements": [
                    {
                        "ref": {"value": "yes-ref"},
                        "kind": "dialogue_response",
                        "label": "Yes",
                        "index": 0,
                        "visible": True,
                        "enabled": True,
                    },
                    {
                        "ref": {"value": "no-ref"},
                        "kind": "dialogue_response",
                        "label": "No",
                        "index": 1,
                        "visible": True,
                        "enabled": True,
                    },
                ],
            }
        },
    }


def _sleep_prompt_not_ready():
    result = _sleep_prompt()
    for element in result["output"]["snapshot"]["elements"]:
        element["enabled"] = False
    return result


class _SuccessfulContext:
    def __init__(self):
        self.calls = []
        self.world_queries = 0
        self.runtime_queries = 0

    async def call_tool(self, name, arguments):
        self.calls.append((name, arguments))
        if name == "stardew_query_runtime":
            self.runtime_queries += 1
            return _runtime(26 if self.runtime_queries == 1 else 27)
        if name == "stardew_query_world":
            self.world_queries += 1
            return _bed_query() if self.world_queries == 1 else {
                "status": "failed",
                "error": {"code": "out_of_range", "message": "区域超出地图", "retryable": False},
            }
        if name == "stardew_navigate":
            return {
                "status": "failed",
                "error": {"code": "timeout", "message": "菜单打开后导航达到 deadline", "retryable": True},
            }
        if name == "stardew_query_ui":
            return _sleep_prompt()
        if name == "stardew_activate_ui":
            return {"status": "succeeded", "output": {}}
        raise AssertionError(f"unexpected Tool: {name}")


def test_sleep_script_completes_after_navigation_timeout_when_prompt_is_observed() -> None:
    run = _load_run(SCRIPT, "test_stardew_sleep_until_next_day")
    context = _SuccessfulContext()

    result = asyncio.run(run(context, {"timeoutSeconds": 60}))

    assert result["status"] == "succeeded"
    assert result["output"]["dateBefore"]["dayOfMonth"] == 26
    assert result["output"]["dateAfter"]["dayOfMonth"] == 27
    assert result["output"]["sleepPromptSeen"] is True
    assert result["output"]["sleepConfirmed"] is True
    assert result["output"]["playerCanMoveAfter"] is True
    assert context.calls[-2][0] == "stardew_activate_ui"
    assert context.calls[-1][0] == "stardew_query_runtime"


def test_sleep_script_waits_for_dialogue_responses_to_become_enabled() -> None:
    class Context(_SuccessfulContext):
        def __init__(self):
            super().__init__()
            self.ui_queries = 0

        async def call_tool(self, name, arguments):
            if name == "stardew_query_ui":
                self.calls.append((name, arguments))
                self.ui_queries += 1
                return _sleep_prompt_not_ready() if self.ui_queries == 1 else _sleep_prompt()
            return await super().call_tool(name, arguments)

    run = _load_run(SCRIPT, "test_stardew_sleep_prompt_settle")
    context = Context()

    result = asyncio.run(run(context, {"timeoutSeconds": 60}))

    assert result["status"] == "succeeded"
    assert result["output"]["sleepConfirmed"] is True
    assert context.ui_queries == 2


def test_builtin_sleep_skill_is_visible_only_when_all_atomic_dependencies_are_available() -> None:
    class Client:
        pass

    host = create_builtin_skill_host(Client())
    read_only = [types.Tool(name="stardew_query_runtime", inputSchema={"type": "object"})]
    assert host.available_tools(read_only) == []

    dependencies = (
        "stardew_query_runtime",
        "stardew_query_world",
        "stardew_navigate",
        "stardew_interact",
        "stardew_query_ui",
        "stardew_activate_ui",
        "stardew_close_menu",
    )
    tools = [types.Tool(name=name, inputSchema={"type": "object"}) for name in dependencies]
    assert [tool.name for tool in host.available_tools(tools)] == ["stardew_skill_sleep_until_next_day"]
