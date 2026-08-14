from __future__ import annotations

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[1] / "release_version.py"
SPEC = importlib.util.spec_from_file_location("release_version", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
release_version = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = release_version
SPEC.loader.exec_module(release_version)


class FakeUv:
    def __init__(self, *, fail_check: bool = False) -> None:
        self.fail_check = fail_check
        self.calls: list[tuple[list[str], Path]] = []

    def __call__(self, command: list[str], cwd: Path) -> None:
        self.calls.append((list(command), cwd))
        if command[-1] == "--check":
            if self.fail_check:
                raise subprocess.CalledProcessError(1, command)
            return
        pyproject = (cwd / "mcp/pyproject.toml").read_text(encoding="utf-8")
        version = release_version.VersionRepository._read_pyproject(
            cwd / "mcp/pyproject.toml"
        )
        lock = cwd / "mcp/uv.lock"
        text = lock.read_text(encoding="utf-8")
        text = text.replace(self.lock_version(text), version, 1)
        lock.write_text(text, encoding="utf-8")
        self.assert_project_version(pyproject, version)

    @staticmethod
    def lock_version(text: str) -> str:
        marker = 'name = "stardew-valley-mcp"\nversion = "'
        start = text.index(marker) + len(marker)
        return text[start : text.index('"', start)]

    @staticmethod
    def assert_project_version(text: str, version: str) -> None:
        if f'version = "{version}"' not in text:
            raise AssertionError("假 uv 未观察到同步后的 pyproject 版本")


class VersionRepositoryTest(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self._write_fixture("0.1.0-alpha.1", "0.1.0a1")
        self.uv = FakeUv()
        self.repository = release_version.VersionRepository(
            self.root, uv_bin="fake-uv", runner=self.uv
        )

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def _write(self, relative: str, content: str) -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def _write_fixture(self, semver: str, pep440: str) -> None:
        self._write("VERSION", f"{semver}\n")
        self._write(
            "mod/src/StardewValleyMcp.Mod/manifest.json",
            '{\n  "Name": "Stardew Valley MCP",\n'
            f'  "Version": "{semver}",\n  "Unrelated": true\n}}\n',
        )
        self._write(
            "mod/src/StardewValleyMcp.Mod/StardewValleyMcp.Mod.csproj",
            "<Project><PropertyGroup>\n"
            f"  <Version>{semver}</Version>\n"
            "  <Unrelated>keep</Unrelated>\n"
            "</PropertyGroup></Project>\n",
        )
        self._write(
            "mcp/pyproject.toml",
            "[build-system]\nrequires = []\n\n[project]\n"
            'name = "stardew-valley-mcp"\n'
            f'version = "{pep440}"\n'
            'description = "keep"\n\n[tool.example]\nversion = "unrelated"\n',
        )
        self._write(
            "mcp/src/stardew_valley_mcp/__init__.py",
            '"""keep"""\n\n'
            f'__version__ = "{pep440}"\n',
        )
        self._write(
            "mcp/uv.lock",
            "version = 1\n\n[[package]]\n"
            'name = "stardew-valley-mcp"\n'
            f'version = "{pep440}"\n'
            'source = { editable = "." }\n',
        )

    def test_valid_version_mappings(self) -> None:
        cases = {
            "0.1.0": "0.1.0",
            "1.2.3-alpha.4": "1.2.3a4",
            "1.2.3-beta.5": "1.2.3b5",
            "1.2.3-rc.6": "1.2.3rc6",
        }
        for semver, pep440 in cases.items():
            with self.subTest(semver=semver):
                parsed = release_version.ProductVersion.parse(semver)
                self.assertEqual(parsed.pep440, pep440)
                self.assertEqual(parsed.tag, f"v{semver}")

    def test_invalid_versions_are_rejected(self) -> None:
        invalid = (
            "v1.2.3",
            "1.2",
            "1.2.3-alpha",
            "1.2.3-alpha.0",
            "1.2.3-beta.0",
            "1.2.3-rc.0",
            "1.2.3-preview.1",
            "1.2.3-alpha.01",
            "01.2.3",
            "1.2.3+build.1",
            "1.2.3-alpha.1+build.1",
            " 1.2.3",
        )
        for value in invalid:
            with self.subTest(value=value):
                with self.assertRaises(release_version.VersionError):
                    release_version.ProductVersion.parse(value)

    def test_tag_must_be_exact_canonical_version(self) -> None:
        version = release_version.ProductVersion.parse("1.2.3-beta.4")
        version.validate_tag("v1.2.3-beta.4")
        for tag in (
            "1.2.3-beta.4",
            "v1.2.3-beta.5",
            "vv1.2.3-beta.4",
            "v1.2.3+build.1",
        ):
            with self.subTest(tag=tag):
                with self.assertRaises(release_version.VersionError):
                    version.validate_tag(tag)

    def test_check_detects_every_derived_file_drift(self) -> None:
        changes = {
            "mod/src/StardewValleyMcp.Mod/manifest.json": (
                '"Version": "0.1.0-alpha.1"',
                '"Version": "0.1.0-alpha.2"',
            ),
            "mod/src/StardewValleyMcp.Mod/StardewValleyMcp.Mod.csproj": (
                "<Version>0.1.0-alpha.1</Version>",
                "<Version>0.1.0-alpha.2</Version>",
            ),
            "mcp/pyproject.toml": ('version = "0.1.0a1"', 'version = "0.1.0a2"'),
            "mcp/src/stardew_valley_mcp/__init__.py": (
                '__version__ = "0.1.0a1"',
                '__version__ = "0.1.0a2"',
            ),
            "mcp/uv.lock": ('version = "0.1.0a1"', 'version = "0.1.0a2"'),
        }
        for relative, (old, new) in changes.items():
            with self.subTest(relative=relative):
                path = self.root / relative
                original = path.read_text(encoding="utf-8")
                path.write_text(original.replace(old, new, 1), encoding="utf-8")
                with self.assertRaises(release_version.VersionError):
                    self.repository.check()
                path.write_text(original, encoding="utf-8")

    def test_check_detects_uv_locked_environment_drift(self) -> None:
        repository = release_version.VersionRepository(
            self.root, uv_bin="fake-uv", runner=FakeUv(fail_check=True)
        )
        with self.assertRaisesRegex(release_version.VersionError, "uv.lock"):
            repository.check()

    def test_set_is_idempotent_and_preserves_unrelated_content(self) -> None:
        self.repository.set("2.3.4-rc.5")
        after_first = {
            path.relative_to(self.root): path.read_bytes()
            for path in self.repository.managed_paths
        }
        self.repository.set()
        after_second = {
            path.relative_to(self.root): path.read_bytes()
            for path in self.repository.managed_paths
        }
        self.assertEqual(after_first, after_second)
        self.assertEqual(self.repository.check().semver, "2.3.4-rc.5")
        self.assertIn(
            '<Unrelated>keep</Unrelated>',
            self.repository.csproj_path.read_text(encoding="utf-8"),
        )
        self.assertIn(
            'version = "unrelated"',
            self.repository.pyproject_path.read_text(encoding="utf-8"),
        )
        self.assertEqual(self.repository.show("pep440"), "2.3.4rc5")
        self.assertEqual(self.repository.show("tag"), "v2.3.4-rc.5")

    def test_cli_main_success_and_failure_codes(self) -> None:
        with mock.patch.object(
            release_version, "VersionRepository", return_value=self.repository
        ):
            self.assertEqual(release_version.main(["show", "--format", "tag"]), 0)
            (self.root / "VERSION").write_text("1.2.3-alpha.0\n", encoding="utf-8")
            self.assertEqual(release_version.main(["show"]), 1)

    def test_set_rolls_back_when_uv_fails(self) -> None:
        before = {
            path.relative_to(self.root): path.read_bytes()
            for path in self.repository.managed_paths
        }

        def fail_uv(command: list[str], cwd: Path) -> None:
            raise subprocess.CalledProcessError(1, command)

        repository = release_version.VersionRepository(self.root, runner=fail_uv)
        with self.assertRaisesRegex(release_version.VersionError, "uv"):
            repository.set("9.8.7")
        after = {
            path.relative_to(self.root): path.read_bytes()
            for path in self.repository.managed_paths
        }
        self.assertEqual(before, after)


if __name__ == "__main__":
    unittest.main()
