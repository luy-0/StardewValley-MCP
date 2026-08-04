from google.protobuf.internal import enum_type_wrapper as _enum_type_wrapper
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class Direction(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    DIRECTION_UNSPECIFIED: _ClassVar[Direction]
    DIRECTION_UP: _ClassVar[Direction]
    DIRECTION_RIGHT: _ClassVar[Direction]
    DIRECTION_DOWN: _ClassVar[Direction]
    DIRECTION_LEFT: _ClassVar[Direction]

class MenuKind(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    MENU_KIND_UNSPECIFIED: _ClassVar[MenuKind]
    MENU_KIND_INVENTORY: _ClassVar[MenuKind]
    MENU_KIND_SKILLS: _ClassVar[MenuKind]
    MENU_KIND_SOCIAL: _ClassVar[MenuKind]
    MENU_KIND_MAP: _ClassVar[MenuKind]
    MENU_KIND_CRAFTING: _ClassVar[MenuKind]
    MENU_KIND_COLLECTIONS: _ClassVar[MenuKind]
    MENU_KIND_OPTIONS: _ClassVar[MenuKind]

class ErrorCode(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    ERROR_CODE_UNSPECIFIED: _ClassVar[ErrorCode]
    ERROR_CODE_INVALID_ARGUMENT: _ClassVar[ErrorCode]
    ERROR_CODE_UNAUTHENTICATED: _ClassVar[ErrorCode]
    ERROR_CODE_PERMISSION_DENIED: _ClassVar[ErrorCode]
    ERROR_CODE_UNSUPPORTED_VERSION: _ClassVar[ErrorCode]
    ERROR_CODE_UNSUPPORTED_CAPABILITY: _ClassVar[ErrorCode]
    ERROR_CODE_CAPABILITY_SET_CHANGED: _ClassVar[ErrorCode]
    ERROR_CODE_STALE_LEASE: _ClassVar[ErrorCode]
    ERROR_CODE_CONFLICT: _ClassVar[ErrorCode]
    ERROR_CODE_BUSY: _ClassVar[ErrorCode]
    ERROR_CODE_NOT_READY: _ClassVar[ErrorCode]
    ERROR_CODE_NOT_FOUND: _ClassVar[ErrorCode]
    ERROR_CODE_DEADLINE_EXCEEDED: _ClassVar[ErrorCode]
    ERROR_CODE_CANCELLED: _ClassVar[ErrorCode]
    ERROR_CODE_STALE_REF: _ClassVar[ErrorCode]
    ERROR_CODE_OUT_OF_RANGE: _ClassVar[ErrorCode]
    ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED: _ClassVar[ErrorCode]
    ERROR_CODE_EXECUTION_FAILED: _ClassVar[ErrorCode]
    ERROR_CODE_PROTOCOL_VIOLATION: _ClassVar[ErrorCode]
    ERROR_CODE_INTERNAL: _ClassVar[ErrorCode]
DIRECTION_UNSPECIFIED: Direction
DIRECTION_UP: Direction
DIRECTION_RIGHT: Direction
DIRECTION_DOWN: Direction
DIRECTION_LEFT: Direction
MENU_KIND_UNSPECIFIED: MenuKind
MENU_KIND_INVENTORY: MenuKind
MENU_KIND_SKILLS: MenuKind
MENU_KIND_SOCIAL: MenuKind
MENU_KIND_MAP: MenuKind
MENU_KIND_CRAFTING: MenuKind
MENU_KIND_COLLECTIONS: MenuKind
MENU_KIND_OPTIONS: MenuKind
ERROR_CODE_UNSPECIFIED: ErrorCode
ERROR_CODE_INVALID_ARGUMENT: ErrorCode
ERROR_CODE_UNAUTHENTICATED: ErrorCode
ERROR_CODE_PERMISSION_DENIED: ErrorCode
ERROR_CODE_UNSUPPORTED_VERSION: ErrorCode
ERROR_CODE_UNSUPPORTED_CAPABILITY: ErrorCode
ERROR_CODE_CAPABILITY_SET_CHANGED: ErrorCode
ERROR_CODE_STALE_LEASE: ErrorCode
ERROR_CODE_CONFLICT: ErrorCode
ERROR_CODE_BUSY: ErrorCode
ERROR_CODE_NOT_READY: ErrorCode
ERROR_CODE_NOT_FOUND: ErrorCode
ERROR_CODE_DEADLINE_EXCEEDED: ErrorCode
ERROR_CODE_CANCELLED: ErrorCode
ERROR_CODE_STALE_REF: ErrorCode
ERROR_CODE_OUT_OF_RANGE: ErrorCode
ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED: ErrorCode
ERROR_CODE_EXECUTION_FAILED: ErrorCode
ERROR_CODE_PROTOCOL_VIOLATION: ErrorCode
ERROR_CODE_INTERNAL: ErrorCode

class Error(_message.Message):
    __slots__ = ("code", "message", "navigation")
    CODE_FIELD_NUMBER: _ClassVar[int]
    MESSAGE_FIELD_NUMBER: _ClassVar[int]
    NAVIGATION_FIELD_NUMBER: _ClassVar[int]
    code: ErrorCode
    message: str
    navigation: NavigationFailureContext
    def __init__(self, code: _Optional[_Union[ErrorCode, str]] = ..., message: _Optional[str] = ..., navigation: _Optional[_Union[NavigationFailureContext, _Mapping]] = ...) -> None: ...

class NavigationFailureContext(_message.Message):
    __slots__ = ("last_confirmed_position", "route_segments_total", "route_segments_completed", "interruption_reason", "resume_hint")
    LAST_CONFIRMED_POSITION_FIELD_NUMBER: _ClassVar[int]
    ROUTE_SEGMENTS_TOTAL_FIELD_NUMBER: _ClassVar[int]
    ROUTE_SEGMENTS_COMPLETED_FIELD_NUMBER: _ClassVar[int]
    INTERRUPTION_REASON_FIELD_NUMBER: _ClassVar[int]
    RESUME_HINT_FIELD_NUMBER: _ClassVar[int]
    last_confirmed_position: WorldPosition
    route_segments_total: int
    route_segments_completed: int
    interruption_reason: str
    resume_hint: str
    def __init__(self, last_confirmed_position: _Optional[_Union[WorldPosition, _Mapping]] = ..., route_segments_total: _Optional[int] = ..., route_segments_completed: _Optional[int] = ..., interruption_reason: _Optional[str] = ..., resume_hint: _Optional[str] = ...) -> None: ...

class GameDate(_message.Message):
    __slots__ = ("season", "day_of_month", "year")
    SEASON_FIELD_NUMBER: _ClassVar[int]
    DAY_OF_MONTH_FIELD_NUMBER: _ClassVar[int]
    YEAR_FIELD_NUMBER: _ClassVar[int]
    season: str
    day_of_month: int
    year: int
    def __init__(self, season: _Optional[str] = ..., day_of_month: _Optional[int] = ..., year: _Optional[int] = ...) -> None: ...

class TilePoint(_message.Message):
    __slots__ = ("x", "y")
    X_FIELD_NUMBER: _ClassVar[int]
    Y_FIELD_NUMBER: _ClassVar[int]
    x: int
    y: int
    def __init__(self, x: _Optional[int] = ..., y: _Optional[int] = ...) -> None: ...

class WorldPosition(_message.Message):
    __slots__ = ("location_id", "x", "y")
    LOCATION_ID_FIELD_NUMBER: _ClassVar[int]
    X_FIELD_NUMBER: _ClassVar[int]
    Y_FIELD_NUMBER: _ClassVar[int]
    location_id: str
    x: int
    y: int
    def __init__(self, location_id: _Optional[str] = ..., x: _Optional[int] = ..., y: _Optional[int] = ...) -> None: ...

class TileArea(_message.Message):
    __slots__ = ("location_id", "x", "y", "width", "height")
    LOCATION_ID_FIELD_NUMBER: _ClassVar[int]
    X_FIELD_NUMBER: _ClassVar[int]
    Y_FIELD_NUMBER: _ClassVar[int]
    WIDTH_FIELD_NUMBER: _ClassVar[int]
    HEIGHT_FIELD_NUMBER: _ClassVar[int]
    location_id: str
    x: int
    y: int
    width: int
    height: int
    def __init__(self, location_id: _Optional[str] = ..., x: _Optional[int] = ..., y: _Optional[int] = ..., width: _Optional[int] = ..., height: _Optional[int] = ...) -> None: ...

class RadiusArea(_message.Message):
    __slots__ = ("center", "radius")
    CENTER_FIELD_NUMBER: _ClassVar[int]
    RADIUS_FIELD_NUMBER: _ClassVar[int]
    center: WorldPosition
    radius: int
    def __init__(self, center: _Optional[_Union[WorldPosition, _Mapping]] = ..., radius: _Optional[int] = ...) -> None: ...

class PixelPoint(_message.Message):
    __slots__ = ("x", "y")
    X_FIELD_NUMBER: _ClassVar[int]
    Y_FIELD_NUMBER: _ClassVar[int]
    x: int
    y: int
    def __init__(self, x: _Optional[int] = ..., y: _Optional[int] = ...) -> None: ...

class ResourceChange(_message.Message):
    __slots__ = ("before", "after", "delta")
    BEFORE_FIELD_NUMBER: _ClassVar[int]
    AFTER_FIELD_NUMBER: _ClassVar[int]
    DELTA_FIELD_NUMBER: _ClassVar[int]
    before: float
    after: float
    delta: float
    def __init__(self, before: _Optional[float] = ..., after: _Optional[float] = ..., delta: _Optional[float] = ...) -> None: ...

class ExecutionStats(_message.Message):
    __slots__ = ("elapsed_ticks", "completion_reason")
    ELAPSED_TICKS_FIELD_NUMBER: _ClassVar[int]
    COMPLETION_REASON_FIELD_NUMBER: _ClassVar[int]
    elapsed_ticks: int
    completion_reason: str
    def __init__(self, elapsed_ticks: _Optional[int] = ..., completion_reason: _Optional[str] = ...) -> None: ...

class MenuTransition(_message.Message):
    __slots__ = ("menu_type_before", "menu_type_after", "ui_revision_before", "ui_revision_after")
    MENU_TYPE_BEFORE_FIELD_NUMBER: _ClassVar[int]
    MENU_TYPE_AFTER_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_BEFORE_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_AFTER_FIELD_NUMBER: _ClassVar[int]
    menu_type_before: str
    menu_type_after: str
    ui_revision_before: str
    ui_revision_after: str
    def __init__(self, menu_type_before: _Optional[str] = ..., menu_type_after: _Optional[str] = ..., ui_revision_before: _Optional[str] = ..., ui_revision_after: _Optional[str] = ...) -> None: ...
