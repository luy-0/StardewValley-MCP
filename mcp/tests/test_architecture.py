from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "mcp" / "src" / "stardew_valley_mcp"
MOD = ROOT / "mod" / "src" / "StardewValleyMcp.Mod"


def test_transport_is_protocol_and_capability_agnostic() -> None:
    source = (PACKAGE / "transport.py").read_text()
    forbidden = ("capabilities_pb2", "queries_pb2", "common_pb2", "query_runtime", "query_world", "CapabilityResult", "_project_result", "QUERY_", "TIMEOUT_MS")
    assert not [token for token in forbidden if token in source]


def test_package_has_one_generated_catalog_and_no_single_tool_json() -> None:
    assert (PACKAGE / "generated" / "tool_catalog.json").is_file()
    assert not list(PACKAGE.glob("*_tool.json"))


def test_command_runtime_is_the_only_authenticated_frame_reader() -> None:
    runtime = (PACKAGE / "command_runtime.py").read_text()
    client = (PACKAGE / "client.py").read_text()
    server = (PACKAGE / "server.py").read_text()

    assert runtime.count("receive_authenticated()") == 1
    assert "receive_authenticated(" not in client
    assert "receive_authenticated(" not in server


def test_client_operation_mapping_comes_from_command_request_descriptor() -> None:
    source = (PACKAGE / "client.py").read_text()

    assert "CommandRequest.DESCRIPTOR.fields_by_name" in source
    assert "GetMessageClass(field.message_type)" in source
    assert "request_classes" not in source
    assert "queries_pb2" not in source


def test_catalog_support_set_is_not_hardcoded_to_observation_capabilities() -> None:
    source = (PACKAGE / "catalog.py").read_text()

    assert "OBSERVATION_POLICY" not in source
    assert "frozenset(self._capabilities)" in source


