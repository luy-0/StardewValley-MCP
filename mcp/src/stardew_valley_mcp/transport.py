"""连接公开 V1 Mod，并执行唯一已实现的 query_runtime。"""

from __future__ import annotations

import asyncio
import base64
import hashlib
import hmac
import ipaddress
import os
import struct
import uuid
from dataclasses import dataclass
from typing import Any

from google.protobuf.message import DecodeError

from .protocol import capabilities_pb2, common_pb2, queries_pb2, transport_pb2


MAX_FRAME_BYTES = 1_048_576
QUERY_RUNTIME_DIGEST = "6c9c9fc8002032a8b4191e3d4809f74ae9c20abcfb26fbf579d7a329d7daf199"
QUERY_RUNTIME_TIMEOUT_MS = 5_000


class ConfigurationError(ValueError):
    pass


class ProtocolError(ValueError):
    pass


@dataclass(frozen=True)
class ConnectionConfig:
    host: str
    port: int
    secret: bytes

    @classmethod
    def from_env(cls) -> "ConnectionConfig":
        host = os.environ.get("STARDEW_VALLEY_MCP_HOST", "127.0.0.1")
        try:
            if not ipaddress.ip_address(host).is_loopback:
                raise ConfigurationError("STARDEW_VALLEY_MCP_HOST 必须是 loopback IP")
        except ValueError as exc:
            raise ConfigurationError("STARDEW_VALLEY_MCP_HOST 必须是 loopback IP") from exc

        try:
            port = int(os.environ.get("STARDEW_VALLEY_MCP_PORT", "24642"))
        except ValueError as exc:
            raise ConfigurationError("STARDEW_VALLEY_MCP_PORT 必须是整数") from exc
        if not 1024 <= port <= 65535:
            raise ConfigurationError("STARDEW_VALLEY_MCP_PORT 必须位于 1024..65535")

        try:
            secret = base64.b64decode(
                os.environ.get("STARDEW_VALLEY_MCP_SHARED_SECRET", ""), validate=True
            )
        except ValueError as exc:
            raise ConfigurationError("STARDEW_VALLEY_MCP_SHARED_SECRET 必须是 Base64") from exc
        if len(secret) < 32:
            raise ConfigurationError("共享秘密解码后至少需要 32 字节")
        return cls(host, port, secret)


def _lp(value: str | bytes) -> bytes:
    raw = value.encode() if isinstance(value, str) else value
    return struct.pack(">I", len(raw)) + raw


def client_auth_tag(
    secret: bytes,
    mod_id: str,
    client_id: str,
    server_nonce: bytes,
    client_nonce: bytes,
    resume_session_id: str,
) -> bytes:
    value = b"".join(
        (
            _lp("stardew-valley-mcp/v1/client-auth"),
            _lp(mod_id),
            _lp(client_id),
            _lp(server_nonce),
            _lp(client_nonce),
            struct.pack(">II", 1, 0),
            _lp(resume_session_id),
        )
    )
    return hmac.new(secret, value, hashlib.sha256).digest()


def server_auth_tag(
    secret: bytes,
    mod_id: str,
    client_id: str,
    server_nonce: bytes,
    client_nonce: bytes,
    ready: transport_pb2.ServerReady,
) -> bytes:
    value = b"".join(
        (
            _lp("stardew-valley-mcp/v1/server-auth"),
            _lp(mod_id),
            _lp(client_id),
            _lp(server_nonce),
            _lp(client_nonce),
            struct.pack(">II", ready.selected_version.major, ready.selected_version.minor),
            _lp(ready.session_id),
            struct.pack(">Q", ready.lease_epoch),
            _lp(ready.capability_snapshot.digest),
            struct.pack(">II", ready.result_retention_ms, ready.reconnect_grace_ms),
        )
    )
    return hmac.new(secret, value, hashlib.sha256).digest()


def capability_digest(descriptors: Any) -> str:
    value = bytearray()
    for descriptor in sorted(descriptors, key=lambda item: item.id.encode()):
        value.extend(_lp(descriptor.id))
        value.extend(_lp(descriptor.contract_version))
        value.extend(bytes((descriptor.side_effect, descriptor.execution, int(descriptor.cancellable))))
        value.extend(struct.pack(">II", descriptor.default_timeout_ms, descriptor.max_timeout_ms))
        value.extend(_lp(descriptor.request_type))
        value.extend(_lp(descriptor.result_type))
        value.extend(_lp(descriptor.required_scope))
        risks = sorted(descriptor.risks, key=lambda item: item.encode())
        value.extend(struct.pack(">I", len(risks)))
        for risk in risks:
            value.extend(_lp(risk))
        value.append(int(descriptor.destructive))
    return hashlib.sha256(value).hexdigest()


async def read_frame(reader: asyncio.StreamReader) -> transport_pb2.TransportFrame:
    header = await reader.readexactly(4)
    length = struct.unpack(">I", header)[0]
    if not 1 <= length <= MAX_FRAME_BYTES:
        raise ProtocolError("非法帧长度")
    frame = transport_pb2.TransportFrame()
    try:
        frame.ParseFromString(await reader.readexactly(length))
    except DecodeError as exc:
        raise ProtocolError("Proto 帧无法解析") from exc
    if frame.WhichOneof("body") is None:
        raise ProtocolError("帧缺少 body")
    return frame


