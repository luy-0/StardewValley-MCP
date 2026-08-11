"""标准 MCP Session；Tool 清单来自公共 Catalog 与 Mod 公告的交集。"""

from __future__ import annotations

import asyncio
import sys
from collections.abc import Sequence
from pathlib import Path
from typing import Any

from mcp import types
from mcp.server.lowlevel import NotificationOptions, Server
from mcp.server.stdio import stdio_server

from . import __version__
from .catalog import Catalog, CatalogPolicy
from .client import StardewClient
from .skill_loader import SkillLoadError, load_skill_host
from .skill_host import SkillHost
from .transport import ConfigurationError, ConnectionConfig


def create_server(client: Any, *, skill_host: SkillHost | None = None) -> Server:
    server = Server("stardew-valley-mcp", version=__version__)

    @server.list_tools()
    async def list_tools() -> list[types.Tool]:
        atomic_tools = await client.available_tools()
        if skill_host is None:
            return atomic_tools
        return [*atomic_tools, *skill_host.available_tools(atomic_tools)]

    @server.call_tool()
    async def call_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any] | types.CallToolResult:
        if skill_host is not None and skill_host.handles(name):
            result = await skill_host.invoke(name, arguments)
        else:
            result = await client.call_tool(name, arguments)
        if result["status"] == "succeeded":
            return result
        return types.CallToolResult(
            content=[types.TextContent(type="text", text=result["error"]["message"])],
            structuredContent=result,
            isError=True,
        )

    return server


def catalog_for(*, allow_write: bool) -> Catalog:
    scopes = {"game:read"}
    if allow_write:
        scopes.add("game:write")
    return Catalog.load(CatalogPolicy(None, frozenset(scopes)))


async def run_stdio(
    config: ConnectionConfig,
    *,
    allow_write: bool = False,
    skill_roots: Sequence[Path] = (),
) -> None:
    client = StardewClient(config, catalog_for(allow_write=allow_write))
    try:
        server = create_server(client, skill_host=load_skill_host(client, skill_roots))
        async with stdio_server() as (read_stream, write_stream):
            await server.run(
                read_stream,
                write_stream,
                server.create_initialization_options(NotificationOptions(), {}),
            )
    finally:
        await client.aclose()


def main(*, allow_write: bool = False, skill_roots: Sequence[Path] = ()) -> int:
    try:
        config = ConnectionConfig.from_env()
    except ConfigurationError as error:
        print(str(error), file=sys.stderr)
        return 2
    try:
        asyncio.run(run_stdio(config, allow_write=allow_write, skill_roots=skill_roots))
    except SkillLoadError as error:
        print(str(error), file=sys.stderr)
        return 2
    return 0
