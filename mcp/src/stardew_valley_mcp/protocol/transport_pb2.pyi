from . import capabilities_pb2 as _capabilities_pb2
from . import common_pb2 as _common_pb2
from google.protobuf.internal import containers as _containers
from google.protobuf.internal import enum_type_wrapper as _enum_type_wrapper
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class SideEffect(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    SIDE_EFFECT_UNSPECIFIED: _ClassVar[SideEffect]
    SIDE_EFFECT_READ_ONLY: _ClassVar[SideEffect]
    SIDE_EFFECT_MUTATING: _ClassVar[SideEffect]

class ExecutionMode(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    EXECUTION_MODE_UNSPECIFIED: _ClassVar[ExecutionMode]
    EXECUTION_MODE_IMMEDIATE: _ClassVar[ExecutionMode]
    EXECUTION_MODE_LONG_RUNNING: _ClassVar[ExecutionMode]
SIDE_EFFECT_UNSPECIFIED: SideEffect
SIDE_EFFECT_READ_ONLY: SideEffect
SIDE_EFFECT_MUTATING: SideEffect
EXECUTION_MODE_UNSPECIFIED: ExecutionMode
EXECUTION_MODE_IMMEDIATE: ExecutionMode
EXECUTION_MODE_LONG_RUNNING: ExecutionMode

class ProtocolVersion(_message.Message):
    __slots__ = ("major", "minor")
    MAJOR_FIELD_NUMBER: _ClassVar[int]
    MINOR_FIELD_NUMBER: _ClassVar[int]
    major: int
    minor: int
    def __init__(self, major: _Optional[int] = ..., minor: _Optional[int] = ...) -> None: ...

class CapabilityDescriptor(_message.Message):
    __slots__ = ("id", "contract_version", "side_effect", "execution", "cancellable", "default_timeout_ms", "max_timeout_ms", "request_type", "result_type", "required_scope", "risks", "destructive")
    ID_FIELD_NUMBER: _ClassVar[int]
    CONTRACT_VERSION_FIELD_NUMBER: _ClassVar[int]
    SIDE_EFFECT_FIELD_NUMBER: _ClassVar[int]
    EXECUTION_FIELD_NUMBER: _ClassVar[int]
    CANCELLABLE_FIELD_NUMBER: _ClassVar[int]
    DEFAULT_TIMEOUT_MS_FIELD_NUMBER: _ClassVar[int]
    MAX_TIMEOUT_MS_FIELD_NUMBER: _ClassVar[int]
    REQUEST_TYPE_FIELD_NUMBER: _ClassVar[int]
    RESULT_TYPE_FIELD_NUMBER: _ClassVar[int]
    REQUIRED_SCOPE_FIELD_NUMBER: _ClassVar[int]
    RISKS_FIELD_NUMBER: _ClassVar[int]
    DESTRUCTIVE_FIELD_NUMBER: _ClassVar[int]
    id: str
    contract_version: str
    side_effect: SideEffect
    execution: ExecutionMode
    cancellable: bool
    default_timeout_ms: int
    max_timeout_ms: int
    request_type: str
    result_type: str
    required_scope: str
    risks: _containers.RepeatedScalarFieldContainer[str]
    destructive: bool
    def __init__(self, id: _Optional[str] = ..., contract_version: _Optional[str] = ..., side_effect: _Optional[_Union[SideEffect, str]] = ..., execution: _Optional[_Union[ExecutionMode, str]] = ..., cancellable: _Optional[bool] = ..., default_timeout_ms: _Optional[int] = ..., max_timeout_ms: _Optional[int] = ..., request_type: _Optional[str] = ..., result_type: _Optional[str] = ..., required_scope: _Optional[str] = ..., risks: _Optional[_Iterable[str]] = ..., destructive: _Optional[bool] = ...) -> None: ...

class CapabilitySnapshot(_message.Message):
    __slots__ = ("digest", "capabilities")
    DIGEST_FIELD_NUMBER: _ClassVar[int]
    CAPABILITIES_FIELD_NUMBER: _ClassVar[int]
    digest: str
    capabilities: _containers.RepeatedCompositeFieldContainer[CapabilityDescriptor]
    def __init__(self, digest: _Optional[str] = ..., capabilities: _Optional[_Iterable[_Union[CapabilityDescriptor, _Mapping]]] = ...) -> None: ...

class SessionFence(_message.Message):
    __slots__ = ("session_id", "lease_epoch", "capability_digest")
    SESSION_ID_FIELD_NUMBER: _ClassVar[int]
    LEASE_EPOCH_FIELD_NUMBER: _ClassVar[int]
    CAPABILITY_DIGEST_FIELD_NUMBER: _ClassVar[int]
    session_id: str
    lease_epoch: int
    capability_digest: str
    def __init__(self, session_id: _Optional[str] = ..., lease_epoch: _Optional[int] = ..., capability_digest: _Optional[str] = ...) -> None: ...

class ServerHello(_message.Message):
    __slots__ = ("version", "mod_instance_id", "server_nonce")
    VERSION_FIELD_NUMBER: _ClassVar[int]
    MOD_INSTANCE_ID_FIELD_NUMBER: _ClassVar[int]
    SERVER_NONCE_FIELD_NUMBER: _ClassVar[int]
    version: ProtocolVersion
    mod_instance_id: str
    server_nonce: bytes
    def __init__(self, version: _Optional[_Union[ProtocolVersion, _Mapping]] = ..., mod_instance_id: _Optional[str] = ..., server_nonce: _Optional[bytes] = ...) -> None: ...

class ClientHello(_message.Message):
    __slots__ = ("requested_version", "client_instance_id", "client_nonce", "resume_session_id", "auth_tag")
    REQUESTED_VERSION_FIELD_NUMBER: _ClassVar[int]
    CLIENT_INSTANCE_ID_FIELD_NUMBER: _ClassVar[int]
    CLIENT_NONCE_FIELD_NUMBER: _ClassVar[int]
    RESUME_SESSION_ID_FIELD_NUMBER: _ClassVar[int]
    AUTH_TAG_FIELD_NUMBER: _ClassVar[int]
    requested_version: ProtocolVersion
    client_instance_id: str
    client_nonce: bytes
    resume_session_id: str
    auth_tag: bytes
    def __init__(self, requested_version: _Optional[_Union[ProtocolVersion, _Mapping]] = ..., client_instance_id: _Optional[str] = ..., client_nonce: _Optional[bytes] = ..., resume_session_id: _Optional[str] = ..., auth_tag: _Optional[bytes] = ...) -> None: ...

class ServerReady(_message.Message):
    __slots__ = ("selected_version", "session_id", "lease_epoch", "capability_snapshot", "result_retention_ms", "reconnect_grace_ms", "auth_tag")
    SELECTED_VERSION_FIELD_NUMBER: _ClassVar[int]
    SESSION_ID_FIELD_NUMBER: _ClassVar[int]
    LEASE_EPOCH_FIELD_NUMBER: _ClassVar[int]
    CAPABILITY_SNAPSHOT_FIELD_NUMBER: _ClassVar[int]
    RESULT_RETENTION_MS_FIELD_NUMBER: _ClassVar[int]
    RECONNECT_GRACE_MS_FIELD_NUMBER: _ClassVar[int]
    AUTH_TAG_FIELD_NUMBER: _ClassVar[int]
    selected_version: ProtocolVersion
    session_id: str
    lease_epoch: int
    capability_snapshot: CapabilitySnapshot
    result_retention_ms: int
    reconnect_grace_ms: int
    auth_tag: bytes
    def __init__(self, selected_version: _Optional[_Union[ProtocolVersion, _Mapping]] = ..., session_id: _Optional[str] = ..., lease_epoch: _Optional[int] = ..., capability_snapshot: _Optional[_Union[CapabilitySnapshot, _Mapping]] = ..., result_retention_ms: _Optional[int] = ..., reconnect_grace_ms: _Optional[int] = ..., auth_tag: _Optional[bytes] = ...) -> None: ...

class HandshakeRejected(_message.Message):
    __slots__ = ("error",)
    ERROR_FIELD_NUMBER: _ClassVar[int]
    error: _common_pb2.Error
    def __init__(self, error: _Optional[_Union[_common_pb2.Error, _Mapping]] = ...) -> None: ...

class ProtocolError(_message.Message):
    __slots__ = ("error",)
    ERROR_FIELD_NUMBER: _ClassVar[int]
    error: _common_pb2.Error
    def __init__(self, error: _Optional[_Union[_common_pb2.Error, _Mapping]] = ...) -> None: ...

class Ping(_message.Message):
    __slots__ = ("sequence",)
    SEQUENCE_FIELD_NUMBER: _ClassVar[int]
    sequence: int
    def __init__(self, sequence: _Optional[int] = ...) -> None: ...

class Pong(_message.Message):
    __slots__ = ("sequence",)
    SEQUENCE_FIELD_NUMBER: _ClassVar[int]
    sequence: int
    def __init__(self, sequence: _Optional[int] = ...) -> None: ...

class TransportFrame(_message.Message):
    __slots__ = ("message_id", "reply_to", "fence", "server_hello", "client_hello", "server_ready", "handshake_rejected", "command_request", "command_event", "cancel_command_request", "cancel_command_response", "get_command_status_request", "get_command_status_response", "ping", "pong", "protocol_error")
    MESSAGE_ID_FIELD_NUMBER: _ClassVar[int]
    REPLY_TO_FIELD_NUMBER: _ClassVar[int]
    FENCE_FIELD_NUMBER: _ClassVar[int]
    SERVER_HELLO_FIELD_NUMBER: _ClassVar[int]
    CLIENT_HELLO_FIELD_NUMBER: _ClassVar[int]
    SERVER_READY_FIELD_NUMBER: _ClassVar[int]
    HANDSHAKE_REJECTED_FIELD_NUMBER: _ClassVar[int]
    COMMAND_REQUEST_FIELD_NUMBER: _ClassVar[int]
    COMMAND_EVENT_FIELD_NUMBER: _ClassVar[int]
    CANCEL_COMMAND_REQUEST_FIELD_NUMBER: _ClassVar[int]
    CANCEL_COMMAND_RESPONSE_FIELD_NUMBER: _ClassVar[int]
    GET_COMMAND_STATUS_REQUEST_FIELD_NUMBER: _ClassVar[int]
    GET_COMMAND_STATUS_RESPONSE_FIELD_NUMBER: _ClassVar[int]
    PING_FIELD_NUMBER: _ClassVar[int]
    PONG_FIELD_NUMBER: _ClassVar[int]
    PROTOCOL_ERROR_FIELD_NUMBER: _ClassVar[int]
    message_id: str
    reply_to: str
    fence: SessionFence
    server_hello: ServerHello
    client_hello: ClientHello
    server_ready: ServerReady
    handshake_rejected: HandshakeRejected
    command_request: _capabilities_pb2.CommandRequest
    command_event: _capabilities_pb2.CommandEvent
    cancel_command_request: _capabilities_pb2.CancelCommandRequest
    cancel_command_response: _capabilities_pb2.CancelCommandResponse
    get_command_status_request: _capabilities_pb2.GetCommandStatusRequest
    get_command_status_response: _capabilities_pb2.GetCommandStatusResponse
    ping: Ping
    pong: Pong
    protocol_error: ProtocolError
    def __init__(self, message_id: _Optional[str] = ..., reply_to: _Optional[str] = ..., fence: _Optional[_Union[SessionFence, _Mapping]] = ..., server_hello: _Optional[_Union[ServerHello, _Mapping]] = ..., client_hello: _Optional[_Union[ClientHello, _Mapping]] = ..., server_ready: _Optional[_Union[ServerReady, _Mapping]] = ..., handshake_rejected: _Optional[_Union[HandshakeRejected, _Mapping]] = ..., command_request: _Optional[_Union[_capabilities_pb2.CommandRequest, _Mapping]] = ..., command_event: _Optional[_Union[_capabilities_pb2.CommandEvent, _Mapping]] = ..., cancel_command_request: _Optional[_Union[_capabilities_pb2.CancelCommandRequest, _Mapping]] = ..., cancel_command_response: _Optional[_Union[_capabilities_pb2.CancelCommandResponse, _Mapping]] = ..., get_command_status_request: _Optional[_Union[_capabilities_pb2.GetCommandStatusRequest, _Mapping]] = ..., get_command_status_response: _Optional[_Union[_capabilities_pb2.GetCommandStatusResponse, _Mapping]] = ..., ping: _Optional[_Union[Ping, _Mapping]] = ..., pong: _Optional[_Union[Pong, _Mapping]] = ..., protocol_error: _Optional[_Union[ProtocolError, _Mapping]] = ...) -> None: ...
