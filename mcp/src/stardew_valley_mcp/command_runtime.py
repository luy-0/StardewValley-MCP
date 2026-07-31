"""命令等待、单 Reader 分发与同 ID 断线收敛。"""

from __future__ import annotations

import asyncio
import uuid
from dataclasses import dataclass, field
from typing import Any

from google.protobuf.message import Message
from jsonschema import Draft202012Validator, ValidationError

from .catalog import Catalog
from .projection import project_message
from .protocol import capabilities_pb2, common_pb2, transport_pb2
from .transport import ProtocolError, TransportConnection


_ERRORS = {
    common_pb2.ERROR_CODE_INVALID_ARGUMENT: ("failed", "invalid_arguments", False),
    common_pb2.ERROR_CODE_UNAUTHENTICATED: ("failed", "unauthenticated", True),
    common_pb2.ERROR_CODE_PERMISSION_DENIED: ("failed", "capability_denied", False),
    common_pb2.ERROR_CODE_UNSUPPORTED_VERSION: ("failed", "upstream_protocol_error", False),
    common_pb2.ERROR_CODE_UNSUPPORTED_CAPABILITY: ("failed", "capability_denied", False),
    common_pb2.ERROR_CODE_CAPABILITY_SET_CHANGED: ("failed", "capability_changed", True),
    common_pb2.ERROR_CODE_STALE_LEASE: ("failed", "context_expired", True),
    common_pb2.ERROR_CODE_CONFLICT: ("failed", "conflict", False),
    common_pb2.ERROR_CODE_BUSY: ("failed", "busy", True),
    common_pb2.ERROR_CODE_NOT_READY: ("failed", "not_ready", True),
    common_pb2.ERROR_CODE_NOT_FOUND: ("failed", "not_found", False),
    common_pb2.ERROR_CODE_DEADLINE_EXCEEDED: ("failed", "command_timeout", False),
    common_pb2.ERROR_CODE_CANCELLED: ("failed", "command_cancelled", False),
    common_pb2.ERROR_CODE_STALE_REF: ("failed", "stale_ref", False),
    common_pb2.ERROR_CODE_OUT_OF_RANGE: ("failed", "out_of_range", False),
    common_pb2.ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED: ("unknown", "unknown_outcome", False),
    common_pb2.ERROR_CODE_EXECUTION_FAILED: ("failed", "execution_failed", False),
    common_pb2.ERROR_CODE_PROTOCOL_VIOLATION: ("failed", "upstream_protocol_error", False),
    common_pb2.ERROR_CODE_INTERNAL: ("failed", "internal_error", False),
}

DEFAULT_DISCOVERY_TIMEOUT_SECONDS = 6.0
DEFAULT_TRANSPORT_MARGIN_SECONDS = 1.0
_TERMINAL = {
    capabilities_pb2.COMMAND_STATE_SUCCEEDED,
    capabilities_pb2.COMMAND_STATE_FAILED,
    capabilities_pb2.COMMAND_STATE_CANCELLED,
    capabilities_pb2.COMMAND_STATE_TIMED_OUT,
}
_FAILED_ERROR_CODES = {
    common_pb2.ERROR_CODE_INVALID_ARGUMENT,
    common_pb2.ERROR_CODE_NOT_READY,
    common_pb2.ERROR_CODE_NOT_FOUND,
    common_pb2.ERROR_CODE_STALE_REF,
    common_pb2.ERROR_CODE_OUT_OF_RANGE,
    common_pb2.ERROR_CODE_EXECUTION_FAILED,
    common_pb2.ERROR_CODE_INTERNAL,
}


def _error(command_id: str, error: common_pb2.Error) -> dict[str, object]:
    status, code, retryable = _ERRORS.get(error.code, ("failed", "upstream_protocol_error", False))
    return {"status": status, "commandId": command_id, "error": {"code": code, "message": error.message[:512] or "Mod 返回错误", "retryable": retryable}}


def _unknown(command_id: str) -> dict[str, object]:
    return {"status": "unknown", "commandId": command_id, "error": {"code": "unknown_outcome", "message": "无法确认命令终态", "retryable": False}}


def _clone(message: Message) -> Message:
    copy = type(message)()
    copy.CopyFrom(message)
    return copy


class _ConnectionLost(Exception):
    pass


class _ProtocolResponse(Exception):
    def __init__(self, error: common_pb2.Error):
        self.error = error


