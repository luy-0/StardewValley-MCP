from __future__ import annotations

import asyncio
import base64
import hmac
import json
import time
from pathlib import Path

import anyio
import pytest
from google.protobuf import json_format
from jsonschema import Draft202012Validator
from mcp import ClientSession

from stardew_valley_mcp.protocol import capabilities_pb2, common_pb2, queries_pb2, transport_pb2
from stardew_valley_mcp.catalog import Catalog, CatalogPolicy, descriptor_digest
from stardew_valley_mcp.client import StardewClient, _operation_for
from stardew_valley_mcp.command_runtime import CommandRuntime
from stardew_valley_mcp.projection import project_message
from stardew_valley_mcp.server import create_server
from stardew_valley_mcp.transport import (
    ConnectionConfig,
    client_auth_tag,
    read_frame,
    server_auth_tag,
    ProtocolError,
    TransportConnection,
    write_frame,
)


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "spec" / "fixtures" / "v1" / "bootstrap"
OBSERVATION_FIXTURES = ROOT / "spec" / "fixtures" / "v1" / "observation"
SECRET = bytes(range(32))


def fixture(name: str) -> transport_pb2.TransportFrame:
    frame = transport_pb2.TransportFrame()
    json_format.Parse((FIXTURES / name).read_text(), frame)
    return frame


def test_hmac_and_capability_digest_match_public_fixtures() -> None:
    vector = json.loads((FIXTURES / "hmac-sha256.json").read_text())
    assert client_auth_tag(
        base64.b64decode(vector["secretBase64"]),
        vector["modInstanceId"],
        vector["clientInstanceId"],
        base64.b64decode(vector["serverNonceBase64"]),
        base64.b64decode(vector["clientNonceBase64"]),
        vector["resumeSessionId"],
    ) == base64.b64decode(vector["clientAuthTagBase64"])
    ready = fixture("server-ready.json").server_ready
    assert descriptor_digest(ready.capability_snapshot.capabilities) == vector["capabilityDigest"]
    assert server_auth_tag(
        base64.b64decode(vector["secretBase64"]),
        vector["modInstanceId"],
        vector["clientInstanceId"],
        base64.b64decode(vector["serverNonceBase64"]),
        base64.b64decode(vector["clientNonceBase64"]),
        ready,
    ) == base64.b64decode(vector["serverAuthTagBase64"])


