from . import actions_pb2 as _actions_pb2
from . import common_pb2 as _common_pb2
from . import queries_pb2 as _queries_pb2
from google.protobuf.internal import enum_type_wrapper as _enum_type_wrapper
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class CommandState(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    COMMAND_STATE_UNSPECIFIED: _ClassVar[CommandState]
    COMMAND_STATE_ACCEPTED: _ClassVar[CommandState]
    COMMAND_STATE_RUNNING: _ClassVar[CommandState]
    COMMAND_STATE_SUCCEEDED: _ClassVar[CommandState]
    COMMAND_STATE_FAILED: _ClassVar[CommandState]
    COMMAND_STATE_CANCELLED: _ClassVar[CommandState]
    COMMAND_STATE_TIMED_OUT: _ClassVar[CommandState]
COMMAND_STATE_UNSPECIFIED: CommandState
COMMAND_STATE_ACCEPTED: CommandState
COMMAND_STATE_RUNNING: CommandState
COMMAND_STATE_SUCCEEDED: CommandState
COMMAND_STATE_FAILED: CommandState
COMMAND_STATE_CANCELLED: CommandState
COMMAND_STATE_TIMED_OUT: CommandState

class CommandRequest(_message.Message):
    __slots__ = ("command_id", "timeout_ms", "say", "emote", "face", "navigate", "interact", "use_tool", "equip", "open_menu", "activate_ui", "close_menu", "transfer_inventory_item", "set_equipment_slot", "move_inventory_item", "craft_item", "purchase_shop_item", "query_runtime", "query_world", "query_inventory", "query_ui", "inspect")
    COMMAND_ID_FIELD_NUMBER: _ClassVar[int]
    TIMEOUT_MS_FIELD_NUMBER: _ClassVar[int]
    SAY_FIELD_NUMBER: _ClassVar[int]
    EMOTE_FIELD_NUMBER: _ClassVar[int]
    FACE_FIELD_NUMBER: _ClassVar[int]
    NAVIGATE_FIELD_NUMBER: _ClassVar[int]
    INTERACT_FIELD_NUMBER: _ClassVar[int]
    USE_TOOL_FIELD_NUMBER: _ClassVar[int]
    EQUIP_FIELD_NUMBER: _ClassVar[int]
    OPEN_MENU_FIELD_NUMBER: _ClassVar[int]
    ACTIVATE_UI_FIELD_NUMBER: _ClassVar[int]
    CLOSE_MENU_FIELD_NUMBER: _ClassVar[int]
    TRANSFER_INVENTORY_ITEM_FIELD_NUMBER: _ClassVar[int]
    SET_EQUIPMENT_SLOT_FIELD_NUMBER: _ClassVar[int]
    MOVE_INVENTORY_ITEM_FIELD_NUMBER: _ClassVar[int]
    CRAFT_ITEM_FIELD_NUMBER: _ClassVar[int]
    PURCHASE_SHOP_ITEM_FIELD_NUMBER: _ClassVar[int]
    QUERY_RUNTIME_FIELD_NUMBER: _ClassVar[int]
    QUERY_WORLD_FIELD_NUMBER: _ClassVar[int]
    QUERY_INVENTORY_FIELD_NUMBER: _ClassVar[int]
    QUERY_UI_FIELD_NUMBER: _ClassVar[int]
    INSPECT_FIELD_NUMBER: _ClassVar[int]
    command_id: str
    timeout_ms: int
    say: _actions_pb2.SayRequest
    emote: _actions_pb2.EmoteRequest
    face: _actions_pb2.FaceRequest
    navigate: _actions_pb2.NavigateRequest
    interact: _actions_pb2.InteractRequest
    use_tool: _actions_pb2.UseToolRequest
    equip: _actions_pb2.EquipRequest
    open_menu: _actions_pb2.OpenMenuRequest
    activate_ui: _actions_pb2.ActivateUiRequest
    close_menu: _actions_pb2.CloseMenuRequest
    transfer_inventory_item: _actions_pb2.TransferInventoryItemRequest
    set_equipment_slot: _actions_pb2.SetEquipmentSlotRequest
    move_inventory_item: _actions_pb2.MoveInventoryItemRequest
    craft_item: _actions_pb2.CraftItemRequest
    purchase_shop_item: _actions_pb2.PurchaseShopItemRequest
    query_runtime: _queries_pb2.QueryRuntimeRequest
    query_world: _queries_pb2.QueryWorldRequest
    query_inventory: _queries_pb2.QueryInventoryRequest
    query_ui: _queries_pb2.QueryUiRequest
    inspect: _queries_pb2.InspectRequest
    def __init__(self, command_id: _Optional[str] = ..., timeout_ms: _Optional[int] = ..., say: _Optional[_Union[_actions_pb2.SayRequest, _Mapping]] = ..., emote: _Optional[_Union[_actions_pb2.EmoteRequest, _Mapping]] = ..., face: _Optional[_Union[_actions_pb2.FaceRequest, _Mapping]] = ..., navigate: _Optional[_Union[_actions_pb2.NavigateRequest, _Mapping]] = ..., interact: _Optional[_Union[_actions_pb2.InteractRequest, _Mapping]] = ..., use_tool: _Optional[_Union[_actions_pb2.UseToolRequest, _Mapping]] = ..., equip: _Optional[_Union[_actions_pb2.EquipRequest, _Mapping]] = ..., open_menu: _Optional[_Union[_actions_pb2.OpenMenuRequest, _Mapping]] = ..., activate_ui: _Optional[_Union[_actions_pb2.ActivateUiRequest, _Mapping]] = ..., close_menu: _Optional[_Union[_actions_pb2.CloseMenuRequest, _Mapping]] = ..., transfer_inventory_item: _Optional[_Union[_actions_pb2.TransferInventoryItemRequest, _Mapping]] = ..., set_equipment_slot: _Optional[_Union[_actions_pb2.SetEquipmentSlotRequest, _Mapping]] = ..., move_inventory_item: _Optional[_Union[_actions_pb2.MoveInventoryItemRequest, _Mapping]] = ..., craft_item: _Optional[_Union[_actions_pb2.CraftItemRequest, _Mapping]] = ..., purchase_shop_item: _Optional[_Union[_actions_pb2.PurchaseShopItemRequest, _Mapping]] = ..., query_runtime: _Optional[_Union[_queries_pb2.QueryRuntimeRequest, _Mapping]] = ..., query_world: _Optional[_Union[_queries_pb2.QueryWorldRequest, _Mapping]] = ..., query_inventory: _Optional[_Union[_queries_pb2.QueryInventoryRequest, _Mapping]] = ..., query_ui: _Optional[_Union[_queries_pb2.QueryUiRequest, _Mapping]] = ..., inspect: _Optional[_Union[_queries_pb2.InspectRequest, _Mapping]] = ...) -> None: ...

class CapabilityResult(_message.Message):
    __slots__ = ("say", "emote", "face", "navigate", "interact", "use_tool", "equip", "open_menu", "activate_ui", "close_menu", "transfer_inventory_item", "set_equipment_slot", "move_inventory_item", "craft_item", "purchase_shop_item", "query_runtime", "query_world", "query_inventory", "query_ui", "inspect")
    SAY_FIELD_NUMBER: _ClassVar[int]
    EMOTE_FIELD_NUMBER: _ClassVar[int]
    FACE_FIELD_NUMBER: _ClassVar[int]
    NAVIGATE_FIELD_NUMBER: _ClassVar[int]
    INTERACT_FIELD_NUMBER: _ClassVar[int]
    USE_TOOL_FIELD_NUMBER: _ClassVar[int]
    EQUIP_FIELD_NUMBER: _ClassVar[int]
    OPEN_MENU_FIELD_NUMBER: _ClassVar[int]
    ACTIVATE_UI_FIELD_NUMBER: _ClassVar[int]
    CLOSE_MENU_FIELD_NUMBER: _ClassVar[int]
    TRANSFER_INVENTORY_ITEM_FIELD_NUMBER: _ClassVar[int]
    SET_EQUIPMENT_SLOT_FIELD_NUMBER: _ClassVar[int]
    MOVE_INVENTORY_ITEM_FIELD_NUMBER: _ClassVar[int]
    CRAFT_ITEM_FIELD_NUMBER: _ClassVar[int]
    PURCHASE_SHOP_ITEM_FIELD_NUMBER: _ClassVar[int]
    QUERY_RUNTIME_FIELD_NUMBER: _ClassVar[int]
    QUERY_WORLD_FIELD_NUMBER: _ClassVar[int]
    QUERY_INVENTORY_FIELD_NUMBER: _ClassVar[int]
    QUERY_UI_FIELD_NUMBER: _ClassVar[int]
    INSPECT_FIELD_NUMBER: _ClassVar[int]
    say: _actions_pb2.SayResult
    emote: _actions_pb2.EmoteResult
    face: _actions_pb2.FaceResult
    navigate: _actions_pb2.NavigateResult
    interact: _actions_pb2.InteractResult
    use_tool: _actions_pb2.UseToolResult
    equip: _actions_pb2.EquipResult
    open_menu: _actions_pb2.OpenMenuResult
    activate_ui: _actions_pb2.ActivateUiResult
    close_menu: _actions_pb2.CloseMenuResult
    transfer_inventory_item: _actions_pb2.TransferInventoryItemResult
    set_equipment_slot: _actions_pb2.SetEquipmentSlotResult
    move_inventory_item: _actions_pb2.MoveInventoryItemResult
    craft_item: _actions_pb2.CraftItemResult
    purchase_shop_item: _actions_pb2.PurchaseShopItemResult
    query_runtime: _queries_pb2.QueryRuntimeResult
    query_world: _queries_pb2.QueryWorldResult
    query_inventory: _queries_pb2.QueryInventoryResult
    query_ui: _queries_pb2.QueryUiResult
    inspect: _queries_pb2.InspectResult
    def __init__(self, say: _Optional[_Union[_actions_pb2.SayResult, _Mapping]] = ..., emote: _Optional[_Union[_actions_pb2.EmoteResult, _Mapping]] = ..., face: _Optional[_Union[_actions_pb2.FaceResult, _Mapping]] = ..., navigate: _Optional[_Union[_actions_pb2.NavigateResult, _Mapping]] = ..., interact: _Optional[_Union[_actions_pb2.InteractResult, _Mapping]] = ..., use_tool: _Optional[_Union[_actions_pb2.UseToolResult, _Mapping]] = ..., equip: _Optional[_Union[_actions_pb2.EquipResult, _Mapping]] = ..., open_menu: _Optional[_Union[_actions_pb2.OpenMenuResult, _Mapping]] = ..., activate_ui: _Optional[_Union[_actions_pb2.ActivateUiResult, _Mapping]] = ..., close_menu: _Optional[_Union[_actions_pb2.CloseMenuResult, _Mapping]] = ..., transfer_inventory_item: _Optional[_Union[_actions_pb2.TransferInventoryItemResult, _Mapping]] = ..., set_equipment_slot: _Optional[_Union[_actions_pb2.SetEquipmentSlotResult, _Mapping]] = ..., move_inventory_item: _Optional[_Union[_actions_pb2.MoveInventoryItemResult, _Mapping]] = ..., craft_item: _Optional[_Union[_actions_pb2.CraftItemResult, _Mapping]] = ..., purchase_shop_item: _Optional[_Union[_actions_pb2.PurchaseShopItemResult, _Mapping]] = ..., query_runtime: _Optional[_Union[_queries_pb2.QueryRuntimeResult, _Mapping]] = ..., query_world: _Optional[_Union[_queries_pb2.QueryWorldResult, _Mapping]] = ..., query_inventory: _Optional[_Union[_queries_pb2.QueryInventoryResult, _Mapping]] = ..., query_ui: _Optional[_Union[_queries_pb2.QueryUiResult, _Mapping]] = ..., inspect: _Optional[_Union[_queries_pb2.InspectResult, _Mapping]] = ...) -> None: ...

class CommandEvent(_message.Message):
    __slots__ = ("command_id", "state", "phase", "progress_percent", "result", "error")
    COMMAND_ID_FIELD_NUMBER: _ClassVar[int]
    STATE_FIELD_NUMBER: _ClassVar[int]
    PHASE_FIELD_NUMBER: _ClassVar[int]
    PROGRESS_PERCENT_FIELD_NUMBER: _ClassVar[int]
    RESULT_FIELD_NUMBER: _ClassVar[int]
    ERROR_FIELD_NUMBER: _ClassVar[int]
    command_id: str
    state: CommandState
    phase: str
    progress_percent: int
    result: CapabilityResult
    error: _common_pb2.Error
    def __init__(self, command_id: _Optional[str] = ..., state: _Optional[_Union[CommandState, str]] = ..., phase: _Optional[str] = ..., progress_percent: _Optional[int] = ..., result: _Optional[_Union[CapabilityResult, _Mapping]] = ..., error: _Optional[_Union[_common_pb2.Error, _Mapping]] = ...) -> None: ...

class CancelCommandRequest(_message.Message):
    __slots__ = ("command_id", "reason")
    COMMAND_ID_FIELD_NUMBER: _ClassVar[int]
    REASON_FIELD_NUMBER: _ClassVar[int]
    command_id: str
    reason: str
    def __init__(self, command_id: _Optional[str] = ..., reason: _Optional[str] = ...) -> None: ...

class CancelCommandResponse(_message.Message):
    __slots__ = ("command_id", "accepted", "current", "error")
    COMMAND_ID_FIELD_NUMBER: _ClassVar[int]
    ACCEPTED_FIELD_NUMBER: _ClassVar[int]
    CURRENT_FIELD_NUMBER: _ClassVar[int]
    ERROR_FIELD_NUMBER: _ClassVar[int]
    command_id: str
    accepted: bool
    current: CommandEvent
    error: _common_pb2.Error
    def __init__(self, command_id: _Optional[str] = ..., accepted: _Optional[bool] = ..., current: _Optional[_Union[CommandEvent, _Mapping]] = ..., error: _Optional[_Union[_common_pb2.Error, _Mapping]] = ...) -> None: ...

class GetCommandStatusRequest(_message.Message):
    __slots__ = ("command_id",)
    COMMAND_ID_FIELD_NUMBER: _ClassVar[int]
    command_id: str
    def __init__(self, command_id: _Optional[str] = ...) -> None: ...

class GetCommandStatusResponse(_message.Message):
    __slots__ = ("command_id", "found", "current")
    COMMAND_ID_FIELD_NUMBER: _ClassVar[int]
    FOUND_FIELD_NUMBER: _ClassVar[int]
    CURRENT_FIELD_NUMBER: _ClassVar[int]
    command_id: str
    found: bool
    current: CommandEvent
    def __init__(self, command_id: _Optional[str] = ..., found: _Optional[bool] = ..., current: _Optional[_Union[CommandEvent, _Mapping]] = ...) -> None: ...
