from . import common_pb2 as _common_pb2
from . import facts_pb2 as _facts_pb2
from . import refs_pb2 as _refs_pb2
from google.protobuf.internal import containers as _containers
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class QueryWarning(_message.Message):
    __slots__ = ("code", "message", "ref")
    CODE_FIELD_NUMBER: _ClassVar[int]
    MESSAGE_FIELD_NUMBER: _ClassVar[int]
    REF_FIELD_NUMBER: _ClassVar[int]
    code: str
    message: str
    ref: _refs_pb2.Ref
    def __init__(self, code: _Optional[str] = ..., message: _Optional[str] = ..., ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ...) -> None: ...

class QueryRuntimeRequest(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class QueryRuntimeResult(_message.Message):
    __slots__ = ("snapshot", "warnings")
    SNAPSHOT_FIELD_NUMBER: _ClassVar[int]
    WARNINGS_FIELD_NUMBER: _ClassVar[int]
    snapshot: _facts_pb2.RuntimeSnapshot
    warnings: _containers.RepeatedCompositeFieldContainer[QueryWarning]
    def __init__(self, snapshot: _Optional[_Union[_facts_pb2.RuntimeSnapshot, _Mapping]] = ..., warnings: _Optional[_Iterable[_Union[QueryWarning, _Mapping]]] = ...) -> None: ...

class QueryWorldRequest(_message.Message):
    __slots__ = ("area", "around", "entity_kinds", "include_tiles", "include_entities", "include_characters", "max_entities", "max_characters")
    AREA_FIELD_NUMBER: _ClassVar[int]
    AROUND_FIELD_NUMBER: _ClassVar[int]
    ENTITY_KINDS_FIELD_NUMBER: _ClassVar[int]
    INCLUDE_TILES_FIELD_NUMBER: _ClassVar[int]
    INCLUDE_ENTITIES_FIELD_NUMBER: _ClassVar[int]
    INCLUDE_CHARACTERS_FIELD_NUMBER: _ClassVar[int]
    MAX_ENTITIES_FIELD_NUMBER: _ClassVar[int]
    MAX_CHARACTERS_FIELD_NUMBER: _ClassVar[int]
    area: _common_pb2.TileArea
    around: _common_pb2.RadiusArea
    entity_kinds: _containers.RepeatedScalarFieldContainer[_facts_pb2.EntityKind]
    include_tiles: bool
    include_entities: bool
    include_characters: bool
    max_entities: int
    max_characters: int
    def __init__(self, area: _Optional[_Union[_common_pb2.TileArea, _Mapping]] = ..., around: _Optional[_Union[_common_pb2.RadiusArea, _Mapping]] = ..., entity_kinds: _Optional[_Iterable[_Union[_facts_pb2.EntityKind, str]]] = ..., include_tiles: _Optional[bool] = ..., include_entities: _Optional[bool] = ..., include_characters: _Optional[bool] = ..., max_entities: _Optional[int] = ..., max_characters: _Optional[int] = ...) -> None: ...

class QueryWorldResult(_message.Message):
    __slots__ = ("snapshot", "warnings")
    SNAPSHOT_FIELD_NUMBER: _ClassVar[int]
    WARNINGS_FIELD_NUMBER: _ClassVar[int]
    snapshot: _facts_pb2.WorldSnapshot
    warnings: _containers.RepeatedCompositeFieldContainer[QueryWarning]
    def __init__(self, snapshot: _Optional[_Union[_facts_pb2.WorldSnapshot, _Mapping]] = ..., warnings: _Optional[_Iterable[_Union[QueryWarning, _Mapping]]] = ...) -> None: ...

class QueryInventoryRequest(_message.Message):
    __slots__ = ("player_inventory", "container_ref", "include_empty_slots")
    PLAYER_INVENTORY_FIELD_NUMBER: _ClassVar[int]
    CONTAINER_REF_FIELD_NUMBER: _ClassVar[int]
    INCLUDE_EMPTY_SLOTS_FIELD_NUMBER: _ClassVar[int]
    player_inventory: PlayerInventorySelector
    container_ref: _refs_pb2.Ref
    include_empty_slots: bool
    def __init__(self, player_inventory: _Optional[_Union[PlayerInventorySelector, _Mapping]] = ..., container_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., include_empty_slots: _Optional[bool] = ...) -> None: ...

class PlayerInventorySelector(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class QueryInventoryResult(_message.Message):
    __slots__ = ("snapshot", "warnings")
    SNAPSHOT_FIELD_NUMBER: _ClassVar[int]
    WARNINGS_FIELD_NUMBER: _ClassVar[int]
    snapshot: _facts_pb2.InventorySnapshot
    warnings: _containers.RepeatedCompositeFieldContainer[QueryWarning]
    def __init__(self, snapshot: _Optional[_Union[_facts_pb2.InventorySnapshot, _Mapping]] = ..., warnings: _Optional[_Iterable[_Union[QueryWarning, _Mapping]]] = ...) -> None: ...

class QueryUiRequest(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class QueryUiResult(_message.Message):
    __slots__ = ("snapshot", "warnings")
    SNAPSHOT_FIELD_NUMBER: _ClassVar[int]
    WARNINGS_FIELD_NUMBER: _ClassVar[int]
    snapshot: _facts_pb2.UiSnapshot
    warnings: _containers.RepeatedCompositeFieldContainer[QueryWarning]
    def __init__(self, snapshot: _Optional[_Union[_facts_pb2.UiSnapshot, _Mapping]] = ..., warnings: _Optional[_Iterable[_Union[QueryWarning, _Mapping]]] = ...) -> None: ...

class InspectRequest(_message.Message):
    __slots__ = ("refs",)
    REFS_FIELD_NUMBER: _ClassVar[int]
    refs: _containers.RepeatedCompositeFieldContainer[_refs_pb2.Ref]
    def __init__(self, refs: _Optional[_Iterable[_Union[_refs_pb2.Ref, _Mapping]]] = ...) -> None: ...

class InspectResult(_message.Message):
    __slots__ = ("items", "warnings")
    ITEMS_FIELD_NUMBER: _ClassVar[int]
    WARNINGS_FIELD_NUMBER: _ClassVar[int]
    items: _containers.RepeatedCompositeFieldContainer[InspectedRef]
    warnings: _containers.RepeatedCompositeFieldContainer[QueryWarning]
    def __init__(self, items: _Optional[_Iterable[_Union[InspectedRef, _Mapping]]] = ..., warnings: _Optional[_Iterable[_Union[QueryWarning, _Mapping]]] = ...) -> None: ...

class InspectedRef(_message.Message):
    __slots__ = ("resolution", "world_entity", "character", "inventory_item", "inventory", "ui_element")
    RESOLUTION_FIELD_NUMBER: _ClassVar[int]
    WORLD_ENTITY_FIELD_NUMBER: _ClassVar[int]
    CHARACTER_FIELD_NUMBER: _ClassVar[int]
    INVENTORY_ITEM_FIELD_NUMBER: _ClassVar[int]
    INVENTORY_FIELD_NUMBER: _ClassVar[int]
    UI_ELEMENT_FIELD_NUMBER: _ClassVar[int]
    resolution: _refs_pb2.RefResolution
    world_entity: _facts_pb2.WorldEntityFact
    character: _facts_pb2.CharacterFact
    inventory_item: _facts_pb2.ItemFact
    inventory: _facts_pb2.InventorySnapshot
    ui_element: _facts_pb2.UiElementFact
    def __init__(self, resolution: _Optional[_Union[_refs_pb2.RefResolution, _Mapping]] = ..., world_entity: _Optional[_Union[_facts_pb2.WorldEntityFact, _Mapping]] = ..., character: _Optional[_Union[_facts_pb2.CharacterFact, _Mapping]] = ..., inventory_item: _Optional[_Union[_facts_pb2.ItemFact, _Mapping]] = ..., inventory: _Optional[_Union[_facts_pb2.InventorySnapshot, _Mapping]] = ..., ui_element: _Optional[_Union[_facts_pb2.UiElementFact, _Mapping]] = ...) -> None: ...
