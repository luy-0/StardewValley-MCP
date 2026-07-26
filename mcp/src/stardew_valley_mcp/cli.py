from __future__ import annotations

import argparse
from collections.abc import Sequence

from . import __version__
from .protocol import transport_pb2


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="stardew-valley-mcp", description="Stardew Valley MCP 服务端")
    parser.add_argument("--version", action="version", version=__version__)
    subcommands = parser.add_subparsers(dest="command")
    subcommands.add_parser("doctor", help="检查本地 Python 包和协议生成物")
    subcommands.add_parser("serve", help="以 stdio 启动 MCP 服务端")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command == "doctor":
        descriptor = transport_pb2.TransportFrame.DESCRIPTOR
        print(f"doctor_ok package={__version__} protocol={descriptor.file.package}")
        return 0
    if args.command == "serve":
        from .server import main as run_server

        return run_server()
    parser.print_help()
    return 0
