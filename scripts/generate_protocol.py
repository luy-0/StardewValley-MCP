#!/usr/bin/env python3
"""从公共 Proto 单一来源生成 C# 与 Python 协议代码。"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROTO_ROOT = ROOT / "spec" / "proto"
CSHARP_TARGET = ROOT / "mod" / "src" / "StardewValleyMcp.Protocol" / "Generated"
PYTHON_TARGET = ROOT / "mcp" / "src" / "stardew_valley_mcp" / "protocol"
SCHEMA_GENERATOR = ROOT / "spec" / "conformance" / "generate_mcp_tool_schemas.py"
TOOL_SCHEMAS = ROOT / "spec" / "mcp" / "tool-schemas.json"
QUERY_RUNTIME_TOOL = ROOT / "mcp" / "src" / "stardew_valley_mcp" / "query_runtime_tool.json"
EXPECTED_PROTOC = "libprotoc 34.1"
GENERATED_PATTERNS = ("*.cs", "*_pb2.py", "*_pb2.pyi")


def run(command: list[str], cwd: Path | None = None) -> None:
    subprocess.run(command, cwd=cwd, check=True)


def check_protoc() -> str:
    completed = subprocess.run(
        ["protoc", "--version"],
        check=True,
        capture_output=True,
        text=True,
    )
    version = completed.stdout.strip()
    if version != EXPECTED_PROTOC:
        raise SystemExit(f"需要 {EXPECTED_PROTOC}，当前为 {version or 'unknown'}")
    return version


def patch_python_imports(path: Path) -> None:
    text = path.read_text(encoding="utf-8")
    text = re.sub(
        r"(?m)^import ([a-z0-9_]+_pb2) as ([a-zA-Z0-9_]+)$",
        r"from . import \1 as \2",
        text,
    )
    text = re.sub(
        r"(?m)^import ([a-z0-9_]+_pb2)$",
        r"from . import \1",
        text,
    )
    path.write_text(text, encoding="utf-8")


def generated_files(root: Path) -> dict[str, bytes]:
    files: dict[str, bytes] = {}
    for pattern in GENERATED_PATTERNS:
        for path in root.glob(pattern):
            files[path.name] = path.read_bytes()
    return files


def sync_generated(source: Path, target: Path, check: bool) -> None:
    expected = generated_files(source)
    actual = generated_files(target) if target.exists() else {}
    if check:
        if actual != expected:
            missing = sorted(expected.keys() - actual.keys())
            stale = sorted(actual.keys() - expected.keys())
            changed = sorted(name for name in expected.keys() & actual.keys() if expected[name] != actual[name])
            details = f"missing={missing} stale={stale} changed={changed}"
            raise SystemExit(f"生成文件不是最新状态: {target}: {details}")
        return

    target.mkdir(parents=True, exist_ok=True)
    for pattern in GENERATED_PATTERNS:
        for path in target.glob(pattern):
            path.unlink()
    for name, content in sorted(expected.items()):
        (target / name).write_bytes(content)


def sync_query_runtime_tool(check: bool) -> None:
    document = json.loads(TOOL_SCHEMAS.read_text(encoding="utf-8"))
    matches = [tool for tool in document["tools"] if tool["name"] == "stardew_query_runtime"]
    if len(matches) != 1:
        raise SystemExit("tool-schemas.json 必须且只能包含一个 stardew_query_runtime")
    content = (json.dumps(matches[0], ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode()
    if check:
        actual = QUERY_RUNTIME_TOOL.read_bytes() if QUERY_RUNTIME_TOOL.exists() else b""
        if actual != content:
            raise SystemExit("MCP query_runtime Tool 生成物不是最新状态")
    else:
        QUERY_RUNTIME_TOOL.write_bytes(content)


def generate(check: bool) -> None:
    version = check_protoc()
    proto_files = sorted(PROTO_ROOT.glob("*.proto"))
    if not proto_files:
        raise SystemExit("spec/proto 中没有 Proto 文件")

    with tempfile.TemporaryDirectory(prefix="stardew-mcp-proto-") as raw_tmp:
        tmp = Path(raw_tmp)
        csharp_out = tmp / "csharp"
        python_out = tmp / "python"
        csharp_out.mkdir()
        python_out.mkdir()
        names = [path.name for path in proto_files]
        run(
            [
                "protoc",
                f"--proto_path={PROTO_ROOT}",
                f"--csharp_out={csharp_out}",
                f"--python_out={python_out}",
                f"--pyi_out={python_out}",
                *names,
            ],
            cwd=PROTO_ROOT,
        )
        for path in [*python_out.glob("*_pb2.py"), *python_out.glob("*_pb2.pyi")]:
            patch_python_imports(path)
        sync_generated(csharp_out, CSHARP_TARGET, check)
        sync_generated(python_out, PYTHON_TARGET, check)

    schema_command = [sys.executable, str(SCHEMA_GENERATOR)]
    if check:
        schema_command.append("--check")
    run(schema_command, cwd=ROOT)
    sync_query_runtime_tool(check)
    action = "checked" if check else "generated"
    print(f"protocol_{action} protoc={version} files={len(proto_files)}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="只检查已提交生成物，不覆盖文件")
    args = parser.parse_args()
    generate(args.check)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
