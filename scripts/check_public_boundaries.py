#!/usr/bin/env python3
"""拒绝公开仓库文本包含机器专属绝对路径。"""

from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_SUFFIXES = {".cs", ".json", ".proto", ".py", ".toml", ".yaml", ".yml"}
PUBLIC_TEXT_SUFFIXES = SOURCE_SUFFIXES | {".md", ".sh", ".txt", ".props", ".targets", ".sln"}
IGNORED_PARTS = {
    ".git",
    ".agent",
    ".venv",
    ".pytest_cache",
    "__pycache__",
    "agent_workspace",
    "bin",
    "build",
    "dist",
    "obj",
}
PRIVATE_PATHS = {
    "macOS 用户目录": re.compile("/" + r"Users/[^/\s\"']+/"),
    "Linux 用户目录": re.compile("/" + r"home/[^/\s\"']+/"),
    "Windows 用户目录": re.compile(
        r"[A-Za-z]:[\\\\/]" + r"Users[\\\\/][^\\\\/\s\"']+[\\\\/]"
    ),
}


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
        f"public_boundary_ok public_text_files={len(path_files)} "
        f"rules={len(PRIVATE_PATHS)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
