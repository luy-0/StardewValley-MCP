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
    assert "ShopEnabled" not in source


def test_inventory_page_projection_is_isolated_fail_closed_and_read_only() -> None:
    runtime = (MOD / "Projection" / "UiRuntimeProjector.cs").read_text()
    inventory_page = (MOD / "Projection" / "InventoryPageProjector.cs").read_text()
    actions = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()

    assert runtime.count("InventoryPageProjector.Extract(") == 2
    assert runtime.count("InventoryPageProjector.CapturePageState(") == 2
    assert "GetCurrentPage(" not in runtime
    assert "menu.GetCurrentPage()" in inventory_page
    assert "if (completeness == UiElementSetCompleteness.Complete)" in runtime
    assert "inventories.Add(playerLink);" in inventory_page
    assert inventory_page.index("inventories.Add(playerLink);") > inventory_page.index("CreateEquipmentDescriptors(")
    for forbidden in (
        "receiveLeftClick",
        "receiveRightClick",
        ".AddItem(",
        ".removeItem",
        ".Invoke(",
    ):
        assert forbidden not in inventory_page
    activate = actions.split("public MenuActionAttempt Activate", 1)[1]
    for required in (
        "resolved.Target.PublicKind",
        "fact.Kind",
        "typeof(GameMenu)",
        "GameMenu 仅允许激活顶部页签",
    ):
        assert required in activate


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
        "InventoryViewResolver.CreatePlayerForMenu(",
        "menu.inventory.capacity",
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


def test_shop_rows_are_query_only_and_not_generic_activate_targets() -> None:
    runtime = (MOD / "Projection" / "UiRuntimeProjector.cs").read_text()
    actions = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()

    shop = runtime.split(
        "private static UiElementSetCompleteness ExtractShop", 1
    )[1].split("internal static int TabIndex", 1)[0]
    activate = actions.split("public MenuActionAttempt Activate", 1)[1]

    for required in (
        "UiExtractorKind.ShopSaleRow",
        "ItemFactProjector.Project(item)",
        "stockInfo.Price",
        "checked((uint)stockInfo.Stock)",
        '"shop-sale-row:{absoluteIndex}"',
    ):
        assert required in shop
    assert re.search(r"label,\s+visible,\s+false,\s+center\.X", shop)
    assert "UiExtractorKind.ShopSaleRow" not in activate
    assert "typeof(ShopMenu)" not in activate
    assert "menu.receiveLeftClick(center.X, center.Y)" in activate


def test_dialogue_response_activation_selects_native_response_before_click() -> None:
    actions = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()
    response = actions.split(
        "if (resolved.Target.Extractor == UiExtractorKind.DialogueResponse", 1
    )[1].split("var activated =", 1)[0]

    assert "dialogue.performHoverAction(dialogueCenter.X, dialogueCenter.Y);" in response
    assert "dialogue.receiveLeftClick(dialogueCenter.X, dialogueCenter.Y);" in response
    assert response.index("performHoverAction") < response.index("receiveLeftClick")


