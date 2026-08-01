"""纯本地 TCP、握手、Frame 与 Session Fence Transport。"""

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

from google.protobuf.message import DecodeError

from .protocol import transport_pb2


MAX_FRAME_BYTES = 1_048_576


class ConfigurationError(ValueError):
    pass


class ProtocolError(ValueError):
    pass


class HandshakeRejectedError(ProtocolError):
    """形状合法、但 Mod 以稳定 Error 拒绝本次握手。"""

    def __init__(self, code: int, message: str):
        super().__init__(message or "握手被拒绝")
        self.code = code


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
            secret = base64.b64decode(os.environ.get("STARDEW_VALLEY_MCP_SHARED_SECRET", ""), validate=True)
        except ValueError as exc:
            raise ConfigurationError("STARDEW_VALLEY_MCP_SHARED_SECRET 必须是 Base64") from exc
        if len(secret) < 32:
            raise ConfigurationError("共享秘密解码后至少需要 32 字节")
        return cls(host, port, secret)


def _lp(value: str | bytes) -> bytes:
    raw = value.encode() if isinstance(value, str) else value
    return struct.pack(">I", len(raw)) + raw


def client_auth_tag(secret: bytes, mod_id: str, client_id: str, server_nonce: bytes, client_nonce: bytes, resume_session_id: str) -> bytes:
    value = b"".join((_lp("stardew-valley-mcp/v1/client-auth"), _lp(mod_id), _lp(client_id), _lp(server_nonce), _lp(client_nonce), struct.pack(">II", 1, 0), _lp(resume_session_id)))
    return hmac.new(secret, value, hashlib.sha256).digest()


def server_auth_tag(secret: bytes, mod_id: str, client_id: str, server_nonce: bytes, client_nonce: bytes, ready: transport_pb2.ServerReady) -> bytes:
    value = b"".join((_lp("stardew-valley-mcp/v1/server-auth"), _lp(mod_id), _lp(client_id), _lp(server_nonce), _lp(client_nonce), struct.pack(">II", ready.selected_version.major, ready.selected_version.minor), _lp(ready.session_id), struct.pack(">Q", ready.lease_epoch), _lp(ready.capability_snapshot.digest), struct.pack(">II", ready.result_retention_ms, ready.reconnect_grace_ms)))
    return hmac.new(secret, value, hashlib.sha256).digest()


async def read_frame(reader: asyncio.StreamReader) -> transport_pb2.TransportFrame:
    length = struct.unpack(">I", await reader.readexactly(4))[0]
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


class TransportConnection:
    def __init__(self, config: ConnectionConfig):
        self._config = config
        self._client_id = str(uuid.uuid4())
        self._session_id = ""
        self._lease_epoch = 0
        self._digest = ""
        self._reader: asyncio.StreamReader | None = None
        self._writer: asyncio.StreamWriter | None = None
        self._sequence = 0
        self._seen: set[str] = set()
        self._lock = asyncio.Lock()
        self._write_lock = asyncio.Lock()
        self.snapshot: transport_pb2.CapabilitySnapshot | None = None

    async def connect(self) -> transport_pb2.CapabilitySnapshot:
        async with self._lock:
            if self._writer is not None and not self._writer.is_closing() and self.snapshot is not None:
                return self.snapshot
            self._reader, self._writer = await asyncio.open_connection(self._config.host, self._config.port)
            hello_frame = await read_frame(self._reader)
            if hello_frame.WhichOneof("body") != "server_hello" or hello_frame.HasField("fence"):
                raise ProtocolError("需要 ServerHello")
            hello = hello_frame.server_hello
            if (hello.version.major, hello.version.minor) != (1, 0) or len(hello.server_nonce) != 32:
                raise ProtocolError("ServerHello 不兼容")
            nonce = os.urandom(32)
            client_hello = transport_pb2.ClientHello(requested_version=transport_pb2.ProtocolVersion(major=1, minor=0), client_instance_id=self._client_id, client_nonce=nonce)
            if self._session_id:
                client_hello.resume_session_id = self._session_id
            client_hello.auth_tag = client_auth_tag(self._config.secret, hello.mod_instance_id, self._client_id, hello.server_nonce, nonce, self._session_id)
            request_id = self.next_message_id()
            async with self._write_lock:
                await write_frame(self._writer, transport_pb2.TransportFrame(message_id=request_id, reply_to=hello_frame.message_id, client_hello=client_hello))
            ready_frame = await read_frame(self._reader)
            if ready_frame.WhichOneof("body") == "handshake_rejected":
                if ready_frame.HasField("fence") or ready_frame.reply_to != request_id:
                    raise ProtocolError("HandshakeRejected 关联无效")
                raise HandshakeRejectedError(
                    ready_frame.handshake_rejected.error.code,
                    ready_frame.handshake_rejected.error.message,
                )
            if ready_frame.WhichOneof("body") != "server_ready" or ready_frame.HasField("fence") or ready_frame.reply_to != request_id:
                raise ProtocolError("需要 ServerReady")
            ready = ready_frame.server_ready
            if (ready.selected_version.major, ready.selected_version.minor) != (1, 0) or ready.result_retention_ms < 300_000 or ready.reconnect_grace_ms < 10_000 or not hmac.compare_digest(ready.auth_tag, server_auth_tag(self._config.secret, hello.mod_instance_id, self._client_id, hello.server_nonce, nonce, ready)):
                raise ProtocolError("ServerReady 无效")
            self._session_id, self._lease_epoch, self._digest = ready.session_id, ready.lease_epoch, ready.capability_snapshot.digest
            self._seen = {hello_frame.message_id, ready_frame.message_id}
            self.snapshot = ready.capability_snapshot
            return self.snapshot

    def next_message_id(self) -> str:
        self._sequence += 1
        return f"c-{self._sequence}"

    def fence(self) -> transport_pb2.SessionFence:
        return transport_pb2.SessionFence(session_id=self._session_id, lease_epoch=self._lease_epoch, capability_digest=self._digest)

    async def send_authenticated(self, frame: transport_pb2.TransportFrame) -> None:
        if self._writer is None:
            raise ProtocolError("连接未认证")
        async with self._write_lock:
            if self._writer is None:
                raise ProtocolError("连接未认证")
            await write_frame(self._writer, frame)

    async def receive_authenticated(self) -> transport_pb2.TransportFrame:
        if self._reader is None:
            raise ProtocolError("连接未认证")
        frame = await read_frame(self._reader)
        if frame.message_id in self._seen or not 1 <= len(frame.message_id) <= 64:
            raise ProtocolError("message_id 无效或重复")
        self._seen.add(frame.message_id)
        if not frame.HasField("fence") or frame.fence.session_id != self._session_id or frame.fence.lease_epoch != self._lease_epoch or frame.fence.capability_digest != self._digest:
            raise ProtocolError("Session Fence 不匹配")
        return frame

    async def close(self) -> None:
        if self._writer is not None:
            self._writer.close()
            try:
                await self._writer.wait_closed()
            except OSError:
                pass
        self._reader = self._writer = None
        self.snapshot = None
