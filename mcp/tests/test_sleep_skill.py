from __future__ import annotations

import asyncio
import sys
from pathlib import Path

import pytest
from mcp import types

from stardew_valley_mcp.skill_host import SkillHost
from stardew_valley_mcp.skill_loader import _load_entrypoint, load_executable_skills


ROOT = Path(__file__).resolve().parents[2]
SKILL_DIR = ROOT / "skill" / "examples" / "stardew-sleep-until-next-day"
SCRIPT = SKILL_DIR / "scripts" / "run.py"


def _run():
    return load_executable_skills([SKILL_DIR])[0].run


def _module():
    run = _load_entrypoint(SCRIPT, "run")
    return sys.modules[run.__module__]


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
                "menu": {
                    "menuType": "DialogueBox",
                    "dialogueKind": "sleep_confirmation",
                    "dialogueText": "Go to sleep for the night?",
                },
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


def _players(*, is_in_bed: bool = True):
    return {
        "status": "succeeded",
        "output": {
            "snapshot": {
                "players": [
                    {
                        "playerId": "17",
                        "displayName": "Nicole",
                        "relation": "myself",
                        "online": True,
                        "isHost": True,
                        "position": {"locationId": "FarmHouse", "x": 30, "y": 28},
                        "isInBed": is_in_bed,
                        "homeLocationId": "FarmHouse",
                    }
                ]
            }
        },
    }


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
        if name == "stardew_query_players":
            return _players()
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
    run = _run()
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

    run = _run()
    context = Context()

    result = asyncio.run(run(context, {"timeoutSeconds": 60}))

    assert result["status"] == "succeeded"
    assert result["output"]["sleepConfirmed"] is True
    assert context.ui_queries == 2


def test_sleep_skill_host_accepts_the_script_result_against_public_output_schema() -> None:
    class Client(_SuccessfulContext):
        async def available_tools(self):
            read_only = {
                "stardew_query_runtime",
                "stardew_query_players",
                "stardew_query_world",
                "stardew_query_ui",
            }
            return [
                types.Tool(
                    name=name,
                    inputSchema={"type": "object"},
                    annotations=types.ToolAnnotations(readOnlyHint=name in read_only),
                )
                for name in load_executable_skills([SKILL_DIR])[0].allowed_tools
            ]

    client = Client()
    host = SkillHost(client, load_executable_skills([SKILL_DIR]))

    result = asyncio.run(host.invoke("stardew_skill_sleep_until_next_day", {"timeoutSeconds": 60}))

    assert result["status"] == "succeeded"
    assert result["output"]["finalStatus"] == "completed"


def test_sleep_skill_host_resolves_unknown_navigation_from_prompt_and_bed_facts() -> None:
    class Client(_SuccessfulContext):
        async def available_tools(self):
            read_only = {
                "stardew_query_runtime", "stardew_query_players",
                "stardew_query_world", "stardew_query_ui",
            }
            return [
                types.Tool(
                    name=name,
                    inputSchema={"type": "object"},
                    annotations=types.ToolAnnotations(readOnlyHint=name in read_only),
                )
                for name in load_executable_skills([SKILL_DIR])[0].allowed_tools
            ]

        async def call_tool(self, name, arguments):
            if name == "stardew_navigate":
                self.calls.append((name, arguments))
                return {
                    "status": "unknown",
                    "error": {"code": "unknown_outcome", "message": "结果未知", "retryable": False},
                }
            return await super().call_tool(name, arguments)

    skill = load_executable_skills([SKILL_DIR])[0]
    result = asyncio.run(SkillHost(Client(), [skill]).invoke(skill.name, {"timeoutSeconds": 60}))

    assert result["status"] == "succeeded"
    assert result["output"]["sleepConfirmed"] is True


def test_sleep_confirmation_unknown_is_preserved_and_not_retryable() -> None:
    class Context(_SuccessfulContext):
        async def call_tool(self, name, arguments):
            if name == "stardew_activate_ui":
                self.calls.append((name, arguments))
                return {
                    "status": "unknown",
                    "error": {"code": "unknown_outcome", "message": "结果未知", "retryable": False},
                }
            return await super().call_tool(name, arguments)

    result = asyncio.run(_run()(Context(), {"timeoutSeconds": 60}))

    assert result["status"] == "unknown"
    assert result["error"]["code"] == "sleep_confirmation_unknown"
    assert result["error"]["retryable"] is False
    assert result["output"]["finalStatus"] == "unknown"


