"""把 Skill 目录中的规范脚本作为 MCP wheel 的运行资源打包。"""

from __future__ import annotations

from pathlib import Path

from hatchling.builders.hooks.plugin.interface import BuildHookInterface


class CustomBuildHook(BuildHookInterface):
    def initialize(self, version: str, build_data: dict) -> None:
        del version
        project_root = Path(self.root)
        source = (
            project_root.parent
            / "skill"
            / "examples"
            / "stardew-sleep-until-next-day"
            / "scripts"
            / "run.py"
        )
        if not source.is_file():
            source = (
                project_root
                / "src"
                / "stardew_valley_mcp"
                / "builtin_skill_scripts"
                / "sleep_until_next_day.py"
            )
        if not source.is_file():
            raise RuntimeError("缺少 stardew-sleep-until-next-day 可执行脚本")

        destination = "stardew_valley_mcp/builtin_skill_scripts/sleep_until_next_day.py"
        if self.target_name == "sdist":
            destination = f"src/{destination}"
        build_data["force_include"][str(source)] = destination
