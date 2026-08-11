"""可执行 Skill 的最小宿主边界。

Skill 只获得受限 ToolInvoker；连接、身份和权限仍由现有 StardewClient 持有。
"""

from __future__ import annotations

import asyncio
from collections.abc import Awaitable, Callable, Collection, Mapping
from dataclasses import dataclass
from typing import Any

from jsonschema import Draft202012Validator, ValidationError
from mcp import types


SkillHandler = Callable[["SkillContext", dict[str, Any]], Awaitable[dict[str, Any]]]


@dataclass(frozen=True)
class ExecutableSkill:
    name: str
    title: str
    description: str
    input_schema: dict[str, Any]
    output_schema: dict[str, Any]
    annotations: types.ToolAnnotations
    allowed_tools: frozenset[str]
    timeout_seconds: float
    concurrency: str
    run: SkillHandler

    def as_tool(self) -> types.Tool:
        return types.Tool(
            name=self.name,
            title=self.title,
            description=self.description,
            inputSchema=self.input_schema,
            outputSchema=self.output_schema,
            annotations=self.annotations,
        )


class SkillContext:
    """一次 Skill 调用获得的、可撤销的 Tool 权限子集。"""

    def __init__(
        self,
        client: Any,
        allowed_tools: Collection[str],
        tool_read_only: Mapping[str, bool] | None = None,
    ):
        self.__client = client
        self.__allowed_tools = frozenset(allowed_tools)
        read_only = tool_read_only or {}
        self.__mutating_tools = frozenset(
            name for name in self.__allowed_tools if not read_only.get(name, False)
        )
        self.__active = True
        self.__calls_completed = 0
        self.__last_tool: str | None = None
        self.__pending_mutating_tool: str | None = None
        self.__last_effectful_tool: str | None = None

    def revoke(self) -> None:
        self.__active = False

    @property
    def mutation_outcome_possible(self) -> bool:
        return self.__last_effectful_tool is not None or self.__pending_mutating_tool is not None

    def timeout_diagnostics(self) -> dict[str, Any]:
        pending = self.__pending_mutating_tool is not None
        return {
            "phase": "call_tool" if pending else "after_tool",
            "lastTool": self.__last_tool,
            "mutatingTool": self.__pending_mutating_tool or self.__last_effectful_tool,
            "callsCompleted": self.__calls_completed,
        }

    async def call_tool(self, name: str, arguments: dict[str, Any]) -> dict[str, Any]:
        if not self.__active:
            return {
                "status": "failed",
                "error": {
                    "code": "skill_grant_revoked",
                    "message": "本次 Skill 调用权柄已撤销",
                    "retryable": False,
                },
            }
        if name not in self.__allowed_tools:
            return {
                "status": "failed",
                "error": {
                    "code": "skill_tool_denied",
                    "message": f"Skill 未获授权调用 Tool: {name}",
                    "retryable": False,
                },
            }
        self.__last_tool = name
        mutating = name in self.__mutating_tools
        if mutating:
            self.__pending_mutating_tool = name
        result = await self.__client.call_tool(name, arguments)
        self.__calls_completed += 1
        if mutating:
            self.__pending_mutating_tool = None
            if result.get("status") in {"succeeded", "unknown"}:
                self.__last_effectful_tool = name
        return result


class SkillHost:
    """执行已经过 Loader 校验的 Skill，并为每次调用授予受限 Tool 子集。"""

    def __init__(self, client: Any, skills: Collection[ExecutableSkill]):
        self.__client = client
        self.__skills = {skill.name: skill for skill in skills}
        if len(self.__skills) != len(skills):
            raise ValueError("可执行 Skill 名称不得重复")
        self.__exclusive_lock = asyncio.Lock()

    def handles(self, name: str) -> bool:
        return name in self.__skills

    def available_tools(self, available_atomic_tools: Collection[types.Tool]) -> list[types.Tool]:
        available_names = {tool.name for tool in available_atomic_tools}
        return [
            skill.as_tool()
            for skill in self.__skills.values()
            if skill.allowed_tools <= available_names
        ]

    async def invoke(self, name: str, arguments: dict[str, Any]) -> dict[str, Any]:
        skill = self.__skills.get(name)
        if skill is None:
            return _failure("skill_not_found", "未知可执行 Skill")

        atomic_tools = await self.__client.available_tools()
        available_names = {tool.name for tool in atomic_tools}
        missing = sorted(skill.allowed_tools - available_names)
        if missing:
            return _failure("skill_dependency_unavailable", f"Skill 依赖的 Tool 当前不可用: {', '.join(missing)}")

        try:
            Draft202012Validator(skill.input_schema).validate(arguments)
        except ValidationError:
            return _failure("invalid_arguments", "参数不符合 Skill 输入 Schema")

        tool_read_only = {
            tool.name: bool(tool.annotations and tool.annotations.readOnlyHint is True)
            for tool in atomic_tools
        }
        context = SkillContext(self.__client, skill.allowed_tools, tool_read_only)
        try:
            async with asyncio.timeout(skill.timeout_seconds):
                if skill.concurrency != "exclusive":
                    return _failure("skill_internal", "Skill 并发策略不受支持")
                async with self.__exclusive_lock:
                    result = await skill.run(context, arguments)
                try:
                    Draft202012Validator(skill.output_schema).validate(result)
                except ValidationError:
                    if context.mutation_outcome_possible:
                        return _unknown(
                            "skill_output_invalid",
                            "Skill 已执行变更，但返回结果不符合公开 Schema；禁止自动重放",
                            context.timeout_diagnostics(),
                        )
                    return _failure("skill_output_invalid", "Skill 返回结果不符合公开 Schema")
                return result
        except TimeoutError:
            diagnostics = context.timeout_diagnostics()
            if context.mutation_outcome_possible:
                return _unknown(
                    "skill_timeout_unknown_outcome",
                    "Skill 超时，已提交的变更结果无法确认；禁止自动重放",
                    diagnostics,
                )
            return {
                **_failure("skill_timeout", "Skill 执行超过宿主允许时间", retryable=True),
                "output": {"finalStatus": "failed", **diagnostics},
            }
        except asyncio.CancelledError:
            raise
        except Exception:
            if context.mutation_outcome_possible:
                return _unknown(
                    "skill_execution_unknown_outcome",
                    "Skill 执行异常，已提交的变更结果无法确认；禁止自动重放",
                    context.timeout_diagnostics(),
                )
            return _failure("skill_internal", "Skill 执行失败")
        finally:
            context.revoke()


def _failure(code: str, message: str, *, retryable: bool = False) -> dict[str, Any]:
    return {
        "status": "failed",
        "error": {"code": code, "message": message, "retryable": retryable},
    }


def _unknown(code: str, message: str, diagnostics: Mapping[str, Any]) -> dict[str, Any]:
    return {
        "status": "unknown",
        "error": {"code": code, "message": message, "retryable": False},
        "output": {"finalStatus": "unknown", **diagnostics},
    }
