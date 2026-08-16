"""MCP Tool Schema 的公共运行时门禁。"""

from __future__ import annotations

from typing import Any

from jsonschema import Draft202012Validator
from jsonschema.exceptions import SchemaError


def require_mcp_object_root(schema: dict[str, Any], label: str) -> None:
    """落实 MCP 2025-11-25 对 Tool Schema 的根对象限制。"""

    if schema.get("type") != "object":
        raise ValueError(f"{label} 顶层 type 必须是 object")


def validate_mcp_tool_schema(schema: dict[str, Any], label: str) -> None:
    """验证公开 V1 JSON Schema 及 MCP 根对象限制。"""

    try:
        Draft202012Validator.check_schema(schema)
    except SchemaError as error:
        raise ValueError(f"{label} 不是有效的 JSON Schema") from error
    require_mcp_object_root(schema, label)