def test_arbitrary_two_choice_dialogue_without_bed_context_is_not_sleep_prompt() -> None:
    module = _module()
    dialogue = _sleep_prompt()
    dialogue["output"]["snapshot"]["menu"]["dialogueText"] = "Donate the item?"
    dialogue["output"]["snapshot"]["elements"][0]["label"] = "Donate"
    dialogue["output"]["snapshot"]["elements"][1]["label"] = "Keep"

    class Context:
        async def call_tool(self, name, arguments):
            assert name == "stardew_query_players"
            assert arguments == {}
            return _players(is_in_bed=False)

    result = asyncio.run(
        module._sleep_prompt(
            Context(),
            dialogue["output"]["snapshot"],
            _bed_query()["output"]["snapshot"]["entities"][0],
        )
    )

    assert result is None


def test_arbitrary_two_choice_dialogue_on_bed_is_not_sleep_prompt_without_semantic_kind() -> None:
    module = _module()
    dialogue = _sleep_prompt()
    dialogue["output"]["snapshot"]["menu"].pop("dialogueKind")
    dialogue["output"]["snapshot"]["menu"]["dialogueText"] = "Donate the item?"
    dialogue["output"]["snapshot"]["elements"][0]["label"] = "Donate"
    dialogue["output"]["snapshot"]["elements"][1]["label"] = "Keep"

    class Context:
        async def call_tool(self, name, arguments):
            raise AssertionError(f"不应为未知对话读取玩家或激活响应: {name} {arguments}")

    result = asyncio.run(
        module._sleep_prompt(
            Context(),
            dialogue["output"]["snapshot"],
            _bed_query()["output"]["snapshot"]["entities"][0],
        )
    )

    assert result is None


def test_fallback_navigation_failure_does_not_submit_interaction() -> None:
    module = _module()

    class Context:
        def __init__(self):
            self.calls = []

        async def call_tool(self, name, arguments):
            self.calls.append((name, arguments))
            assert name == "stardew_navigate"
            return {
                "status": "failed",
                "error": {"code": "path_blocked", "message": "路径受阻", "retryable": False},
            }

    context = Context()
    with pytest.raises(module.SkillAbort) as captured:
        asyncio.run(
            module._fallback_interact(
                context,
                _bed_query()["output"]["snapshot"]["entities"][0],
                float("inf"),
            )
        )

    assert captured.value.code == "fallback_navigation_failed"
    assert captured.value.details["navigation"]["error"]["code"] == "path_blocked"
    assert [name for name, _ in context.calls] == ["stardew_navigate"]


def test_fallback_interaction_failure_preserves_original_reason() -> None:
    module = _module()

    class Context:
        def __init__(self):
            self.calls = []

        async def call_tool(self, name, arguments):
            self.calls.append((name, arguments))
            if name == "stardew_navigate":
                return {"status": "succeeded", "output": {}}
            assert name == "stardew_interact"
            return {
                "status": "failed",
                "error": {"code": "target_blocked", "message": "目标被阻挡", "retryable": True},
            }

    context = Context()
    with pytest.raises(module.SkillAbort) as captured:
        asyncio.run(
            module._fallback_interact(
                context,
                _bed_query()["output"]["snapshot"]["entities"][0],
                float("inf"),
            )
        )

    assert captured.value.code == "fallback_interaction_failed"
    assert captured.value.retryable is True
    assert captured.value.details["interaction"]["error"]["code"] == "target_blocked"
    assert [name for name, _ in context.calls] == ["stardew_navigate", "stardew_interact"]


def test_builtin_sleep_skill_is_visible_only_when_all_atomic_dependencies_are_available() -> None:
    class Client:
        pass

    host = SkillHost(Client(), load_executable_skills([SKILL_DIR]))
    read_only = [types.Tool(name="stardew_query_runtime", inputSchema={"type": "object"})]
    assert host.available_tools(read_only) == []

    dependencies = (
        "stardew_query_runtime",
        "stardew_query_players",
        "stardew_query_world",
        "stardew_navigate",
        "stardew_interact",
        "stardew_query_ui",
        "stardew_activate_ui",
        "stardew_close_menu",
    )
    tools = [types.Tool(name=name, inputSchema={"type": "object"}) for name in dependencies]
    available = host.available_tools(tools)
    assert [tool.name for tool in available] == ["stardew_skill_sleep_until_next_day"]
    assert available[0].outputSchema is not None
    assert available[0].annotations is not None
    assert available[0].annotations.readOnlyHint is False
    assert available[0].annotations.destructiveHint is True
