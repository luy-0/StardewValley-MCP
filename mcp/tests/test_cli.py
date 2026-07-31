from __future__ import annotations

import contextlib
import io

from stardew_valley_mcp.catalog import Catalog
from stardew_valley_mcp.cli import main
from stardew_valley_mcp.server import catalog_for


def test_doctor_reports_generated_protocol() -> None:
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        result = main(["doctor"])
    assert result == 0
    assert "doctor_ok" in output.getvalue()
    assert "stardew_valley.mcp.v1" in output.getvalue()


def test_serve_write_policy_is_explicit() -> None:
    read_only = catalog_for(allow_write=False)
    read_write = catalog_for(allow_write=True)

    assert read_only.policy.allowed_scopes == frozenset({"game:read"})
    assert read_write.policy.allowed_scopes == frozenset({"game:read", "game:write"})
    assert isinstance(read_only, Catalog)
