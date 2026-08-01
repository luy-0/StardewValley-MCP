from __future__ import annotations

import asyncio
from pathlib import Path

from google.protobuf import json_format

from stardew_valley_mcp.catalog import Catalog, CatalogPolicy, descriptor_digest
from stardew_valley_mcp.command_runtime import CommandRuntime
from stardew_valley_mcp.protocol import actions_pb2, capabilities_pb2, common_pb2, queries_pb2, transport_pb2
from stardew_valley_mcp.transport import HandshakeRejectedError


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "spec" / "fixtures" / "v1" / "bootstrap"


def _snapshot() -> transport_pb2.CapabilitySnapshot:
    frame = transport_pb2.TransportFrame()
    json_format.Parse((FIXTURES / "server-ready.json").read_text(), frame)
    return frame.server_ready.capability_snapshot


def _snapshot_with_face() -> transport_pb2.CapabilitySnapshot:
    snapshot = _snapshot()
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
    return snapshot


def _success(command_id: str) -> capabilities_pb2.CommandEvent:
    frame = transport_pb2.TransportFrame()
    json_format.Parse((FIXTURES / "query-runtime.succeeded.json").read_text(), frame)
    frame.command_event.command_id = command_id
    return frame.command_event


def _event(command_id: str, state: int, *, reply_to: str = "") -> transport_pb2.TransportFrame:
    return transport_pb2.TransportFrame(
        message_id=f"s-{command_id[-1]}-{state}",
        reply_to=reply_to,
        fence=transport_pb2.SessionFence(session_id="session", lease_epoch=1, capability_digest="digest"),
        command_event=capabilities_pb2.CommandEvent(command_id=command_id, state=state),
    )


def _terminal(command_id: str) -> transport_pb2.TransportFrame:
    frame = _event(command_id, capabilities_pb2.COMMAND_STATE_SUCCEEDED)
    frame.command_event.CopyFrom(_success(command_id))
    return frame


class _QueueConnection:
    def __init__(self):
        self.snapshot = _snapshot()
        self.incoming: asyncio.Queue[object] = asyncio.Queue()
        self.sent: list[transport_pb2.TransportFrame] = []
        self.sequence = 0
        self.connect_count = 0
        self.closed = False
        self.reader_tasks: set[asyncio.Task[object]] = set()
        self.connect_errors: list[BaseException] = []

    async def connect(self):
        self.connect_count += 1
        if self.connect_errors:
            raise self.connect_errors.pop(0)
        self.closed = False
        return self.snapshot

    async def close(self):
        self.closed = True

    def next_message_id(self):
        self.sequence += 1
        return f"c-{self.sequence}"

    def fence(self):
        return transport_pb2.SessionFence(session_id="session", lease_epoch=1, capability_digest="digest")

    async def send_authenticated(self, frame):
        self.sent.append(frame)

    async def receive_authenticated(self):
        self.reader_tasks.add(asyncio.current_task())
        value = await self.incoming.get()
        if isinstance(value, BaseException):
            raise value
        return value


async def _until(predicate) -> None:
    for _ in range(100):
        if predicate():
            return
        await asyncio.sleep(0)
    raise AssertionError("等待条件超时")


