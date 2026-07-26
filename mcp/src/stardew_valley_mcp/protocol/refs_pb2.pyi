from . import common_pb2 as _common_pb2
from google.protobuf.internal import enum_type_wrapper as _enum_type_wrapper
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class RefKind(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    REF_KIND_UNSPECIFIED: _ClassVar[RefKind]
    REF_KIND_WORLD_ENTITY: _ClassVar[RefKind]
    REF_KIND_CHARACTER: _ClassVar[RefKind]
    REF_KIND_INVENTORY_ITEM: _ClassVar[RefKind]
    REF_KIND_CONTAINER: _ClassVar[RefKind]
    REF_KIND_UI_ELEMENT: _ClassVar[RefKind]

class RefStatus(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    REF_STATUS_UNSPECIFIED: _ClassVar[RefStatus]
    REF_STATUS_RESOLVED: _ClassVar[RefStatus]
    REF_STATUS_STALE: _ClassVar[RefStatus]
    REF_STATUS_NOT_FOUND: _ClassVar[RefStatus]
    REF_STATUS_UNSUPPORTED: _ClassVar[RefStatus]
REF_KIND_UNSPECIFIED: RefKind
REF_KIND_WORLD_ENTITY: RefKind
REF_KIND_CHARACTER: RefKind
REF_KIND_INVENTORY_ITEM: RefKind
REF_KIND_CONTAINER: RefKind
REF_KIND_UI_ELEMENT: RefKind
REF_STATUS_UNSPECIFIED: RefStatus
REF_STATUS_RESOLVED: RefStatus
REF_STATUS_STALE: RefStatus
REF_STATUS_NOT_FOUND: RefStatus
REF_STATUS_UNSUPPORTED: RefStatus

class Ref(_message.Message):
    __slots__ = ("value",)
    VALUE_FIELD_NUMBER: _ClassVar[int]
    value: str
    def __init__(self, value: _Optional[str] = ...) -> None: ...

class RefResolution(_message.Message):
    __slots__ = ("ref", "status", "kind", "error")
    REF_FIELD_NUMBER: _ClassVar[int]
    STATUS_FIELD_NUMBER: _ClassVar[int]
    KIND_FIELD_NUMBER: _ClassVar[int]
    ERROR_FIELD_NUMBER: _ClassVar[int]
    ref: Ref
    status: RefStatus
    kind: RefKind
    error: _common_pb2.Error
    def __init__(self, ref: _Optional[_Union[Ref, _Mapping]] = ..., status: _Optional[_Union[RefStatus, str]] = ..., kind: _Optional[_Union[RefKind, str]] = ..., error: _Optional[_Union[_common_pb2.Error, _Mapping]] = ...) -> None: ...
