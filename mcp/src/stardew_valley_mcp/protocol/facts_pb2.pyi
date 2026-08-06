from . import common_pb2 as _common_pb2
from . import refs_pb2 as _refs_pb2
from google.protobuf.internal import containers as _containers
from google.protobuf.internal import enum_type_wrapper as _enum_type_wrapper
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class PlayerRelation(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    PLAYER_RELATION_UNSPECIFIED: _ClassVar[PlayerRelation]
    PLAYER_RELATION_MYSELF: _ClassVar[PlayerRelation]
    PLAYER_RELATION_OTHER: _ClassVar[PlayerRelation]

class WeatherKind(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    WEATHER_KIND_UNSPECIFIED: _ClassVar[WeatherKind]
    WEATHER_KIND_SUN: _ClassVar[WeatherKind]
    WEATHER_KIND_RAIN: _ClassVar[WeatherKind]
    WEATHER_KIND_STORM: _ClassVar[WeatherKind]
    WEATHER_KIND_SNOW: _ClassVar[WeatherKind]
    WEATHER_KIND_WIND: _ClassVar[WeatherKind]
    WEATHER_KIND_GREEN_RAIN: _ClassVar[WeatherKind]
    WEATHER_KIND_FESTIVAL: _ClassVar[WeatherKind]
    WEATHER_KIND_WEDDING: _ClassVar[WeatherKind]

class DailyLuckTier(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    DAILY_LUCK_TIER_UNSPECIFIED: _ClassVar[DailyLuckTier]
    DAILY_LUCK_TIER_VERY_UNLUCKY: _ClassVar[DailyLuckTier]
    DAILY_LUCK_TIER_UNLUCKY: _ClassVar[DailyLuckTier]
    DAILY_LUCK_TIER_NEUTRAL: _ClassVar[DailyLuckTier]
    DAILY_LUCK_TIER_LUCKY: _ClassVar[DailyLuckTier]
    DAILY_LUCK_TIER_VERY_LUCKY: _ClassVar[DailyLuckTier]

class EntityKind(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    ENTITY_KIND_UNSPECIFIED: _ClassVar[EntityKind]
    ENTITY_KIND_TREE: _ClassVar[EntityKind]
    ENTITY_KIND_FRUIT_TREE: _ClassVar[EntityKind]
    ENTITY_KIND_CROP: _ClassVar[EntityKind]
    ENTITY_KIND_RESOURCE_NODE: _ClassVar[EntityKind]
    ENTITY_KIND_RESOURCE_CLUMP: _ClassVar[EntityKind]
    ENTITY_KIND_MACHINE: _ClassVar[EntityKind]
    ENTITY_KIND_CONTAINER: _ClassVar[EntityKind]
    ENTITY_KIND_BED: _ClassVar[EntityKind]
    ENTITY_KIND_FURNITURE: _ClassVar[EntityKind]
    ENTITY_KIND_LOOSE_ITEM: _ClassVar[EntityKind]
    ENTITY_KIND_DOOR: _ClassVar[EntityKind]
    ENTITY_KIND_WARP: _ClassVar[EntityKind]
    ENTITY_KIND_GENERIC_OBJECT: _ClassVar[EntityKind]
    ENTITY_KIND_HOE_DIRT: _ClassVar[EntityKind]

class CropHarvestAction(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    CROP_HARVEST_ACTION_UNSPECIFIED: _ClassVar[CropHarvestAction]
    CROP_HARVEST_ACTION_INTERACT: _ClassVar[CropHarvestAction]
    CROP_HARVEST_ACTION_SCYTHE: _ClassVar[CropHarvestAction]

class CharacterKind(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    CHARACTER_KIND_UNSPECIFIED: _ClassVar[CharacterKind]
    CHARACTER_KIND_NPC: _ClassVar[CharacterKind]
    CHARACTER_KIND_MONSTER: _ClassVar[CharacterKind]
    CHARACTER_KIND_FARM_ANIMAL: _ClassVar[CharacterKind]
    CHARACTER_KIND_PET: _ClassVar[CharacterKind]
    CHARACTER_KIND_HORSE: _ClassVar[CharacterKind]

class UiElementKind(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    UI_ELEMENT_KIND_UNSPECIFIED: _ClassVar[UiElementKind]
    UI_ELEMENT_KIND_BUTTON: _ClassVar[UiElementKind]
    UI_ELEMENT_KIND_TAB: _ClassVar[UiElementKind]
    UI_ELEMENT_KIND_OPTION: _ClassVar[UiElementKind]
    UI_ELEMENT_KIND_ITEM_SLOT: _ClassVar[UiElementKind]
    UI_ELEMENT_KIND_DIALOGUE_RESPONSE: _ClassVar[UiElementKind]
    UI_ELEMENT_KIND_DIALOGUE_ADVANCE: _ClassVar[UiElementKind]
    UI_ELEMENT_KIND_EQUIPMENT_SLOT: _ClassVar[UiElementKind]
    UI_ELEMENT_KIND_CRAFTING_RECIPE: _ClassVar[UiElementKind]

class UiInventorySide(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    UI_INVENTORY_SIDE_UNSPECIFIED: _ClassVar[UiInventorySide]
    UI_INVENTORY_SIDE_PLAYER: _ClassVar[UiInventorySide]
    UI_INVENTORY_SIDE_CONTAINER: _ClassVar[UiInventorySide]

class UiEquipmentSlotKind(int, metaclass=_enum_type_wrapper.EnumTypeWrapper):
    __slots__ = ()
    UI_EQUIPMENT_SLOT_KIND_UNSPECIFIED: _ClassVar[UiEquipmentSlotKind]
    UI_EQUIPMENT_SLOT_KIND_HAT: _ClassVar[UiEquipmentSlotKind]
    UI_EQUIPMENT_SLOT_KIND_LEFT_RING: _ClassVar[UiEquipmentSlotKind]
    UI_EQUIPMENT_SLOT_KIND_RIGHT_RING: _ClassVar[UiEquipmentSlotKind]
    UI_EQUIPMENT_SLOT_KIND_BOOTS: _ClassVar[UiEquipmentSlotKind]
    UI_EQUIPMENT_SLOT_KIND_SHIRT: _ClassVar[UiEquipmentSlotKind]
    UI_EQUIPMENT_SLOT_KIND_PANTS: _ClassVar[UiEquipmentSlotKind]
    UI_EQUIPMENT_SLOT_KIND_TRINKET: _ClassVar[UiEquipmentSlotKind]
PLAYER_RELATION_UNSPECIFIED: PlayerRelation
PLAYER_RELATION_MYSELF: PlayerRelation
PLAYER_RELATION_OTHER: PlayerRelation
WEATHER_KIND_UNSPECIFIED: WeatherKind
WEATHER_KIND_SUN: WeatherKind
WEATHER_KIND_RAIN: WeatherKind
WEATHER_KIND_STORM: WeatherKind
WEATHER_KIND_SNOW: WeatherKind
WEATHER_KIND_WIND: WeatherKind
WEATHER_KIND_GREEN_RAIN: WeatherKind
WEATHER_KIND_FESTIVAL: WeatherKind
WEATHER_KIND_WEDDING: WeatherKind
DAILY_LUCK_TIER_UNSPECIFIED: DailyLuckTier
DAILY_LUCK_TIER_VERY_UNLUCKY: DailyLuckTier
DAILY_LUCK_TIER_UNLUCKY: DailyLuckTier
DAILY_LUCK_TIER_NEUTRAL: DailyLuckTier
DAILY_LUCK_TIER_LUCKY: DailyLuckTier
DAILY_LUCK_TIER_VERY_LUCKY: DailyLuckTier
ENTITY_KIND_UNSPECIFIED: EntityKind
ENTITY_KIND_TREE: EntityKind
ENTITY_KIND_FRUIT_TREE: EntityKind
ENTITY_KIND_CROP: EntityKind
ENTITY_KIND_RESOURCE_NODE: EntityKind
ENTITY_KIND_RESOURCE_CLUMP: EntityKind
ENTITY_KIND_MACHINE: EntityKind
ENTITY_KIND_CONTAINER: EntityKind
ENTITY_KIND_BED: EntityKind
ENTITY_KIND_FURNITURE: EntityKind
ENTITY_KIND_LOOSE_ITEM: EntityKind
ENTITY_KIND_DOOR: EntityKind
ENTITY_KIND_WARP: EntityKind
ENTITY_KIND_GENERIC_OBJECT: EntityKind
ENTITY_KIND_HOE_DIRT: EntityKind
CROP_HARVEST_ACTION_UNSPECIFIED: CropHarvestAction
CROP_HARVEST_ACTION_INTERACT: CropHarvestAction
CROP_HARVEST_ACTION_SCYTHE: CropHarvestAction
CHARACTER_KIND_UNSPECIFIED: CharacterKind
CHARACTER_KIND_NPC: CharacterKind
CHARACTER_KIND_MONSTER: CharacterKind
CHARACTER_KIND_FARM_ANIMAL: CharacterKind
CHARACTER_KIND_PET: CharacterKind
CHARACTER_KIND_HORSE: CharacterKind
UI_ELEMENT_KIND_UNSPECIFIED: UiElementKind
UI_ELEMENT_KIND_BUTTON: UiElementKind
UI_ELEMENT_KIND_TAB: UiElementKind
UI_ELEMENT_KIND_OPTION: UiElementKind
UI_ELEMENT_KIND_ITEM_SLOT: UiElementKind
UI_ELEMENT_KIND_DIALOGUE_RESPONSE: UiElementKind
UI_ELEMENT_KIND_DIALOGUE_ADVANCE: UiElementKind
UI_ELEMENT_KIND_EQUIPMENT_SLOT: UiElementKind
UI_ELEMENT_KIND_CRAFTING_RECIPE: UiElementKind
UI_INVENTORY_SIDE_UNSPECIFIED: UiInventorySide
UI_INVENTORY_SIDE_PLAYER: UiInventorySide
UI_INVENTORY_SIDE_CONTAINER: UiInventorySide
UI_EQUIPMENT_SLOT_KIND_UNSPECIFIED: UiEquipmentSlotKind
UI_EQUIPMENT_SLOT_KIND_HAT: UiEquipmentSlotKind
UI_EQUIPMENT_SLOT_KIND_LEFT_RING: UiEquipmentSlotKind
UI_EQUIPMENT_SLOT_KIND_RIGHT_RING: UiEquipmentSlotKind
UI_EQUIPMENT_SLOT_KIND_BOOTS: UiEquipmentSlotKind
UI_EQUIPMENT_SLOT_KIND_SHIRT: UiEquipmentSlotKind
UI_EQUIPMENT_SLOT_KIND_PANTS: UiEquipmentSlotKind
UI_EQUIPMENT_SLOT_KIND_TRINKET: UiEquipmentSlotKind

class RuntimeSnapshot(_message.Message):
    __slots__ = ("date", "time_of_day", "player", "weather", "ui", "daily_luck", "queen_of_sauce")
    DATE_FIELD_NUMBER: _ClassVar[int]
    TIME_OF_DAY_FIELD_NUMBER: _ClassVar[int]
    PLAYER_FIELD_NUMBER: _ClassVar[int]
    WEATHER_FIELD_NUMBER: _ClassVar[int]
    UI_FIELD_NUMBER: _ClassVar[int]
    DAILY_LUCK_FIELD_NUMBER: _ClassVar[int]
    QUEEN_OF_SAUCE_FIELD_NUMBER: _ClassVar[int]
    date: _common_pb2.GameDate
    time_of_day: int
    player: PlayerFact
    weather: WeatherFact
    ui: UiSummary
    daily_luck: DailyLuckFact
    queen_of_sauce: TvCookingRecipeFact
    def __init__(self, date: _Optional[_Union[_common_pb2.GameDate, _Mapping]] = ..., time_of_day: _Optional[int] = ..., player: _Optional[_Union[PlayerFact, _Mapping]] = ..., weather: _Optional[_Union[WeatherFact, _Mapping]] = ..., ui: _Optional[_Union[UiSummary, _Mapping]] = ..., daily_luck: _Optional[_Union[DailyLuckFact, _Mapping]] = ..., queen_of_sauce: _Optional[_Union[TvCookingRecipeFact, _Mapping]] = ...) -> None: ...

class PlayerFact(_message.Message):
    __slots__ = ("position", "facing", "money", "energy", "max_energy", "health", "max_health", "can_move", "home_location_id")
    POSITION_FIELD_NUMBER: _ClassVar[int]
    FACING_FIELD_NUMBER: _ClassVar[int]
    MONEY_FIELD_NUMBER: _ClassVar[int]
    ENERGY_FIELD_NUMBER: _ClassVar[int]
    MAX_ENERGY_FIELD_NUMBER: _ClassVar[int]
    HEALTH_FIELD_NUMBER: _ClassVar[int]
    MAX_HEALTH_FIELD_NUMBER: _ClassVar[int]
    CAN_MOVE_FIELD_NUMBER: _ClassVar[int]
    HOME_LOCATION_ID_FIELD_NUMBER: _ClassVar[int]
    position: _common_pb2.WorldPosition
    facing: _common_pb2.Direction
    money: int
    energy: float
    max_energy: float
    health: int
    max_health: int
    can_move: bool
    home_location_id: str
    def __init__(self, position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., facing: _Optional[_Union[_common_pb2.Direction, str]] = ..., money: _Optional[int] = ..., energy: _Optional[float] = ..., max_energy: _Optional[float] = ..., health: _Optional[int] = ..., max_health: _Optional[int] = ..., can_move: _Optional[bool] = ..., home_location_id: _Optional[str] = ...) -> None: ...

class PlayersSnapshot(_message.Message):
    __slots__ = ("players",)
    PLAYERS_FIELD_NUMBER: _ClassVar[int]
    players: _containers.RepeatedCompositeFieldContainer[PlayerPresenceFact]
    def __init__(self, players: _Optional[_Iterable[_Union[PlayerPresenceFact, _Mapping]]] = ...) -> None: ...

class PlayerPresenceFact(_message.Message):
    __slots__ = ("player_id", "display_name", "relation", "online", "is_host", "position", "facing", "energy", "max_energy", "is_in_bed", "home_location_id")
    PLAYER_ID_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    RELATION_FIELD_NUMBER: _ClassVar[int]
    ONLINE_FIELD_NUMBER: _ClassVar[int]
    IS_HOST_FIELD_NUMBER: _ClassVar[int]
    POSITION_FIELD_NUMBER: _ClassVar[int]
    FACING_FIELD_NUMBER: _ClassVar[int]
    ENERGY_FIELD_NUMBER: _ClassVar[int]
    MAX_ENERGY_FIELD_NUMBER: _ClassVar[int]
    IS_IN_BED_FIELD_NUMBER: _ClassVar[int]
    HOME_LOCATION_ID_FIELD_NUMBER: _ClassVar[int]
    player_id: str
    display_name: str
    relation: PlayerRelation
    online: bool
    is_host: bool
    position: _common_pb2.WorldPosition
    facing: _common_pb2.Direction
    energy: float
    max_energy: float
    is_in_bed: bool
    home_location_id: str
    def __init__(self, player_id: _Optional[str] = ..., display_name: _Optional[str] = ..., relation: _Optional[_Union[PlayerRelation, str]] = ..., online: _Optional[bool] = ..., is_host: _Optional[bool] = ..., position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., facing: _Optional[_Union[_common_pb2.Direction, str]] = ..., energy: _Optional[float] = ..., max_energy: _Optional[float] = ..., is_in_bed: _Optional[bool] = ..., home_location_id: _Optional[str] = ...) -> None: ...

class WeatherFact(_message.Message):
    __slots__ = ("raining", "lightning", "snowing", "green_rain", "festival_day", "kind", "tomorrow")
    RAINING_FIELD_NUMBER: _ClassVar[int]
    LIGHTNING_FIELD_NUMBER: _ClassVar[int]
    SNOWING_FIELD_NUMBER: _ClassVar[int]
    GREEN_RAIN_FIELD_NUMBER: _ClassVar[int]
    FESTIVAL_DAY_FIELD_NUMBER: _ClassVar[int]
    KIND_FIELD_NUMBER: _ClassVar[int]
    TOMORROW_FIELD_NUMBER: _ClassVar[int]
    raining: bool
    lightning: bool
    snowing: bool
    green_rain: bool
    festival_day: bool
    kind: WeatherKind
    tomorrow: WeatherKind
    def __init__(self, raining: _Optional[bool] = ..., lightning: _Optional[bool] = ..., snowing: _Optional[bool] = ..., green_rain: _Optional[bool] = ..., festival_day: _Optional[bool] = ..., kind: _Optional[_Union[WeatherKind, str]] = ..., tomorrow: _Optional[_Union[WeatherKind, str]] = ...) -> None: ...

class UiSummary(_message.Message):
    __slots__ = ("menu_open", "menu_type")
    MENU_OPEN_FIELD_NUMBER: _ClassVar[int]
    MENU_TYPE_FIELD_NUMBER: _ClassVar[int]
    menu_open: bool
    menu_type: str
    def __init__(self, menu_open: _Optional[bool] = ..., menu_type: _Optional[str] = ...) -> None: ...

class DailyLuckFact(_message.Message):
    __slots__ = ("value", "tier")
    VALUE_FIELD_NUMBER: _ClassVar[int]
    TIER_FIELD_NUMBER: _ClassVar[int]
    value: float
    tier: DailyLuckTier
    def __init__(self, value: _Optional[float] = ..., tier: _Optional[_Union[DailyLuckTier, str]] = ...) -> None: ...

class TvCookingRecipeFact(_message.Message):
    __slots__ = ("available", "rerun", "recipe_key", "display_name", "already_known", "learnable")
    AVAILABLE_FIELD_NUMBER: _ClassVar[int]
    RERUN_FIELD_NUMBER: _ClassVar[int]
    RECIPE_KEY_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    ALREADY_KNOWN_FIELD_NUMBER: _ClassVar[int]
    LEARNABLE_FIELD_NUMBER: _ClassVar[int]
    available: bool
    rerun: bool
    recipe_key: str
    display_name: str
    already_known: bool
    learnable: bool
    def __init__(self, available: _Optional[bool] = ..., rerun: _Optional[bool] = ..., recipe_key: _Optional[str] = ..., display_name: _Optional[str] = ..., already_known: _Optional[bool] = ..., learnable: _Optional[bool] = ...) -> None: ...

class WorldSnapshot(_message.Message):
    __slots__ = ("world_revision", "area", "outdoors", "tiles", "entities", "characters", "entities_truncated", "characters_truncated")
    WORLD_REVISION_FIELD_NUMBER: _ClassVar[int]
    AREA_FIELD_NUMBER: _ClassVar[int]
    OUTDOORS_FIELD_NUMBER: _ClassVar[int]
    TILES_FIELD_NUMBER: _ClassVar[int]
    ENTITIES_FIELD_NUMBER: _ClassVar[int]
    CHARACTERS_FIELD_NUMBER: _ClassVar[int]
    ENTITIES_TRUNCATED_FIELD_NUMBER: _ClassVar[int]
    CHARACTERS_TRUNCATED_FIELD_NUMBER: _ClassVar[int]
    world_revision: str
    area: _common_pb2.TileArea
    outdoors: bool
    tiles: _containers.RepeatedCompositeFieldContainer[TileFact]
    entities: _containers.RepeatedCompositeFieldContainer[WorldEntityFact]
    characters: _containers.RepeatedCompositeFieldContainer[CharacterFact]
    entities_truncated: bool
    characters_truncated: bool
    def __init__(self, world_revision: _Optional[str] = ..., area: _Optional[_Union[_common_pb2.TileArea, _Mapping]] = ..., outdoors: _Optional[bool] = ..., tiles: _Optional[_Iterable[_Union[TileFact, _Mapping]]] = ..., entities: _Optional[_Iterable[_Union[WorldEntityFact, _Mapping]]] = ..., characters: _Optional[_Iterable[_Union[CharacterFact, _Mapping]]] = ..., entities_truncated: _Optional[bool] = ..., characters_truncated: _Optional[bool] = ...) -> None: ...

class TileFact(_message.Message):
    __slots__ = ("position", "passable", "occupied", "diggable", "water", "terrain_kind")
    POSITION_FIELD_NUMBER: _ClassVar[int]
    PASSABLE_FIELD_NUMBER: _ClassVar[int]
    OCCUPIED_FIELD_NUMBER: _ClassVar[int]
    DIGGABLE_FIELD_NUMBER: _ClassVar[int]
    WATER_FIELD_NUMBER: _ClassVar[int]
    TERRAIN_KIND_FIELD_NUMBER: _ClassVar[int]
    position: _common_pb2.WorldPosition
    passable: bool
    occupied: bool
    diggable: bool
    water: bool
    terrain_kind: str
    def __init__(self, position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., passable: _Optional[bool] = ..., occupied: _Optional[bool] = ..., diggable: _Optional[bool] = ..., water: _Optional[bool] = ..., terrain_kind: _Optional[str] = ...) -> None: ...

class WorldEntityFact(_message.Message):
    __slots__ = ("ref", "kind", "position", "display_name", "actionable", "tree", "fruit_tree", "crop", "resource_node", "resource_clump", "machine", "container", "bed", "furniture", "loose_item", "door", "warp", "generic_object", "hoe_dirt")
    REF_FIELD_NUMBER: _ClassVar[int]
    KIND_FIELD_NUMBER: _ClassVar[int]
    POSITION_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    ACTIONABLE_FIELD_NUMBER: _ClassVar[int]
    TREE_FIELD_NUMBER: _ClassVar[int]
    FRUIT_TREE_FIELD_NUMBER: _ClassVar[int]
    CROP_FIELD_NUMBER: _ClassVar[int]
    RESOURCE_NODE_FIELD_NUMBER: _ClassVar[int]
    RESOURCE_CLUMP_FIELD_NUMBER: _ClassVar[int]
    MACHINE_FIELD_NUMBER: _ClassVar[int]
    CONTAINER_FIELD_NUMBER: _ClassVar[int]
    BED_FIELD_NUMBER: _ClassVar[int]
    FURNITURE_FIELD_NUMBER: _ClassVar[int]
    LOOSE_ITEM_FIELD_NUMBER: _ClassVar[int]
    DOOR_FIELD_NUMBER: _ClassVar[int]
    WARP_FIELD_NUMBER: _ClassVar[int]
    GENERIC_OBJECT_FIELD_NUMBER: _ClassVar[int]
    HOE_DIRT_FIELD_NUMBER: _ClassVar[int]
    ref: _refs_pb2.Ref
    kind: EntityKind
    position: _common_pb2.WorldPosition
    display_name: str
    actionable: bool
    tree: TreeFact
    fruit_tree: FruitTreeFact
    crop: CropFact
    resource_node: ResourceNodeFact
    resource_clump: ResourceClumpFact
    machine: MachineFact
    container: ContainerFact
    bed: BedFact
    furniture: FurnitureFact
    loose_item: LooseItemFact
    door: DoorFact
    warp: WarpFact
    generic_object: GenericObjectFact
    hoe_dirt: HoeDirtFact
    def __init__(self, ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., kind: _Optional[_Union[EntityKind, str]] = ..., position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., display_name: _Optional[str] = ..., actionable: _Optional[bool] = ..., tree: _Optional[_Union[TreeFact, _Mapping]] = ..., fruit_tree: _Optional[_Union[FruitTreeFact, _Mapping]] = ..., crop: _Optional[_Union[CropFact, _Mapping]] = ..., resource_node: _Optional[_Union[ResourceNodeFact, _Mapping]] = ..., resource_clump: _Optional[_Union[ResourceClumpFact, _Mapping]] = ..., machine: _Optional[_Union[MachineFact, _Mapping]] = ..., container: _Optional[_Union[ContainerFact, _Mapping]] = ..., bed: _Optional[_Union[BedFact, _Mapping]] = ..., furniture: _Optional[_Union[FurnitureFact, _Mapping]] = ..., loose_item: _Optional[_Union[LooseItemFact, _Mapping]] = ..., door: _Optional[_Union[DoorFact, _Mapping]] = ..., warp: _Optional[_Union[WarpFact, _Mapping]] = ..., generic_object: _Optional[_Union[GenericObjectFact, _Mapping]] = ..., hoe_dirt: _Optional[_Union[HoeDirtFact, _Mapping]] = ...) -> None: ...

class TreeFact(_message.Message):
    __slots__ = ("growth_stage", "stump", "tapped", "mossy", "health")
    GROWTH_STAGE_FIELD_NUMBER: _ClassVar[int]
    STUMP_FIELD_NUMBER: _ClassVar[int]
    TAPPED_FIELD_NUMBER: _ClassVar[int]
    MOSSY_FIELD_NUMBER: _ClassVar[int]
    HEALTH_FIELD_NUMBER: _ClassVar[int]
    growth_stage: int
    stump: bool
    tapped: bool
    mossy: bool
    health: float
    def __init__(self, growth_stage: _Optional[int] = ..., stump: _Optional[bool] = ..., tapped: _Optional[bool] = ..., mossy: _Optional[bool] = ..., health: _Optional[float] = ...) -> None: ...

class FruitTreeFact(_message.Message):
    __slots__ = ("fruit_item_id", "growth_stage", "days_until_mature", "fruit_count", "stump")
    FRUIT_ITEM_ID_FIELD_NUMBER: _ClassVar[int]
    GROWTH_STAGE_FIELD_NUMBER: _ClassVar[int]
    DAYS_UNTIL_MATURE_FIELD_NUMBER: _ClassVar[int]
    FRUIT_COUNT_FIELD_NUMBER: _ClassVar[int]
    STUMP_FIELD_NUMBER: _ClassVar[int]
    fruit_item_id: str
    growth_stage: int
    days_until_mature: int
    fruit_count: int
    stump: bool
    def __init__(self, fruit_item_id: _Optional[str] = ..., growth_stage: _Optional[int] = ..., days_until_mature: _Optional[int] = ..., fruit_count: _Optional[int] = ..., stump: _Optional[bool] = ...) -> None: ...

class CropFact(_message.Message):
    __slots__ = ("crop_id", "harvest_item_id", "growth_phase", "ready_for_harvest", "watered", "dead", "regrows", "harvest_action")
    CROP_ID_FIELD_NUMBER: _ClassVar[int]
    HARVEST_ITEM_ID_FIELD_NUMBER: _ClassVar[int]
    GROWTH_PHASE_FIELD_NUMBER: _ClassVar[int]
    READY_FOR_HARVEST_FIELD_NUMBER: _ClassVar[int]
    WATERED_FIELD_NUMBER: _ClassVar[int]
    DEAD_FIELD_NUMBER: _ClassVar[int]
    REGROWS_FIELD_NUMBER: _ClassVar[int]
    HARVEST_ACTION_FIELD_NUMBER: _ClassVar[int]
    crop_id: str
    harvest_item_id: str
    growth_phase: int
    ready_for_harvest: bool
    watered: bool
    dead: bool
    regrows: bool
    harvest_action: CropHarvestAction
    def __init__(self, crop_id: _Optional[str] = ..., harvest_item_id: _Optional[str] = ..., growth_phase: _Optional[int] = ..., ready_for_harvest: _Optional[bool] = ..., watered: _Optional[bool] = ..., dead: _Optional[bool] = ..., regrows: _Optional[bool] = ..., harvest_action: _Optional[_Union[CropHarvestAction, str]] = ...) -> None: ...

class HoeDirtFact(_message.Message):
    __slots__ = ("watered",)
    WATERED_FIELD_NUMBER: _ClassVar[int]
    watered: bool
    def __init__(self, watered: _Optional[bool] = ...) -> None: ...

class ResourceNodeFact(_message.Message):
    __slots__ = ("node_kind", "hits_to_destroy", "required_tool")
    NODE_KIND_FIELD_NUMBER: _ClassVar[int]
    HITS_TO_DESTROY_FIELD_NUMBER: _ClassVar[int]
    REQUIRED_TOOL_FIELD_NUMBER: _ClassVar[int]
    node_kind: str
    hits_to_destroy: int
    required_tool: str
    def __init__(self, node_kind: _Optional[str] = ..., hits_to_destroy: _Optional[int] = ..., required_tool: _Optional[str] = ...) -> None: ...

class ResourceClumpFact(_message.Message):
    __slots__ = ("clump_kind", "width", "height", "health", "required_tool", "required_tool_level")
    CLUMP_KIND_FIELD_NUMBER: _ClassVar[int]
    WIDTH_FIELD_NUMBER: _ClassVar[int]
    HEIGHT_FIELD_NUMBER: _ClassVar[int]
    HEALTH_FIELD_NUMBER: _ClassVar[int]
    REQUIRED_TOOL_FIELD_NUMBER: _ClassVar[int]
    REQUIRED_TOOL_LEVEL_FIELD_NUMBER: _ClassVar[int]
    clump_kind: str
    width: int
    height: int
    health: int
    required_tool: str
    required_tool_level: int
    def __init__(self, clump_kind: _Optional[str] = ..., width: _Optional[int] = ..., height: _Optional[int] = ..., health: _Optional[int] = ..., required_tool: _Optional[str] = ..., required_tool_level: _Optional[int] = ...) -> None: ...

class ItemFact(_message.Message):
    __slots__ = ("ref", "qualified_item_id", "display_name", "stack", "quality", "category", "tool", "tool_level", "water_remaining", "water_capacity", "bottomless")
    REF_FIELD_NUMBER: _ClassVar[int]
    QUALIFIED_ITEM_ID_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    STACK_FIELD_NUMBER: _ClassVar[int]
    QUALITY_FIELD_NUMBER: _ClassVar[int]
    CATEGORY_FIELD_NUMBER: _ClassVar[int]
    TOOL_FIELD_NUMBER: _ClassVar[int]
    TOOL_LEVEL_FIELD_NUMBER: _ClassVar[int]
    WATER_REMAINING_FIELD_NUMBER: _ClassVar[int]
    WATER_CAPACITY_FIELD_NUMBER: _ClassVar[int]
    BOTTOMLESS_FIELD_NUMBER: _ClassVar[int]
    ref: _refs_pb2.Ref
    qualified_item_id: str
    display_name: str
    stack: int
    quality: int
    category: str
    tool: bool
    tool_level: int
    water_remaining: int
    water_capacity: int
    bottomless: bool
    def __init__(self, ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., qualified_item_id: _Optional[str] = ..., display_name: _Optional[str] = ..., stack: _Optional[int] = ..., quality: _Optional[int] = ..., category: _Optional[str] = ..., tool: _Optional[bool] = ..., tool_level: _Optional[int] = ..., water_remaining: _Optional[int] = ..., water_capacity: _Optional[int] = ..., bottomless: _Optional[bool] = ...) -> None: ...

class MachineFact(_message.Message):
    __slots__ = ("qualified_item_id", "ready_for_harvest", "minutes_until_ready", "held_item")
    QUALIFIED_ITEM_ID_FIELD_NUMBER: _ClassVar[int]
    READY_FOR_HARVEST_FIELD_NUMBER: _ClassVar[int]
    MINUTES_UNTIL_READY_FIELD_NUMBER: _ClassVar[int]
    HELD_ITEM_FIELD_NUMBER: _ClassVar[int]
    qualified_item_id: str
    ready_for_harvest: bool
    minutes_until_ready: int
    held_item: ItemFact
    def __init__(self, qualified_item_id: _Optional[str] = ..., ready_for_harvest: _Optional[bool] = ..., minutes_until_ready: _Optional[int] = ..., held_item: _Optional[_Union[ItemFact, _Mapping]] = ...) -> None: ...

class ContainerFact(_message.Message):
    __slots__ = ("container_kind", "capacity", "item_count")
    CONTAINER_KIND_FIELD_NUMBER: _ClassVar[int]
    CAPACITY_FIELD_NUMBER: _ClassVar[int]
    ITEM_COUNT_FIELD_NUMBER: _ClassVar[int]
    container_kind: str
    capacity: int
    item_count: int
    def __init__(self, container_kind: _Optional[str] = ..., capacity: _Optional[int] = ..., item_count: _Optional[int] = ...) -> None: ...

class BedFact(_message.Message):
    __slots__ = ("can_sleep", "occupied_tiles", "sleep_position")
    CAN_SLEEP_FIELD_NUMBER: _ClassVar[int]
    OCCUPIED_TILES_FIELD_NUMBER: _ClassVar[int]
    SLEEP_POSITION_FIELD_NUMBER: _ClassVar[int]
    can_sleep: bool
    occupied_tiles: _containers.RepeatedCompositeFieldContainer[_common_pb2.WorldPosition]
    sleep_position: _common_pb2.WorldPosition
    def __init__(self, can_sleep: _Optional[bool] = ..., occupied_tiles: _Optional[_Iterable[_Union[_common_pb2.WorldPosition, _Mapping]]] = ..., sleep_position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ...) -> None: ...

class FurnitureFact(_message.Message):
    __slots__ = ("furniture_kind", "rotation", "occupied_tiles")
    FURNITURE_KIND_FIELD_NUMBER: _ClassVar[int]
    ROTATION_FIELD_NUMBER: _ClassVar[int]
    OCCUPIED_TILES_FIELD_NUMBER: _ClassVar[int]
    furniture_kind: str
    rotation: int
    occupied_tiles: _containers.RepeatedCompositeFieldContainer[_common_pb2.WorldPosition]
    def __init__(self, furniture_kind: _Optional[str] = ..., rotation: _Optional[int] = ..., occupied_tiles: _Optional[_Iterable[_Union[_common_pb2.WorldPosition, _Mapping]]] = ...) -> None: ...

class LooseItemFact(_message.Message):
    __slots__ = ("item", "can_pick_up")
    ITEM_FIELD_NUMBER: _ClassVar[int]
    CAN_PICK_UP_FIELD_NUMBER: _ClassVar[int]
    item: ItemFact
    can_pick_up: bool
    def __init__(self, item: _Optional[_Union[ItemFact, _Mapping]] = ..., can_pick_up: _Optional[bool] = ...) -> None: ...

class DoorFact(_message.Message):
    __slots__ = ("locked", "target_location_id", "target_tile")
    LOCKED_FIELD_NUMBER: _ClassVar[int]
    TARGET_LOCATION_ID_FIELD_NUMBER: _ClassVar[int]
    TARGET_TILE_FIELD_NUMBER: _ClassVar[int]
    locked: bool
    target_location_id: str
    target_tile: _common_pb2.TilePoint
    def __init__(self, locked: _Optional[bool] = ..., target_location_id: _Optional[str] = ..., target_tile: _Optional[_Union[_common_pb2.TilePoint, _Mapping]] = ...) -> None: ...

class WarpFact(_message.Message):
    __slots__ = ("destination", "npc_only")
    DESTINATION_FIELD_NUMBER: _ClassVar[int]
    NPC_ONLY_FIELD_NUMBER: _ClassVar[int]
    destination: _common_pb2.WorldPosition
    npc_only: bool
    def __init__(self, destination: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., npc_only: _Optional[bool] = ...) -> None: ...

class GenericObjectFact(_message.Message):
    __slots__ = ("runtime_type", "qualified_item_id")
    RUNTIME_TYPE_FIELD_NUMBER: _ClassVar[int]
    QUALIFIED_ITEM_ID_FIELD_NUMBER: _ClassVar[int]
    runtime_type: str
    qualified_item_id: str
    def __init__(self, runtime_type: _Optional[str] = ..., qualified_item_id: _Optional[str] = ...) -> None: ...

class CharacterFact(_message.Message):
    __slots__ = ("ref", "kind", "name", "display_name", "position", "facing", "npc", "monster", "farm_animal", "pet", "horse")
    REF_FIELD_NUMBER: _ClassVar[int]
    KIND_FIELD_NUMBER: _ClassVar[int]
    NAME_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    POSITION_FIELD_NUMBER: _ClassVar[int]
    FACING_FIELD_NUMBER: _ClassVar[int]
    NPC_FIELD_NUMBER: _ClassVar[int]
    MONSTER_FIELD_NUMBER: _ClassVar[int]
    FARM_ANIMAL_FIELD_NUMBER: _ClassVar[int]
    PET_FIELD_NUMBER: _ClassVar[int]
    HORSE_FIELD_NUMBER: _ClassVar[int]
    ref: _refs_pb2.Ref
    kind: CharacterKind
    name: str
    display_name: str
    position: _common_pb2.WorldPosition
    facing: _common_pb2.Direction
    npc: NpcFact
    monster: MonsterFact
    farm_animal: FarmAnimalFact
    pet: PetFact
    horse: HorseFact
    def __init__(self, ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., kind: _Optional[_Union[CharacterKind, str]] = ..., name: _Optional[str] = ..., display_name: _Optional[str] = ..., position: _Optional[_Union[_common_pb2.WorldPosition, _Mapping]] = ..., facing: _Optional[_Union[_common_pb2.Direction, str]] = ..., npc: _Optional[_Union[NpcFact, _Mapping]] = ..., monster: _Optional[_Union[MonsterFact, _Mapping]] = ..., farm_animal: _Optional[_Union[FarmAnimalFact, _Mapping]] = ..., pet: _Optional[_Union[PetFact, _Mapping]] = ..., horse: _Optional[_Union[HorseFact, _Mapping]] = ...) -> None: ...

class NpcFact(_message.Message):
    __slots__ = ("can_socialize", "friendship_points", "has_dialogue")
    CAN_SOCIALIZE_FIELD_NUMBER: _ClassVar[int]
    FRIENDSHIP_POINTS_FIELD_NUMBER: _ClassVar[int]
    HAS_DIALOGUE_FIELD_NUMBER: _ClassVar[int]
    can_socialize: bool
    friendship_points: int
    has_dialogue: bool
    def __init__(self, can_socialize: _Optional[bool] = ..., friendship_points: _Optional[int] = ..., has_dialogue: _Optional[bool] = ...) -> None: ...

class MonsterFact(_message.Message):
    __slots__ = ("health", "max_health", "contact_damage")
    HEALTH_FIELD_NUMBER: _ClassVar[int]
    MAX_HEALTH_FIELD_NUMBER: _ClassVar[int]
    CONTACT_DAMAGE_FIELD_NUMBER: _ClassVar[int]
    health: int
    max_health: int
    contact_damage: int
    def __init__(self, health: _Optional[int] = ..., max_health: _Optional[int] = ..., contact_damage: _Optional[int] = ...) -> None: ...

class FarmAnimalFact(_message.Message):
    __slots__ = ("animal_type", "produce_ready", "petted_today", "friendship", "happiness")
    ANIMAL_TYPE_FIELD_NUMBER: _ClassVar[int]
    PRODUCE_READY_FIELD_NUMBER: _ClassVar[int]
    PETTED_TODAY_FIELD_NUMBER: _ClassVar[int]
    FRIENDSHIP_FIELD_NUMBER: _ClassVar[int]
    HAPPINESS_FIELD_NUMBER: _ClassVar[int]
    animal_type: str
    produce_ready: bool
    petted_today: bool
    friendship: int
    happiness: int
    def __init__(self, animal_type: _Optional[str] = ..., produce_ready: _Optional[bool] = ..., petted_today: _Optional[bool] = ..., friendship: _Optional[int] = ..., happiness: _Optional[int] = ...) -> None: ...

class PetFact(_message.Message):
    __slots__ = ("pet_type", "petted_today", "friendship")
    PET_TYPE_FIELD_NUMBER: _ClassVar[int]
    PETTED_TODAY_FIELD_NUMBER: _ClassVar[int]
    FRIENDSHIP_FIELD_NUMBER: _ClassVar[int]
    pet_type: str
    petted_today: bool
    friendship: int
    def __init__(self, pet_type: _Optional[str] = ..., petted_today: _Optional[bool] = ..., friendship: _Optional[int] = ...) -> None: ...

class HorseFact(_message.Message):
    __slots__ = ("has_rider",)
    HAS_RIDER_FIELD_NUMBER: _ClassVar[int]
    has_rider: bool
    def __init__(self, has_rider: _Optional[bool] = ...) -> None: ...

class InventorySnapshot(_message.Message):
    __slots__ = ("inventory_revision", "container_kind", "container_ref", "slot_count", "slots")
    INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    CONTAINER_KIND_FIELD_NUMBER: _ClassVar[int]
    CONTAINER_REF_FIELD_NUMBER: _ClassVar[int]
    SLOT_COUNT_FIELD_NUMBER: _ClassVar[int]
    SLOTS_FIELD_NUMBER: _ClassVar[int]
    inventory_revision: str
    container_kind: str
    container_ref: _refs_pb2.Ref
    slot_count: int
    slots: _containers.RepeatedCompositeFieldContainer[InventorySlot]
    def __init__(self, inventory_revision: _Optional[str] = ..., container_kind: _Optional[str] = ..., container_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., slot_count: _Optional[int] = ..., slots: _Optional[_Iterable[_Union[InventorySlot, _Mapping]]] = ...) -> None: ...

class InventorySlot(_message.Message):
    __slots__ = ("index", "item")
    INDEX_FIELD_NUMBER: _ClassVar[int]
    ITEM_FIELD_NUMBER: _ClassVar[int]
    index: int
    item: ItemFact
    def __init__(self, index: _Optional[int] = ..., item: _Optional[_Union[ItemFact, _Mapping]] = ...) -> None: ...

class CraftingMaterialRequirement(_message.Message):
    __slots__ = ("ingredient_key", "display_name", "required_quantity", "available_quantity")
    INGREDIENT_KEY_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    REQUIRED_QUANTITY_FIELD_NUMBER: _ClassVar[int]
    AVAILABLE_QUANTITY_FIELD_NUMBER: _ClassVar[int]
    ingredient_key: str
    display_name: str
    required_quantity: int
    available_quantity: int
    def __init__(self, ingredient_key: _Optional[str] = ..., display_name: _Optional[str] = ..., required_quantity: _Optional[int] = ..., available_quantity: _Optional[int] = ...) -> None: ...

class CraftingOutputFact(_message.Message):
    __slots__ = ("qualified_item_id", "display_name", "quantity")
    QUALIFIED_ITEM_ID_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    QUANTITY_FIELD_NUMBER: _ClassVar[int]
    qualified_item_id: str
    display_name: str
    quantity: int
    def __init__(self, qualified_item_id: _Optional[str] = ..., display_name: _Optional[str] = ..., quantity: _Optional[int] = ...) -> None: ...

class CraftingRecipeFact(_message.Message):
    __slots__ = ("recipe_key", "display_name", "known", "craftable", "materials", "possible_outputs")
    RECIPE_KEY_FIELD_NUMBER: _ClassVar[int]
    DISPLAY_NAME_FIELD_NUMBER: _ClassVar[int]
    KNOWN_FIELD_NUMBER: _ClassVar[int]
    CRAFTABLE_FIELD_NUMBER: _ClassVar[int]
    MATERIALS_FIELD_NUMBER: _ClassVar[int]
    POSSIBLE_OUTPUTS_FIELD_NUMBER: _ClassVar[int]
    recipe_key: str
    display_name: str
    known: bool
    craftable: bool
    materials: _containers.RepeatedCompositeFieldContainer[CraftingMaterialRequirement]
    possible_outputs: _containers.RepeatedCompositeFieldContainer[CraftingOutputFact]
    def __init__(self, recipe_key: _Optional[str] = ..., display_name: _Optional[str] = ..., known: _Optional[bool] = ..., craftable: _Optional[bool] = ..., materials: _Optional[_Iterable[_Union[CraftingMaterialRequirement, _Mapping]]] = ..., possible_outputs: _Optional[_Iterable[_Union[CraftingOutputFact, _Mapping]]] = ...) -> None: ...

class UiInventoryLink(_message.Message):
    __slots__ = ("side", "inventory_revision", "slot_count", "container_ref")
    SIDE_FIELD_NUMBER: _ClassVar[int]
    INVENTORY_REVISION_FIELD_NUMBER: _ClassVar[int]
    SLOT_COUNT_FIELD_NUMBER: _ClassVar[int]
    CONTAINER_REF_FIELD_NUMBER: _ClassVar[int]
    side: UiInventorySide
    inventory_revision: str
    slot_count: int
    container_ref: _refs_pb2.Ref
    def __init__(self, side: _Optional[_Union[UiInventorySide, str]] = ..., inventory_revision: _Optional[str] = ..., slot_count: _Optional[int] = ..., container_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ...) -> None: ...

class UiSnapshot(_message.Message):
    __slots__ = ("ui_revision", "menu_open", "menu", "elements", "inventories")
    UI_REVISION_FIELD_NUMBER: _ClassVar[int]
    MENU_OPEN_FIELD_NUMBER: _ClassVar[int]
    MENU_FIELD_NUMBER: _ClassVar[int]
    ELEMENTS_FIELD_NUMBER: _ClassVar[int]
    INVENTORIES_FIELD_NUMBER: _ClassVar[int]
    ui_revision: str
    menu_open: bool
    menu: UiMenuFact
    elements: _containers.RepeatedCompositeFieldContainer[UiElementFact]
    inventories: _containers.RepeatedCompositeFieldContainer[UiInventoryLink]
    def __init__(self, ui_revision: _Optional[str] = ..., menu_open: _Optional[bool] = ..., menu: _Optional[_Union[UiMenuFact, _Mapping]] = ..., elements: _Optional[_Iterable[_Union[UiElementFact, _Mapping]]] = ..., inventories: _Optional[_Iterable[_Union[UiInventoryLink, _Mapping]]] = ...) -> None: ...

class UiMenuFact(_message.Message):
    __slots__ = ("menu_type", "menu_kind", "title", "modal", "dialogue_text")
    MENU_TYPE_FIELD_NUMBER: _ClassVar[int]
    MENU_KIND_FIELD_NUMBER: _ClassVar[int]
    TITLE_FIELD_NUMBER: _ClassVar[int]
    MODAL_FIELD_NUMBER: _ClassVar[int]
    DIALOGUE_TEXT_FIELD_NUMBER: _ClassVar[int]
    menu_type: str
    menu_kind: _common_pb2.MenuKind
    title: str
    modal: bool
    dialogue_text: str
    def __init__(self, menu_type: _Optional[str] = ..., menu_kind: _Optional[_Union[_common_pb2.MenuKind, str]] = ..., title: _Optional[str] = ..., modal: _Optional[bool] = ..., dialogue_text: _Optional[str] = ...) -> None: ...

class UiElementFact(_message.Message):
    __slots__ = ("ref", "kind", "label", "visible", "enabled", "center", "index", "item", "price", "stock", "inventory_side", "item_ref", "equipment_slot_kind", "crafting_recipe")
    REF_FIELD_NUMBER: _ClassVar[int]
    KIND_FIELD_NUMBER: _ClassVar[int]
    LABEL_FIELD_NUMBER: _ClassVar[int]
    VISIBLE_FIELD_NUMBER: _ClassVar[int]
    ENABLED_FIELD_NUMBER: _ClassVar[int]
    CENTER_FIELD_NUMBER: _ClassVar[int]
    INDEX_FIELD_NUMBER: _ClassVar[int]
    ITEM_FIELD_NUMBER: _ClassVar[int]
    PRICE_FIELD_NUMBER: _ClassVar[int]
    STOCK_FIELD_NUMBER: _ClassVar[int]
    INVENTORY_SIDE_FIELD_NUMBER: _ClassVar[int]
    ITEM_REF_FIELD_NUMBER: _ClassVar[int]
    EQUIPMENT_SLOT_KIND_FIELD_NUMBER: _ClassVar[int]
    CRAFTING_RECIPE_FIELD_NUMBER: _ClassVar[int]
    ref: _refs_pb2.Ref
    kind: UiElementKind
    label: str
    visible: bool
    enabled: bool
    center: _common_pb2.PixelPoint
    index: int
    item: ItemFact
    price: int
    stock: int
    inventory_side: UiInventorySide
    item_ref: _refs_pb2.Ref
    equipment_slot_kind: UiEquipmentSlotKind
    crafting_recipe: CraftingRecipeFact
    def __init__(self, ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., kind: _Optional[_Union[UiElementKind, str]] = ..., label: _Optional[str] = ..., visible: _Optional[bool] = ..., enabled: _Optional[bool] = ..., center: _Optional[_Union[_common_pb2.PixelPoint, _Mapping]] = ..., index: _Optional[int] = ..., item: _Optional[_Union[ItemFact, _Mapping]] = ..., price: _Optional[int] = ..., stock: _Optional[int] = ..., inventory_side: _Optional[_Union[UiInventorySide, str]] = ..., item_ref: _Optional[_Union[_refs_pb2.Ref, _Mapping]] = ..., equipment_slot_kind: _Optional[_Union[UiEquipmentSlotKind, str]] = ..., crafting_recipe: _Optional[_Union[CraftingRecipeFact, _Mapping]] = ...) -> None: ...