def test_interleaved_running_events_use_one_reader_and_each_command_converges() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        first_id = "11111111-1111-4111-8111-111111111111"
        second_id = "22222222-2222-4222-8222-222222222222"
        first = asyncio.create_task(runtime.execute(first_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        second = asyncio.create_task(runtime.execute(second_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 2)
        request_ids = {frame.command_request.command_id: frame.message_id for frame in connection.sent}
        await connection.incoming.put(_event(first_id, capabilities_pb2.COMMAND_STATE_ACCEPTED, reply_to=request_ids[first_id]))
        await connection.incoming.put(_event(second_id, capabilities_pb2.COMMAND_STATE_ACCEPTED, reply_to=request_ids[second_id]))
        await connection.incoming.put(_event(first_id, capabilities_pb2.COMMAND_STATE_RUNNING))
        await connection.incoming.put(_event(second_id, capabilities_pb2.COMMAND_STATE_RUNNING))
        await connection.incoming.put(_event(first_id, capabilities_pb2.COMMAND_STATE_RUNNING))
        await connection.incoming.put(_terminal(second_id))
        await connection.incoming.put(_terminal(first_id))
        assert (await first)["status"] == "succeeded"
        second_result = await second
        assert second_result["status"] == "succeeded", second_result
        assert len(connection.reader_tasks) == 1
        await runtime.aclose()

    asyncio.run(exercise())


def test_cancel_current_snapshot_does_not_repeat_accepted_transition() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "33333333-3333-4333-8333-333333333333"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        await connection.incoming.put(_event(command_id, capabilities_pb2.COMMAND_STATE_ACCEPTED, reply_to="c-1"))
        await connection.incoming.put(_event(command_id, capabilities_pb2.COMMAND_STATE_RUNNING))
        cancel = asyncio.create_task(runtime.cancel(command_id, "test"))
        await _until(lambda: len(connection.sent) == 2)
        response = transport_pb2.TransportFrame(
            message_id="s-cancel",
            reply_to="c-2",
            fence=connection.fence(),
            cancel_command_response=capabilities_pb2.CancelCommandResponse(command_id=command_id, accepted=True),
        )
        response.cancel_command_response.current.CopyFrom(_event(command_id, capabilities_pb2.COMMAND_STATE_RUNNING).command_event)
        await connection.incoming.put(response)
        cancelled = _event(command_id, capabilities_pb2.COMMAND_STATE_CANCELLED)
        cancelled.command_event.error.code = common_pb2.ERROR_CODE_CANCELLED
        cancelled.command_event.error.message = "cancelled"
        await connection.incoming.put(cancelled)
        assert (await cancel).accepted is True
        assert (await execute)["error"]["code"] == "command_cancelled"
        await runtime.aclose()

    asyncio.run(exercise())


def test_disconnect_recovers_by_status_with_same_command_id_only() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "44444444-4444-4444-8444-444444444444"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        connection.connect_errors.append(
            HandshakeRejectedError(common_pb2.ERROR_CODE_BUSY, "旧连接仍在清理")
        )
        await connection.incoming.put(asyncio.IncompleteReadError(partial=b"", expected=1))
        await asyncio.sleep(0.06)
        await _until(lambda: len(connection.sent) == 2)
        status = connection.sent[1]
        assert status.WhichOneof("body") == "get_command_status_request"
        assert status.get_command_status_request.command_id == command_id
        response = transport_pb2.TransportFrame(
            message_id="s-status",
            reply_to=status.message_id,
            fence=connection.fence(),
            get_command_status_response=capabilities_pb2.GetCommandStatusResponse(command_id=command_id, found=True),
        )
        response.get_command_status_response.current.CopyFrom(_success(command_id))
        await connection.incoming.put(response)
        result = await execute
        assert result["status"] == "succeeded"
        assert [frame.WhichOneof("body") for frame in connection.sent] == ["command_request", "get_command_status_request"]
        assert connection.connect_count == 3
        await runtime.aclose()

    asyncio.run(exercise())


def test_disconnect_status_not_found_converges_to_unknown_without_replay() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "77777777-7777-4777-8777-777777777777"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        await connection.incoming.put(asyncio.IncompleteReadError(partial=b"", expected=1))
        await _until(lambda: len(connection.sent) == 2)
        status = connection.sent[1]
        await connection.incoming.put(
            transport_pb2.TransportFrame(
                message_id="s-not-found",
                reply_to=status.message_id,
                fence=connection.fence(),
                get_command_status_response=capabilities_pb2.GetCommandStatusResponse(command_id=command_id, found=False),
            )
        )

        result = await execute
        assert result["status"] == "unknown"
        assert result["error"]["code"] == "unknown_outcome"
        assert [frame.WhichOneof("body") for frame in connection.sent] == ["command_request", "get_command_status_request"]
        await runtime.aclose()

    asyncio.run(exercise())


def test_disconnect_status_idempotency_expired_converges_to_unknown() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "88888888-8888-4888-8888-888888888888"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        await connection.incoming.put(asyncio.IncompleteReadError(partial=b"", expected=1))
        await _until(lambda: len(connection.sent) == 2)
        status = connection.sent[1]
        await connection.incoming.put(
            transport_pb2.TransportFrame(
                message_id="s-expired",
                reply_to=status.message_id,
                fence=connection.fence(),
                protocol_error=transport_pb2.ProtocolError(
                    error=common_pb2.Error(
                        code=common_pb2.ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED,
                        message="retention expired",
                    )
                ),
            )
        )

        result = await execute
        assert result["status"] == "unknown"
        assert result["error"]["code"] == "unknown_outcome"
        assert [frame.WhichOneof("body") for frame in connection.sent] == ["command_request", "get_command_status_request"]
        await runtime.aclose()

    asyncio.run(exercise())


def test_invalid_known_event_is_protocol_failure_and_never_issues_status() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "55555555-5555-4555-8555-555555555555"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        invalid = _event(command_id, capabilities_pb2.COMMAND_STATE_RUNNING, reply_to="c-1")
        invalid.command_event.error.code = common_pb2.ERROR_CODE_INTERNAL
        invalid.command_event.error.message = "running must not have an outcome"
        await connection.incoming.put(invalid)

        result = await execute
        assert result["status"] == "failed"
        assert result["error"]["code"] == "upstream_protocol_error"
        assert [frame.WhichOneof("body") for frame in connection.sent] == ["command_request"]
        await runtime.aclose()

    asyncio.run(exercise())


def test_silent_control_response_times_out_and_cleans_its_waiter() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load(), discovery_timeout_seconds=0.02)

        try:
            await runtime.get_status("66666666-6666-4666-8666-666666666666")
        except asyncio.TimeoutError:
            pass
        else:
            raise AssertionError("无响应控制请求必须超时")

        assert [frame.WhichOneof("body") for frame in connection.sent] == ["get_command_status_request"]
        assert runtime._controls == {}
        await runtime.aclose()

    asyncio.run(exercise())


def test_cancelled_tool_call_sends_cancel_for_accepted_cancellable_command() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        connection.snapshot = _snapshot_with_face()
        catalog = Catalog.load(CatalogPolicy(frozenset({"face"}), frozenset({"game:write"})))
        runtime = CommandRuntime(connection, catalog)
        command_id = "99999999-9999-4999-8999-999999999999"
        execute = asyncio.create_task(
            runtime.execute(command_id, "face", actions_pb2.FaceRequest(direction=common_pb2.DIRECTION_UP))
        )
        await _until(lambda: len(connection.sent) == 1)
        await connection.incoming.put(
            _event(command_id, capabilities_pb2.COMMAND_STATE_ACCEPTED, reply_to=connection.sent[0].message_id)
        )
        await _until(lambda: runtime._commands[command_id].accepted)

        execute.cancel()
        await _until(lambda: len(connection.sent) == 2)
        cancel_request = connection.sent[1]
        assert cancel_request.WhichOneof("body") == "cancel_command_request"
        assert cancel_request.cancel_command_request.command_id == command_id
        cancel_response = transport_pb2.TransportFrame(
            message_id="s-client-cancel",
            reply_to=cancel_request.message_id,
            fence=connection.fence(),
            cancel_command_response=capabilities_pb2.CancelCommandResponse(
                command_id=command_id,
                accepted=True,
                current=capabilities_pb2.CommandEvent(
                    command_id=command_id,
                    state=capabilities_pb2.COMMAND_STATE_ACCEPTED,
                    phase="cancelling",
                ),
            ),
        )
        await connection.incoming.put(cancel_response)
        try:
            await execute
        except asyncio.CancelledError:
            pass
        else:
            raise AssertionError("被取消的 Tool task 必须保留取消语义")
        await runtime.aclose()

    asyncio.run(exercise())


def test_failed_event_rejects_error_code_outside_protocol_allowlist() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        await connection.incoming.put(
            _event(command_id, capabilities_pb2.COMMAND_STATE_ACCEPTED, reply_to=connection.sent[0].message_id)
        )
        failed = _event(command_id, capabilities_pb2.COMMAND_STATE_FAILED)
        failed.command_event.error.code = common_pb2.ERROR_CODE_BUSY
        failed.command_event.error.message = "invalid terminal error code"
        await connection.incoming.put(failed)

        result = await execute
        assert result["error"]["code"] == "upstream_protocol_error"
        await runtime.aclose()

    asyncio.run(exercise())


def test_failed_event_accepts_contextual_invalid_argument() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "abababab-abab-4bab-8bab-abababababab"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        await connection.incoming.put(
            _event(command_id, capabilities_pb2.COMMAND_STATE_ACCEPTED, reply_to=connection.sent[0].message_id)
        )
        failed = _event(command_id, capabilities_pb2.COMMAND_STATE_FAILED)
        failed.command_event.error.code = common_pb2.ERROR_CODE_INVALID_ARGUMENT
        failed.command_event.error.message = "引用类型与能力不匹配"
        await connection.incoming.put(failed)

        result = await execute
        assert result["error"]["code"] == "invalid_arguments"
        await runtime.aclose()

    asyncio.run(exercise())


def test_proactive_events_wait_for_correlated_acceptance_then_keep_order() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        await connection.incoming.put(_event(command_id, capabilities_pb2.COMMAND_STATE_RUNNING))
        await connection.incoming.put(_terminal(command_id))
        await asyncio.sleep(0)

        assert not execute.done()
        assert runtime._commands[command_id].pending_events
        await connection.incoming.put(
            _event(command_id, capabilities_pb2.COMMAND_STATE_ACCEPTED, reply_to=connection.sent[0].message_id)
        )

        result = await execute
        assert result["status"] == "succeeded"
        await runtime.aclose()

    asyncio.run(exercise())


def test_invalid_buffered_event_remains_protocol_failure_after_acceptance() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "bcbcbcbc-bcbc-4bcb-8bcb-bcbcbcbcbcbc"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        invalid = _event(command_id, capabilities_pb2.COMMAND_STATE_RUNNING)
        invalid.command_event.error.code = common_pb2.ERROR_CODE_INTERNAL
        invalid.command_event.error.message = "running must not have an outcome"
        await connection.incoming.put(invalid)
        await connection.incoming.put(
            _event(command_id, capabilities_pb2.COMMAND_STATE_ACCEPTED, reply_to=connection.sent[0].message_id)
        )

        result = await execute
        assert result["error"]["code"] == "upstream_protocol_error"
        await runtime.aclose()

    asyncio.run(exercise())


def test_correlated_cached_running_proves_acceptance_and_can_reach_terminal() -> None:
    async def exercise() -> None:
        connection = _QueueConnection()
        runtime = CommandRuntime(connection, Catalog.load())
        command_id = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
        execute = asyncio.create_task(runtime.execute(command_id, "query_runtime", queries_pb2.QueryRuntimeRequest()))
        await _until(lambda: len(connection.sent) == 1)
        await connection.incoming.put(
            _event(command_id, capabilities_pb2.COMMAND_STATE_RUNNING, reply_to=connection.sent[0].message_id)
        )
        await connection.incoming.put(_terminal(command_id))

        result = await execute
        assert result["status"] == "succeeded"
        await runtime.aclose()

    asyncio.run(exercise())
