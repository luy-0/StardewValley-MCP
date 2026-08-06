#!/usr/bin/env python3
"""确认 MCP 发行包携带可执行 Skill 脚本。"""

from __future__ import annotations

import tarfile
import zipfile
from pathlib import Path


MCP_ROOT = Path(__file__).resolve().parents[1]
DIST = MCP_ROOT / "dist"
WHEEL_SKILL = "stardew_valley_mcp/builtin_skill_scripts/sleep_until_next_day.py"
SDIST_SKILL_SUFFIX = "/src/stardew_valley_mcp/builtin_skill_scripts/sleep_until_next_day.py"


def main() -> int:
    wheels = sorted(DIST.glob("*.whl"), key=lambda path: path.stat().st_mtime)
    sdists = sorted(DIST.glob("*.tar.gz"), key=lambda path: path.stat().st_mtime)
    if not wheels or not sdists:
        raise SystemExit("缺少 MCP wheel 或 sdist")

    with zipfile.ZipFile(wheels[-1]) as archive:
        if WHEEL_SKILL not in archive.namelist():
            raise SystemExit("wheel 缺少可执行睡眠 Skill")
    with tarfile.open(sdists[-1]) as archive:
        if not any(name.endswith(SDIST_SKILL_SUFFIX) for name in archive.getnames()):
            raise SystemExit("sdist 缺少可执行睡眠 Skill")

    print("mcp_distribution_ok executable_skills=1")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