def test_query_inventory_is_composed_without_transport_or_server_branch() -> None:
    composition = (MOD / "Bootstrap" / "DefaultCapabilitySet.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    local_server = (MOD / "Transport" / "LocalServer.cs").read_text()

    assert "new QueryInventoryHandler(refs)" in composition
    assert "query_inventory" not in transport
    assert "QueryInventory" not in local_server


def test_chest_inventory_reader_never_creates_shared_backing() -> None:
    source = (MOD / "Projection" / "ChestInventoryReader.cs").read_text()
    assert ".GetItemsForPlayer(" not in source
    assert ".GetOrCreateGlobalInventory(" not in source


def test_query_ui_is_composed_without_transport_server_or_projection_branch() -> None:
    composition = (MOD / "Bootstrap" / "DefaultCapabilitySet.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    server = (PACKAGE / "server.py").read_text()
    projection = (PACKAGE / "projection.py").read_text()
    local_server = (MOD / "Transport" / "LocalServer.cs").read_text()

    assert "new QueryUiHandler(refs)" in composition
    assert "query_ui" not in transport
    assert "query_ui" not in server
    assert "query_ui" not in projection
    assert "QueryUi" not in local_server


def test_query_ui_runtime_has_no_generic_clickable_mutation_or_callback_invocation() -> None:
    source = (MOD / "Projection" / "UiRuntimeProjector.cs").read_text()
    forbidden = (
        "allClickableComponents",
        "populateClickableComponentList",
        "GetCurrentPage(",
        "containsPoint(",
        "receiveLeftClick",
        "receiveRightClick",
        "performHoverAction",
        "receiveKeyPress",
        "setUpIcons(",
        "changeTab(",
        "canPurchaseCheck(",
        "onPurchase(",
        "onSell(",
        ".Invoke(",
    )
    assert not [token for token in forbidden if token in source]
    assert "menu.GetType()" in source
    assert "ClassifyExact" in source
    assert source.count("getCurrentString(") == 1
    assert "GetType().Assembly" not in source
    assert "IsExactActivationKnownType" in source


def test_item_grab_projection_is_isolated_read_only_and_not_activatable() -> None:
    runtime = (MOD / "Projection" / "UiRuntimeProjector.cs").read_text()
    item_grab = (MOD / "Projection" / "ItemGrabMenuProjector.cs").read_text()
    actions = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()

    assert runtime.count("ItemGrabMenuProjector.Extract(") == 2
    assert "TryLocateSupportedContainer" not in runtime
    assert "typeof(ItemGrabMenu)" in runtime
    for required in (
        "menu.source == ItemGrabMenu.source_chest",
        "candidate.GetType() == typeof(Chest)",
        "Chest.SpecialChestTypes.BigChest",
        "current.GetFridge(",
        "current.Objects.Pairs",
        "menu.heldItem is not null",
        "InventoryViewResolver.CreatePlayer(player)",
        "InventoryViewResolver.CreateAttachedContainer(",
        "InventoryProjector.Project(",
        "UI_INVENTORY_CAPTURE_INCOMPLETE",
    ):
        assert required in item_grab
    forbidden = (
        "receiveLeftClick",
        "receiveRightClick",
        "actualInventory[",
        ".AddItem(",
        ".removeItem",
        ".Invoke(",
    )
    assert not [token for token in forbidden if token in item_grab]
    activate = actions.split("public MenuActionAttempt Activate", 1)[1]
    assert "UiExtractorKind.ItemGrabSlot" not in activate


def test_inventory_transfer_is_an_isolated_revision_guarded_transaction() -> None:
    composition = (MOD / "Bootstrap" / "DefaultCapabilitySet.cs").read_text()
    handler = (MOD / "Capabilities" / "Actions" / "TransferInventoryItemHandler.cs").read_text()
    adapter = (MOD / "Capabilities" / "Actions" / "InventoryTransferRuntimeAdapter.cs").read_text()
    planner = (MOD / "Capabilities" / "Actions" / "InventoryTransferPlanner.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    server = (PACKAGE / "server.py").read_text()
    projection = (PACKAGE / "projection.py").read_text()

    assert "new TransferInventoryItemHandler(refs)" in composition
    assert "transfer_inventory_item" not in transport
    assert "transfer_inventory_item" not in server
    assert "transfer_inventory_item" not in projection
    for required in (
        "InventoryTransferPlanner.Plan(",
        "CanCancel => !_committing",
        "RollbackAndVerify(",
        "TargetWritesHold(",
    ):
        assert required in handler
    for required in (
        "UiRuntimeProjector.Capture(",
        "GetMutex().IsLockHeld()",
        "TryLocateSupportedContainer(",
        "IInventoryTransferCommit Commit(",
        "void Rollback()",
    ):
        assert required in adapter
    forbidden = (
        "receiveLeftClick",
        "receiveRightClick",
        "leftClick(",
        "rightClick(",
        "behaviorOnItemGrab",
        "behaviorFunction",
        "heldItem =",
        ".Invoke(",
    )
    runtime = handler + adapter
    assert not [token for token in forbidden if token in runtime]
    assert "StardewValley.Menus" not in planner
    assert "StardewValley.Objects" not in planner


def test_dialogue_advance_activation_uses_native_semantic_path() -> None:
    source = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()
    semantic_branch = source.index(
        "resolved.Target.Extractor == UiExtractorKind.DialogueAdvance"
    )
    native_click = source.index("((DialogueBox)menu).receiveLeftClick(0, 0)")
    component_branch = source.index(
        "resolved.Target.Component is not ClickableComponent component"
    )
    assert semantic_branch < native_click < component_branch


def test_close_menu_blocks_dialogue_family_before_generic_exit_path() -> None:
    source = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()
    close = source.split("public MenuActionAttempt Close()", 1)[1].split(
        "public MenuActionAttempt Activate", 1
    )[0]
    family_guard = close.index("menu is DialogueBox dialogue")
    exact_guard = close.index("dialogue.GetType() != typeof(DialogueBox)")
    native_close = close.index("dialogue.receiveLeftClick(0, 0)")
    generic_ready = close.index("!menu.readyToClose()")
    generic_exit = close.index("menu.exitThisMenu()")
    assert family_guard < exact_guard < native_close < generic_ready < generic_exit
    assert "dialogue.exitThisMenu" not in close
    helper = close.split("private static bool CanSafelyCloseDialogue", 1)[1]
    assert helper.index("try") < helper.index("isOnFinalDialogue()") < helper.index("catch")


def test_default_capability_set_is_the_unique_concrete_handler_composition_root() -> None:
    composition_path = MOD / "Bootstrap" / "DefaultCapabilitySet.cs"
    registry = (MOD / "Runtime" / "CapabilityRegistry.cs").read_text()
    handlers = {
        "SayHandler",
        "EmoteHandler",
        "FaceHandler",
        "NavigateHandler",
        "InteractHandler",
        "UseToolHandler",
        "EquipHandler",
        "TransferInventoryItemHandler",
        "OpenMenuHandler",
        "ActivateUiHandler",
        "CloseMenuHandler",
        "QueryRuntimeHandler",
        "QueryWorldHandler",
        "QueryInventoryHandler",
        "QueryUiHandler",
        "InspectHandler",
    }

    assert "CapabilityRegistry(IEnumerable<ICapabilityHandler> handlers)" in registry
    assert not re.findall(r"new\s+\w*Handler\s*\(", registry)

    for source_path in MOD.rglob("*.cs"):
        constructions = set(re.findall(r"new\s+(\w*Handler)\s*\(", source_path.read_text()))
        if source_path == composition_path:
            assert constructions == handlers
        else:
            assert not constructions, source_path.relative_to(MOD)
