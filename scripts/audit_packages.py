#!/usr/bin/env python3
"""检查发布包结构，并拒绝本机路径、调试垃圾和配置文件。"""

from __future__ import annotations

import argparse
from email.parser import BytesParser
import json
import tarfile
import zipfile
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[1]
PRIVATE_MARKERS = (
    b"/Users/",
    b"/home/",
    b"/private/tmp/",
    b"/tmp/",
    b"\\Users\\",
)


def audit_member(path: Path, name: str, content: bytes) -> None:
    member = PurePosixPath(name)
    if member.is_absolute() or ".." in member.parts:
        raise SystemExit(f"发布包包含非法路径: {path.name}: {name}")
    if "__pycache__" in member.parts or member.suffix in {".pyc", ".pdb"}:
        raise SystemExit(f"发布包包含调试或缓存文件: {path.name}: {name}")
    if any(marker in content for marker in PRIVATE_MARKERS):
        raise SystemExit(f"发布包泄露本机路径: {path.name}: {name}")


def audit_zip(path: Path) -> set[str]:
    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        for name in names:
            audit_member(path, name, archive.read(name))
        return names


def audit_tar(path: Path) -> set[str]:
    names: set[str] = set()
    with tarfile.open(path, "r:gz") as archive:
        for member in archive.getmembers():
            if member.isdir():
                continue
            if not member.isfile():
                raise SystemExit(f"发布包包含非普通文件: {path.name}: {member.name}")
            extracted = archive.extractfile(member)
            if extracted is None:
                raise SystemExit(f"无法读取发布包文件: {path.name}: {member.name}")
            names.add(member.name)
            audit_member(path, member.name, extracted.read())
    return names


def audit_mcp() -> None:
    wheels = sorted((ROOT / "mcp" / "dist").glob("*.whl"))
    if len(wheels) != 1:
        raise SystemExit(f"MCP dist 必须且只能包含一个 wheel，当前为 {len(wheels)}")
    names = audit_zip(wheels[0])
    required = {
        "stardew_valley_mcp/server.py",
        "stardew_valley_mcp/transport.py",
        "stardew_valley_mcp/generated/tool_catalog.json",
    }
    if not required <= names:
        raise SystemExit(f"MCP wheel 缺少运行文件: {sorted(required - names)}")
    if any(name.endswith("_tool.json") for name in names):
        raise SystemExit("MCP wheel 不得包含单 Tool JSON")
    license_names = {name for name in names if name.endswith(".dist-info/licenses/LICENSE")}
    if len(license_names) != 1:
        raise SystemExit(f"MCP wheel 必须携带一份 Apache-2.0 LICENSE，当前为 {sorted(license_names)}")
    metadata_names = [name for name in names if name.endswith(".dist-info/METADATA")]
    if len(metadata_names) != 1:
        raise SystemExit(f"MCP wheel 必须且只能包含一份 METADATA，当前为 {metadata_names}")
    with zipfile.ZipFile(wheels[0]) as archive:
        metadata = BytesParser().parsebytes(archive.read(metadata_names[0]))
    if metadata.get("License-Expression") != "Apache-2.0":
        raise SystemExit("MCP wheel License-Expression 必须为 Apache-2.0")
    if "LICENSE" not in metadata.get_all("License-File", []):
        raise SystemExit("MCP wheel METADATA 缺少 License-File: LICENSE")

    source_archives = sorted((ROOT / "mcp" / "dist").glob("*.tar.gz"))
    if len(source_archives) != 1:
        raise SystemExit(f"MCP dist 必须且只能包含一个源码包，当前为 {len(source_archives)}")
    source_names = audit_tar(source_archives[0])
    for required_suffix in required:
        if not any(name.endswith(f"/src/{required_suffix}") for name in source_names):
            raise SystemExit(f"MCP 源码包缺少运行文件: {required_suffix}")
    if not any(name.endswith("/LICENSE") for name in source_names):
        raise SystemExit("MCP 源码包缺少 Apache-2.0 LICENSE")


def audit_mod() -> None:
    zips = sorted(
        (ROOT / "mod" / "src" / "StardewValleyMcp.Mod" / "bin" / "Release" / "net6.0").glob(
            "StardewValleyMCP *.zip"
        )
    )
    if len(zips) != 1:
        raise SystemExit(f"Mod 输出必须且只能包含一个 ZIP，当前为 {len(zips)}")
    names = audit_zip(zips[0])
    expected = {
        "StardewValleyMCP/LICENSE",
        "StardewValleyMCP/THIRD_PARTY_NOTICES.md",
        "StardewValleyMCP/manifest.json",
        "StardewValleyMCP/StardewValleyMcp.Mod.dll",
        "StardewValleyMCP/StardewValleyMcp.Protocol.dll",
        "StardewValleyMCP/Google.Protobuf.dll",
        "StardewValleyMCP/licenses/Google.Protobuf-BSD-3-Clause.txt",
    }
    if names != expected:
        raise SystemExit(f"Mod ZIP 内容不符合发布清单: {sorted(names ^ expected)}")
    with zipfile.ZipFile(zips[0]) as archive:
        manifest = json.loads(archive.read("StardewValleyMCP/manifest.json"))
    if manifest.get("UniqueID") != "StardewValleyMCP.Mod":
        raise SystemExit("Mod manifest UniqueID 不匹配")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--with-mod", action="store_true", help="同时检查本机构建的 Mod ZIP")
    args = parser.parse_args()
    if (ROOT / "LICENSE").read_bytes() != (ROOT / "mcp" / "LICENSE").read_bytes():
        raise SystemExit("根 LICENSE 与 MCP 包 LICENSE 不一致")
    for relative in ["THIRD_PARTY_NOTICES.md", "licenses/Google.Protobuf-BSD-3-Clause.txt"]:
        if not (ROOT / relative).is_file():
            raise SystemExit(f"源码发行缺少法律文件: {relative}")
    audit_mcp()
    if args.with_mod:
        audit_mod()
    print(f"package_audit_ok with_mod={args.with_mod}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
