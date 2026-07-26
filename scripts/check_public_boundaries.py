#!/usr/bin/env python3
"""拒绝公开仓库引入历史实现、私有平台依赖或本机路径。"""

from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = (ROOT / "mod" / "src", ROOT / "mcp" / "src")
SOURCE_SUFFIXES = {".cs", ".json", ".proto", ".py", ".toml", ".yaml", ".yml"}
PUBLIC_TEXT_SUFFIXES = SOURCE_SUFFIXES | {".md", ".sh", ".txt", ".props", ".targets", ".sln"}
IGNORED_PARTS = {
    ".git",
    ".venv",
    ".pytest_cache",
    "__pycache__",
    "agent_workspace",
    "bin",
    "build",
    "dist",
    "obj",
}
FORBIDDEN = {
    "AdapterV2": re.compile(r"\bAdapterV2\b", re.IGNORECASE),
    "CommandProcessor": re.compile(r"\bCommandProcessor\b", re.IGNORECASE),
    "CompoundDispatcher": re.compile(r"\bCompoundDispatcher\b", re.IGNORECASE),
    "FallbackToLegacyMapper": re.compile(r"\bFallbackToLegacyMapper\b", re.IGNORECASE),
    "V2 protocol symbol": re.compile(r"\bV2(?:Command|Result)\b|v2-json", re.IGNORECASE),
    "Legacy namespace": re.compile(r"\bLegacy(?:\.|::)|namespace\s+[^\n]*Legacy\b", re.IGNORECASE),
    "File Bridge": re.compile(r"\bFileBridge\b|file[ _-]?bridge", re.IGNORECASE),
    "runtime Rendezvous": re.compile(r"\brendezvous(?:\.json)?\b", re.IGNORECASE),
    "private Python package": re.compile(r"\b(?:agent\.protocol|runtime_manager)\b"),
    "Hosted Credential": re.compile(r"\bHostedCredential\b|hosted[ _-]?credential", re.IGNORECASE),
    "old repository import": re.compile(r"\bstar[-_.]?coplay(?:[-_.]?hosted[-_.]?agent[-_.]?runtime)?\b", re.IGNORECASE),
}
PRIVATE_PATHS = {
    "macOS 用户目录": re.compile("/" + r"Users/[^/\s\"']+/"),
    "Linux 用户目录": re.compile("/" + r"home/[^/\s\"']+/"),
    "Windows 用户目录": re.compile(
        r"[A-Za-z]:[\\\\/]" + r"Users[\\\\/][^\\\\/\s\"']+[\\\\/]"
    ),
}


def source_files() -> list[Path]:
    return sorted(
        path
        for root in SOURCE_ROOTS
        for path in root.rglob("*")
        if path.is_file()
        and path.suffix in SOURCE_SUFFIXES
        and not any(part in IGNORED_PARTS for part in path.relative_to(ROOT).parts)
    )


def public_text_files() -> list[Path]:
    return sorted(
        path
        for path in ROOT.rglob("*")
        if path.is_file()
        and not any(part in IGNORED_PARTS for part in path.relative_to(ROOT).parts)
        and (path.suffix in PUBLIC_TEXT_SUFFIXES or path.name in {".gitignore", "Dockerfile"})
    )


def main() -> int:
    violations: list[str] = []
    files = source_files()
    for path in files:
        text = path.read_text(encoding="utf-8")
        for line_number, line in enumerate(text.splitlines(), start=1):
            for label, pattern in FORBIDDEN.items():
                if pattern.search(line):
                    relative = path.relative_to(ROOT)
                    violations.append(f"{relative}:{line_number}: {label}: {line.strip()}")

    path_files = public_text_files()
    for path in path_files:
        text = path.read_text(encoding="utf-8")
        for line_number, line in enumerate(text.splitlines(), start=1):
            for label, pattern in PRIVATE_PATHS.items():
                if pattern.search(line):
                    relative = path.relative_to(ROOT)
                    violations.append(f"{relative}:{line_number}: {label}: {line.strip()}")

    if violations:
        print("public_boundary_failed")
        print("\n".join(violations))
        return 1

    print(
        f"public_boundary_ok runtime_files={len(files)} "
        f"public_text_files={len(path_files)} rules={len(FORBIDDEN) + len(PRIVATE_PATHS)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
