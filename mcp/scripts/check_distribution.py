#!/usr/bin/env python3
"""确认 MCP 发行包携带完整的可执行 Skill 包与公共 Manifest Schema。"""

from __future__ import annotations

import tarfile
import zipfile
from pathlib import Path


MCP_ROOT = Path(__file__).resolve().parents[1]
DIST = MCP_ROOT / "dist"


def expected_runtime_files() -> tuple[list[str], int]:
    source_root = MCP_ROOT.parent / "skill" / "examples"
    skill_dirs = sorted(path.parent for path in source_root.glob("*/runtime.yaml"))
    if not skill_dirs:
        raise SystemExit("源码目录中缺少可执行 Skill 包")

    files = ["skill_contract/runtime-manifest.schema.json"]
    for skill_dir in skill_dirs:
        files.extend(
            f"builtin_skill_packages/{skill_dir.name}/{source.relative_to(skill_dir).as_posix()}"
            for source in sorted(path for path in skill_dir.rglob("*") if path.is_file())
            if "__pycache__" not in source.parts and source.suffix != ".pyc"
        )
    return files, len(skill_dirs)


def main() -> int:
    expected_files, skill_count = expected_runtime_files()
    wheels = sorted(DIST.glob("*.whl"), key=lambda path: path.stat().st_mtime)
    sdists = sorted(DIST.glob("*.tar.gz"), key=lambda path: path.stat().st_mtime)
    if not wheels or not sdists:
        raise SystemExit("缺少 MCP wheel 或 sdist")

    with zipfile.ZipFile(wheels[-1]) as archive:
        names = set(archive.namelist())
        missing = [name for name in expected_files if f"stardew_valley_mcp/{name}" not in names]
        if missing:
            raise SystemExit(f"wheel 缺少可执行 Skill 资源: {', '.join(missing)}")
    with tarfile.open(sdists[-1]) as archive:
        names = archive.getnames()
        missing = [
            name
            for name in expected_files
            if not any(candidate.endswith(f"/src/stardew_valley_mcp/{name}") for candidate in names)
        ]
        if missing:
            raise SystemExit(f"sdist 缺少可执行 Skill 资源: {', '.join(missing)}")

    print(f"mcp_distribution_ok executable_skills={skill_count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