async def write_frame(writer: asyncio.StreamWriter, frame: transport_pb2.TransportFrame) -> None:
    payload = frame.SerializeToString(deterministic=True)
    if not 1 <= len(payload) <= MAX_FRAME_BYTES:
        raise ProtocolError("非法帧长度")
    writer.write(struct.pack(">I", len(payload)) + payload)
    await writer.drain()


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


def _error(command_id: str, error: common_pb2.Error) -> dict[str, Any]:
    status, code, retryable = _ERRORS.get(
        error.code, ("failed", "upstream_protocol_error", False)
    )
    return {
        "status": status,
        "commandId": command_id,
        "error": {"code": code, "message": error.message[:512] or "Mod 返回错误", "retryable": retryable},
    }


def _project_result(command_id: str, result: queries_pb2.QueryRuntimeResult) -> dict[str, Any]:
    snapshot = result.snapshot
    facing = common_pb2.Direction.Name(snapshot.player.facing).removeprefix("DIRECTION_").lower()
    return {
        "status": "succeeded",
        "commandId": command_id,
        "output": {
            "snapshot": {
                "date": {
                    "season": snapshot.date.season,
                    "dayOfMonth": snapshot.date.day_of_month,
                    "year": snapshot.date.year,
                },
                "timeOfDay": snapshot.time_of_day,
                "player": {
                    "position": {
                        "locationId": snapshot.player.position.location_id,
                        "x": snapshot.player.position.x,
                        "y": snapshot.player.position.y,
                    },
                    "facing": facing,
                    "money": str(snapshot.player.money),
                    "energy": snapshot.player.energy,
                    "maxEnergy": snapshot.player.max_energy,
                    "health": snapshot.player.health,
                    "maxHealth": snapshot.player.max_health,
                    "canMove": snapshot.player.can_move,
                },
                "weather": {
                    "raining": snapshot.weather.raining,
                    "lightning": snapshot.weather.lightning,
                    "snowing": snapshot.weather.snowing,
                    "greenRain": snapshot.weather.green_rain,
                    "festivalDay": snapshot.weather.festival_day,
                },
                "ui": {
                    "menuOpen": snapshot.ui.menu_open,
                    "menuType": snapshot.ui.menu_type,
                },
            },
            "warnings": [],
        },
    }


