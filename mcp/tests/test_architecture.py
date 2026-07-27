from __future__ import annotations

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


def test_query_inventory_uses_registry_without_transport_or_server_branch() -> None:
    registry = (MOD / "CapabilityRegistry.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    local_server = (MOD / "LocalServer.cs").read_text()

    assert "new QueryInventoryHandler(refs)" in registry
    assert "query_inventory" not in transport
    assert "QueryInventory" not in local_server


def test_chest_inventory_reader_never_creates_shared_backing() -> None:
    source = (MOD / "ChestInventoryReader.cs").read_text()
    assert ".GetItemsForPlayer(" not in source
    assert ".GetOrCreateGlobalInventory(" not in source


def test_query_ui_uses_registry_without_transport_server_or_projection_branch() -> None:
    registry = (MOD / "CapabilityRegistry.cs").read_text()
    transport = (PACKAGE / "transport.py").read_text()
    server = (PACKAGE / "server.py").read_text()
    projection = (PACKAGE / "projection.py").read_text()
    local_server = (MOD / "LocalServer.cs").read_text()

    assert "new QueryUiHandler(refs)" in registry
    assert "query_ui" not in transport
    assert "query_ui" not in server
    assert "query_ui" not in projection
    assert "QueryUi" not in local_server


def test_query_ui_runtime_has_no_generic_clickable_mutation_or_callback_invocation() -> None:
    source = (MOD / "UiRuntimeProjector.cs").read_text()
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
