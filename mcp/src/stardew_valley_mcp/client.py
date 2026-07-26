"""观察能力的统一运行调用器。"""

from __future__ import annotations

from typing import Any

from google.protobuf.json_format import ParseError
from jsonschema import Draft202012Validator, ValidationError

from .catalog import Catalog
from .command_runtime import CommandRuntime
from .projection import parse_message
from .protocol import queries_pb2
from .transport import ConnectionConfig, TransportConnection


class StardewClient:
    def __init__(self, config: ConnectionConfig, catalog: Catalog | None = None):
        self._catalog = catalog or Catalog.load()
        self._runtime = CommandRuntime(TransportConnection(config), self._catalog)

    async def available_tools(self) -> list[Any]:
        return await self._runtime.available_tools()

    async def query_runtime(self) -> dict[str, object]:
        return await self.call_tool("stardew_query_runtime", {})

    async def call_tool(self, tool_name: str, arguments: dict[str, Any]) -> dict[str, object]:
        command_id = self._runtime.new_command_id()
        try:
            capability_id = self._catalog.capability_for_tool(tool_name)
        except ValueError:
            return {"status": "failed", "commandId": command_id, "error": {"code": "invalid_arguments", "message": "未知 MCP Tool", "retryable": False}}
        if not self._catalog.allows(capability_id):
            return {"status": "failed", "commandId": command_id, "error": {"code": "capability_denied", "message": "当前策略未授权该能力", "retryable": False}}
        try:
            Draft202012Validator(self._catalog.tool(capability_id).inputSchema).validate(arguments)
            operation = _operation_for(capability_id, arguments)
        except (ValueError, ValidationError, ParseError):
            return {"status": "failed", "commandId": command_id, "error": {"code": "invalid_arguments", "message": "参数不符合公开 Tool Schema", "retryable": False}}
        return await self._runtime.execute(command_id, capability_id, operation)


def _operation_for(capability_id: str, arguments: dict[str, Any]):
    """公开观察能力的显式 request-class 注册表。"""
    request_classes = {
        "query_runtime": queries_pb2.QueryRuntimeRequest,
        "query_world": queries_pb2.QueryWorldRequest,
        "query_inventory": queries_pb2.QueryInventoryRequest,
        "query_ui": queries_pb2.QueryUiRequest,
        "inspect": queries_pb2.InspectRequest,
    }
    message_class = request_classes.get(capability_id)
    if message_class is None:
        raise ValueError("该能力尚无运行调用实现")
    return parse_message(arguments, message_class)
