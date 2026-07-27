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
