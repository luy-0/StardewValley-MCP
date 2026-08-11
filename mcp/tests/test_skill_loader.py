from __future__ import annotations

import shutil
import tempfile
from pathlib import Path

import pytest

from stardew_valley_mcp.cli import build_parser
from stardew_valley_mcp.skill_loader import (
    SkillLoadError,
    discover_skill_packages,
    load_executable_skills,
)


ROOT = Path(__file__).resolve().parents[2]
SLEEP_SKILL = ROOT / "skill" / "examples" / "stardew-sleep-until-next-day"


def test_repository_sleep_skill_is_loaded_entirely_from_runtime_manifest() -> None:
    skill = load_executable_skills([SLEEP_SKILL])[0]

    assert skill.name == "stardew_skill_sleep_until_next_day"
    assert skill.title == "回家睡觉并进入下一天"
    assert skill.allowed_tools == frozenset(
        {
            "stardew_query_runtime",
            "stardew_query_players",
            "stardew_query_world",
            "stardew_navigate",
            "stardew_interact",
            "stardew_query_ui",
            "stardew_activate_ui",
            "stardew_close_menu",
        }
    )
    assert skill.timeout_seconds == 305
    assert skill.concurrency == "exclusive"
    assert skill.annotations.readOnlyHint is False
    assert skill.annotations.destructiveHint is True
    assert skill.as_tool().outputSchema == skill.output_schema


def test_parent_directory_discovers_only_direct_executable_skill_packages() -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        shutil.copytree(SLEEP_SKILL, root / SLEEP_SKILL.name)
        prompt_only = root / "prompt-only"
        prompt_only.mkdir()
        (prompt_only / "SKILL.md").write_text("---\nname: prompt-only\ndescription: test\n---\n")

        packages = discover_skill_packages([root])

    assert [package.name for package in packages] == ["stardew-sleep-until-next-day"]


def test_manifest_cannot_reference_a_resource_outside_the_skill_package() -> None:
    with tempfile.TemporaryDirectory() as directory:
        skill = Path(directory) / SLEEP_SKILL.name
        shutil.copytree(SLEEP_SKILL, skill)
        manifest = (skill / "runtime.yaml").read_text(encoding="utf-8")
        (skill / "runtime.yaml").write_text(
            manifest.replace("scripts/run.py:run", "../outside.py:run"),
            encoding="utf-8",
        )

        with pytest.raises(SkillLoadError, match="runtime.yaml 不符合契约"):
            load_executable_skills([skill])


def test_tool_name_is_derived_from_skill_directory_name() -> None:
    with tempfile.TemporaryDirectory() as directory:
        skill = Path(directory) / SLEEP_SKILL.name
        shutil.copytree(SLEEP_SKILL, skill)
        manifest = (skill / "runtime.yaml").read_text(encoding="utf-8")
        (skill / "runtime.yaml").write_text(
            manifest.replace(
                "stardew_skill_sleep_until_next_day",
                "stardew_skill_unrelated_name",
            ),
            encoding="utf-8",
        )

        with pytest.raises(SkillLoadError, match="Tool 名必须为"):
            load_executable_skills([skill])


def test_entrypoint_must_be_a_declared_module_level_async_function() -> None:
    with tempfile.TemporaryDirectory() as directory:
        skill = Path(directory) / SLEEP_SKILL.name
        shutil.copytree(SLEEP_SKILL, skill)
        manifest = (skill / "runtime.yaml").read_text(encoding="utf-8")
        (skill / "runtime.yaml").write_text(
            manifest.replace("scripts/run.py:run", "scripts/run.py:missing"),
            encoding="utf-8",
        )

        with pytest.raises(SkillLoadError, match="模块级异步函数"):
            load_executable_skills([skill])


def test_cli_accepts_multiple_explicit_trusted_skill_directories() -> None:
    args = build_parser().parse_args(
        ["serve", "--skill-dir", "one", "--skill-dir", "two", "--allow-write"]
    )

    assert args.skill_dir == [Path("one"), Path("two")]
    assert args.allow_write is True
