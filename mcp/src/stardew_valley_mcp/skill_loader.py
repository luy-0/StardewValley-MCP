"""从受信任目录发现并加载可执行 Skill 包。"""

from __future__ import annotations

import ast
import hashlib
import importlib.util
import inspect
import json
import sys
from collections.abc import Awaitable, Callable, Sequence
from pathlib import Path
from typing import Any

import yaml
from jsonschema import Draft202012Validator, ValidationError
from mcp import types

from .skill_host import ExecutableSkill, SkillContext, SkillHost


SkillHandler = Callable[[SkillContext, dict[str, Any]], Awaitable[dict[str, Any]]]
RUNTIME_MANIFEST = "runtime.yaml"


class SkillLoadError(ValueError):
    """可执行 Skill 包不符合公共加载契约。"""


def builtin_skill_root() -> Path:
    """返回发行包资源目录；源码工作区回退到公开示例目录。"""

    packaged = Path(__file__).with_name("builtin_skill_packages")
    if packaged.is_dir():
        return packaged
    repository = Path(__file__).resolve().parents[3] / "skill" / "examples"
    if repository.is_dir():
        return repository
    raise SkillLoadError("发行包缺少内置可执行 Skill")


def load_skill_host(
    client: Any,
    extra_roots: Sequence[Path] = (),
) -> SkillHost:
    roots = [builtin_skill_root(), *extra_roots]
    return SkillHost(client, load_executable_skills(roots))


def load_executable_skills(roots: Sequence[Path]) -> list[ExecutableSkill]:
    manifest_schema = _load_json(_contract_schema_path(), "可执行 Skill Manifest Schema")
    Draft202012Validator.check_schema(manifest_schema)
    validator = Draft202012Validator(manifest_schema)
    skills = [_load_skill(directory, validator) for directory in discover_skill_packages(roots)]
    names = [skill.name for skill in skills]
    if len(names) != len(set(names)):
        duplicates = sorted(name for name in set(names) if names.count(name) > 1)
        raise SkillLoadError(f"可执行 Skill Tool 名称重复: {', '.join(duplicates)}")
    return skills


def discover_skill_packages(roots: Sequence[Path]) -> list[Path]:
    """只扫描显式根目录本身或其直接子目录，避免意外扩大信任范围。"""

    packages: set[Path] = set()
    for root in roots:
        resolved = root.expanduser().resolve()
        if not resolved.is_dir():
            raise SkillLoadError(f"Skill 搜索目录不存在: {root}")
        if (resolved / RUNTIME_MANIFEST).is_file():
            packages.add(resolved)
            continue
        packages.update(
            child.resolve()
            for child in resolved.iterdir()
            if child.is_dir() and (child / RUNTIME_MANIFEST).is_file()
        )
    return sorted(packages, key=lambda path: (path.name, str(path)))


def _load_skill(directory: Path, validator: Draft202012Validator) -> ExecutableSkill:
    manifest_path = directory / RUNTIME_MANIFEST
    try:
        manifest = yaml.safe_load(manifest_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, yaml.YAMLError) as error:
        raise SkillLoadError(f"Skill '{directory.name}' 的 runtime.yaml 不可读") from error
    try:
        validator.validate(manifest)
    except ValidationError as error:
        location = ".".join(str(part) for part in error.absolute_path) or "root"
        raise SkillLoadError(
            f"Skill '{directory.name}' 的 runtime.yaml 不符合契约: {location}: {error.message}"
        ) from error

    skill_name = _skill_metadata_name(directory)
    tool = manifest["tool"]
    expected_tool_name = f"stardew_skill_{skill_name.replace('-', '_').removeprefix('stardew_')}"
    if tool["name"] != expected_tool_name:
        raise SkillLoadError(
            f"Skill '{directory.name}' 的 Tool 名必须为 {expected_tool_name}"
        )

    input_schema = _load_json(
        _resolve_member(directory, tool["inputSchema"]),
        f"Skill '{directory.name}' Input Schema",
    )
    output_schema = _load_json(
        _resolve_member(directory, tool["outputSchema"]),
        f"Skill '{directory.name}' Output Schema",
    )
    try:
        Draft202012Validator.check_schema(input_schema)
        Draft202012Validator.check_schema(output_schema)
    except Exception as error:
        raise SkillLoadError(f"Skill '{directory.name}' 的 JSON Schema 无效") from error
    _validate_schema_refs(input_schema, f"Skill '{directory.name}' Input Schema")
    _validate_schema_refs(output_schema, f"Skill '{directory.name}' Output Schema")

    entrypoint_path, separator, function_name = manifest["entrypoint"].partition(":")
    if not separator:
        raise SkillLoadError(f"Skill '{directory.name}' 缺少入口函数")
    script = _resolve_member(directory, entrypoint_path)
    try:
        syntax_tree = ast.parse(script.read_text(encoding="utf-8"), filename=script.name)
    except (OSError, UnicodeDecodeError, SyntaxError) as error:
        raise SkillLoadError(f"Skill '{directory.name}' 的入口脚本不可解析") from error
    entrypoints = [
        node
        for node in syntax_tree.body
        if isinstance(node, ast.AsyncFunctionDef) and node.name == function_name
    ]
    if len(entrypoints) != 1:
        raise SkillLoadError(
            f"Skill '{directory.name}' 的入口必须是唯一的模块级异步函数: {function_name}"
        )

    annotations = types.ToolAnnotations(**tool["annotations"])
    execution = manifest["execution"]
    return ExecutableSkill(
        name=tool["name"],
        title=tool["title"],
        description=tool["description"],
        input_schema=input_schema,
        output_schema=output_schema,
        annotations=annotations,
        allowed_tools=frozenset(manifest["requires"]["tools"]),
        timeout_seconds=float(execution["timeoutSeconds"]),
        concurrency=execution["concurrency"],
        run=_lazy_entrypoint(script, function_name),
    )