@dataclass
class _CommandWaiter:
    command_id: str
    capability_id: str
    request_id: str
    terminal: asyncio.Future[capabilities_pb2.CommandEvent]
    disconnected: asyncio.Event = field(default_factory=asyncio.Event)
    current: capabilities_pb2.CommandEvent | None = None
    accepted: bool = False
    sent: bool = False


@dataclass
class _ControlWaiter:
    body: str
    command_id: str
    future: asyncio.Future[transport_pb2.TransportFrame] | None


class CommandRuntime:
    """认证后由一个 task 读取 Frame，其他调用者只注册并等待 future。"""

    def __init__(
        self,
        connection: TransportConnection,
        catalog: Catalog,
        *,
        discovery_timeout_seconds: float = DEFAULT_DISCOVERY_TIMEOUT_SECONDS,
        transport_margin_seconds: float = DEFAULT_TRANSPORT_MARGIN_SECONDS,
    ):
        self._connection = connection
        self._catalog = catalog
        self._discovery_timeout_seconds = discovery_timeout_seconds
        self._transport_margin_seconds = transport_margin_seconds
        self._lifecycle_lock = asyncio.Lock()
        self._send_lock = asyncio.Lock()
        self._reader_task: asyncio.Task[None] | None = None
        self._generation = 0
        self._closed = False
        self._commands: dict[str, _CommandWaiter] = {}
        self._controls: dict[str, _ControlWaiter] = {}
        self._output_validators: dict[str, Draft202012Validator] = {}

    async def available_tools(self) -> list[Any]:
        try:
            snapshot = await self._ensure_dispatcher()
            return self._catalog.tools_for(snapshot)
        except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError, ProtocolError, ValueError):
            await self._connection.close()
            return []

    @staticmethod
    def new_command_id() -> str:
        return str(uuid.uuid4())

    async def execute(self, command_id: str, capability_id: str, operation: Message) -> dict[str, object]:
        try:
            snapshot = await self._ensure_dispatcher()
            descriptor = self._catalog.descriptor(capability_id, snapshot)
            if operation.DESCRIPTOR.name != descriptor.request_type:
                raise ProtocolError(
                    f"请求类型不匹配: expected={descriptor.request_type} actual={operation.DESCRIPTOR.full_name}"
                )
        except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError):
            await self._connection.close()
            return self._route_unavailable(command_id)
        except (ProtocolError, ValueError):
            return self._protocol_failure(command_id)

        loop = asyncio.get_running_loop()
        deadline = loop.time() + descriptor.max_timeout_ms / 1000 + self._transport_margin_seconds
        request_id = self._connection.next_message_id()
        waiter = _CommandWaiter(command_id, capability_id, request_id, loop.create_future())
        request = capabilities_pb2.CommandRequest(command_id=command_id, timeout_ms=descriptor.default_timeout_ms)
        getattr(request, capability_id).CopyFrom(operation)
        try:
            async with self._send_lock:
                if command_id in self._commands:
                    raise ProtocolError("当前 MCP Runtime 已在等待相同 command_id")
                self._commands[command_id] = waiter
                self._controls[request_id] = _ControlWaiter("command_request", command_id, None)
                waiter.sent = True
                await self._connection.send_authenticated(
                    transport_pb2.TransportFrame(
                        message_id=request_id,
                        fence=self._connection.fence(),
                        command_request=request,
                    )
                )
        except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError):
            try:
                return await self._recover_or_unknown(waiter, deadline)
            finally:
                self._discard_waiter(waiter)
        except (ProtocolError, ValueError):
            self._discard_waiter(waiter)
            return self._protocol_failure(command_id)

        try:
            terminal = await self._wait_terminal(waiter, deadline)
            return self._project_terminal(waiter, terminal)
        except asyncio.CancelledError:
            if descriptor.cancellable and waiter.accepted and not waiter.terminal.done():
                try:
                    await asyncio.shield(self.cancel(command_id, "MCP Tool 调用已取消"))
                except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError, ProtocolError, ValueError, _ConnectionLost, _ProtocolResponse):
                    pass
            raise
        except _ProtocolResponse as response:
            return _error(command_id, response.error)
        except (_ConnectionLost, asyncio.TimeoutError):
            return await self._recover_or_unknown(waiter, deadline)
        except (ProtocolError, ValueError):
            await self._connection.close()
            return self._protocol_failure(command_id)
        finally:
            self._discard_waiter(waiter)

    async def cancel(self, command_id: str, reason: str) -> capabilities_pb2.CancelCommandResponse:
        frame = await self._send_control(
            "cancel_command_response",
            command_id,
            lambda: capabilities_pb2.CancelCommandRequest(command_id=command_id, reason=reason),
        )
        response = frame.cancel_command_response
        if response.command_id != command_id:
            raise ProtocolError("CancelCommandResponse command_id 不匹配")
        if response.accepted:
            if not response.HasField("current") or response.HasField("error"):
                raise ProtocolError("接受取消的响应形状无效")
            if response.current.state not in {
                capabilities_pb2.COMMAND_STATE_ACCEPTED,
                capabilities_pb2.COMMAND_STATE_RUNNING,
            }:
                raise ProtocolError("接受取消时 current 必须仍为活动状态")
        else:
            if not response.HasField("error") or response.error.code not in {
                common_pb2.ERROR_CODE_NOT_FOUND,
                common_pb2.ERROR_CODE_CONFLICT,
            }:
                raise ProtocolError("拒绝取消的错误码无效")
            if response.error.code == common_pb2.ERROR_CODE_CONFLICT and not response.HasField("current"):
                raise ProtocolError("已知命令拒绝取消时必须回显 current")
            if response.error.code == common_pb2.ERROR_CODE_NOT_FOUND and response.HasField("current"):
                raise ProtocolError("未知命令拒绝取消时不得回显 current")
        if response.HasField("current") and command_id in self._commands:
            self._apply_event(self._commands[command_id], response.current, snapshot=True)
        return response

    async def get_status(self, command_id: str) -> capabilities_pb2.CommandEvent | None:
        frame = await self._send_control(
            "get_command_status_response",
            command_id,
            lambda: capabilities_pb2.GetCommandStatusRequest(command_id=command_id),
        )
        response = frame.get_command_status_response
        if response.command_id != command_id or response.found != response.HasField("current"):
            raise ProtocolError("GetCommandStatusResponse 形状无效")
        if not response.found:
            return None
        current = response.current
        waiter = self._commands.get(command_id)
        if waiter is not None:
            self._apply_event(waiter, current, snapshot=True)
        return _clone(current)

    async def aclose(self) -> None:
        async with self._lifecycle_lock:
            self._closed = True
            task = self._reader_task
            self._reader_task = None
        if task is not None and task is not asyncio.current_task():
            task.cancel()
            try:
                await task
            except asyncio.CancelledError:
                pass
        await self._connection.close()
        for waiter in self._commands.values():
            waiter.disconnected.set()
        for control in self._controls.values():
            if control.future is not None and not control.future.done():
                control.future.set_exception(_ConnectionLost())

    async def _ensure_dispatcher(self) -> transport_pb2.CapabilitySnapshot:
        async with self._lifecycle_lock:
            if self._closed:
                raise ProtocolError("CommandRuntime 已关闭")
            async with asyncio.timeout(self._discovery_timeout_seconds):
                snapshot = await self._connection.connect()
            if self._reader_task is None or self._reader_task.done():
                self._generation += 1
                self._reader_task = asyncio.create_task(self._read_loop(self._generation))
            return snapshot

    async def _read_loop(self, generation: int) -> None:
        try:
            while True:
                frame = await self._connection.receive_authenticated()
                self._dispatch(frame)
        except asyncio.CancelledError:
            raise
        except (ProtocolError, ValueError, AttributeError) as error:
            await self._reader_protocol_failed(generation, error)
        except (OSError, asyncio.IncompleteReadError) as error:
            await self._reader_failed(generation, error)

    async def _reader_failed(self, generation: int, error: Exception) -> None:
        async with self._lifecycle_lock:
            if generation != self._generation or self._closed:
                return
            self._reader_task = None
            await self._connection.close()
            for waiter in self._commands.values():
                waiter.disconnected.set()
            for control in self._controls.values():
                if control.future is not None and not control.future.done():
                    control.future.set_exception(_ConnectionLost())

    async def _reader_protocol_failed(self, generation: int, error: Exception) -> None:
        """协议违例是确定性证据，绝不能降级成同 ID 的 Status 恢复。"""
        async with self._lifecycle_lock:
            if generation != self._generation or self._closed:
                return
            self._reader_task = None
            await self._connection.close()
            response = _ProtocolResponse(
                common_pb2.Error(
                    code=common_pb2.ERROR_CODE_PROTOCOL_VIOLATION,
                    message=f"认证后 Frame 不符合协议: {error}",
                )
            )
            for waiter in self._commands.values():
                if not waiter.terminal.done():
                    waiter.terminal.set_exception(response)
            for control in self._controls.values():
                if control.future is not None and not control.future.done():
                    control.future.set_exception(response)

    def _dispatch(self, frame: transport_pb2.TransportFrame) -> None:
        body = frame.WhichOneof("body")
        if body == "command_event":
            waiter = self._commands.get(frame.command_event.command_id)
            if waiter is None:
                return
            if frame.reply_to not in {"", waiter.request_id}:
                return
            if (
                frame.command_event.state in _TERMINAL | {capabilities_pb2.COMMAND_STATE_RUNNING}
                and not waiter.accepted
                and frame.reply_to != waiter.request_id
            ):
                raise ProtocolError("未关联 CommandRequest 的状态事件无效")
            self._apply_event(waiter, frame.command_event)
            return

        control = self._controls.get(frame.reply_to)
        if control is None:
            return
        if body == "protocol_error":
            if control.body == "command_request":
                command = self._commands.get(control.command_id)
                if command is not None and not command.terminal.done():
                    command.terminal.set_exception(_ProtocolResponse(_clone(frame.protocol_error.error)))
            elif control.future is not None and not control.future.done():
                control.future.set_exception(_ProtocolResponse(_clone(frame.protocol_error.error)))
            return
        if body != control.body:
            raise ProtocolError("控制响应 body 不匹配")
        if body == "cancel_command_response" and frame.cancel_command_response.command_id != control.command_id:
            raise ProtocolError("CancelCommandResponse command_id 不匹配")
        if body == "get_command_status_response" and frame.get_command_status_response.command_id != control.command_id:
            raise ProtocolError("GetCommandStatusResponse command_id 不匹配")
        if control.body != "command_request" and control.future is not None and not control.future.done():
            control.future.set_result(frame)

    def _apply_event(self, waiter: _CommandWaiter, event: capabilities_pb2.CommandEvent, *, snapshot: bool = False) -> None:
        if event.command_id != waiter.command_id:
            raise ProtocolError("CommandEvent command_id 不匹配")
        outcome = event.WhichOneof("outcome")
        state = event.state
        if event.HasField("progress_percent") and event.progress_percent > 100:
            raise ProtocolError("CommandEvent progress_percent 无效")
        if state in {capabilities_pb2.COMMAND_STATE_ACCEPTED, capabilities_pb2.COMMAND_STATE_RUNNING}:
            if outcome is not None:
                raise ProtocolError("非终态不得携带 outcome")
            if state == capabilities_pb2.COMMAND_STATE_ACCEPTED:
                # 控制响应的 current 是读取瞬间的快照。它可能在 reader
                # 已收到终态之后才被分发，不能倒退或改写本地终态。
                if snapshot and waiter.terminal.done():
                    return
                if waiter.current is not None:
                    if snapshot and waiter.current.state == state:
                        return
                    raise ProtocolError("ACCEPTED 只能出现一次")
                waiter.accepted = True
            else:
                if snapshot and waiter.terminal.done():
                    return
                if waiter.current is not None and waiter.current.state not in {
                    capabilities_pb2.COMMAND_STATE_ACCEPTED,
                    capabilities_pb2.COMMAND_STATE_RUNNING,
                    }:
                        raise ProtocolError("RUNNING 状态转换无效")
                waiter.accepted = True
            waiter.current = _clone(event)
            return
        if state not in _TERMINAL:
            raise ProtocolError("未知 CommandEvent 状态")
        if waiter.terminal.done():
            if snapshot:
                existing = waiter.terminal.result()
                if existing.SerializeToString(deterministic=True) == event.SerializeToString(deterministic=True):
                    return
            raise ProtocolError("终态不可改写")
        if state == capabilities_pb2.COMMAND_STATE_SUCCEEDED:
            if outcome != "result" or event.result.WhichOneof("result") != waiter.capability_id:
                raise ProtocolError("成功结果与命令能力不匹配")
        else:
            if outcome != "error":
                raise ProtocolError("失败终态必须携带 Error")
            if state == capabilities_pb2.COMMAND_STATE_CANCELLED and event.error.code != common_pb2.ERROR_CODE_CANCELLED:
                raise ProtocolError("CANCELLED 错误码无效")
            if state == capabilities_pb2.COMMAND_STATE_TIMED_OUT and event.error.code != common_pb2.ERROR_CODE_DEADLINE_EXCEEDED:
                raise ProtocolError("TIMED_OUT 错误码无效")
            if state == capabilities_pb2.COMMAND_STATE_FAILED and event.error.code not in _FAILED_ERROR_CODES:
                raise ProtocolError("FAILED 错误码无效")
        waiter.accepted = True
        waiter.current = _clone(event)
        waiter.terminal.set_result(_clone(event))

    async def _send_control(self, expected_body: str, command_id: str, body_factory: Any) -> transport_pb2.TransportFrame:
        await self._ensure_dispatcher()
        loop = asyncio.get_running_loop()
        async with self._send_lock:
            request_id = self._connection.next_message_id()
            future: asyncio.Future[transport_pb2.TransportFrame] = loop.create_future()
            self._controls[request_id] = _ControlWaiter(expected_body, command_id, future)
            try:
                frame = transport_pb2.TransportFrame(message_id=request_id, fence=self._connection.fence())
                getattr(frame, expected_body.replace("_response", "_request")).CopyFrom(body_factory())
                await self._connection.send_authenticated(frame)
            except Exception:
                self._controls.pop(request_id, None)
                raise
        try:
            async with asyncio.timeout(self._discovery_timeout_seconds):
                return await future
        finally:
            self._controls.pop(request_id, None)

    async def _wait_terminal(self, waiter: _CommandWaiter, deadline: float) -> capabilities_pb2.CommandEvent:
        remaining = deadline - asyncio.get_running_loop().time()
        if remaining <= 0:
            raise asyncio.TimeoutError
        async with asyncio.timeout(remaining):
            while True:
                if waiter.terminal.done():
                    return await waiter.terminal
                disconnected = asyncio.create_task(waiter.disconnected.wait())
                done, _ = await asyncio.wait({waiter.terminal, disconnected}, return_when=asyncio.FIRST_COMPLETED)
                disconnected.cancel()
                if waiter.terminal in done:
                    return await waiter.terminal
                raise _ConnectionLost()

    async def _recover_or_unknown(self, waiter: _CommandWaiter, deadline: float) -> dict[str, object]:
        if not waiter.sent:
            return self._route_unavailable(waiter.command_id)
        try:
            remaining = deadline - asyncio.get_running_loop().time()
            if remaining <= 0:
                return _unknown(waiter.command_id)
            async with asyncio.timeout(remaining):
                waiter.disconnected.clear()
                status = await self.get_status(waiter.command_id)
                if status is None:
                    return _unknown(waiter.command_id)
                if waiter.terminal.done():
                    return self._project_terminal(waiter, await waiter.terminal)
                terminal = await self._wait_terminal(waiter, deadline)
                return self._project_terminal(waiter, terminal)
        except _ProtocolResponse as response:
            if response.error.code == common_pb2.ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED:
                return _unknown(waiter.command_id)
            await self._connection.close()
            return self._protocol_failure(waiter.command_id)
        except (ProtocolError, ValueError):
            await self._connection.close()
            return self._protocol_failure(waiter.command_id)
        except (_ConnectionLost, OSError, asyncio.IncompleteReadError, asyncio.TimeoutError):
            return _unknown(waiter.command_id)

    def _project_terminal(self, waiter: _CommandWaiter, event: capabilities_pb2.CommandEvent) -> dict[str, object]:
        if event.state != capabilities_pb2.COMMAND_STATE_SUCCEEDED:
            return _error(waiter.command_id, event.error)
        result = {
            "status": "succeeded",
            "commandId": waiter.command_id,
            "output": project_message(getattr(event.result, waiter.capability_id)),
        }
        validator = self._output_validators.get(waiter.capability_id)
        if validator is None:
            validator = Draft202012Validator(self._catalog.tool(waiter.capability_id).outputSchema)
            self._output_validators[waiter.capability_id] = validator
        try:
            validator.validate(result)
        except ValidationError as error:
            raise ProtocolError("成功结果不符合公共 Output Schema") from error
        return result

    def _discard_waiter(self, waiter: _CommandWaiter) -> None:
        if self._commands.get(waiter.command_id) is waiter:
            self._commands.pop(waiter.command_id, None)
        if self._controls.get(waiter.request_id) is not None:
            self._controls.pop(waiter.request_id, None)

    @staticmethod
    def _route_unavailable(command_id: str) -> dict[str, object]:
        return {"status": "failed", "commandId": command_id, "error": {"code": "route_unavailable", "message": "无法连接本地 Mod", "retryable": True}}

    @staticmethod
    def _protocol_failure(command_id: str) -> dict[str, object]:
        return {"status": "failed", "commandId": command_id, "error": {"code": "upstream_protocol_error", "message": "本地 Mod 返回了无效协议响应", "retryable": False}}
