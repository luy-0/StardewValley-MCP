"""可执行 Skill 的最小宿主边界。

Skill 只获得受限 ToolInvoker；连接、身份和权限仍由现有 StardewClient 持有。
"""

from __future__ import annotations

import asyncio
from collections.abc import Awaitable, Callable, Collection
from dataclasses import dataclass
from typing import Any

from jsonschema import Draft202012Validator, ValidationError
from mcp import types


SkillHandler = Callable[["SkillContext", dict[str, Any]], Awaitable[dict[str, Any]]]


@dataclass(frozen=True)
class ExecutableSkill:
    name: str
    description: str
    input_schema: dict[str, Any]
    allowed_tools: frozenset[str]
    timeout_seconds: float
    run: SkillHandler

    def as_tool(self) -> types.Tool:
        return types.Tool(name=self.name, description=self.description, inputSchema=self.input_schema)


class SkillContext:
    """一次 Skill 调用获得的、可撤销的 Tool 权限子集。"""

    def __init__(self, client: Any, allowed_tools: Collection[str]):
        self.__client = client
        self.__allowed_tools = frozenset(allowed_tools)
        self.__active = True

    def revoke(self) -> None:
        self.__active = False

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
        return await self.__client.call_tool(name, arguments)


class SkillHost:
    """显式注册并执行 Skill；试点阶段不扫描目录、不加载第三方依赖。"""

    def __init__(self, client: Any, skills: Collection[ExecutableSkill]):
        self.__client = client
        self.__skills = {skill.name: skill for skill in skills}
        if len(self.__skills) != len(skills):
            raise ValueError("可执行 Skill 名称不得重复")

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

        context = SkillContext(self.__client, skill.allowed_tools)
        try:
            async with asyncio.timeout(skill.timeout_seconds):
                return await skill.run(context, arguments)
        except TimeoutError:
            return _failure("skill_timeout", "Skill 执行超过宿主允许时间", retryable=True)
        except asyncio.CancelledError:
            raise
        except Exception:
            return _failure("skill_internal", "Skill 执行失败")
        finally:
            context.revoke()


def _failure(code: str, message: str, *, retryable: bool = False) -> dict[str, Any]:
    return {
        "status": "failed",
        "error": {"code": code, "message": message, "retryable": retryable},
    }
