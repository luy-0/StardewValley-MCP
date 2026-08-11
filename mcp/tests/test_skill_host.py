from __future__ import annotations

import asyncio

import anyio
import pytest
from mcp import ClientSession, types

from stardew_valley_mcp.server import create_server
from stardew_valley_mcp.skill_host import ExecutableSkill, SkillContext, SkillHost


QUERY_TOOL = types.Tool(
    name="stardew_query_runtime",
    description="查询运行状态",
    inputSchema={"type": "object", "additionalProperties": False},
    annotations=types.ToolAnnotations(readOnlyHint=True),
)

MUTATING_TOOL = types.Tool(
    name="stardew_navigate",
    description="移动玩家",
    inputSchema={"type": "object", "additionalProperties": False},
    annotations=types.ToolAnnotations(readOnlyHint=False),
)


async def _run_probe(ctx, arguments):
    del arguments
    first = await ctx.call_tool("stardew_query_runtime", {})
    if first.get("status") != "succeeded":
        return first
    second = await ctx.call_tool("stardew_query_runtime", {})
    if second.get("status") != "succeeded":
        return second
    return {
        "status": "succeeded",
        "output": {
            "callsCompleted": 2,
            "first": first.get("output", {}),
            "second": second.get("output", {}),
        },
    }


def _skill(run=None, *, allowed_tools=frozenset({"stardew_query_runtime"}), timeout_seconds=1.0):
    return ExecutableSkill(
        name="stardew_skill_runtime_probe",
        title="运行状态探针",
        description="验证可执行 Skill 复用当前 MCP 会话",
        input_schema={"type": "object", "additionalProperties": False},
        output_schema={"type": "object", "required": ["status"]},
        annotations=types.ToolAnnotations(
            readOnlyHint=True,
            destructiveHint=False,
            idempotentHint=True,
            openWorldHint=False,
        ),
        allowed_tools=allowed_tools,
        timeout_seconds=timeout_seconds,
        concurrency="exclusive",
        run=run or _run_probe,
    )


class _Client:
    def __init__(self, tools=None):
        self.calls: list[tuple[str, dict[str, object]]] = []
        self.tools = tools or [QUERY_TOOL]

    async def available_tools(self):
        return self.tools

    async def call_tool(self, name, arguments):
        self.calls.append((name, arguments))
        return {
            "status": "succeeded",
            "output": {"sequence": len(self.calls)},
        }


def test_probe_script_reuses_one_injected_client_for_multiple_tool_calls() -> None:
    client = _Client()
    host = SkillHost(client, [_skill()])

    result = asyncio.run(host.invoke("stardew_skill_runtime_probe", {}))

    assert result["status"] == "succeeded"
    assert result["output"]["callsCompleted"] == 2
    assert result["output"]["first"] == {"sequence": 1}
    assert result["output"]["second"] == {"sequence": 2}
    assert client.calls == [
        ("stardew_query_runtime", {}),
        ("stardew_query_runtime", {}),
    ]


def test_context_denies_tools_outside_delegated_subset() -> None:
    client = _Client()
    context = SkillContext(client, {"stardew_query_runtime"})

    result = asyncio.run(context.call_tool("stardew_navigate", {"x": 1, "y": 2}))

    assert result["error"]["code"] == "skill_tool_denied"
    assert client.calls == []


def test_context_grant_is_revoked_when_invocation_returns() -> None:
    captured = []

    async def capture_context(ctx, arguments):
        del arguments
        captured.append(ctx)
        return {"status": "succeeded", "output": {}}

    client = _Client()
    host = SkillHost(client, [_skill(capture_context)])
    assert asyncio.run(host.invoke("stardew_skill_runtime_probe", {}))["status"] == "succeeded"

    result = asyncio.run(captured[0].call_tool("stardew_query_runtime", {}))
    assert result["error"]["code"] == "skill_grant_revoked"
    assert client.calls == []


def test_host_hides_and_rejects_skill_when_atomic_dependency_is_unavailable() -> None:
    client = _Client()
    skill = _skill(allowed_tools=frozenset({"stardew_query_runtime", "stardew_navigate"}))
    host = SkillHost(client, [skill])

    assert host.available_tools([QUERY_TOOL]) == []
    result = asyncio.run(host.invoke(skill.name, {}))
    assert result["error"]["code"] == "skill_dependency_unavailable"
    assert client.calls == []


def test_host_cancels_skill_at_the_invocation_deadline() -> None:
    async def wait_forever(ctx, arguments):
        del ctx, arguments
        await asyncio.sleep(60)
        return {"status": "succeeded"}

    client = _Client()
    host = SkillHost(client, [_skill(wait_forever, timeout_seconds=0.01)])

    result = asyncio.run(host.invoke("stardew_skill_runtime_probe", {}))
    assert result["error"]["code"] == "skill_timeout"
    assert result["error"]["retryable"] is True


