#!/usr/bin/env python3
"""验证并收集发行产物，生成可重复校验的 SHA-256 清单。"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from email.parser import BytesParser
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
from typing import Any, Callable
from urllib.parse import quote
from urllib.request import Request, urlopen
import zipfile


ROOT = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class ReleaseAssets:
    mod: Path
    wheel: Path
    sdist: Path
    checksums: Path

    def paths(self) -> tuple[Path, ...]:
        return (self.mod, self.wheel, self.sdist, self.checksums)


JsonObject = dict[str, Any]


def _one(paths: list[Path], label: str) -> Path:
    if len(paths) != 1:
        raise ValueError(f"{label} 必须且只能有一个，当前为 {len(paths)}")
    return paths[0]


def _wheel_version(path: Path) -> str:
    with zipfile.ZipFile(path) as archive:
        metadata_names = [
            name for name in archive.namelist() if name.endswith(".dist-info/METADATA")
        ]
        metadata_name = _one([Path(name) for name in metadata_names], "wheel METADATA")
        metadata = BytesParser().parsebytes(archive.read(metadata_name.as_posix()))
    version = metadata.get("Version")
    if not version:
        raise ValueError("wheel METADATA 缺少 Version")
    return version


def _sdist_version(path: Path) -> str:
    with tarfile.open(path, "r:gz") as archive:
        package_info = [
            member
            for member in archive.getmembers()
            if member.isfile() and Path(member.name).name == "PKG-INFO"
        ]
        member = _one([Path(item.name) for item in package_info], "sdist PKG-INFO")
        extracted = archive.extractfile(member.as_posix())
        if extracted is None:
            raise ValueError("sdist PKG-INFO 无法读取")
        metadata = BytesParser().parsebytes(extracted.read())
    version = metadata.get("Version")
    if not version:
        raise ValueError("sdist PKG-INFO 缺少 Version")
    return version


def _mod_version(path: Path) -> str:
    with zipfile.ZipFile(path) as archive:
        try:
            manifest = json.loads(archive.read("StardewValleyMCP/manifest.json"))
        except KeyError as error:
            raise ValueError("Mod ZIP 缺少 StardewValleyMCP/manifest.json") from error
    version = manifest.get("Version")
    if not isinstance(version, str) or not version:
        raise ValueError("Mod manifest 缺少 Version")
    return version


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_github_tag_objects(
    ref_object: JsonObject,
    tag_object: JsonObject,
    *,
    expected_tag: str,
    expected_commit: str,
) -> None:
    reference_target = ref_object.get("object")
    if not isinstance(reference_target, dict) or reference_target.get("type") != "tag":
        raise ValueError("GitHub Tag reference 必须指向 annotated Tag object")
    if reference_target.get("sha") != tag_object.get("sha"):
        raise ValueError("GitHub Tag reference 与 Tag object SHA 不一致")
    if tag_object.get("tag") != expected_tag:
        raise ValueError("GitHub Tag object 的 Tag 名与触发 Tag 不一致")

    tagger = tag_object.get("tagger")
    required_tagger_fields = ("name", "email", "date")
    if not isinstance(tagger, dict) or not all(tagger.get(field) for field in required_tagger_fields):
        raise ValueError("GitHub annotated Tag 缺少完整 tagger 信息")

    target = tag_object.get("object")
    if not isinstance(target, dict) or target.get("type") != "commit":
        raise ValueError("GitHub annotated Tag 必须直接指向 commit")
    if target.get("sha") != expected_commit:
        raise ValueError("GitHub annotated Tag 指向的 commit 与 checkout SHA 不一致")

    verification = tag_object.get("verification")
    if not isinstance(verification, dict) or verification.get("verified") is not True:
        reason = verification.get("reason") if isinstance(verification, dict) else "missing"
        raise ValueError(f"GitHub annotated Tag 未通过签名验证: {reason}")


def verify_github_signed_tag(
    *,
    repository: str,
    tag: str,
    expected_commit: str,
    token: str,
    api_url: str = "https://api.github.com",
    fetch_json: Callable[[str, dict[str, str]], JsonObject] | None = None,
) -> None:
    if not repository or not token:
        raise ValueError("校验 GitHub Tag 需要 GITHUB_REPOSITORY 与 GITHUB_TOKEN")

    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": f"Bearer {token}",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "StardewValley-MCP-release-verifier",
    }

    def fetch(url: str, request_headers: dict[str, str]) -> JsonObject:
        request = Request(url, headers=request_headers)
        with urlopen(request, timeout=30) as response:
            value = json.load(response)
        if not isinstance(value, dict):
            raise ValueError("GitHub API 返回了非对象 JSON")
        return value

    load = fetch_json or fetch
    base = api_url.rstrip("/")
    encoded_repository = quote(repository, safe="/")
    encoded_tag = quote(tag, safe="")
    ref_object = load(
        f"{base}/repos/{encoded_repository}/git/ref/tags/{encoded_tag}",
        headers,
    )
    reference_target = ref_object.get("object")
    tag_object_sha = reference_target.get("sha") if isinstance(reference_target, dict) else None
    if not isinstance(tag_object_sha, str) or not tag_object_sha:
        raise ValueError("GitHub Tag reference 缺少 Tag object SHA")
    tag_object = load(
        f"{base}/repos/{encoded_repository}/git/tags/{quote(tag_object_sha, safe='')}",
        headers,
    )
    validate_github_tag_objects(
        ref_object,
        tag_object,
        expected_tag=tag,
        expected_commit=expected_commit,
    )


def prepare_assets(
    root: Path,
    output: Path,
    *,
    semver: str,
    pep440: str,
) -> ReleaseAssets:
    if output.exists() and any(output.iterdir()):
        raise ValueError(f"发行产物目录必须为空: {output}")
    output.mkdir(parents=True, exist_ok=True)

    mod_source = _one(
        sorted(
            (root / "mod" / "src" / "StardewValleyMcp.Mod" / "bin" / "Release" / "net6.0").glob(
                "StardewValleyMCP *.zip"
            )
        ),
        "Mod ZIP",
    )
    wheel_source = _one(sorted((root / "mcp" / "dist").glob("*.whl")), "MCP wheel")
    sdist_source = _one(sorted((root / "mcp" / "dist").glob("*.tar.gz")), "MCP sdist")

    versions = {
        "Mod ZIP": _mod_version(mod_source),
        "MCP wheel": _wheel_version(wheel_source),
        "MCP sdist": _sdist_version(sdist_source),
    }
    expected = {"Mod ZIP": semver, "MCP wheel": pep440, "MCP sdist": pep440}
    mismatches = [
        f"{label}: 期望 {expected[label]}，实际 {version}"
        for label, version in versions.items()
        if version != expected[label]
    ]
    if mismatches:
        raise ValueError("发行包版本不一致: " + "; ".join(mismatches))

    expected_wheel_name = f"stardew_valley_mcp-{pep440}-py3-none-any.whl"
    expected_sdist_name = f"stardew_valley_mcp-{pep440}.tar.gz"
    if wheel_source.name != expected_wheel_name:
        raise ValueError(f"MCP wheel 文件名不符合约定: {wheel_source.name}")
    if sdist_source.name != expected_sdist_name:
        raise ValueError(f"MCP sdist 文件名不符合约定: {sdist_source.name}")

    mod = output / f"StardewValleyMCP-Mod-v{semver}.zip"
    wheel = output / wheel_source.name
    sdist = output / sdist_source.name
    for source, destination in ((mod_source, mod), (wheel_source, wheel), (sdist_source, sdist)):
        shutil.copyfile(source, destination)

    checksums = output / "SHA256SUMS.txt"
    binary_assets = sorted((mod, wheel, sdist), key=lambda path: path.name)
    checksums.write_text(
        "".join(f"{_sha256(path)}  {path.name}\n" for path in binary_assets),
        encoding="utf-8",
        newline="\n",
    )
    assets = ReleaseAssets(mod=mod, wheel=wheel, sdist=sdist, checksums=checksums)
    expected_names = {path.name for path in assets.paths()}
    actual_names = {path.name for path in output.iterdir() if path.is_file()}
    if actual_names != expected_names:
        raise ValueError(f"发行产物集合不符合约定: {sorted(actual_names ^ expected_names)}")
    return assets


def _version_from_source(root: Path, format_name: str) -> str:
    result = subprocess.run(
        [
            sys.executable,
            str(root / "scripts" / "release_version.py"),
            "show",
            "--format",
            format_name,
        ],
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    operation = parser.add_mutually_exclusive_group(required=True)
    operation.add_argument("--output", type=Path, help="必须为空的产物输出目录")
    operation.add_argument("--verify-github-tag", metavar="TAG", help="通过 GitHub API 验证签名 Tag")
    parser.add_argument("--expected-commit", help="Tag 必须直接指向的 commit SHA")
    args = parser.parse_args()
    try:
        if args.verify_github_tag:
            if not args.expected_commit:
                parser.error("--verify-github-tag 必须同时提供 --expected-commit")
            import os

            verify_github_signed_tag(
                repository=os.environ.get("GITHUB_REPOSITORY", ""),
                tag=args.verify_github_tag,
                expected_commit=args.expected_commit,
                token=os.environ.get("GITHUB_TOKEN", ""),
                api_url=os.environ.get("GITHUB_API_URL", "https://api.github.com"),
            )
            print(f"github_signed_tag_ok tag={args.verify_github_tag} commit={args.expected_commit}")
            return 0

        assert args.output is not None
        subprocess.run(
            [sys.executable, str(ROOT / "scripts" / "release_version.py"), "check"],
            cwd=ROOT,
            check=True,
        )
        assets = prepare_assets(
            ROOT,
            args.output.resolve(),
            semver=_version_from_source(ROOT, "semver"),
            pep440=_version_from_source(ROOT, "pep440"),
        )
    except (OSError, subprocess.CalledProcessError, ValueError) as error:
        raise SystemExit(str(error)) from error
    print("release_assets_ok " + " ".join(path.name for path in assets.paths()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
