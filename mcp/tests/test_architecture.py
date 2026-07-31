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


def test_default_capability_set_is_the_unique_concrete_handler_composition_root() -> None:
    composition_path = MOD / "Bootstrap" / "DefaultCapabilitySet.cs"
    registry = (MOD / "Runtime" / "CapabilityRegistry.cs").read_text()
    handlers = {
        "SayHandler",
        "EmoteHandler",
        "FaceHandler",
        "EquipHandler",
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
