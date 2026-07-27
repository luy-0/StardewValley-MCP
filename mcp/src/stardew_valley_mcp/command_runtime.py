"""通用命令生命周期：ID、ACCEPTED、终态与断线结果。"""

from __future__ import annotations

import asyncio
import uuid
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


def _error(command_id: str, error: common_pb2.Error) -> dict[str, object]:
    status, code, retryable = _ERRORS.get(error.code, ("failed", "upstream_protocol_error", False))
    return {"status": status, "commandId": command_id, "error": {"code": code, "message": error.message[:512] or "Mod 返回错误", "retryable": retryable}}

# TODO： 当前 Python CommandRuntime 只完整处理了 ACCEPTED → SUCCEEDED/FAILED/CANCELLED/TIMED_OUT 说明阶段 3 还没有实现长期动作的进度事件消费。长期动作如 navigate/interact/use_tool 已在 manifest 中标为 long_running/cancellable，但 Python Runtime 还没有实现 RUNNING 进度、取消请求、状态查询的 MCP 暴露
class CommandRuntime:
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
        self._lock = asyncio.Lock()
        self._output_validators: dict[str, Draft202012Validator] = {}

    async def available_tools(self) -> list[Any]:
        try:
            async with asyncio.timeout(self._discovery_timeout_seconds):
                snapshot = await self._connection.connect()
            return self._catalog.tools_for(snapshot)
        except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError, ProtocolError, ValueError):
            await self._connection.close()
            return []

    @staticmethod
    def new_command_id() -> str:
        return str(uuid.uuid4())

    async def execute(self, command_id: str, capability_id: str, operation: Message) -> dict[str, object]:
        accepted = False
        async with self._lock:
            try:
                async with asyncio.timeout(self._discovery_timeout_seconds):
                    snapshot = await self._connection.connect()
                descriptor = self._catalog.descriptor(capability_id, snapshot)
                if operation.DESCRIPTOR.name != descriptor.request_type:
                    raise ProtocolError(
                        f"请求类型不匹配: expected={descriptor.request_type} actual={operation.DESCRIPTOR.full_name}"
                    )
                async with asyncio.timeout(descriptor.max_timeout_ms / 1000 + self._transport_margin_seconds):
                    request = capabilities_pb2.CommandRequest(command_id=command_id, timeout_ms=descriptor.default_timeout_ms)
                    getattr(request, capability_id).CopyFrom(operation)
                    request_id = self._connection.next_message_id()
                    await self._connection.send_authenticated(transport_pb2.TransportFrame(message_id=request_id, fence=self._connection.fence(), command_request=request))
                    while True:
                        frame = await self._connection.receive_authenticated()
                        if frame.WhichOneof("body") == "protocol_error":
                            return _error(command_id, frame.protocol_error.error)
                        if frame.WhichOneof("body") != "command_event":
                            raise ProtocolError("等待命令结果时收到错误消息类型")
                        event = frame.command_event
                        if event.command_id != command_id:
                            raise ProtocolError("CommandEvent 身份不匹配")
                        if event.state == capabilities_pb2.COMMAND_STATE_ACCEPTED:
                            if accepted or frame.reply_to != request_id or event.WhichOneof("outcome") is not None:
                                raise ProtocolError("ACCEPTED 状态无效")
                            accepted = True
                            continue
                        if not accepted:
                            if frame.reply_to != request_id:
                                raise ProtocolError("未见 ACCEPTED 的终态必须是当前请求的直接响应")
                        elif frame.reply_to not in {"", request_id}:
                            raise ProtocolError("命令终态 reply_to 无效")
                        if event.state == capabilities_pb2.COMMAND_STATE_SUCCEEDED:
                            if event.WhichOneof("outcome") != "result" or event.result.WhichOneof("result") != capability_id:
                                raise ProtocolError("成功结果与命令能力不匹配")
                            result = {
                                "status": "succeeded",
                                "commandId": command_id,
                                "output": project_message(getattr(event.result, capability_id)),
                            }
                            validator = self._output_validators.get(capability_id)
                            if validator is None:
                                validator = Draft202012Validator(
                                    self._catalog.tool(capability_id).outputSchema
                                )
                                self._output_validators[capability_id] = validator
                            try:
                                validator.validate(result)
                            except ValidationError as error:
                                raise ProtocolError("成功结果不符合公共 Output Schema") from error
                            return result
                        if event.state in {capabilities_pb2.COMMAND_STATE_FAILED, capabilities_pb2.COMMAND_STATE_CANCELLED, capabilities_pb2.COMMAND_STATE_TIMED_OUT} and event.WhichOneof("outcome") == "error":
                            return _error(command_id, event.error)
                        raise ProtocolError("命令终态无效") 
            except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError):
                await self._connection.close()
                if accepted:
                    return {"status": "unknown", "commandId": command_id, "error": {"code": "unknown_outcome", "message": "连接中断，无法确认命令终态", "retryable": False}}
                return {"status": "failed", "commandId": command_id, "error": {"code": "route_unavailable", "message": "无法连接本地 Mod", "retryable": True}}
            except (ProtocolError, ValueError):
                await self._connection.close()
                return {"status": "failed", "commandId": command_id, "error": {"code": "upstream_protocol_error", "message": "本地 Mod 返回了无效协议响应", "retryable": False}}
