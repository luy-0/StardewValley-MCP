"""只暴露已实现的 stardew_query_runtime。"""

from __future__ import annotations

import asyncio
import json
import sys
from importlib.resources import files
from typing import Any

from mcp import types
from mcp.server.lowlevel import NotificationOptions, Server
from mcp.server.stdio import stdio_server

from . import __version__
from .transport import ConfigurationError, ConnectionConfig, QueryRuntimeClient


def load_tool() -> types.Tool:
    source = json.loads(files("stardew_valley_mcp").joinpath("query_runtime_tool.json").read_text())
    return types.Tool(
        name=source["name"],
        title=source["title"],
        description=source["description"],
        inputSchema=source["inputSchema"],
        outputSchema=source["outputSchema"],
        annotations=types.ToolAnnotations(**source["annotations"]),
    )


def create_server(client: QueryRuntimeClient) -> Server:
    server = Server("stardew-valley-mcp", version=__version__)
    tool = load_tool()

    @server.list_tools()
    async def list_tools() -> list[types.Tool]:
        return [tool] if await client.available() else []

    @server.call_tool()
    async def call_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any] | types.CallToolResult:
        if name != tool.name or arguments:
            return types.CallToolResult(
                content=[types.TextContent(type="text", text="参数不符合 stardew_query_runtime 契约")],
                isError=True,
            )
        result = await client.query_runtime()
        if result["status"] == "succeeded":
            return result
        return types.CallToolResult(
            content=[types.TextContent(type="text", text=result["error"]["message"])],
            structuredContent=result,
            isError=True,
        )

    return server


async def run_stdio(config: ConnectionConfig) -> None:
    server = create_server(QueryRuntimeClient(config))
    async with stdio_server() as (read_stream, write_stream):
        await server.run(
            read_stream,
            write_stream,
            server.create_initialization_options(NotificationOptions(), {}),
        )


def main() -> int:
    try:
        config = ConnectionConfig.from_env()
    except ConfigurationError as error:
        print(str(error), file=sys.stderr)
        return 2
    asyncio.run(run_stdio(config))
    return 0
