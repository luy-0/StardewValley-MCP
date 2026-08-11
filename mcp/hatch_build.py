"""把公共契约与全部可执行 Skill 包作为 MCP 运行资源打包。"""

from __future__ import annotations

from pathlib import Path

from hatchling.builders.hooks.plugin.interface import BuildHookInterface


class CustomBuildHook(BuildHookInterface):
    def initialize(self, version: str, build_data: dict) -> None:
        del version
        project_root = Path(self.root)
        package_root = project_root / "src" / "stardew_valley_mcp"
        source_root = project_root.parent / "skill" / "examples"
        if not source_root.is_dir():
            source_root = package_root / "builtin_skill_packages"

        skill_dirs = sorted(
            path.parent for path in source_root.glob("*/runtime.yaml") if path.is_file()
        )
        if not skill_dirs:
            raise RuntimeError("缺少可执行 Skill 包")

        contract = project_root.parent / "spec" / "skill" / "runtime-manifest.schema.json"
        if not contract.is_file():
            contract = package_root / "skill_contract" / "runtime-manifest.schema.json"
        if not contract.is_file():
            raise RuntimeError("缺少可执行 Skill Manifest Schema")

        prefix = "src/" if self.target_name == "sdist" else ""
        force_include = build_data["force_include"]
        for skill_dir in skill_dirs:
            for source in sorted(path for path in skill_dir.rglob("*") if path.is_file()):
                if "__pycache__" in source.parts or source.suffix == ".pyc":
                    continue
                relative = source.relative_to(skill_dir)
                destination = (
                    f"{prefix}stardew_valley_mcp/builtin_skill_packages/"
                    f"{skill_dir.name}/{relative.as_posix()}"
                )
                force_include[str(source)] = destination
        force_include[str(contract)] = (
            f"{prefix}stardew_valley_mcp/skill_contract/runtime-manifest.schema.json"
        )
