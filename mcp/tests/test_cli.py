from __future__ import annotations

import contextlib
import io

from stardew_valley_mcp.cli import main


def test_doctor_reports_generated_protocol() -> None:
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        result = main(["doctor"])
    assert result == 0
    assert "doctor_ok" in output.getvalue()
    assert "stardew_valley.mcp.v1" in output.getvalue()
