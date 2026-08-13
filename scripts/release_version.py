#!/usr/bin/env python3
"""同步并校验 Stardew Valley MCP 的产品版本。"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import json
import os
from pathlib import Path
import re
import subprocess
from typing import Callable, Sequence


ROOT = Path(__file__).resolve().parents[1]
SEMVER_PATTERN = re.compile(
    r"^(?P<major>0|[1-9]\d*)\."
    r"(?P<minor>0|[1-9]\d*)\."
    r"(?P<patch>0|[1-9]\d*)"
    r"(?:-(?P<stage>alpha|beta|rc)\.(?P<number>[1-9]\d*))?$"
)
TAG_PATTERN = re.compile(r"^v(?P<version>.+)$")


class VersionError(RuntimeError):
    """版本来源无效或派生版本发生漂移。"""


@dataclass(frozen=True)
class ProductVersion:
    """仓库产品版本及其等价发行格式。"""

    semver: str
    pep440: str

    @property
    def tag(self) -> str:
        return f"v{self.semver}"

    @classmethod
    def parse(cls, value: str) -> ProductVersion:
        match = SEMVER_PATTERN.fullmatch(value)
        if match is None:
            raise VersionError(
                "产品版本格式无效：仅允许 MAJOR.MINOR.PATCH 或 "
                "MAJOR.MINOR.PATCH-(alpha|beta|rc).N，且不得包含前导零或构建元数据"
            )
        base = f"{match['major']}.{match['minor']}.{match['patch']}"
        stage = match["stage"]
        number = match["number"]
        if stage is None:
            pep440 = base
        else:
            pep_stage = {"alpha": "a", "beta": "b", "rc": "rc"}[stage]
            pep440 = f"{base}{pep_stage}{number}"
        return cls(semver=value, pep440=pep440)

    def validate_tag(self, tag: str) -> None:
        match = TAG_PATTERN.fullmatch(tag)
        if match is None:
            raise VersionError("Tag 格式无效：必须为 v 加完整产品版本")
        try:
            parsed = ProductVersion.parse(match["version"])
        except VersionError as error:
            raise VersionError(f"Tag 格式无效：{error}") from error
        if parsed.semver != self.semver:
            raise VersionError(
                f"Tag 与 VERSION 不一致：Tag={parsed.tag}，VERSION={self.tag}"
            )


Runner = Callable[[Sequence[str], Path], None]


def run_checked(command: Sequence[str], cwd: Path) -> None:
    """运行外部命令，并保留其原始输出。"""
    subprocess.run(list(command), cwd=cwd, check=True)


class VersionRepository:
    """读取、同步和检查仓库内的产品版本。"""

    def __init__(
        self,
        root: Path = ROOT,
        *,
        uv_bin: str | None = None,
        runner: Runner = run_checked,
    ) -> None:
        self.root = root
        self.uv_bin = uv_bin or os.environ.get("UV_BIN", "uv")
        self.runner = runner
        self.version_path = root / "VERSION"
        self.manifest_path = root / "mod/src/StardewValleyMcp.Mod/manifest.json"
        self.csproj_path = root / (
            "mod/src/StardewValleyMcp.Mod/StardewValleyMcp.Mod.csproj"
        )
        self.pyproject_path = root / "mcp/pyproject.toml"
        self.init_path = root / "mcp/src/stardew_valley_mcp/__init__.py"
        self.lock_path = root / "mcp/uv.lock"

    @property
    def managed_paths(self) -> tuple[Path, ...]:
        return (
            self.version_path,
            self.manifest_path,
            self.csproj_path,
            self.pyproject_path,
            self.init_path,
            self.lock_path,
        )

    def canonical(self) -> ProductVersion:
        try:
            source = self.version_path.read_text(encoding="utf-8")
        except FileNotFoundError as error:
            raise VersionError("缺少根 VERSION 文件") from error
        if not source.endswith("\n") or source.count("\n") != 1:
            raise VersionError("VERSION 必须只包含一行产品版本并以换行结束")
        return ProductVersion.parse(source.removesuffix("\n"))

    def show(self, output_format: str) -> str:
        version = self.canonical()
        return {
            "semver": version.semver,
            "pep440": version.pep440,
            "tag": version.tag,
        }[output_format]

    def check(self, tag: str | None = None) -> ProductVersion:
        version = self.canonical()
        expected = {
            self.manifest_path: version.semver,
            self.csproj_path: version.semver,
            self.pyproject_path: version.pep440,
            self.init_path: version.pep440,
            self.lock_path: version.pep440,
        }
        readers = {
            self.manifest_path: self._read_manifest,
            self.csproj_path: self._read_csproj,
            self.pyproject_path: self._read_pyproject,
            self.init_path: self._read_init,
            self.lock_path: self._read_lock,
        }
        drift: list[str] = []
        for path, wanted in expected.items():
            actual = readers[path](path)
            if actual != wanted:
                drift.append(
                    f"{path.relative_to(self.root).as_posix()}：当前 {actual!r}，应为 {wanted!r}"
                )
        if drift:
            raise VersionError("产品版本派生文件发生漂移：\n- " + "\n- ".join(drift))
        if tag is not None:
            version.validate_tag(tag)
        try:
            self.runner(
                [self.uv_bin, "lock", "--project", "mcp", "--check"],
                self.root,
            )
        except (OSError, subprocess.CalledProcessError) as error:
            raise VersionError("mcp/uv.lock 与 mcp/pyproject.toml 不一致") from error
        return version

    def set(self, value: str | None = None) -> ProductVersion:
        version = ProductVersion.parse(value) if value is not None else self.canonical()
        originals = {
            path: path.read_bytes() if path.exists() else None
            for path in self.managed_paths
        }
        try:
            self.version_path.write_text(f"{version.semver}\n", encoding="utf-8")
            self._replace_manifest(version.semver)
            self._replace_unique(
                self.csproj_path,
                r"(?m)(<Version>)[^<]*(</Version>)",
                version.semver,
                "Mod csproj Version",
            )
            self._replace_project_version(version.pep440)
            self._replace_unique(
                self.init_path,
                r'(?m)^(__version__\s*=\s*")[^"]*(")$',
                version.pep440,
                "Python __version__",
            )
            try:
                self.runner([self.uv_bin, "lock", "--project", "mcp"], self.root)
            except (OSError, subprocess.CalledProcessError) as error:
                raise VersionError("无法使用 uv 刷新 mcp/uv.lock") from error
            if self._read_lock(self.lock_path) != version.pep440:
                raise VersionError("uv 刷新后 mcp/uv.lock 的本地项目版本仍不匹配")
        except Exception:
            for path, content in originals.items():
                if content is None:
                    if path.exists():
                        path.unlink()
                else:
                    path.write_bytes(content)
            raise
        return version

    @staticmethod
    def _read_manifest(path: Path) -> str:
        try:
            value = json.loads(path.read_text(encoding="utf-8"))["Version"]
        except (FileNotFoundError, KeyError, json.JSONDecodeError) as error:
            raise VersionError("无法读取 Mod manifest Version") from error
        if not isinstance(value, str):
            raise VersionError("Mod manifest Version 必须是字符串")
        return value

    @staticmethod
    def _read_csproj(path: Path) -> str:
        return VersionRepository._extract_unique(
            path, r"(?m)<Version>([^<]*)</Version>", "Mod csproj Version"
        )

    @staticmethod
    def _read_pyproject(path: Path) -> str:
        section = VersionRepository._project_section(path)
        match = re.findall(r'(?m)^version\s*=\s*"([^"]+)"\s*$', section)
        if len(match) != 1:
            raise VersionError("mcp/pyproject.toml 的 [project] 必须有唯一 version")
        return match[0]

    @staticmethod
    def _read_init(path: Path) -> str:
        return VersionRepository._extract_unique(
            path, r'(?m)^__version__\s*=\s*"([^"]+)"$', "Python __version__"
        )

    @staticmethod
    def _read_lock(path: Path) -> str:
        text = path.read_text(encoding="utf-8")
        blocks = re.split(r"(?m)^\[\[package\]\]\s*$", text)
        matching = [
            block
            for block in blocks
            if re.search(r'(?m)^name\s*=\s*"stardew-valley-mcp"\s*$', block)
        ]
        if len(matching) != 1:
            raise VersionError("mcp/uv.lock 必须有唯一的本地项目包条目")
        versions = re.findall(r'(?m)^version\s*=\s*"([^"]+)"\s*$', matching[0])
        if len(versions) != 1:
            raise VersionError("mcp/uv.lock 的本地项目包条目必须有唯一 version")
        return versions[0]

    def _replace_manifest(self, value: str) -> None:
        self._replace_unique(
            self.manifest_path,
            r'(?m)^(\s*"Version"\s*:\s*")[^"]*("\s*,?\s*)$',
            value,
            "Mod manifest Version",
        )

    def _replace_project_version(self, value: str) -> None:
        text = self.pyproject_path.read_text(encoding="utf-8")
        start, end = self._project_section_bounds(text)
        section = text[start:end]
        replaced, count = re.subn(
            r'(?m)^(version\s*=\s*")[^"]*("\s*)$',
            rf"\g<1>{value}\g<2>",
            section,
        )
        if count != 1:
            raise VersionError("mcp/pyproject.toml 的 [project] 必须有唯一 version")
        self.pyproject_path.write_text(text[:start] + replaced + text[end:], encoding="utf-8")

    @staticmethod
    def _project_section(path: Path) -> str:
        text = path.read_text(encoding="utf-8")
        start, end = VersionRepository._project_section_bounds(text)
        return text[start:end]

    @staticmethod
    def _project_section_bounds(text: str) -> tuple[int, int]:
        match = re.search(r"(?m)^\[project\]\s*$", text)
        if match is None:
            raise VersionError("mcp/pyproject.toml 缺少 [project]")
        next_section = re.search(r"(?m)^\[[^\n]+\]\s*$", text[match.end() :])
        end = match.end() + next_section.start() if next_section else len(text)
        return match.start(), end

    @staticmethod
    def _extract_unique(path: Path, pattern: str, label: str) -> str:
        values = re.findall(pattern, path.read_text(encoding="utf-8"))
        if len(values) != 1:
            raise VersionError(f"{label} 必须唯一存在")
        return values[0]

    @staticmethod
    def _replace_unique(path: Path, pattern: str, value: str, label: str) -> None:
        text = path.read_text(encoding="utf-8")
        replaced, count = re.subn(
            pattern,
            rf"\g<1>{value}\g<2>",
            text,
        )
        if count != 1:
            raise VersionError(f"{label} 必须唯一存在")
        path.write_text(replaced, encoding="utf-8")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    set_parser = subparsers.add_parser("set", help="同步 VERSION 与所有派生版本")
    set_parser.add_argument("version", nargs="?", help="可选的新产品版本")

    check_parser = subparsers.add_parser("check", help="只读校验所有产品版本")
    check_parser.add_argument("--tag", help="同时校验 v 前缀 Tag 与 VERSION 一致")

    show_parser = subparsers.add_parser("show", help="输出根 VERSION 的指定格式")
    show_parser.add_argument(
        "--format",
        choices=("semver", "pep440", "tag"),
        default="semver",
        help="输出 SemVer、PEP 440 或 Tag（默认 SemVer）",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    repository = VersionRepository()
    try:
        if args.command == "set":
            version = repository.set(args.version)
            print(
                f"产品版本同步完成：SemVer={version.semver}，"
                f"PEP440={version.pep440}，Tag={version.tag}"
            )
        elif args.command == "check":
            version = repository.check(args.tag)
            print(
                f"产品版本校验通过：SemVer={version.semver}，"
                f"PEP440={version.pep440}，Tag={version.tag}"
            )
        else:
            print(repository.show(args.format))
    except VersionError as error:
        print(f"产品版本处理失败：{error}", file=os.sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