def test_purchase_shop_item_is_isolated_ref_driven_and_uses_no_coordinate_click() -> None:
    composition = (MOD / "Bootstrap" / "DefaultCapabilitySet.cs").read_text()
    handler = (MOD / "Capabilities" / "Actions" / "PurchaseShopItemHandler.cs").read_text()
    adapter = (MOD / "Capabilities" / "Actions" / "ShopPurchaseRuntimeAdapter.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    server = (PACKAGE / "server.py").read_text()
    projection = (PACKAGE / "projection.py").read_text()

    assert "new PurchaseShopItemHandler(refs)" in composition
    for source in (transport, server, projection):
        assert "purchase_shop_item" not in source
    for required in (
        "CanCancel => !_committing",
        "UiExtractorKind.ShopSaleRow",
        'candidate.Name == "tryToPurchaseItem"',
        "typeof(ISalable), typeof(ISalable), typeof(int), typeof(int), typeof(int)",
        "state.Player.GetItemReceiveBehavior(",
        "if (!needsInventorySpace)",
        "output.CanBuyItem(state.Player)",
        "state.Player.addItemToInventory(purchased)",
        "state.Menu.heldItem = remainder",
        "InventoryProjector.Project(",
    ):
        assert required in handler + adapter
    for forbidden in (
        "receiveLeftClick",
        "receiveRightClick",
        "createItemDebris",
        "Game1.oldKBState",
        "LeftShift",
        "LeftControl",
    ):
        assert forbidden not in handler + adapter


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


def test_equipment_slot_write_is_isolated_fail_closed_and_not_ui_click_driven() -> None:
    composition = (MOD / "Bootstrap" / "DefaultCapabilitySet.cs").read_text()
    handler = (MOD / "Capabilities" / "Actions" / "SetEquipmentSlotHandler.cs").read_text()
    adapter = (MOD / "Capabilities" / "Actions" / "EquipmentSlotRuntimeAdapter.cs").read_text()
    menu_actions = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()
    ui_runtime = (MOD / "Projection" / "UiRuntimeProjector.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    server = (PACKAGE / "server.py").read_text()
    projection = (PACKAGE / "projection.py").read_text()

    assert "new SetEquipmentSlotHandler(refs)" in composition
    assert "set_equipment_slot" not in transport
    assert "set_equipment_slot" not in server
    assert "set_equipment_slot" not in projection
    assert "set_equipment_slot" not in menu_actions
    assert "set_equipment_slot" not in ui_runtime
    for required in (
        "CanCancel => !_committing",
        "EquipmentSlotMutationPlanner.SamePlan(",
        "RollbackAndVerify(",
        "UiRuntimeProjector.Capture(",
        "InventoryPageProjector.TryClassifyEquipmentComponent(",
        "item.GetType() == typeof(Hat)",
        "item.GetType() != typeof(CombinedRing)",
        "ring.GetType() == typeof(Ring)",
        "state.Player.CurrentToolIndex != capture.CurrentToolIndex",
    ):
        assert required in handler + adapter
    forbidden = (
        "PerformSpecialItemPlaceReplacement",
        "PerformSpecialItemGrabReplacement",
        "receiveLeftClick",
        "receiveRightClick",
        "CursorSlotItem =",
        "move_inventory_item",
        "behaviorFunction",
        "behaviorOnItemGrab",
    )
    assert not [token for token in forbidden if token in handler + adapter]


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


def test_inventory_slot_move_is_isolated_fail_closed_and_not_ui_click_driven() -> None:
    composition = (MOD / "Bootstrap" / "DefaultCapabilitySet.cs").read_text()
    handler = (MOD / "Capabilities" / "Actions" / "MoveInventoryItemHandler.cs").read_text()
    adapter = (MOD / "Capabilities" / "Actions" / "InventorySlotMoveRuntimeAdapter.cs").read_text()
    planner = (MOD / "Capabilities" / "Actions" / "InventorySlotMutationPlanner.cs").read_text()
    menu_actions = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()
    ui_runtime = (MOD / "Projection" / "UiRuntimeProjector.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    server = (PACKAGE / "server.py").read_text()
    projection = (PACKAGE / "projection.py").read_text()

    assert "new MoveInventoryItemHandler(refs)" in composition
    for source in (transport, server, projection, menu_actions, ui_runtime):
        assert "move_inventory_item" not in source
    for required in (
        "CanCancel => !_committing",
        "InventorySlotMutationPlanner.SamePlan(",
        "RollbackAndVerify(",
        "UiRuntimeProjector.Capture(",
        "InventoryPageProjector.IsCompleteBackpackMenu(",
        "state.Player.CurrentToolIndex != capture.CurrentToolIndex",
        "ReferenceEquals(value.Component, value.Target)",
    ):
        assert required in handler + adapter
    forbidden = (
        "Utility.addItemToInventory",
        "OnItemReceived",
        "receiveLeftClick",
        "receiveRightClick",
        "CursorSlotItem =",
        "heldItem =",
        "canStackWith",
        "getOne()",
    )
    runtime = handler + adapter + planner
    assert not [token for token in forbidden if token in runtime]
    assert "using StardewValley" not in planner


def test_crafting_projection_is_read_only_modular_and_descriptor_projected() -> None:
    runtime = (MOD / "Projection" / "UiRuntimeProjector.cs").read_text()
    page = (MOD / "Projection" / "CraftingPageProjector.cs").read_text()
    fact = (MOD / "Projection" / "CraftingRecipeFactProjector.cs").read_text()
    ui = (MOD / "Projection" / "UiProjector.cs").read_text()
    menu_actions = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    projection = (PACKAGE / "projection.py").read_text()

    assert "CraftingPageProjector.Extract(" in runtime
    assert "CraftingRecipeFactProjector.Project(" in page
    assert "CraftingRecipe: item.Fact" in page
    assert "UiElementKind.CraftingRecipe" in ui
    assert "CraftingRecipe" not in transport
    assert "crafting_recipe" not in projection
    for forbidden in (
        "createItem(",
        "consumeIngredients(",
        "receiveLeftClick(",
        "receiveRightClick(",
        "addItemToInventory",
        "createItemDebris(",
        "GetItemData(",
    ):
        assert forbidden not in page + fact
    assert "GameMenu 仅允许激活顶部页签" in menu_actions


def test_craft_item_is_an_isolated_ref_driven_action_without_mcp_special_cases() -> None:
    composition = (MOD / "Bootstrap" / "DefaultCapabilitySet.cs").read_text()
    handler = (MOD / "Capabilities" / "Actions" / "CraftItemHandler.cs").read_text()
    adapter = (MOD / "Capabilities" / "Actions" / "CraftItemRuntimeAdapter.cs").read_text()
    transfer = (
        MOD
        / "Capabilities"
        / "Actions"
        / "InventoryTransferRuntimePrimitives.cs"
    ).read_text()
    transport = (PACKAGE / "transport.py").read_text()
    server = (PACKAGE / "server.py").read_text()
    projection = (PACKAGE / "projection.py").read_text()
    menu_actions = (MOD / "Capabilities" / "Actions" / "MenuActionHandlers.cs").read_text()

    assert "new CraftItemHandler(refs)" in composition
    for source in (transport, server, projection, menu_actions):
        assert "craft_item" not in source
    for required in (
        "CanCancel => !_committing",
        "UiElementKind.CraftingRecipe",
        "UiRuntimeProjector.Capture(",
        "recipe.doesFarmerHaveIngredientsInInventory(",
        "recipe.consumeIngredients(",
        "recipe.createItem()",
        "InventoryViewResolver.CreatePlayerForMenu(",
        "InventoryTransferRuntimeItemFactory.Wrap(",
        "InventoryTransferRuntimeCommitter.Commit(",
        "PreserveCreatedOutput(state.Page, crafted)",
        "player.NotifyQuests(",
        "quest.OnRecipeCrafted(",
        "recipe.numberProducedPerCraft",
        "Game1.stats.checkForCraftingAchievements()",
    ):
        assert required in handler + adapter
    consume = adapter.index("recipe.consumeIngredients(")
    recovery_point = adapter.index("state.Page.heldItem = crafted", consume)
    quest = adapter.index("UpdateCraftingQuest(", recovery_point)
    inventory_plan = adapter.index("var insertion = PreparePlayerInsertion", quest)
    inventory_commit = adapter.index(
        "InventoryTransferRuntimeCommitter.Commit(", inventory_plan
    )
    assert consume < recovery_point < quest < inventory_plan < inventory_commit
    assert "state.Player.addItemToInventory(" not in adapter
    assert "checkForQuestComplete" not in adapter
    for required in (
        "InventoryTransferRuntimeItemFactory",
        "InventoryTransferRuntimeCommitter",
        "public static IInventoryTransferCommit Commit(",
        "journal.Rollback()",
    ):
        assert required in transfer
    for forbidden in (
        "receiveLeftClick",
        "receiveRightClick",
        "createItemDebris",
        "new CraftingRecipe(",
    ):
        assert forbidden not in handler + adapter


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
        "SetEquipmentSlotHandler",
        "MoveInventoryItemHandler",
        "CraftItemHandler",
        "PurchaseShopItemHandler",
        "OpenMenuHandler",
        "ActivateUiHandler",
        "CloseMenuHandler",
        "QueryRuntimeHandler",
        "QueryWorldHandler",
        "QueryInventoryHandler",
        "QueryPlayersHandler",
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


def test_game_advance_policy_is_the_only_pause_when_unfocused_owner() -> None:
    policy_path = MOD / "Game" / "Runtime" / "GameAdvancePolicy.cs"
    save_loader = (MOD / "Bootstrap" / "SaveAutoLoader.cs").read_text()
    entry = (MOD / "Bootstrap" / "ModEntry.cs").read_text()
    owners = [
        source_path.relative_to(MOD)
        for source_path in MOD.rglob("*.cs")
        if "pauseWhenOutOfFocus" in source_path.read_text()
    ]

    assert owners == [policy_path.relative_to(MOD)]
    assert "_gameAdvancePolicy.EnsureGameCanAdvance();" in save_loader
    assert "_gameAdvancePolicy.RestoreIfWorldNotReady();" in save_loader
    policy_construction = entry.index("new GameAdvancePolicy(helper)")
    loader_construction = entry.index("new SaveAutoLoader(")
    assert policy_construction < loader_construction