def _skill_metadata_name(directory: Path) -> str:
    skill_file = directory / "SKILL.md"
    try:
        content = skill_file.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as error:
        raise SkillLoadError(f"Skill '{directory.name}' 缺少可读的 SKILL.md") from error
    if not content.startswith("---\n"):
        raise SkillLoadError(f"Skill '{directory.name}' 的 SKILL.md 缺少 Frontmatter")
    end = content.find("\n---\n", 4)
    if end < 0:
        raise SkillLoadError(f"Skill '{directory.name}' 的 SKILL.md Frontmatter 未结束")
    try:
        metadata = yaml.safe_load(content[4:end])
    except yaml.YAMLError as error:
        raise SkillLoadError(f"Skill '{directory.name}' 的 SKILL.md Frontmatter 无效") from error
    if not isinstance(metadata, dict) or metadata.get("name") != directory.name:
        raise SkillLoadError(f"Skill '{directory.name}' 的目录名与 Frontmatter name 不一致")
    return directory.name


def _resolve_member(directory: Path, relative: str) -> Path:
    candidate = Path(relative)
    if candidate.is_absolute():
        raise SkillLoadError(f"Skill '{directory.name}' 的资源路径必须是相对路径")
    resolved = (directory / candidate).resolve()
    if directory != resolved and directory not in resolved.parents:
        raise SkillLoadError(f"Skill '{directory.name}' 的资源路径越出包目录")
    if not resolved.is_file():
        raise SkillLoadError(f"Skill '{directory.name}' 缺少资源: {relative}")
    return resolved


def _load_json(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise SkillLoadError(f"{label} 不是可读的 JSON 对象") from error
    if not isinstance(value, dict):
        raise SkillLoadError(f"{label} 必须是 JSON 对象")
    return value


def _validate_schema_refs(schema: dict[str, Any], label: str) -> None:
    """V1 只允许可在同一 JSON 文档内静态解析的本地引用。"""

    for node in _walk_json(schema):
        if "$dynamicRef" in node:
            raise SkillLoadError(f"{label} 不支持 $dynamicRef")
        reference = node.get("$ref")
        if reference is None:
            continue
        if not isinstance(reference, str) or not reference.startswith("#"):
            raise SkillLoadError(f"{label} 的 $ref 必须引用当前 JSON 文档")
        if not _local_ref_exists(schema, reference):
            raise SkillLoadError(f"{label} 包含无法解析的本地 $ref: {reference}")


def _walk_json(value: Any):
    if isinstance(value, dict):
        yield value
        for child in value.values():
            yield from _walk_json(child)
    elif isinstance(value, list):
        for child in value:
            yield from _walk_json(child)


def _local_ref_exists(schema: dict[str, Any], reference: str) -> bool:
    if reference == "#":
        return True
    if not reference.startswith("#/"):
        return False
    current: Any = schema
    for raw_part in reference[2:].split("/"):
        part = raw_part.replace("~1", "/").replace("~0", "~")
        if isinstance(current, dict) and part in current:
            current = current[part]
            continue
        if isinstance(current, list) and part.isdigit() and int(part) < len(current):
            current = current[int(part)]
            continue
        return False
    return True


def _contract_schema_path() -> Path:
    packaged = Path(__file__).with_name("skill_contract") / "runtime-manifest.schema.json"
    if packaged.is_file():
        return packaged
    repository = Path(__file__).resolve().parents[3] / "spec" / "skill" / "runtime-manifest.schema.json"
    if repository.is_file():
        return repository
    raise SkillLoadError("发行包缺少可执行 Skill Manifest Schema")


def _lazy_entrypoint(path: Path, function_name: str) -> SkillHandler:
    handler: SkillHandler | None = None

    async def run(context: SkillContext, arguments: dict[str, Any]) -> dict[str, Any]:
        nonlocal handler
        if handler is None:
            handler = _load_entrypoint(path, function_name)
        result = handler(context, arguments)
        if not inspect.isawaitable(result):
            raise TypeError("可执行 Skill 入口必须返回 Awaitable")
        return await result

    return run


def _load_entrypoint(path: Path, function_name: str) -> SkillHandler:
    digest = hashlib.sha256(str(path).encode("utf-8")).hexdigest()[:16]
    module_name = f"stardew_valley_mcp_skill_{digest}"
    module = sys.modules.get(module_name)
    if module is None:
        spec = importlib.util.spec_from_file_location(module_name, path)
        if spec is None or spec.loader is None:
            raise SkillLoadError("无法创建可执行 Skill 模块")
        module = importlib.util.module_from_spec(spec)
        sys.modules[module_name] = module
        try:
            spec.loader.exec_module(module)
        except Exception:
            sys.modules.pop(module_name, None)
            raise
    handler = getattr(module, function_name, None)
    if not callable(handler):
        raise SkillLoadError("可执行 Skill 入口函数不存在")
    return handler
