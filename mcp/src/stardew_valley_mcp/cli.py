from __future__ import annotations

import argparse
from collections.abc import Sequence
from pathlib import Path

from . import __version__
from .protocol import transport_pb2


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="stardew-valley-mcp", description="Stardew Valley MCP 服务端")
    parser.add_argument("--version", action="version", version=__version__)
    subcommands = parser.add_subparsers(dest="command")
    subcommands.add_parser("doctor", help="检查本地 Python 包和协议生成物")
    serve = subcommands.add_parser("serve", help="以 stdio 启动 MCP 服务端")
    serve.add_argument(
        "--allow-write",
        action="store_true",
        help="允许暴露需要 game:write 权限的操作能力",
    )
    serve.add_argument(
        "--skill-dir",
        action="append",
        default=[],
        type=Path,
        help="加载一个可信可执行 Skill 包或包含多个包的目录；可以重复指定",
    )
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

        return run_server(allow_write=args.allow_write, skill_roots=args.skill_dir)
    parser.print_help()
    return 0
