"""公开仓库随附可执行 Skill 的显式组合入口。"""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

from .skill_host import ExecutableSkill, SkillHost


def create_builtin_skill_host(client) -> SkillHost:
    sleep_script = Path(__file__).with_name("builtin_skill_scripts") / "sleep_until_next_day.py"
    if not sleep_script.is_file():
        sleep_script = _repository_root() / "skill" / "examples" / "stardew-sleep-until-next-day" / "scripts" / "run.py"
    run = _load_run(sleep_script, "stardew_sleep_until_next_day")
    sleep = ExecutableSkill(
        name="stardew_skill_sleep_until_next_day",
        description="连续完成回家、上床、确认睡眠、换日与安全日结 UI 收敛",
        input_schema={
            "type": "object",
            "additionalProperties": False,
            "properties": {
                "timeoutSeconds": {"type": "integer", "minimum": 60, "maximum": 300, "default": 180},
            },
        },
        allowed_tools=frozenset(
            {
                "stardew_query_runtime",
                "stardew_query_world",
                "stardew_navigate",
                "stardew_interact",
                "stardew_query_ui",
                "stardew_activate_ui",
                "stardew_close_menu",
            }
        ),
        timeout_seconds=305,
        run=run,
    )
    return SkillHost(client, [sleep])


def _repository_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _load_run(path: Path, module_name: str):
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"无法加载可执行 Skill: {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    try:
        spec.loader.exec_module(module)
    except Exception:
        sys.modules.pop(module_name, None)
        raise
    return module.run
