from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).resolve().parents[1] / "prepare_release_assets.py"
SPEC = importlib.util.spec_from_file_location("prepare_release_assets", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
prepare_release_assets = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = prepare_release_assets
SPEC.loader.exec_module(prepare_release_assets)
prepare_assets = prepare_release_assets.prepare_assets
validate_github_tag_objects = prepare_release_assets.validate_github_tag_objects


SEMVER = "0.1.0-alpha.1"
PEP440 = "0.1.0a1"


class PrepareReleaseAssetsTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.mod_dist = (
            self.root / "mod" / "src" / "StardewValleyMcp.Mod" / "bin" / "Release" / "net6.0"
        )
        self.mcp_dist = self.root / "mcp" / "dist"
        self.mod_dist.mkdir(parents=True)
        self.mcp_dist.mkdir(parents=True)

    def write_packages(
        self,
        *,
        mod_version: str = SEMVER,
        python_version: str = PEP440,
    ) -> None:
        with zipfile.ZipFile(self.mod_dist / f"StardewValleyMCP {SEMVER}.zip", "w") as archive:
            archive.writestr(
                "StardewValleyMCP/manifest.json",
                json.dumps({"Version": mod_version}),
            )
        wheel = self.mcp_dist / f"stardew_valley_mcp-{PEP440}-py3-none-any.whl"
        with zipfile.ZipFile(wheel, "w") as archive:
            archive.writestr(
                f"stardew_valley_mcp-{PEP440}.dist-info/METADATA",
                f"Metadata-Version: 2.4\nName: stardew-valley-mcp\nVersion: {python_version}\n",
            )
        package_info = self.root / "PKG-INFO"
        package_info.write_text(
            f"Metadata-Version: 2.4\nName: stardew-valley-mcp\nVersion: {python_version}\n",
            encoding="utf-8",
        )
        with tarfile.open(self.mcp_dist / f"stardew_valley_mcp-{PEP440}.tar.gz", "w:gz") as archive:
            archive.add(package_info, arcname=f"stardew_valley_mcp-{PEP440}/PKG-INFO")

    def test_collects_exactly_four_named_assets_with_valid_checksums(self) -> None:
        self.write_packages()
        output = self.root / "release-assets"

        assets = prepare_assets(self.root, output, semver=SEMVER, pep440=PEP440)

        self.assertEqual(
            {path.name for path in assets.paths()},
            {
                f"StardewValleyMCP-Mod-v{SEMVER}.zip",
                f"stardew_valley_mcp-{PEP440}-py3-none-any.whl",
                f"stardew_valley_mcp-{PEP440}.tar.gz",
                "SHA256SUMS.txt",
            },
        )
        checksum_lines = assets.checksums.read_text(encoding="utf-8").splitlines()
        self.assertEqual(len(checksum_lines), 3)
        for line in checksum_lines:
            expected_digest, filename = line.split("  ", maxsplit=1)
            self.assertEqual(
                expected_digest,
                hashlib.sha256((output / filename).read_bytes()).hexdigest(),
            )

    def test_rejects_package_version_drift(self) -> None:
        self.write_packages(mod_version="9.9.9")

        with self.assertRaisesRegex(ValueError, "Mod ZIP: 期望"):
            prepare_assets(
                self.root,
                self.root / "release-assets",
                semver=SEMVER,
                pep440=PEP440,
            )

    def test_rejects_noncanonical_python_artifact_name(self) -> None:
        self.write_packages()
        wheel = self.mcp_dist / f"stardew_valley_mcp-{PEP440}-py3-none-any.whl"
        wheel.rename(self.mcp_dist / "stardew-valley-mcp-custom.whl")

        with self.assertRaisesRegex(ValueError, "MCP wheel 文件名不符合约定"):
            prepare_assets(
                self.root,
                self.root / "release-assets",
                semver=SEMVER,
                pep440=PEP440,
            )

    def test_rejects_missing_or_duplicate_package(self) -> None:
        with self.assertRaisesRegex(ValueError, "Mod ZIP 必须且只能有一个"):
            prepare_assets(
                self.root,
                self.root / "release-assets",
                semver=SEMVER,
                pep440=PEP440,
            )

        self.write_packages()
        (self.mcp_dist / "duplicate.whl").write_bytes(b"not a wheel")
        with self.assertRaisesRegex(ValueError, "MCP wheel 必须且只能有一个"):
            prepare_assets(
                self.root,
                self.root / "second-output",
                semver=SEMVER,
                pep440=PEP440,
            )

    def test_rejects_nonempty_output_directory(self) -> None:
        self.write_packages()
        output = self.root / "release-assets"
        output.mkdir()
        (output / "unexpected-secret.txt").write_text("unexpected", encoding="utf-8")

        with self.assertRaisesRegex(ValueError, "发行产物目录必须为空"):
            prepare_assets(self.root, output, semver=SEMVER, pep440=PEP440)

    def test_accepts_signed_annotated_tag_for_exact_checkout_commit(self) -> None:
        validate_github_tag_objects(
            {"object": {"type": "tag", "sha": "tag-object-sha"}},
            {
                "sha": "tag-object-sha",
                "tag": f"v{SEMVER}",
                "tagger": {
                    "name": "Release Maintainer",
                    "email": "maintainer@example.com",
                    "date": "2026-08-13T00:00:00Z",
                },
                "object": {"type": "commit", "sha": "checkout-sha"},
                "verification": {"verified": True, "reason": "valid"},
            },
            expected_tag=f"v{SEMVER}",
            expected_commit="checkout-sha",
        )

    def test_rejects_unsigned_or_wrong_target_tag(self) -> None:
        reference = {"object": {"type": "tag", "sha": "tag-object-sha"}}
        tag_object = {
            "sha": "tag-object-sha",
            "tag": f"v{SEMVER}",
            "tagger": {
                "name": "Release Maintainer",
                "email": "maintainer@example.com",
                "date": "2026-08-13T00:00:00Z",
            },
            "object": {"type": "commit", "sha": "wrong-sha"},
            "verification": {"verified": True, "reason": "valid"},
        }
        with self.assertRaisesRegex(ValueError, "commit 与 checkout SHA 不一致"):
            validate_github_tag_objects(
                reference,
                tag_object,
                expected_tag=f"v{SEMVER}",
                expected_commit="checkout-sha",
            )

        tag_object["object"] = {"type": "commit", "sha": "checkout-sha"}
        tag_object["verification"] = {"verified": False, "reason": "unsigned"}
        with self.assertRaisesRegex(ValueError, "未通过签名验证: unsigned"):
            validate_github_tag_objects(
                reference,
                tag_object,
                expected_tag=f"v{SEMVER}",
                expected_commit="checkout-sha",
            )


if __name__ == "__main__":
    unittest.main()
