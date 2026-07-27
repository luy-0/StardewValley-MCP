"""标准 MCP Session；Tool 清单来自公共 Catalog 与 Mod 公告的交集。"""

from __future__ import annotations

import asyncio
import sys
from typing import Any

from mcp import types
from mcp.server.lowlevel import NotificationOptions, Server
from mcp.server.stdio import stdio_server

from . import __version__
from .client import StardewClient
from .transport import ConfigurationError, ConnectionConfig


def create_server(client: Any) -> Server:
    server = Server("stardew-valley-mcp", version=__version__)

    @server.list_tools()
    async def list_tools() -> list[types.Tool]:
        return await client.available_tools()

    @server.call_tool()
    async def call_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any] | types.CallToolResult:
        result = await client.call_tool(name, arguments)
        if result["status"] == "succeeded":
            return result
        return types.CallToolResult(
            content=[types.TextContent(type="text", text=result["error"]["message"])],
            structuredContent=result,
            isError=True,
        )

    return server


async def run_stdio(config: ConnectionConfig) -> None:
    client = StardewClient(config)
    server = create_server(client)
    try:
        async with stdio_server() as (read_stream, write_stream):
            await server.run(
                read_stream,
                write_stream,
                server.create_initialization_options(NotificationOptions(), {}),
            )
    finally:
        await client.aclose()


def main() -> int:
    try:
        config = ConnectionConfig.from_env()
    except ConfigurationError as error:
        print(str(error), file=sys.stderr)
        return 2
    asyncio.run(run_stdio(config))
    return 0
