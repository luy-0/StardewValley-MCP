from . import common_pb2 as _common_pb2
from . import facts_pb2 as _facts_pb2
from . import refs_pb2 as _refs_pb2
from google.protobuf.internal import containers as _containers
from google.protobuf.internal import enum_type_wrapper as _enum_type_wrapper
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class EmoteKind(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    EMOTE_KIND_UNSPECIFIED: _ClassVar[EmoteKind]
    EMOTE_KIND_HAPPY: _ClassVar[EmoteKind]
    EMOTE_KIND_SAD: _ClassVar[EmoteKind]
    EMOTE_KIND_HEART: _ClassVar[EmoteKind]
    EMOTE_KIND_EXCLAMATION: _ClassVar[EmoteKind]
    EMOTE_KIND_QUESTION: _ClassVar[EmoteKind]
    EMOTE_KIND_ANGRY: _ClassVar[EmoteKind]
    EMOTE_KIND_SLEEP: _ClassVar[EmoteKind]
    EMOTE_KIND_MUSIC: _ClassVar[EmoteKind]
    EMOTE_KIND_NOTE: _ClassVar[EmoteKind]
    EMOTE_KIND_GAME: _ClassVar[EmoteKind]
    EMOTE_KIND_X: _ClassVar[EmoteKind]
    EMOTE_KIND_PAUSE: _ClassVar[EmoteKind]
    EMOTE_KIND_BLUSH: _ClassVar[EmoteKind]
    EMOTE_KIND_YES: _ClassVar[EmoteKind]
    EMOTE_KIND_NO: _ClassVar[EmoteKind]
    EMOTE_KIND_SICK: _ClassVar[EmoteKind]
    EMOTE_KIND_LAUGH: _ClassVar[EmoteKind]
    EMOTE_KIND_SURPRISED: _ClassVar[EmoteKind]
    EMOTE_KIND_HI: _ClassVar[EmoteKind]
    EMOTE_KIND_TAUNT: _ClassVar[EmoteKind]
    EMOTE_KIND_UH: _ClassVar[EmoteKind]
    EMOTE_KIND_JAR: _ClassVar[EmoteKind]

class ArrivalMode(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    ARRIVAL_MODE_UNSPECIFIED: _ClassVar[ArrivalMode]
    ARRIVAL_MODE_EXACT: _ClassVar[ArrivalMode]
    ARRIVAL_MODE_ADJACENT: _ClassVar[ArrivalMode]

class InventoryTransferDirection(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    INVENTORY_TRANSFER_DIRECTION_UNSPECIFIED: _ClassVar[InventoryTransferDirection]
    INVENTORY_TRANSFER_DIRECTION_PLAYER_TO_CONTAINER: _ClassVar[InventoryTransferDirection]
    INVENTORY_TRANSFER_DIRECTION_CONTAINER_TO_PLAYER: _ClassVar[InventoryTransferDirection]

class CraftItemStopReason(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    CRAFT_ITEM_STOP_REASON_UNSPECIFIED: _ClassVar[CraftItemStopReason]
    CRAFT_ITEM_STOP_REASON_COMPLETED: _ClassVar[CraftItemStopReason]
    CRAFT_ITEM_STOP_REASON_MATERIALS_INSUFFICIENT: _ClassVar[CraftItemStopReason]
    CRAFT_ITEM_STOP_REASON_INVENTORY_FULL: _ClassVar[CraftItemStopReason]
EMOTE_KIND_UNSPECIFIED: EmoteKind
EMOTE_KIND_HAPPY: EmoteKind
EMOTE_KIND_SAD: EmoteKind
EMOTE_KIND_HEART: EmoteKind
EMOTE_KIND_EXCLAMATION: EmoteKind
EMOTE_KIND_QUESTION: EmoteKind
EMOTE_KIND_ANGRY: EmoteKind
EMOTE_KIND_SLEEP: EmoteKind
EMOTE_KIND_MUSIC: EmoteKind
EMOTE_KIND_NOTE: EmoteKind
EMOTE_KIND_GAME: EmoteKind
EMOTE_KIND_X: EmoteKind
EMOTE_KIND_PAUSE: EmoteKind
EMOTE_KIND_BLUSH: EmoteKind
EMOTE_KIND_YES: EmoteKind
EMOTE_KIND_NO: EmoteKind
EMOTE_KIND_SICK: EmoteKind
EMOTE_KIND_LAUGH: EmoteKind
EMOTE_KIND_SURPRISED: EmoteKind
EMOTE_KIND_HI: EmoteKind
EMOTE_KIND_TAUNT: EmoteKind
EMOTE_KIND_UH: EmoteKind
EMOTE_KIND_JAR: EmoteKind
ARRIVAL_MODE_UNSPECIFIED: ArrivalMode
ARRIVAL_MODE_EXACT: ArrivalMode
ARRIVAL_MODE_ADJACENT: ArrivalMode
INVENTORY_TRANSFER_DIRECTION_UNSPECIFIED: InventoryTransferDirection
INVENTORY_TRANSFER_DIRECTION_PLAYER_TO_CONTAINER: InventoryTransferDirection
INVENTORY_TRANSFER_DIRECTION_CONTAINER_TO_PLAYER: InventoryTransferDirection
CRAFT_ITEM_STOP_REASON_UNSPECIFIED: CraftItemStopReason
CRAFT_ITEM_STOP_REASON_COMPLETED: CraftItemStopReason
CRAFT_ITEM_STOP_REASON_MATERIALS_INSUFFICIENT: CraftItemStopReason
CRAFT_ITEM_STOP_REASON_INVENTORY_FULL: CraftItemStopReason

class SayRequest(_message.Message):
    __slots__ = ("content",)
    CONTENT_FIELD_NUMBER: _ClassVar[int]
    content: str
    def __init__(self, content: _Optional[str] = ...) -> None: ...

class SayResult(_message.Message):
    __slots__ = ("content_length",)
    CONTENT_LENGTH_FIELD_NUMBER: _ClassVar[int]
    content_length: int
    def __init__(self, content_length: _Optional[int] = ...) -> None: ...

class EmoteRequest(_message.Message):
    __slots__ = ("emote",)
    EMOTE_FIELD_NUMBER: _ClassVar[int]
    emote: EmoteKind
    def __init__(self, emote: _Optional[_Union[EmoteKind, str]] = ...) -> None: ...

class EmoteResult(_message.Message):
    __slots__ = ("emote",)
    EMOTE_FIELD_NUMBER: _ClassVar[int]
    emote: EmoteKind
    def __init__(self, emote: _Optional[_Union[EmoteKind, str]] = ...) -> None: ...

class FaceRequest(_message.Message):
    __slots__ = ("direction",)
    DIRECTION_FIELD_NUMBER: _ClassVar[int]
    direction: _common_pb2.Direction
    def __init__(self, direction: _Optional[_Union[_common_pb2.Direction, str]] = ...) -> None: ...

class FaceResult(_message.Message):
    __slots__ = ("final_direction", "changed")
    FINAL_DIRECTION_FIELD_NUMBER: _ClassVar[int]
    CHANGED_FIELD_NUMBER: _ClassVar[int]
    final_direction: _common_pb2.Direction
    changed: bool
    def __init__(self, final_direction: _Optional[_Union[_common_pb2.Direction, str]] = ..., changed: _Optional[bool] = ...) -> None: ...

class NavigateRequest(_message.Message):
    __slots__ = ("position", "target_ref", "arrival", "stand_side", "face_on_arrival")
    POSITION_FIELD_NUMBER: _ClassVar[int]
    TARGET_REF_FIELD_NUMBER: _ClassVar[int]
    ARRIVAL_FIELD_NUMBER: _ClassVar[int]
    STAND_SIDE_FIELD_NUMBER: _ClassVar[int]
    FACE_ON_ARRIVAL_FIELD_NUMBER: _ClassVar[int]
    position: _common_pb2.WorldPosition
    target_ref: _refs_pb2.Ref
    arrival: ArrivalMode
    stand_side: _common_pb2.Direction
    face_on_arrival: _common_pb2.Direction
    def __init__(self, position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., target_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., arrival: _Optional[_Union[ArrivalMode, str]] = ..., stand_side: _Optional[_Union[_common_pb2.Direction, str]] = ..., face_on_arrival: _Optional[_Union[_common_pb2.Direction, str]] = ...) -> None: ...

class NavigateResult(_message.Message):
    __slots__ = ("start", "final", "resolved_destination", "route_location_ids", "execution")
    START_FIELD_NUMBER: _ClassVar[int]
    FINAL_FIELD_NUMBER: _ClassVar[int]
    RESOLVED_DESTINATION_FIELD_NUMBER: _ClassVar[int]
    ROUTE_LOCATION_IDS_FIELD_NUMBER: _ClassVar[int]
    EXECUTION_FIELD_NUMBER: _ClassVar[int]
    start: _common_pb2.WorldPosition
    final: _common_pb2.WorldPosition
    resolved_destination: _common_pb2.WorldPosition
    route_location_ids: _containers.RepeatedScalarFieldContainer[str]
    execution: _common_pb2.ExecutionStats
    def __init__(self, start: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., final: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., resolved_destination: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., route_location_ids: _Optional[_Iterable[str]] = ..., execution: _Optional[_Union[_common_pb2.ExecutionStats, _Mapping]] = ...) -> None: ...

class InteractRequest(_message.Message):
    __slots__ = ("position", "target_ref")
    POSITION_FIELD_NUMBER: _ClassVar[int]
    TARGET_REF_FIELD_NUMBER: _ClassVar[int]
    position: _common_pb2.WorldPosition
    target_ref: _refs_pb2.Ref
    def __init__(self, position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., target_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ...) -> None: ...

class InteractResult(_message.Message):
    __slots__ = ("target", "energy", "execution")
    TARGET_FIELD_NUMBER: _ClassVar[int]
    ENERGY_FIELD_NUMBER: _ClassVar[int]
    EXECUTION_FIELD_NUMBER: _ClassVar[int]
    target: _common_pb2.WorldPosition
    energy: _common_pb2.ResourceChange
    execution: _common_pb2.ExecutionStats
    def __init__(self, target: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., energy: _Optional[_Union[_common_pb2.ResourceChange, _Mapping]] = ..., execution: _Optional[_Union[_common_pb2.ExecutionStats, _Mapping]] = ...) -> None: ...

class UseToolRequest(_message.Message):
    __slots__ = ("position", "target_ref", "charge_level")
    POSITION_FIELD_NUMBER: _ClassVar[int]
    TARGET_REF_FIELD_NUMBER: _ClassVar[int]
    CHARGE_LEVEL_FIELD_NUMBER: _ClassVar[int]
    position: _common_pb2.WorldPosition
    target_ref: _refs_pb2.Ref
    charge_level: int
    def __init__(self, position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., target_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., charge_level: _Optional[int] = ...) -> None: ...

class UseToolResult(_message.Message):
    __slots__ = ("target", "tool_qualified_item_id", "charge_level", "energy", "execution")
    TARGET_FIELD_NUMBER: _ClassVar[int]
    TOOL_QUALIFIED_ITEM_ID_FIELD_NUMBER: _ClassVar[int]
    CHARGE_LEVEL_FIELD_NUMBER: _ClassVar[int]
    ENERGY_FIELD_NUMBER: _ClassVar[int]
    EXECUTION_FIELD_NUMBER: _ClassVar[int]
    target: _common_pb2.WorldPosition
    tool_qualified_item_id: str
    charge_level: int
    energy: _common_pb2.ResourceChange
    execution: _common_pb2.ExecutionStats
    def __init__(self, target: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., tool_qualified_item_id: _Optional[str] = ..., charge_level: _Optional[int] = ..., energy: _Optional[_Union[_common_pb2.ResourceChange, _Mapping]] = ..., execution: _Optional[_Union[_common_pb2.ExecutionStats, _Mapping]] = ...) -> None: ...

class EquipRequest(_message.Message):
    __slots__ = ("slot_index", "item_ref", "inventory_revision")
    SLOT_INDEX_FIELD_NUMBER: _ClassVar[int]
    ITEM_REF_FIELD_NUMBER: _ClassVar[int]
    INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    slot_index: int
    item_ref: _refs_pb2.Ref
    inventory_revision: str
    def __init__(self, slot_index: _Optional[int] = ..., item_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., inventory_revision: _Optional[str] = ...) -> None: ...

class EquipResult(_message.Message):
    __slots__ = ("slot_index", "item", "changed")
    SLOT_INDEX_FIELD_NUMBER: _ClassVar[int]
    ITEM_FIELD_NUMBER: _ClassVar[int]
    CHANGED_FIELD_NUMBER: _ClassVar[int]
    slot_index: int
    item: _facts_pb2.ItemFact
    changed: bool
    def __init__(self, slot_index: _Optional[int] = ..., item: _Optional[_Union[_facts_pb2.ItemFact, _Mapping]] = ..., changed: _Optional[bool] = ...) -> None: ...

class TransferInventoryItemRequest(_message.Message):
    __slots__ = ("direction", "item_ref", "quantity", "ui_revision", "player_inventory_revision", "container_inventory_revision")
    DIRECTION_FIELD_NUMBER: _ClassVar[int]
    ITEM_REF_FIELD_NUMBER: _ClassVar[int]
    QUANTITY_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    PLAYER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    CONTAINER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    direction: InventoryTransferDirection
    item_ref: _refs_pb2.Ref
    quantity: int
    ui_revision: str
    player_inventory_revision: str
    container_inventory_revision: str
    def __init__(self, direction: _Optional[_Union[InventoryTransferDirection, str]] = ..., item_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., quantity: _Optional[int] = ..., ui_revision: _Optional[str] = ..., player_inventory_revision: _Optional[str] = ..., container_inventory_revision: _Optional[str] = ...) -> None: ...

class TransferInventoryItemResult(_message.Message):
    __slots__ = ("transferred_quantity", "source_slot_index", "source_remaining_quantity", "player_inventory_revision", "container_inventory_revision")
    TRANSFERRED_QUANTITY_FIELD_NUMBER: _ClassVar[int]
    SOURCE_SLOT_INDEX_FIELD_NUMBER: _ClassVar[int]
    SOURCE_REMAINING_QUANTITY_FIELD_NUMBER: _ClassVar[int]
    PLAYER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    CONTAINER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    transferred_quantity: int
    source_slot_index: int
    source_remaining_quantity: int
    player_inventory_revision: str
    container_inventory_revision: str
    def __init__(self, transferred_quantity: _Optional[int] = ..., source_slot_index: _Optional[int] = ..., source_remaining_quantity: _Optional[int] = ..., player_inventory_revision: _Optional[str] = ..., container_inventory_revision: _Optional[str] = ...) -> None: ...

class SetEquipmentSlotRequest(_message.Message):
    __slots__ = ("equipment_slot_ref", "item_ref", "clear", "ui_revision", "player_inventory_revision")
    EQUIPMENT_SLOT_REF_FIELD_NUMBER: _ClassVar[int]
    ITEM_REF_FIELD_NUMBER: _ClassVar[int]
    CLEAR_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    PLAYER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    equipment_slot_ref: _refs_pb2.Ref
    item_ref: _refs_pb2.Ref
    clear: bool
    ui_revision: str
    player_inventory_revision: str
    def __init__(self, equipment_slot_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., item_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., clear: _Optional[bool] = ..., ui_revision: _Optional[str] = ..., player_inventory_revision: _Optional[str] = ...) -> None: ...

class SetEquipmentSlotResult(_message.Message):
    __slots__ = ("equipment_slot_kind", "equipment_slot_index", "item", "player_inventory_revision", "changed")
    EQUIPMENT_SLOT_KIND_FIELD_NUMBER: _ClassVar[int]
    EQUIPMENT_SLOT_INDEX_FIELD_NUMBER: _ClassVar[int]
    ITEM_FIELD_NUMBER: _ClassVar[int]
    PLAYER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    CHANGED_FIELD_NUMBER: _ClassVar[int]
    equipment_slot_kind: _facts_pb2.UiEquipmentSlotKind
    equipment_slot_index: int
    item: _facts_pb2.ItemFact
    player_inventory_revision: str
    changed: bool
    def __init__(self, equipment_slot_kind: _Optional[_Union[_facts_pb2.UiEquipmentSlotKind, str]] = ..., equipment_slot_index: _Optional[int] = ..., item: _Optional[_Union[_facts_pb2.ItemFact, _Mapping]] = ..., player_inventory_revision: _Optional[str] = ..., changed: _Optional[bool] = ...) -> None: ...

class MoveInventoryItemRequest(_message.Message):
    __slots__ = ("item_ref", "destination_slot_ref", "ui_revision", "player_inventory_revision")
    ITEM_REF_FIELD_NUMBER: _ClassVar[int]
    DESTINATION_SLOT_REF_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    PLAYER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    item_ref: _refs_pb2.Ref
    destination_slot_ref: _refs_pb2.Ref
    ui_revision: str
    player_inventory_revision: str
    def __init__(self, item_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., destination_slot_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., ui_revision: _Optional[str] = ..., player_inventory_revision: _Optional[str] = ...) -> None: ...

class MoveInventoryItemResult(_message.Message):
    __slots__ = ("source_slot_index", "destination_slot_index", "changed", "swapped", "player_inventory_revision")
    SOURCE_SLOT_INDEX_FIELD_NUMBER: _ClassVar[int]
    DESTINATION_SLOT_INDEX_FIELD_NUMBER: _ClassVar[int]
    CHANGED_FIELD_NUMBER: _ClassVar[int]
    SWAPPED_FIELD_NUMBER: _ClassVar[int]
    PLAYER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    source_slot_index: int
    destination_slot_index: int
    changed: bool
    swapped: bool
    player_inventory_revision: str
    def __init__(self, source_slot_index: _Optional[int] = ..., destination_slot_index: _Optional[int] = ..., changed: _Optional[bool] = ..., swapped: _Optional[bool] = ..., player_inventory_revision: _Optional[str] = ...) -> None: ...

class CraftingMaterialConsumption(_message.Message):
    __slots__ = ("ingredient_key", "quantity")
    INGREDIENT_KEY_FIELD_NUMBER: _ClassVar[int]
    QUANTITY_FIELD_NUMBER: _ClassVar[int]
    ingredient_key: str
    quantity: int
    def __init__(self, ingredient_key: _Optional[str] = ..., quantity: _Optional[int] = ...) -> None: ...

class CraftItemRequest(_message.Message):
    __slots__ = ("recipe_ref", "craft_count", "ui_revision")
    RECIPE_REF_FIELD_NUMBER: _ClassVar[int]
    CRAFT_COUNT_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    recipe_ref: _refs_pb2.Ref
    craft_count: int
    ui_revision: str
    def __init__(self, recipe_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., craft_count: _Optional[int] = ..., ui_revision: _Optional[str] = ...) -> None: ...

class CraftItemResult(_message.Message):
    __slots__ = ("requested_craft_count", "completed_craft_count", "stop_reason", "outputs", "materials_consumed", "player_inventory_revision", "ui_revision")
    REQUESTED_CRAFT_COUNT_FIELD_NUMBER: _ClassVar[int]
    COMPLETED_CRAFT_COUNT_FIELD_NUMBER: _ClassVar[int]
    STOP_REASON_FIELD_NUMBER: _ClassVar[int]
    OUTPUTS_FIELD_NUMBER: _ClassVar[int]
    MATERIALS_CONSUMED_FIELD_NUMBER: _ClassVar[int]
    PLAYER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    requested_craft_count: int
    completed_craft_count: int
    stop_reason: CraftItemStopReason
    outputs: _containers.RepeatedCompositeFieldContainer[_facts_pb2.CraftingOutputFact]
    materials_consumed: _containers.RepeatedCompositeFieldContainer[CraftingMaterialConsumption]
    player_inventory_revision: str
    ui_revision: str
    def __init__(self, requested_craft_count: _Optional[int] = ..., completed_craft_count: _Optional[int] = ..., stop_reason: _Optional[_Union[CraftItemStopReason, str]] = ..., outputs: _Optional[_Iterable[_Union[_facts_pb2.CraftingOutputFact, _Mapping]]] = ..., materials_consumed: _Optional[_Iterable[_Union[CraftingMaterialConsumption, _Mapping]]] = ..., player_inventory_revision: _Optional[str] = ..., ui_revision: _Optional[str] = ...) -> None: ...

class PurchaseShopItemRequest(_message.Message):
    __slots__ = ("sale_ref", "purchase_count", "ui_revision")
    SALE_REF_FIELD_NUMBER: _ClassVar[int]
    PURCHASE_COUNT_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    sale_ref: _refs_pb2.Ref
    purchase_count: int
    ui_revision: str
    def __init__(self, sale_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., purchase_count: _Optional[int] = ..., ui_revision: _Optional[str] = ...) -> None: ...

class PurchaseShopItemResult(_message.Message):
    __slots__ = ("purchase_count", "item", "total_price", "money_before", "money_after", "stock_remaining", "player_inventory_revision", "ui_revision")
    PURCHASE_COUNT_FIELD_NUMBER: _ClassVar[int]
    ITEM_FIELD_NUMBER: _ClassVar[int]
    TOTAL_PRICE_FIELD_NUMBER: _ClassVar[int]
    MONEY_BEFORE_FIELD_NUMBER: _ClassVar[int]
    MONEY_AFTER_FIELD_NUMBER: _ClassVar[int]
    STOCK_REMAINING_FIELD_NUMBER: _ClassVar[int]
    PLAYER_INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    purchase_count: int
    item: _facts_pb2.ItemFact
    total_price: int
    money_before: int
    money_after: int
    stock_remaining: int
    player_inventory_revision: str
    ui_revision: str
    def __init__(self, purchase_count: _Optional[int] = ..., item: _Optional[_Union[_facts_pb2.ItemFact, _Mapping]] = ..., total_price: _Optional[int] = ..., money_before: _Optional[int] = ..., money_after: _Optional[int] = ..., stock_remaining: _Optional[int] = ..., player_inventory_revision: _Optional[str] = ..., ui_revision: _Optional[str] = ...) -> None: ...

class OpenMenuRequest(_message.Message):
    __slots__ = ("menu",)
    MENU_FIELD_NUMBER: _ClassVar[int]
    menu: _common_pb2.MenuKind
    def __init__(self, menu: _Optional[_Union[_common_pb2.MenuKind, str]] = ...) -> None: ...

class OpenMenuResult(_message.Message):
    __slots__ = ("transition",)
    TRANSITION_FIELD_NUMBER: _ClassVar[int]
    transition: _common_pb2.MenuTransition
    def __init__(self, transition: _Optional[_Union[_common_pb2.MenuTransition, _Mapping]] = ...) -> None: ...

class ActivateUiRequest(_message.Message):
    __slots__ = ("element_ref", "ui_revision")
    ELEMENT_REF_FIELD_NUMBER: _ClassVar[int]
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    element_ref: _refs_pb2.Ref
    ui_revision: str
    def __init__(self, element_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., ui_revision: _Optional[str] = ...) -> None: ...

class ActivateUiResult(_message.Message):
    __slots__ = ("element_ref", "transition")
    ELEMENT_REF_FIELD_NUMBER: _ClassVar[int]
    TRANSITION_FIELD_NUMBER: _ClassVar[int]
    element_ref: _refs_pb2.Ref
    transition: _common_pb2.MenuTransition
    def __init__(self, element_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., transition: _Optional[_Union[_common_pb2.MenuTransition, _Mapping]] = ...) -> None: ...

class CloseMenuRequest(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class CloseMenuResult(_message.Message):
    __slots__ = ("transition", "already_closed")
    TRANSITION_FIELD_NUMBER: _ClassVar[int]
    ALREADY_CLOSED_FIELD_NUMBER: _ClassVar[int]
    transition: _common_pb2.MenuTransition
    already_closed: bool
    def __init__(self, transition: _Optional[_Union[_common_pb2.MenuTransition, _Mapping]] = ..., already_closed: _Optional[bool] = ...) -> None: ...
