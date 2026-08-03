#!/usr/bin/env python3
"""静态校验仓库中的 Stardew Valley MCP Agent Skill。"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CATALOG = ROOT / "mcp" / "src" / "stardew_valley_mcp" / "generated" / "tool_catalog.json"
DEFAULT_SKILL_ROOTS = (ROOT / "skill" / "templates", ROOT / "skill" / "examples")
NAME_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
TOOL_RE = re.compile(r"`(stardew_[a-z0-9_]+)`")
REQUIRED_SECTIONS = ("可用工具", "工作流程", "停止条件", "输出要求", "安全边界")
ALLOWED_FRONTMATTER = {"name", "description", "license", "allowed-tools", "metadata"}


@dataclass(frozen=True)
class ValidationIssue:
    path: Path
    message: str


def _frontmatter(content: str) -> tuple[dict[str, object], str]:
    if not content.startswith("---\n"):
        raise ValueError("SKILL.md 缺少 YAML Frontmatter")
    end = content.find("\n---\n", 4)
    if end < 0:
        raise ValueError("SKILL.md Frontmatter 未正确结束")

    try:
        values = yaml.safe_load(content[4:end])
    except yaml.YAMLError as error:
        raise ValueError(f"SKILL.md Frontmatter 不是有效 YAML: {error}") from error
    if not isinstance(values, dict):
        raise ValueError("SKILL.md Frontmatter 必须是对象")
    return values, content[end + 5 :]


def _section(body: str, title: str) -> str | None:
    match = re.search(
        rf"^## {re.escape(title)}\s*$\n(?P<body>.*?)(?=^## |\Z)",
        body,
        re.MULTILINE | re.DOTALL,
    )
    return match.group("body").strip() if match else None


def load_catalog(path: Path = DEFAULT_CATALOG) -> set[str]:
    document = json.loads(path.read_text(encoding="utf-8"))
    return {tool["name"] for tool in document["tools"]}


def validate_skill(
    skill_dir: Path,
    catalog_tools: set[str],
    *,
    require_tools: bool = True,
) -> list[ValidationIssue]:
    issues: list[ValidationIssue] = []
    skill_file = skill_dir / "SKILL.md"
    if not skill_file.is_file():
        return [ValidationIssue(skill_dir, "缺少 SKILL.md")]

    try:
        content = skill_file.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        return [ValidationIssue(skill_file, "SKILL.md 必须使用 UTF-8")]

    try:
        metadata, body = _frontmatter(content)
    except ValueError as error:
        return [ValidationIssue(skill_file, str(error))]

    unexpected = sorted(set(metadata) - ALLOWED_FRONTMATTER)
    if unexpected:
        issues.append(ValidationIssue(skill_file, f"Frontmatter 包含未知字段: {', '.join(unexpected)}"))
    name = metadata.get("name", "")
    description = metadata.get("description", "")
    if not isinstance(name, str) or not NAME_RE.fullmatch(name):
        issues.append(ValidationIssue(skill_file, "name 必须使用小写字母、数字和单连字符"))
    elif len(name) > 64:
        issues.append(ValidationIssue(skill_file, "name 不得超过 64 个字符"))
    if name != skill_dir.name:
        issues.append(ValidationIssue(skill_file, f"name 必须与目录名一致: {skill_dir.name}"))
    if not isinstance(description, str) or not description.strip():
        issues.append(ValidationIssue(skill_file, "description 必须是非空字符串"))
    elif len(description) > 1024 or "<" in description or ">" in description:
        issues.append(ValidationIssue(skill_file, "description 不得超过 1024 个字符或包含尖括号"))
    if "TODO" in content:
        issues.append(ValidationIssue(skill_file, "Skill 仍包含 TODO"))
    if len(content.splitlines()) > 500:
        issues.append(ValidationIssue(skill_file, "SKILL.md 不应超过 500 行"))

    sections: dict[str, str] = {}
    for title in REQUIRED_SECTIONS:
        section = _section(body, title)
        if section is None:
            issues.append(ValidationIssue(skill_file, f"缺少必需章节: ## {title}"))
        elif not section:
            issues.append(ValidationIssue(skill_file, f"章节不能为空: ## {title}"))
        else:
            sections[title] = section

    declared_tools = set(TOOL_RE.findall(sections.get("可用工具", "")))
    if require_tools and not declared_tools:
        issues.append(ValidationIssue(skill_file, "## 可用工具 至少声明一个 stardew_* Tool"))
    unknown = sorted(declared_tools - catalog_tools)
    if unknown:
        issues.append(ValidationIssue(skill_file, f"引用了不存在的 MCP Tool: {', '.join(unknown)}"))

    used_tools = set(TOOL_RE.findall(body))
    undeclared = sorted(used_tools - declared_tools)
    if undeclared:
        issues.append(ValidationIssue(skill_file, f"正文使用了未在 ## 可用工具 声明的 Tool: {', '.join(undeclared)}"))
    return issues


def discover_skill_dirs(paths: list[Path]) -> list[Path]:
    discovered: set[Path] = set()
    for path in paths:
        if (path / "SKILL.md").is_file():
            discovered.add(path.resolve())
            continue
        if path.is_dir():
            discovered.update(candidate.parent.resolve() for candidate in path.rglob("SKILL.md"))
    return sorted(discovered)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", type=Path, help="Skill 目录或包含多个 Skill 的目录")
    parser.add_argument("--catalog", type=Path, default=DEFAULT_CATALOG, help="生成的 MCP Tool Catalog")
    args = parser.parse_args(argv)

    roots = args.paths or list(DEFAULT_SKILL_ROOTS)
    skill_dirs = discover_skill_dirs(roots)
    if not skill_dirs:
        print("未发现 SKILL.md", file=sys.stderr)
        return 2

    catalog_tools = load_catalog(args.catalog)
    issues: list[ValidationIssue] = []
    for skill_dir in skill_dirs:
        issues.extend(validate_skill(skill_dir, catalog_tools))

    if issues:
        for issue in issues:
            try:
                path = issue.path.relative_to(ROOT)
            except ValueError:
                path = issue.path
            print(f"{path}: {issue.message}", file=sys.stderr)
        return 1

    print(f"skill_validation_ok count={len(skill_dirs)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
