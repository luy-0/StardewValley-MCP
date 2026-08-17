"""公共 Tool Catalog 与 Mod 能力快照的唯一交集。"""

from __future__ import annotations

import hashlib
import json
import struct
from dataclasses import dataclass
from importlib.resources import files
from typing import Any, Iterable

from mcp import types

from .tool_schema import require_mcp_object_root


@dataclass(frozen=True)
class CatalogPolicy:
    supported_capabilities: frozenset[str] | None
    allowed_scopes: frozenset[str]


DEFAULT_POLICY = CatalogPolicy(None, frozenset({"game:read"}))


def _lp(value: str) -> bytes:
    raw = value.encode("utf-8")
    return struct.pack(">I", len(raw)) + raw


def descriptor_digest(descriptors: Iterable[Any]) -> str:
    value = bytearray()
    for item in sorted(descriptors, key=lambda descriptor: descriptor.id.encode("utf-8")):
        value.extend(_lp(item.id))
        value.extend(_lp(item.contract_version))
        value.extend(bytes((item.side_effect, item.execution, int(item.cancellable))))
        value.extend(struct.pack(">II", item.default_timeout_ms, item.max_timeout_ms))
        value.extend(_lp(item.request_type))
        value.extend(_lp(item.result_type))
        value.extend(_lp(item.required_scope))
        risks = sorted(item.risks, key=lambda risk: risk.encode("utf-8"))
        value.extend(struct.pack(">I", len(risks)))
        for risk in risks:
            value.extend(_lp(risk))
        value.append(int(item.destructive))
    return hashlib.sha256(value).hexdigest()


class Catalog:
    def __init__(self, document: dict[str, Any], policy: CatalogPolicy = DEFAULT_POLICY):
        self._capabilities = {item["id"]: item for item in document["capabilities"]}
        self._tools = {item["capabilityId"]: item for item in document["tools"]}
        if set(self._capabilities) != set(self._tools):
            raise ValueError("public Tool Catalog capability 集合不一致")
        for item in self._tools.values():
            require_mcp_object_root(item["inputSchema"], f"{item['name']} inputSchema")
            require_mcp_object_root(item["outputSchema"], f"{item['name']} outputSchema")
        self._policy = policy
        self._supported_capabilities = (
            frozenset(self._capabilities)
            if policy.supported_capabilities is None
            else policy.supported_capabilities
        )

    @classmethod
    def load(cls, policy: CatalogPolicy = DEFAULT_POLICY) -> "Catalog":
        raw = files("stardew_valley_mcp.generated").joinpath("tool_catalog.json").read_text()
        return cls(json.loads(raw), policy)

    @property
    def capability_ids(self) -> frozenset[str]:
        return frozenset(self._capabilities)

    @property
    def policy(self) -> CatalogPolicy:
        return self._policy

    def validate_snapshot(self, snapshot: Any) -> None:
        if descriptor_digest(snapshot.capabilities) != snapshot.digest:
            raise ValueError("Mod capability digest 不匹配")
        seen: set[str] = set()
        for descriptor in snapshot.capabilities:
            if descriptor.id in seen:
                raise ValueError("Mod capability descriptor 重复")
            seen.add(descriptor.id)
            public = self._capabilities.get(descriptor.id)
            if public is None:
                raise ValueError(f"Mod 公告未知 capability: {descriptor.id}")
            try:
                side_effect = descriptor.DESCRIPTOR.fields_by_name["side_effect"].enum_type.values_by_number[descriptor.side_effect].name.removeprefix("SIDE_EFFECT_").lower()
                execution = descriptor.DESCRIPTOR.fields_by_name["execution"].enum_type.values_by_number[descriptor.execution].name.removeprefix("EXECUTION_MODE_").lower()
            except KeyError as error:
                raise ValueError(f"Mod descriptor 包含未知 enum number: {descriptor.id}") from error
            expected = {
                "contract_version": descriptor.contract_version,
                "default_timeout_ms": descriptor.default_timeout_ms,
                "max_timeout_ms": descriptor.max_timeout_ms,
                "request": descriptor.request_type,
                "result": descriptor.result_type,
                "required_scope": descriptor.required_scope,
                "cancellable": descriptor.cancellable,
                "destructive": descriptor.destructive,
                "side_effect": side_effect,
                "execution": execution,
                "risk": sorted(descriptor.risks),
            }
            if any(
                (sorted(public[key]) if key == "risk" else public[key]) != value
                for key, value in expected.items()
            ):
                raise ValueError(f"Mod descriptor 与公共 Catalog 不一致: {descriptor.id}")

    def tools_for(self, snapshot: Any) -> list[types.Tool]:
        self.validate_snapshot(snapshot)
        announced = {item.id for item in snapshot.capabilities}
        enabled = sorted(
            set(self._tools) & self._supported_capabilities & announced,
            key=lambda capability_id: self._tools[capability_id]["name"],
        )
        return [self._as_tool(self._tools[capability_id]) for capability_id in enabled if self._capabilities[capability_id]["required_scope"] in self._policy.allowed_scopes]

    def descriptor(self, capability_id: str, snapshot: Any) -> Any:
        self.validate_snapshot(snapshot)
        if capability_id not in self._supported_capabilities or capability_id not in self._capabilities:
            raise ValueError("MCP 当前不支持该能力")
        for descriptor in snapshot.capabilities:
            if descriptor.id == capability_id and descriptor.required_scope in self._policy.allowed_scopes:
                return descriptor
        raise ValueError("Mod 未公告或未授权该能力")

    def tool(self, capability_id: str) -> types.Tool:
        return self._as_tool(self._tools[capability_id])

    def capability_for_tool(self, tool_name: str) -> str:
        for capability_id, tool in self._tools.items():
            if tool["name"] == tool_name:
                return capability_id
        raise ValueError("未知 MCP Tool")

    def allows(self, capability_id: str) -> bool:
        return capability_id in self._supported_capabilities and capability_id in self._capabilities and self._capabilities[capability_id]["required_scope"] in self._policy.allowed_scopes

    @staticmethod
    def _as_tool(source: dict[str, Any]) -> types.Tool:
        return types.Tool(name=source["name"], title=source["title"], description=source["description"], inputSchema=source["inputSchema"], outputSchema=source["outputSchema"], annotations=types.ToolAnnotations(**source["annotations"]))
