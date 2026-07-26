from __future__ import annotations

import asyncio
import json
from pathlib import Path

import anyio
from google.protobuf import json_format
from jsonschema import Draft202012Validator
from mcp import ClientSession

from stardew_valley_mcp.catalog import Catalog
from stardew_valley_mcp.client import StardewClient
from stardew_valley_mcp.command_runtime import CommandRuntime
from stardew_valley_mcp.projection import project_message
from stardew_valley_mcp.protocol import transport_pb2
from stardew_valley_mcp.server import create_server
from stardew_valley_mcp.transport import ConnectionConfig


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "spec" / "fixtures" / "v1" / "observation"
SECRET = bytes(range(32))
CAPABILITIES = ("inspect", "query_inventory", "query_runtime", "query_ui", "query_world")
SUCCESS_FIXTURES = {
    "inspect": "inspect.success-complete.json",
    "query_inventory": "query-inventory.success-complete.json",
    "query_runtime": "query-runtime.success.json",
    "query_ui": "query-ui.success-menu.json",
    "query_world": "query-world.success-complete.json",
}
REQUEST_FIXTURES = {
    "inspect": ("inspect.request.json", "inspect"),
    "query_inventory": ("query-inventory.request.json", "queryInventory"),
    "query_runtime": ("query-runtime.request.json", "queryRuntime"),
    "query_ui": ("query-ui.request.json", "queryUi"),
    "query_world": ("query-world.request.json", "queryWorld"),
}


def _snapshot():
    frame = transport_pb2.TransportFrame()
    json_format.Parse((FIXTURES / "server-ready.json").read_text(), frame)
    return frame.server_ready.capability_snapshot


def _arguments(capability_id: str) -> dict[str, object]:
    filename, key = REQUEST_FIXTURES[capability_id]
    document = json.loads((FIXTURES / filename).read_text())
    return document["commandRequest"][key]


def _success(capability_id: str) -> dict[str, object]:
    frame = transport_pb2.TransportFrame()
    json_format.Parse((FIXTURES / SUCCESS_FIXTURES[capability_id]).read_text(), frame)
    return {
        "status": "succeeded",
        "commandId": frame.command_event.command_id,
        "output": project_message(getattr(frame.command_event.result, capability_id)),
    }


class _CaptureRuntime:
    def __init__(self):
        self.calls = []

    def new_command_id(self):
        return "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"

    async def execute(self, command_id, capability_id, operation):
        self.calls.append((command_id, capability_id, operation))
        return _success(capability_id)


class _SnapshotConnection:
    def __init__(self, snapshot):
        self.snapshot = snapshot

    async def connect(self):
        return self.snapshot

    async def close(self):
        return None


def test_observation_profile_lists_exactly_five_tools_and_bootstrap_stays_singleton() -> None:
    catalog = Catalog.load()
    assert [tool.name for tool in catalog.tools_for(_snapshot())] == [f"stardew_{item}" for item in CAPABILITIES]
    bootstrap = transport_pb2.TransportFrame()
    json_format.Parse((ROOT / "spec" / "fixtures" / "v1" / "bootstrap" / "server-ready.json").read_text(), bootstrap)
    assert [tool.name for tool in catalog.tools_for(bootstrap.server_ready.capability_snapshot)] == ["stardew_query_runtime"]


def test_client_builds_all_five_requests_with_descriptor_enum_inverse_mapping() -> None:
    client = StardewClient(ConnectionConfig("127.0.0.1", 1, SECRET))
    runtime = _CaptureRuntime()
    client._runtime = runtime
    for capability_id in CAPABILITIES:
        arguments = _arguments(capability_id)
        if capability_id == "query_world":
            arguments = {**arguments, "entityKinds": ["tree", "container"]}
        result = asyncio.run(client.call_tool(f"stardew_{capability_id}", arguments))
        Draft202012Validator(Catalog.load().tool(capability_id).outputSchema).validate(result)
    assert [call[1] for call in runtime.calls] == list(CAPABILITIES)
    world_request = runtime.calls[-1][2]
    enum = world_request.DESCRIPTOR.fields_by_name["entity_kinds"].enum_type
    assert [enum.values_by_number[value].name for value in world_request.entity_kinds] == ["ENTITY_KIND_TREE", "ENTITY_KIND_CONTAINER"]


def test_all_five_local_invalid_arguments_are_schema_valid() -> None:
    invalid = {
        "query_runtime": {"unknown": True},
        "query_world": {"area": {"locationId": "Farm", "width": 33, "height": 32}},
        "query_inventory": {"playerInventory": {}, "containerRef": {"value": "x"}},
        "query_ui": {"unknown": True},
        "inspect": {"refs": []},
    }
    client = StardewClient(ConnectionConfig("127.0.0.1", 1, SECRET))
    runtime = _CaptureRuntime()
    client._runtime = runtime
    for capability_id, arguments in invalid.items():
        result = asyncio.run(client.call_tool(f"stardew_{capability_id}", arguments))
        assert result["error"]["code"] == "invalid_arguments"
        Draft202012Validator(Catalog.load().tool(capability_id).outputSchema).validate(result)
    assert runtime.calls == []


def test_direct_call_cannot_bypass_mod_announcement_intersection() -> None:
    bootstrap = transport_pb2.TransportFrame()
    json_format.Parse((ROOT / "spec" / "fixtures" / "v1" / "bootstrap" / "server-ready.json").read_text(), bootstrap)
    catalog = Catalog.load()
    client = StardewClient(ConnectionConfig("127.0.0.1", 1, SECRET), catalog)
    client._runtime = CommandRuntime(_SnapshotConnection(bootstrap.server_ready.capability_snapshot), catalog)
    result = asyncio.run(client.call_tool("stardew_query_world", _arguments("query_world")))
    assert result["error"]["code"] == "upstream_protocol_error"
    Draft202012Validator(catalog.tool("query_world").outputSchema).validate(result)


def test_known_non_observation_tool_is_capability_denied_before_argument_parsing() -> None:
    catalog = Catalog.load()
    client = StardewClient(ConnectionConfig("127.0.0.1", 1, SECRET), catalog)
    result = asyncio.run(client.call_tool("stardew_say", {}))
    assert result["error"]["code"] == "capability_denied"
    Draft202012Validator(catalog.tool("say").outputSchema).validate(result)


def test_standard_mcp_session_lists_and_calls_all_five_without_server_branches() -> None:
    expected = {capability_id: _success(capability_id) for capability_id in CAPABILITIES}

    class StubClient:
        async def available_tools(self):
            return Catalog.load().tools_for(_snapshot())

        async def call_tool(self, name, arguments):
            capability_id = Catalog.load().capability_for_tool(name)
            return expected[capability_id]

    async def exercise() -> None:
        server = create_server(StubClient())
        client_send, server_receive = anyio.create_memory_object_stream(10)
        server_send, client_receive = anyio.create_memory_object_stream(10)

        async def run_server() -> None:
            await server.run(server_receive, server_send, server.create_initialization_options(), raise_exceptions=True)

        async with anyio.create_task_group() as tasks:
            tasks.start_soon(run_server)
            async with ClientSession(client_receive, client_send) as session:
                await session.initialize()
                listed = await session.list_tools()
                assert [tool.name for tool in listed.tools] == [f"stardew_{item}" for item in CAPABILITIES]
                for capability_id in CAPABILITIES:
                    result = await session.call_tool(f"stardew_{capability_id}", _arguments(capability_id))
                    assert result.isError is False
                    assert result.structuredContent == expected[capability_id]
            tasks.cancel_scope.cancel()

    anyio.run(exercise)