class QueryRuntimeClient:
    def __init__(self, config: ConnectionConfig):
        self._config = config
        self._client_id = str(uuid.uuid4())
        self._session_id = ""
        self._lease_epoch = 0
        self._reader: asyncio.StreamReader | None = None
        self._writer: asyncio.StreamWriter | None = None
        self._message_sequence = 0
        self._seen_message_ids: set[str] = set()
        self._lock = asyncio.Lock()

    async def available(self) -> bool:
        async with self._lock:
            try:
                await self._connect()
                return True
            except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError, ProtocolError):
                await self._close()
                return False

    async def query_runtime(self) -> dict[str, Any]:
        command_id = str(uuid.uuid4())
        accepted = False
        async with self._lock:
            try:
                async with asyncio.timeout(15):
                    await self._connect()
                    request_id = self._next_message_id()
                    await write_frame(
                        self._writer,
                        transport_pb2.TransportFrame(
                            message_id=request_id,
                            fence=self._fence(),
                            command_request=capabilities_pb2.CommandRequest(
                                command_id=command_id,
                                timeout_ms=QUERY_RUNTIME_TIMEOUT_MS,
                                query_runtime=queries_pb2.QueryRuntimeRequest(),
                            ),
                        ),
                    )
                    while True:
                        frame = await read_frame(self._reader)
                        self._validate_authenticated_frame(frame)
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
                            raise ProtocolError("命令没有先进入 ACCEPTED")
                        if frame.reply_to not in {"", request_id}:
                            raise ProtocolError("终态 reply_to 无效")
                        if event.state == capabilities_pb2.COMMAND_STATE_SUCCEEDED:
                            if event.WhichOneof("outcome") != "result" or event.result.WhichOneof("result") != "query_runtime":
                                raise ProtocolError("成功结果不是 query_runtime")
                            return _project_result(command_id, event.result.query_runtime)
                        if event.state in {
                            capabilities_pb2.COMMAND_STATE_FAILED,
                            capabilities_pb2.COMMAND_STATE_CANCELLED,
                            capabilities_pb2.COMMAND_STATE_TIMED_OUT,
                        } and event.WhichOneof("outcome") == "error":
                            return _error(command_id, event.error)
                        raise ProtocolError("命令终态无效")
            except (OSError, asyncio.IncompleteReadError, asyncio.TimeoutError):
                await self._close()
                if accepted:
                    return {
                        "status": "unknown",
                        "commandId": command_id,
                        "error": {
                            "code": "unknown_outcome",
                            "message": "连接中断，无法确认命令终态",
                            "retryable": False,
                        },
                    }
                return {
                    "status": "failed",
                    "commandId": command_id,
                    "error": {
                        "code": "route_unavailable",
                        "message": "无法连接本地 Mod",
                        "retryable": True,
                    },
                }
            except ProtocolError:
                await self._close()
                return {
                    "status": "failed",
                    "commandId": command_id,
                    "error": {
                        "code": "upstream_protocol_error",
                        "message": "本地 Mod 返回了无效协议响应",
                        "retryable": False,
                    },
                }

    async def _connect(self) -> None:
        if self._writer is not None and not self._writer.is_closing():
            return
        self._reader, self._writer = await asyncio.open_connection(
            self._config.host, self._config.port
        )
        hello_frame = await read_frame(self._reader)
        if hello_frame.WhichOneof("body") != "server_hello" or hello_frame.HasField("fence"):
            raise ProtocolError("需要 ServerHello")
        hello = hello_frame.server_hello
        if (hello.version.major, hello.version.minor) != (1, 0) or len(hello.server_nonce) != 32:
            raise ProtocolError("ServerHello 不兼容")

        client_nonce = os.urandom(32)
        client_hello = transport_pb2.ClientHello(
            requested_version=transport_pb2.ProtocolVersion(major=1, minor=0),
            client_instance_id=self._client_id,
            client_nonce=client_nonce,
        )
        if self._session_id:
            client_hello.resume_session_id = self._session_id
        client_hello.auth_tag = client_auth_tag(
            self._config.secret,
            hello.mod_instance_id,
            self._client_id,
            hello.server_nonce,
            client_nonce,
            self._session_id,
        )
        request_id = self._next_message_id()
        await write_frame(
            self._writer,
            transport_pb2.TransportFrame(
                message_id=request_id, reply_to=hello_frame.message_id, client_hello=client_hello
            ),
        )
        ready_frame = await read_frame(self._reader)
        if ready_frame.WhichOneof("body") == "handshake_rejected":
            raise ProtocolError("握手被拒绝")
        if ready_frame.WhichOneof("body") != "server_ready" or ready_frame.HasField("fence"):
            raise ProtocolError("需要 ServerReady")
        ready = ready_frame.server_ready
        if ready_frame.reply_to != request_id or (ready.selected_version.major, ready.selected_version.minor) != (1, 0):
            raise ProtocolError("ServerReady 不兼容")
        if not hmac.compare_digest(
            ready.auth_tag,
            server_auth_tag(
                self._config.secret,
                hello.mod_instance_id,
                self._client_id,
                hello.server_nonce,
                client_nonce,
                ready,
            ),
        ):
            raise ProtocolError("ServerReady HMAC 无效")
        self._validate_snapshot(ready.capability_snapshot)
        self._session_id = ready.session_id
        self._lease_epoch = ready.lease_epoch
        self._seen_message_ids = {hello_frame.message_id, ready_frame.message_id}

    @staticmethod
    def _validate_snapshot(snapshot: transport_pb2.CapabilitySnapshot) -> None:
        if capability_digest(snapshot.capabilities) != snapshot.digest or snapshot.digest != QUERY_RUNTIME_DIGEST:
            raise ProtocolError("能力摘要不匹配")
        if len(snapshot.capabilities) != 1:
            raise ProtocolError("当前构建只接受 query_runtime")
        item = snapshot.capabilities[0]
        if (
            item.id != "query_runtime"
            or item.contract_version != "1.0.0"
            or item.side_effect != transport_pb2.SIDE_EFFECT_READ_ONLY
            or item.execution != transport_pb2.EXECUTION_MODE_IMMEDIATE
            or item.cancellable
            or item.default_timeout_ms != 5_000
            or item.max_timeout_ms != 15_000
            or item.request_type != "QueryRuntimeRequest"
            or item.result_type != "QueryRuntimeResult"
            or item.required_scope != "game:read"
            or item.destructive
            or item.risks
        ):
            raise ProtocolError("query_runtime Descriptor 不匹配")

    def _validate_authenticated_frame(self, frame: transport_pb2.TransportFrame) -> None:
        if frame.message_id in self._seen_message_ids or not 1 <= len(frame.message_id) <= 64:
            raise ProtocolError("message_id 无效或重复")
        self._seen_message_ids.add(frame.message_id)
        if (
            not frame.HasField("fence")
            or frame.fence.session_id != self._session_id
            or frame.fence.lease_epoch != self._lease_epoch
            or frame.fence.capability_digest != QUERY_RUNTIME_DIGEST
        ):
            raise ProtocolError("Session Fence 不匹配")

    def _fence(self) -> transport_pb2.SessionFence:
        return transport_pb2.SessionFence(
            session_id=self._session_id,
            lease_epoch=self._lease_epoch,
            capability_digest=QUERY_RUNTIME_DIGEST,
        )

    def _next_message_id(self) -> str:
        self._message_sequence += 1
        return f"c-{self._message_sequence}"

    async def _close(self) -> None:
        if self._writer is not None:
            self._writer.close()
            try:
                await self._writer.wait_closed()
            except OSError:
                pass
        self._reader = None
        self._writer = None