@pytest.mark.parametrize("tool_status", ["succeeded", "unknown"])
def test_host_marks_timeout_after_mutating_tool_as_unknown_and_not_retryable(
    tool_status: str,
) -> None:
    async def mutate_then_wait(ctx, arguments):
        del arguments
        await ctx.call_tool("stardew_navigate", {})
        await asyncio.sleep(60)
        return {"status": "succeeded"}

    class Client(_Client):
        async def call_tool(self, name, arguments):
            self.calls.append((name, arguments))
            return {"status": tool_status, "output": {}}

    client = Client([MUTATING_TOOL])
    host = SkillHost(
        client,
        [
            _skill(
                mutate_then_wait,
                allowed_tools=frozenset({"stardew_navigate"}),
                timeout_seconds=0.01,
            )
        ],
    )

    result = asyncio.run(host.invoke("stardew_skill_runtime_probe", {}))

    assert result["status"] == "unknown"
    assert result["error"] == {
        "code": "skill_timeout_unknown_outcome",
        "message": "Skill 超时，已提交的变更结果无法确认；禁止自动重放",
        "retryable": False,
    }
    assert result["output"] == {
        "finalStatus": "unknown",
        "phase": "after_tool",
        "lastTool": "stardew_navigate",
        "mutatingTool": "stardew_navigate",
        "callsCompleted": 1,
    }


def test_host_keeps_read_only_timeout_retryable_after_query() -> None:
    async def query_then_wait(ctx, arguments):
        del arguments
        result = await ctx.call_tool("stardew_query_runtime", {})
        assert result["status"] == "succeeded"
        await asyncio.sleep(60)
        return {"status": "succeeded"}

    client = _Client()
    host = SkillHost(client, [_skill(query_then_wait, timeout_seconds=0.01)])

    result = asyncio.run(host.invoke("stardew_skill_runtime_probe", {}))

    assert result["status"] == "failed"
    assert result["error"]["code"] == "skill_timeout"
    assert result["error"]["retryable"] is True
    assert result["output"] == {
        "finalStatus": "failed",
        "phase": "after_tool",
        "lastTool": "stardew_query_runtime",
        "mutatingTool": None,
        "callsCompleted": 1,
    }


def test_host_marks_timeout_during_mutating_tool_as_unknown() -> None:
    async def mutate(ctx, arguments):
        del arguments
        return await ctx.call_tool("stardew_navigate", {})

    class Client(_Client):
        async def call_tool(self, name, arguments):
            self.calls.append((name, arguments))
            await asyncio.sleep(60)
            return {"status": "succeeded", "output": {}}

    client = Client([MUTATING_TOOL])
    host = SkillHost(
        client,
        [
            _skill(
                mutate,
                allowed_tools=frozenset({"stardew_navigate"}),
                timeout_seconds=0.01,
            )
        ],
    )

    result = asyncio.run(host.invoke("stardew_skill_runtime_probe", {}))

    assert result["status"] == "unknown"
    assert result["error"]["retryable"] is False
    assert result["output"] == {
        "finalStatus": "unknown",
        "phase": "call_tool",
        "lastTool": "stardew_navigate",
        "mutatingTool": "stardew_navigate",
        "callsCompleted": 0,
    }


def test_host_marks_exception_during_mutating_tool_as_unknown() -> None:
    async def mutate(ctx, arguments):
        del arguments
        return await ctx.call_tool("stardew_navigate", {})

    class Client(_Client):
        async def call_tool(self, name, arguments):
            self.calls.append((name, arguments))
            raise RuntimeError("connection lost after submit")

    client = Client([MUTATING_TOOL])
    host = SkillHost(
        client,
        [_skill(mutate, allowed_tools=frozenset({"stardew_navigate"}))],
    )

    result = asyncio.run(host.invoke("stardew_skill_runtime_probe", {}))

    assert result["status"] == "unknown"
    assert result["error"] == {
        "code": "skill_execution_unknown_outcome",
        "message": "Skill 执行异常，已提交的变更结果无法确认；禁止自动重放",
        "retryable": False,
    }
    assert result["output"] == {
        "finalStatus": "unknown",
        "phase": "call_tool",
        "lastTool": "stardew_navigate",
        "mutatingTool": "stardew_navigate",
        "callsCompleted": 0,
    }


def test_standard_mcp_call_can_invoke_skill_without_a_second_mod_client() -> None:
    client = _Client()
    host = SkillHost(client, [_skill()])

    async def exercise() -> None:
        server = create_server(client, skill_host=host)
        client_send, server_receive = anyio.create_memory_object_stream(10)
        server_send, client_receive = anyio.create_memory_object_stream(10)

        async def run_server() -> None:
            await server.run(
                server_receive,
                server_send,
                server.create_initialization_options(),
                raise_exceptions=True,
            )

        async with anyio.create_task_group() as tasks:
            tasks.start_soon(run_server)
            async with ClientSession(client_receive, client_send) as session:
                await session.initialize()
                tools = await session.list_tools()
                assert [tool.name for tool in tools.tools] == [
                    "stardew_query_runtime",
                    "stardew_skill_runtime_probe",
                ]
                result = await session.call_tool("stardew_skill_runtime_probe", {})
                assert result.isError is False
                assert result.structuredContent["output"]["callsCompleted"] == 2
            tasks.cancel_scope.cancel()

    anyio.run(exercise)
    assert len(client.calls) == 2