def test_handshake_rejects_server_ready_below_recovery_retention_minimums() -> None:
    async def rejects(field: str, value: int) -> None:
        async def handle(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
            try:
                hello = transport_pb2.TransportFrame(
                    message_id="s-1",
                    server_hello=transport_pb2.ServerHello(
                        version=transport_pb2.ProtocolVersion(major=1, minor=0),
                        mod_instance_id="d0b63f0c-2b4e-4c10-9d20-1234567890ab",
                        server_nonce=bytes(range(32, 64)),
                    ),
                )
                await write_frame(writer, hello)
                client_frame = await read_frame(reader)
                ready_frame = fixture("server-ready.json")
                ready_frame.message_id = "s-2"
                ready_frame.reply_to = client_frame.message_id
                setattr(ready_frame.server_ready, field, value)
                ready_frame.server_ready.auth_tag = server_auth_tag(
                    SECRET,
                    hello.server_hello.mod_instance_id,
                    client_frame.client_hello.client_instance_id,
                    hello.server_hello.server_nonce,
                    client_frame.client_hello.client_nonce,
                    ready_frame.server_ready,
                )
                await write_frame(writer, ready_frame)
            finally:
                writer.close()
                await writer.wait_closed()

        server = await asyncio.start_server(handle, "127.0.0.1", 0)
        port = server.sockets[0].getsockname()[1]
        connection = TransportConnection(ConnectionConfig("127.0.0.1", port, SECRET))
        try:
            try:
                await connection.connect()
            except ProtocolError:
                pass
            else:
                raise AssertionError(f"{field} 低于下限必须拒绝")
        finally:
            await connection.close()
            server.close()
            await server.wait_closed()

    async def exercise() -> None:
        await rejects("result_retention_ms", 299_999)
        await rejects("reconnect_grace_ms", 9_999)

    asyncio.run(exercise())


async def run_query(terminal_fixture: str) -> dict[str, object]:
    completed = asyncio.get_running_loop().create_future()

    async def handle(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        try:
            hello = transport_pb2.TransportFrame(
                message_id="s-1",
                server_hello=transport_pb2.ServerHello(
                    version=transport_pb2.ProtocolVersion(major=1, minor=0),
                    mod_instance_id="d0b63f0c-2b4e-4c10-9d20-1234567890ab",
                    server_nonce=bytes(range(32, 64)),
                ),
            )
            await write_frame(writer, hello)
            client_frame = await read_frame(reader)
            client = client_frame.client_hello
            assert hmac.compare_digest(
                client.auth_tag,
                client_auth_tag(
                    SECRET,
                    hello.server_hello.mod_instance_id,
                    client.client_instance_id,
                    hello.server_hello.server_nonce,
                    client.client_nonce,
                    client.resume_session_id,
                ),
            )

            ready_frame = fixture("server-ready.json")
            ready_frame.message_id = "s-2"
            ready_frame.reply_to = client_frame.message_id
            ready_frame.server_ready.auth_tag = server_auth_tag(
                SECRET,
                hello.server_hello.mod_instance_id,
                client.client_instance_id,
                hello.server_hello.server_nonce,
                client.client_nonce,
                ready_frame.server_ready,
            )
            await write_frame(writer, ready_frame)

            request = await read_frame(reader)
            command_id = request.command_request.command_id
            accepted = fixture("query-runtime.accepted.json")
            accepted.message_id = "s-3"
            accepted.reply_to = request.message_id
            accepted.command_event.command_id = command_id
            await write_frame(writer, accepted)

            terminal = fixture(terminal_fixture)
            terminal.message_id = "s-4"
            terminal.reply_to = ""
            terminal.command_event.command_id = command_id
            await write_frame(writer, terminal)
            completed.set_result(None)
        except BaseException as error:
            if not completed.done():
                completed.set_exception(error)
        finally:
            writer.close()
            await writer.wait_closed()

    server = await asyncio.start_server(handle, "127.0.0.1", 0)
    port = server.sockets[0].getsockname()[1]
    try:
        result = await StardewClient(ConnectionConfig("127.0.0.1", port, SECRET)).query_runtime()
        await completed
        return result
    finally:
        server.close()
        await server.wait_closed()


def test_query_runtime_success_matches_generated_output_schema() -> None:
    result = asyncio.run(run_query("query-runtime.succeeded.json"))
    assert result["status"] == "succeeded"
    snapshot = result["output"]["snapshot"]
    assert snapshot["player"]["position"]["locationId"] == "Farm"
    assert snapshot["weather"]["kind"] == "sun"
    assert snapshot["weather"]["tomorrow"] == "rain"
    assert snapshot["dailyLuck"] == {"value": 0.08, "tier": "very_lucky"}
    assert snapshot["queenOfSauce"]["learnable"] is True
    assert snapshot["queenOfSauce"]["recipeKey"] == "Stir Fry"
    Draft202012Validator(Catalog.load().tool("query_runtime").outputSchema).validate(result)


def test_query_runtime_not_ready_matches_generated_output_schema() -> None:
    result = asyncio.run(run_query("query-runtime.not-ready.json"))
    assert result["status"] == "failed"
    assert result["error"]["code"] == "not_ready"
    Draft202012Validator(Catalog.load().tool("query_runtime").outputSchema).validate(result)


def test_catalog_intersection_and_descriptor_projection_cover_observation_fixtures() -> None:
    ready = transport_pb2.TransportFrame()
    json_format.Parse((OBSERVATION_FIXTURES / "server-ready.json").read_text(), ready)
    catalog = Catalog.load()
    assert len(catalog.capability_ids) == 22
    assert [tool.name for tool in catalog.tools_for(ready.server_ready.capability_snapshot)] == [
        "stardew_inspect",
        "stardew_query_inventory",
        "stardew_query_players",
        "stardew_query_runtime",
        "stardew_query_ui",
        "stardew_query_world",
    ]
    for capability, fixture_name in [("query_runtime", "query-runtime.success.json"), ("query_world", "query-world.success-complete.json"), ("inspect", "inspect.success-complete.json")]:
        frame = transport_pb2.TransportFrame()
        json_format.Parse((OBSERVATION_FIXTURES / fixture_name).read_text(), frame)
        result = {"status": "succeeded", "commandId": frame.command_event.command_id, "output": project_message(getattr(frame.command_event.result, capability))}
        Draft202012Validator(catalog.tool(capability).outputSchema).validate(result)
    inspect_output = project_message(getattr(frame.command_event.result, "inspect"))
    assert inspect_output["items"][0]["resolution"]["status"] == "resolved"


def test_catalog_requires_mcp_object_root_for_every_tool_schema() -> None:
    catalog = Catalog.load()

    assert len(catalog.capability_ids) == 22
    for capability_id in catalog.capability_ids:
        tool = catalog.tool(capability_id)
        assert tool.inputSchema["type"] == "object"
        assert tool.outputSchema is not None
        assert tool.outputSchema["type"] == "object"
        assert len(tool.outputSchema["oneOf"]) == 3


def test_catalog_rejects_generated_output_schema_without_mcp_object_root() -> None:
    document = json.loads(
        (
            ROOT
            / "mcp"
            / "src"
            / "stardew_valley_mcp"
            / "generated"
            / "tool_catalog.json"
        ).read_text(encoding="utf-8")
    )
    document["tools"][0]["outputSchema"].pop("type")

    with pytest.raises(ValueError, match="outputSchema 顶层 type 必须是 object"):
        Catalog(document)


def test_catalog_rejects_unknown_mod_capability_and_injects_scope_policy() -> None:
    ready = transport_pb2.TransportFrame()
    json_format.Parse((OBSERVATION_FIXTURES / "server-ready.json").read_text(), ready)
    snapshot = ready.server_ready.capability_snapshot
    unknown = snapshot.capabilities.add()
    unknown.id = "unknown"
    unknown.contract_version = "1.0.0"
    snapshot.digest = descriptor_digest(snapshot.capabilities)
    try:
        Catalog.load().validate_snapshot(snapshot)
    except ValueError as error:
        assert "未知" in str(error)
    else:
        raise AssertionError("unknown descriptor 必须拒绝")
    json_format.Parse((OBSERVATION_FIXTURES / "server-ready.json").read_text(), ready)
    policy = CatalogPolicy(frozenset({"query_runtime", "inspect"}), frozenset({"game:read"}))
    assert [tool.name for tool in Catalog.load(policy).tools_for(ready.server_ready.capability_snapshot)] == ["stardew_inspect", "stardew_query_runtime"]
    denied = CatalogPolicy(frozenset({"query_runtime", "inspect"}), frozenset())
    assert Catalog.load(denied).tools_for(ready.server_ready.capability_snapshot) == []


def test_announced_action_enters_tools_through_catalog_snapshot_and_scope_intersection() -> None:
    ready = transport_pb2.TransportFrame()
    json_format.Parse((OBSERVATION_FIXTURES / "server-ready.json").read_text(), ready)
    snapshot = ready.server_ready.capability_snapshot
    snapshot.capabilities.add(
        id="face",
        contract_version="1.0.0",
        side_effect=transport_pb2.SIDE_EFFECT_MUTATING,
        execution=transport_pb2.EXECUTION_MODE_LONG_RUNNING,
        cancellable=True,
        default_timeout_ms=5_000,
        max_timeout_ms=15_000,
        request_type="FaceRequest",
        result_type="FaceResult",
        required_scope="game:write",
        destructive=False,
    )
    snapshot.digest = descriptor_digest(snapshot.capabilities)

    read_only = Catalog.load().tools_for(snapshot)
    read_write = Catalog.load(
        CatalogPolicy(None, frozenset({"game:read", "game:write"}))
    ).tools_for(snapshot)

    assert "stardew_face" not in {tool.name for tool in read_only}
    assert "stardew_face" in {tool.name for tool in read_write}


def test_descriptor_risk_order_is_not_protocol_semantics() -> None:
    snapshot = transport_pb2.CapabilitySnapshot()
    descriptor = snapshot.capabilities.add(
        id="activate_ui",
        contract_version="1.0.0",
        side_effect=transport_pb2.SIDE_EFFECT_MUTATING,
        execution=transport_pb2.EXECUTION_MODE_LONG_RUNNING,
        cancellable=True,
        default_timeout_ms=10_000,
        max_timeout_ms=30_000,
        request_type="ActivateUiRequest",
        result_type="ActivateUiResult",
        required_scope="game:write",
        destructive=True,
    )
    descriptor.risks.extend(
        ["changes_relationship", "spends_money", "consumes_item", "advances_time", "changes_save"]
    )
    snapshot.digest = descriptor_digest(snapshot.capabilities)

    Catalog.load().validate_snapshot(snapshot)


def test_catalog_unknown_enum_number_is_stable_value_error() -> None:
    ready = transport_pb2.TransportFrame()
    json_format.Parse((OBSERVATION_FIXTURES / "server-ready.json").read_text(), ready)
    snapshot = ready.server_ready.capability_snapshot
    snapshot.capabilities[0].side_effect = 99
    snapshot.digest = descriptor_digest(snapshot.capabilities)
    try:
        Catalog.load().validate_snapshot(snapshot)
    except ValueError as error:
        assert "未知 enum" in str(error)
    else:
        raise AssertionError("未知 enum number 必须稳定拒绝")


def test_projection_bytes_follow_proto_json_base64() -> None:
    assert project_message(transport_pb2.ClientHello(client_nonce=b"\xff\x00"))["clientNonce"] == "/wA="


def test_client_uses_one_injected_catalog_for_list_and_call_policy() -> None:
    denied = Catalog.load(CatalogPolicy(frozenset({"query_runtime"}), frozenset()))
    client = StardewClient(ConnectionConfig("127.0.0.1", 1, SECRET), denied)
    assert client._catalog is denied
    result = asyncio.run(client.call_tool("stardew_query_runtime", {}))
    assert result["status"] == "failed"
    assert result["error"]["code"] == "capability_denied"
    assert result["error"]["retryable"] is False
    Draft202012Validator(denied.tool("query_runtime").outputSchema).validate(result)


def test_local_invalid_arguments_has_command_id_and_matches_output_schema() -> None:
    client = StardewClient(ConnectionConfig("127.0.0.1", 1, SECRET))
    result = asyncio.run(client.call_tool("stardew_query_runtime", {"unexpected": True}))
    assert result["status"] == "failed"
    assert result["commandId"]
    assert result["error"]["code"] == "invalid_arguments"
    Draft202012Validator(Catalog.load().tool("query_runtime").outputSchema).validate(result)


def test_operation_factory_builds_action_request_from_command_descriptor() -> None:
    operation = _operation_for("face", {"direction": "left"})

    assert operation.DESCRIPTOR.name == "FaceRequest"
    assert operation.direction == common_pb2.DIRECTION_LEFT


def test_silent_tcp_peer_cannot_hang_discovery_or_execute() -> None:
    async def exercise() -> None:
        async def silent(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
            await reader.read()
            writer.close()
            await writer.wait_closed()

        server = await asyncio.start_server(silent, "127.0.0.1", 0)
        port = server.sockets[0].getsockname()[1]
        runtime = CommandRuntime(
            TransportConnection(ConnectionConfig("127.0.0.1", port, SECRET)),
            Catalog.load(),
            discovery_timeout_seconds=0.05,
        )
        started = time.monotonic()
        assert await runtime.available_tools() == []
        command_id = runtime.new_command_id()
        result = await runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest())
        assert result["commandId"] == command_id
        assert result["error"]["code"] == "route_unavailable"
        assert time.monotonic() - started < 1.0
        server.close()
        await server.wait_closed()

    asyncio.run(exercise())


class _FakeConnection:
    def __init__(self, snapshot, frames=()):
        self.snapshot = snapshot
        self.frames = list(frames)
        self.closed = False
        self._sent = asyncio.Event()

    async def connect(self):
        return self.snapshot

    async def close(self):
        self.closed = True

    def next_message_id(self):
        return "c-1"

    def fence(self):
        return transport_pb2.SessionFence()

    async def send_authenticated(self, frame):
        self._sent.set()

    async def receive_authenticated(self):
        await self._sent.wait()
        if self.frames:
            return self.frames.pop(0)
        await asyncio.Future()


def test_typed_request_mismatch_is_schema_valid_upstream_error() -> None:
    snapshot = fixture("server-ready.json").server_ready.capability_snapshot
    runtime = CommandRuntime(_FakeConnection(snapshot), Catalog.load())
    command_id = runtime.new_command_id()
    result = asyncio.run(runtime.execute(command_id, "query_runtime", queries_pb2.QueryWorldRequest()))
    assert result["commandId"] == command_id
    assert result["error"]["code"] == "upstream_protocol_error"
    Draft202012Validator(Catalog.load().tool("query_runtime").outputSchema).validate(result)


def test_unknown_result_enum_maps_to_schema_valid_upstream_error() -> None:
    snapshot = fixture("server-ready.json").server_ready.capability_snapshot
    command_id = "55555555-5555-4555-8555-555555555555"
    accepted = transport_pb2.TransportFrame(
        message_id="s-3",
        reply_to="c-1",
        command_event=capabilities_pb2.CommandEvent(command_id=command_id, state=capabilities_pb2.COMMAND_STATE_ACCEPTED),
    )
    terminal = transport_pb2.TransportFrame(
        message_id="s-4",
        command_event=capabilities_pb2.CommandEvent(command_id=command_id, state=capabilities_pb2.COMMAND_STATE_SUCCEEDED),
    )
    terminal.command_event.result.query_runtime.snapshot.player.facing = 99
    runtime = CommandRuntime(_FakeConnection(snapshot, [accepted, terminal]), Catalog.load())
    result = asyncio.run(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
    assert result["error"]["code"] == "upstream_protocol_error"
    Draft202012Validator(Catalog.load().tool("query_runtime").outputSchema).validate(result)


def test_success_with_empty_location_id_is_rejected_and_connection_closed() -> None:
    ready = transport_pb2.TransportFrame()
    json_format.Parse((OBSERVATION_FIXTURES / "server-ready.json").read_text(), ready)
    command_id = "11111111-1111-4111-8111-111111111111"
    accepted = transport_pb2.TransportFrame(
        message_id="s-3",
        reply_to="c-1",
        command_event=capabilities_pb2.CommandEvent(
            command_id=command_id,
            state=capabilities_pb2.COMMAND_STATE_ACCEPTED,
        ),
    )
    terminal = transport_pb2.TransportFrame()
    json_format.Parse(
        (OBSERVATION_FIXTURES / "query-world.success-complete.json").read_text(),
        terminal,
    )
    terminal.reply_to = "c-1"
    terminal.command_event.command_id = command_id
    terminal.command_event.result.query_world.snapshot.area.location_id = ""
    connection = _FakeConnection(ready.server_ready.capability_snapshot, [accepted, terminal])
    runtime = CommandRuntime(connection, Catalog.load())

    result = asyncio.run(
        runtime.execute(command_id, "query_world", queries_pb2.QueryWorldRequest())
    )

    assert result["status"] == "failed"
    assert result["error"] == {
        "code": "upstream_protocol_error",
        "message": "本地 Mod 返回了无效协议响应",
        "retryable": False,
    }
    assert connection.closed is True
    Draft202012Validator(Catalog.load().tool("query_world").outputSchema).validate(result)


def test_success_missing_required_fact_is_rejected_and_connection_closed() -> None:
    snapshot = fixture("server-ready.json").server_ready.capability_snapshot
    command_id = "55555555-5555-4555-8555-555555555555"
    accepted = transport_pb2.TransportFrame(
        message_id="s-3",
        reply_to="c-1",
        command_event=capabilities_pb2.CommandEvent(
            command_id=command_id,
            state=capabilities_pb2.COMMAND_STATE_ACCEPTED,
        ),
    )
    terminal = fixture("query-runtime.succeeded.json")
    terminal.reply_to = "c-1"
    terminal.command_event.command_id = command_id
    terminal.command_event.result.query_runtime.ClearField("snapshot")
    connection = _FakeConnection(snapshot, [accepted, terminal])
    runtime = CommandRuntime(connection, Catalog.load())

    result = asyncio.run(
        runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest())
    )

    assert result["status"] == "failed"
    assert result["error"]["code"] == "upstream_protocol_error"
    assert connection.closed is True
    Draft202012Validator(Catalog.load().tool("query_runtime").outputSchema).validate(result)


def test_schema_valid_success_remains_succeeded_and_connection_open() -> None:
    snapshot = fixture("server-ready.json").server_ready.capability_snapshot
    command_id = "55555555-5555-4555-8555-555555555555"
    terminal = fixture("query-runtime.succeeded.json")
    terminal.message_id = "s-cached"
    terminal.reply_to = "c-1"
    terminal.command_event.command_id = command_id
    connection = _FakeConnection(snapshot, [terminal])
    runtime = CommandRuntime(connection, Catalog.load())

    result = asyncio.run(
        runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest())
    )

    assert result["status"] == "succeeded"
    assert connection.closed is False
    Draft202012Validator(Catalog.load().tool("query_runtime").outputSchema).validate(result)


def test_cached_terminal_direct_response_succeeds_without_accepted() -> None:
    snapshot = fixture("server-ready.json").server_ready.capability_snapshot
    command_id = "55555555-5555-4555-8555-555555555555"
    terminal = fixture("query-runtime.succeeded.json")
    terminal.message_id = "s-cached"
    terminal.reply_to = "c-1"
    terminal.command_event.command_id = command_id
    runtime = CommandRuntime(_FakeConnection(snapshot, [terminal]), Catalog.load())
    result = asyncio.run(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
    assert result["status"] == "succeeded"
    assert result["commandId"] == command_id
    Draft202012Validator(Catalog.load().tool("query_runtime").outputSchema).validate(result)


def test_proactive_terminal_can_arrive_before_correlated_accepted() -> None:
    snapshot = fixture("server-ready.json").server_ready.capability_snapshot
    command_id = "55555555-5555-4555-8555-555555555555"
    terminal = fixture("query-runtime.succeeded.json")
    terminal.message_id = "s-unsolicited"
    terminal.reply_to = ""
    terminal.command_event.command_id = command_id
    accepted = transport_pb2.TransportFrame(
        message_id="s-accepted",
        reply_to="c-1",
        command_event=capabilities_pb2.CommandEvent(
            command_id=command_id,
            state=capabilities_pb2.COMMAND_STATE_ACCEPTED,
        ),
    )
    runtime = CommandRuntime(_FakeConnection(snapshot, [terminal, accepted]), Catalog.load())
    result = asyncio.run(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
    assert result["status"] == "succeeded"


def test_mcp_server_exposes_and_calls_only_query_runtime() -> None:
    expected = asyncio.run(run_query("query-runtime.succeeded.json"))

    class StubClient:
        async def available_tools(self):
            return [Catalog.load().tool("query_runtime")]

        async def call_tool(self, name: str, arguments: dict[str, object]) -> dict[str, object]:
            assert name == "stardew_query_runtime"
            assert arguments == {}
            return expected

    async def exercise() -> None:
        server = create_server(StubClient())
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
                assert [tool.name for tool in tools.tools] == ["stardew_query_runtime"]
                result = await session.call_tool("stardew_query_runtime", {})
                assert result.isError is False
                assert result.structuredContent == expected
            tasks.cancel_scope.cancel()

    anyio.run(exercise)
