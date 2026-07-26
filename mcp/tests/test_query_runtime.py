from __future__ import annotations

import asyncio
import base64
import hmac
import json
from pathlib import Path

import anyio
from google.protobuf import json_format
from jsonschema import Draft202012Validator
from mcp import ClientSession

from stardew_valley_mcp.protocol import transport_pb2
from stardew_valley_mcp.server import create_server, load_tool
from stardew_valley_mcp.transport import (
    ConnectionConfig,
    QueryRuntimeClient,
    capability_digest,
    client_auth_tag,
    read_frame,
    server_auth_tag,
    write_frame,
)


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "spec" / "fixtures" / "v1" / "bootstrap"
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
    assert capability_digest(ready.capability_snapshot.capabilities) == vector["capabilityDigest"]
    assert server_auth_tag(
        base64.b64decode(vector["secretBase64"]),
        vector["modInstanceId"],
        vector["clientInstanceId"],
        base64.b64decode(vector["serverNonceBase64"]),
        base64.b64decode(vector["clientNonceBase64"]),
        ready,
    ) == base64.b64decode(vector["serverAuthTagBase64"])


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
        result = await QueryRuntimeClient(ConnectionConfig("127.0.0.1", port, SECRET)).query_runtime()
        await completed
        return result
    finally:
        server.close()
        await server.wait_closed()


def test_query_runtime_success_matches_generated_output_schema() -> None:
    result = asyncio.run(run_query("query-runtime.succeeded.json"))
    assert result["status"] == "succeeded"
    assert result["output"]["snapshot"]["player"]["position"]["locationId"] == "Farm"
    Draft202012Validator(load_tool().outputSchema).validate(result)


def test_query_runtime_not_ready_matches_generated_output_schema() -> None:
    result = asyncio.run(run_query("query-runtime.not-ready.json"))
    assert result["status"] == "failed"
    assert result["error"]["code"] == "not_ready"
    Draft202012Validator(load_tool().outputSchema).validate(result)


def test_mcp_server_exposes_and_calls_only_query_runtime() -> None:
    expected = asyncio.run(run_query("query-runtime.succeeded.json"))

    class StubClient:
        async def available(self) -> bool:
            return True

        async def query_runtime(self) -> dict[str, object]:
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
