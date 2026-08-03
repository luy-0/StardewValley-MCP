from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "skill" / "scripts"))

from validate_skills import load_catalog, validate_skill  # noqa: E402


VALID_BODY = """---
name: {name}
description: 用于测试的 Stardew Valley MCP Agent Skill。
---

# 测试 Skill

## 可用工具

- `stardew_query_runtime`

## 工作流程

1. 调用 `stardew_query_runtime`。

## 停止条件

查询失败时停止。

## 输出要求

返回查询事实。

## 安全边界

只读，不执行游戏变更。
"""


class SkillValidationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_catalog()

    def _write_skill(self, root: Path, name: str, content: str | None = None) -> Path:
        skill_dir = root / name
        skill_dir.mkdir()
        (skill_dir / "SKILL.md").write_text(content or VALID_BODY.format(name=name), encoding="utf-8")
        return skill_dir

    def test_repository_template_and_examples_are_valid(self) -> None:
        roots = (ROOT / "skill" / "templates", ROOT / "skill" / "examples")
        skill_dirs = [path.parent for root in roots for path in root.rglob("SKILL.md")]
        names = {path.name for path in skill_dirs}
        self.assertTrue(
            {"stardew-skill-template", "stardew-nearby-overview", "stardew-remove-tree"}.issubset(names)
        )
        self.assertEqual([], [issue for path in skill_dirs for issue in validate_skill(path, self.catalog)])

    def test_unknown_tool_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            content = VALID_BODY.format(name="bad-tool").replace("stardew_query_runtime", "stardew_missing")
            issues = validate_skill(self._write_skill(Path(directory), "bad-tool", content), self.catalog)
        self.assertTrue(any("不存在的 MCP Tool" in issue.message for issue in issues))

    def test_standard_multiline_description_is_accepted(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            content = VALID_BODY.format(name="multiline-description").replace(
                "description: 用于测试的 Stardew Valley MCP Agent Skill。",
                "description: >-\n  用于测试多行 YAML 的 Stardew Valley MCP Agent Skill。\n  用户要求验证标准 Frontmatter 时使用。",
            )
            issues = validate_skill(
                self._write_skill(Path(directory), "multiline-description", content), self.catalog
            )
        self.assertEqual([], issues)

    def test_missing_section_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            content = VALID_BODY.format(name="missing-output").replace("## 输出要求", "## 其他输出")
            issues = validate_skill(self._write_skill(Path(directory), "missing-output", content), self.catalog)
        self.assertTrue(any("## 输出要求" in issue.message for issue in issues))

    def test_directory_and_frontmatter_name_must_match(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            content = VALID_BODY.format(name="different-name")
            issues = validate_skill(self._write_skill(Path(directory), "actual-name", content), self.catalog)
        self.assertTrue(any("目录名一致" in issue.message for issue in issues))


if __name__ == "__main__":
    unittest.main()
