#!/usr/bin/env python3
"""从 V1 Proto descriptor 和显式 override 生成 MCP Tool Schema 目录。"""

from __future__ import annotations

import argparse
import copy
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any

try:
    import yaml
    from google.protobuf import descriptor_pb2
except ImportError as exc:  # pragma: no cover - 面向未安装依赖的操作提示
    raise SystemExit(
        "缺少生成依赖；请先执行：python3 -m pip install -r spec/conformance/requirements.txt"
    ) from exc


ROOT = Path(__file__).resolve().parents[2]
PROTO_DIR = ROOT / "spec" / "proto"
MANIFEST_PATH = ROOT / "spec" / "capabilities" / "manifest.yaml"
MCP_SPEC_DIR = ROOT / "spec" / "mcp"
SCHEMA_POLICY_PATH = MCP_SPEC_DIR / "schema-policy.yaml"
ERROR_MAP_PATH = MCP_SPEC_DIR / "error-map.yaml"
DEFAULT_OUTPUT = MCP_SPEC_DIR / "tool-schemas.json"
UUID_PATTERN = "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"
REVISION_PATTERN = "^[0-9a-f]{64}$"
INT64_PATTERN = "^-?(0|[1-9][0-9]*)$"
UINT64_PATTERN = "^(0|[1-9][0-9]*)$"

def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def lower_camel(name: str) -> str:
    parts = name.split("_")
    return parts[0] + "".join(part[:1].upper() + part[1:] for part in parts[1:])


def load_yaml(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as stream:
        value = yaml.safe_load(stream)
    require(isinstance(value, dict), f"{path} 顶层必须是对象")
    return value


def compile_descriptor_set() -> descriptor_pb2.FileDescriptorSet:
    proto_files = sorted(path.name for path in PROTO_DIR.glob("*.proto"))
    require(proto_files, f"{PROTO_DIR} 中没有 Proto 文件")
    with tempfile.TemporaryDirectory(prefix="sdvmcp-mcp-schema-") as directory:
        output = Path(directory) / "v1.pb"
        command = [
            "protoc",
            f"--proto_path={PROTO_DIR}",
            "--include_imports",
            f"--descriptor_set_out={output}",
            *proto_files,
        ]
        try:
            subprocess.run(command, cwd=PROTO_DIR, check=True)
        except FileNotFoundError as exc:
            raise SystemExit("找不到 protoc；请先安装 Protocol Buffers 编译器") from exc
        descriptor_set = descriptor_pb2.FileDescriptorSet()
        descriptor_set.ParseFromString(output.read_bytes())
        return descriptor_set


class DescriptorIndex:
    def __init__(self, descriptor_set: descriptor_pb2.FileDescriptorSet, package: str) -> None:
        self.package = package
        self.messages: dict[str, descriptor_pb2.DescriptorProto] = {}
        self.enums: dict[str, descriptor_pb2.EnumDescriptorProto] = {}
        for file_descriptor in descriptor_set.file:
            prefix = f"{file_descriptor.package}." if file_descriptor.package else ""
            self._index_messages(prefix, file_descriptor.message_type)
            for enum in file_descriptor.enum_type:
                self.enums[prefix + enum.name] = enum

    def _index_messages(
        self, prefix: str, messages: Any
    ) -> None:
        for message in messages:
            full_name = prefix + message.name
            self.messages[full_name] = message
            nested_prefix = full_name + "."
            self._index_messages(nested_prefix, message.nested_type)
            for enum in message.enum_type:
                self.enums[nested_prefix + enum.name] = enum

    def message(self, name: str) -> descriptor_pb2.DescriptorProto:
        full_name = name if "." in name else f"{self.package}.{name}"
        require(full_name in self.messages, f"Proto 消息不存在：{full_name}")
        return self.messages[full_name]


def enum_prefix(enum: descriptor_pb2.EnumDescriptorProto) -> str:
    unspecified = next(
        (value.name for value in enum.value if value.number == 0 and value.name.endswith("_UNSPECIFIED")),
        None,
    )
    if unspecified:
        return unspecified[: -len("UNSPECIFIED")]
    names = [value.name for value in enum.value]
    prefix = re.match(r"^[A-Z0-9]+(?:_[A-Z0-9]+)*_", names[0]).group(0) if names else ""
    while prefix and not all(name.startswith(prefix) for name in names):
        prefix = prefix.rsplit("_", 2)[0] + "_" if "_" in prefix[:-1] else ""
    return prefix


def enum_json_value(name: str, prefix: str) -> str:
    raw = name[len(prefix) :] if prefix and name.startswith(prefix) else name
    return raw.lower()


class SchemaBuilder:
    def __init__(
        self,
        index: DescriptorIndex,
        overrides: dict[str, Any],
        mode: str,
    ) -> None:
        self.index = index
        override_section = "messages" if mode == "input" else "output_messages"
        self.overrides = overrides.get(override_section, {})
        self.mode = mode
        self.defs: dict[str, Any] = {}

    def build(self, root_message: str, *, inline_root: bool = False) -> dict[str, Any]:
        self._ensure_message(f"{self.index.package}.{root_message}")
        definitions = {name: self.defs[name] for name in sorted(self.defs)}
        if inline_root:
            root = copy.deepcopy(definitions.pop(root_message))
            root["$schema"] = "https://json-schema.org/draft/2020-12/schema"
            if definitions:
                root["$defs"] = definitions
            return root
        return {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "$ref": f"#/$defs/{root_message}",
            "$defs": definitions,
        }

    def _ensure_message(self, full_name: str) -> str:
        message = self.index.message(full_name)
        key = message.name
        if key in self.defs:
            return key
        self.defs[key] = {}

        override = self.overrides.get(message.name, {})
        real_oneofs: dict[int, list[str]] = {}
        for index, oneof in enumerate(message.oneof_decl):
            members = [
                field.json_name or lower_camel(field.name)
                for field in message.field
                if field.HasField("oneof_index")
                and field.oneof_index == index
                and not field.proto3_optional
            ]
            if members:
                real_oneofs[index] = members

        properties: dict[str, Any] = {}
        for field in message.field:
            json_name = field.json_name or lower_camel(field.name)
            schema = self._field_schema(field)
            field_override = override.get("fields", {}).get(json_name, {})
            schema.update(copy.deepcopy(field_override))
            properties[json_name] = schema

        object_schema: dict[str, Any] = {
            "type": "object",
            "additionalProperties": False,
            "properties": {name: properties[name] for name in sorted(properties)},
        }

        required: list[str]
        if self.mode == "input":
            required = list(override.get("required", []))
        else:
            required = [
                field.json_name or lower_camel(field.name)
                for field in message.field
                if not field.proto3_optional and not (
                    field.HasField("oneof_index") and field.oneof_index in real_oneofs
                )
            ]
        unknown_required = sorted(set(required) - set(properties))
        require(not unknown_required, f"{message.name} 存在未知 required 字段：{unknown_required}")
        if required:
            object_schema["required"] = sorted(required)

        constraints: list[dict[str, Any]] = []
        required_oneofs = set(override.get("required_oneofs", []))
        known_oneofs = {message.oneof_decl[index].name for index in real_oneofs}
        require(
            required_oneofs <= known_oneofs,
            f"{message.name} 存在未知 required_oneofs：{sorted(required_oneofs - known_oneofs)}",
        )
        for index, members in real_oneofs.items():
            oneof_name = message.oneof_decl[index].name
            if oneof_name in required_oneofs:
                constraints.append({"oneOf": [{"required": [member]} for member in members]})
            elif len(members) > 1:
                pairs = [
                    {"required": [members[left], members[right]]}
                    for left in range(len(members))
                    for right in range(left + 1, len(members))
                ]
                constraints.append({"not": {"anyOf": pairs}})
        constraints.extend(copy.deepcopy(override.get("allOf", [])))
        if constraints:
            object_schema["allOf"] = constraints

        self.defs[key] = object_schema
        return key

    def _field_schema(self, field: descriptor_pb2.FieldDescriptorProto) -> dict[str, Any]:
        if field.type == field.TYPE_MESSAGE:
            nested_key = self._ensure_message(field.type_name.lstrip("."))
            schema: dict[str, Any] = {"$ref": f"#/$defs/{nested_key}"}
        elif field.type == field.TYPE_ENUM:
            full_name = field.type_name.lstrip(".")
            enum = self.index.enums[full_name]
            prefix = enum_prefix(enum)
            values = [
                enum_json_value(value.name, prefix)
                for value in enum.value
                if self.mode == "output" or value.number != 0
            ]
            schema = {"type": "string", "enum": values}
        elif field.type in (field.TYPE_INT64, field.TYPE_SINT64, field.TYPE_SFIXED64):
            schema = {"type": "string", "pattern": INT64_PATTERN}
        elif field.type in (field.TYPE_UINT64, field.TYPE_FIXED64):
            schema = {"type": "string", "pattern": UINT64_PATTERN}
        elif field.type in (
            field.TYPE_INT32,
            field.TYPE_SINT32,
            field.TYPE_SFIXED32,
            field.TYPE_UINT32,
            field.TYPE_FIXED32,
        ):
            schema = {"type": "integer"}
            if field.type in (field.TYPE_UINT32, field.TYPE_FIXED32):
                schema.update({"minimum": 0, "maximum": 4294967295})
            else:
                schema.update({"minimum": -2147483648, "maximum": 2147483647})
        elif field.type in (field.TYPE_DOUBLE, field.TYPE_FLOAT):
            schema = {"type": "number"}
        elif field.type == field.TYPE_BOOL:
            schema = {"type": "boolean"}
        elif field.type == field.TYPE_STRING:
            schema = {"type": "string", "maxLength": 512, "pattern": "^[^\\u0000]*$"}
            json_name = field.json_name or lower_camel(field.name)
            if json_name == "locationId" or json_name.endswith("LocationId"):
                schema.update({"minLength": 1, "maxLength": 128})
            if json_name.endswith("Revision"):
                schema["pattern"] = REVISION_PATTERN
        elif field.type == field.TYPE_BYTES:
            schema = {"type": "string", "contentEncoding": "base64"}
        else:
            raise ValueError(f"不支持的 Proto 字段类型：{field.type}")

        if field.label == field.LABEL_REPEATED:
            return {"type": "array", "items": schema}
        return schema


def load_tool_errors() -> dict[str, dict[str, Any]]:
    error_map = load_yaml(ERROR_MAP_PATH)
    require(error_map.get("schema_version") == 1, "error-map schema_version 必须为 1")
    mappings = error_map.get("mappings")
    require(isinstance(mappings, dict) and mappings, "error-map mappings 不能为空")
    tool_errors: dict[str, dict[str, Any]] = {}
    for proto_code, mapping in mappings.items():
        require(isinstance(mapping, dict), f"{proto_code} 的错误映射必须是对象")
        tool_code = mapping.get("tool_code")
        require(
            isinstance(tool_code, str) and re.fullmatch(r"[a-z][a-z0-9_]*", tool_code) is not None,
            f"{proto_code} tool_code 无效",
        )
        normalized = {
            "outcome": mapping.get("outcome"),
            "retryable": mapping.get("retryable"),
        }
        require(normalized["outcome"] in ("failed", "unknown"), f"{proto_code} outcome 无效")
        require(isinstance(normalized["retryable"], bool), f"{proto_code} retryable 无效")
        if tool_code in tool_errors:
            require(tool_errors[tool_code] == normalized, f"{tool_code} 存在冲突错误映射")
        else:
            tool_errors[tool_code] = normalized
    local_errors = error_map.get("local_errors", {})
    require(isinstance(local_errors, dict), "error-map local_errors 必须是对象")
    for tool_code, mapping in local_errors.items():
        require(re.fullmatch(r"[a-z][a-z0-9_]*", tool_code) is not None, f"本地 Tool Error 名称无效: {tool_code}")
        require(tool_code not in tool_errors, f"本地 Tool Error 与 Proto 映射冲突: {tool_code}")
        require(isinstance(mapping, dict), f"本地 Tool Error 必须是对象: {tool_code}")
        normalized = {"outcome": mapping.get("outcome"), "retryable": mapping.get("retryable")}
        require(normalized["outcome"] in ("failed", "unknown"), f"{tool_code} outcome 无效")
        require(isinstance(normalized["retryable"], bool), f"{tool_code} retryable 无效")
        tool_errors[tool_code] = normalized
    return tool_errors


def error_context_schema(
    index: DescriptorIndex,
    overrides: dict[str, Any],
    generated_defs: dict[str, Any],
    capability_id: str,
) -> dict[str, Any] | None:
    contexts_by_capability = overrides.get("error_contexts", {})
    require(isinstance(contexts_by_capability, dict), "error_contexts 必须是对象")
    contexts = contexts_by_capability.get(capability_id, {})
    require(isinstance(contexts, dict), f"{capability_id} 错误上下文必须是对象")
    if not contexts:
        return None
    properties: dict[str, Any] = {}
    for name, context in sorted(contexts.items()):
        require(isinstance(name, str) and re.fullmatch(r"[a-z][a-z0-9_]*", name) is not None, "错误上下文名称无效")
        require(isinstance(context, dict) and isinstance(context.get("message"), str), f"{name} 错误上下文消息无效")
        builder = SchemaBuilder(index, overrides, "output")
        document = builder.build(context["message"])
        for definition, schema in document["$defs"].items():
            if definition in generated_defs:
                require(generated_defs[definition] == schema, f"错误上下文定义冲突：{definition}")
            else:
                generated_defs[definition] = schema
        properties[lower_camel(name)] = {"$ref": f"#/$defs/{context['message']}"}
    return {
        "type": "object",
        "additionalProperties": False,
        "properties": properties,
        "oneOf": [{"required": [name]} for name in sorted(properties)],
    }


def tool_error_schema(
    tool_errors: dict[str, dict[str, Any]],
    details: dict[str, Any] | None,
    outcome: str | None = None,
) -> dict[str, Any]:
    branches: list[dict[str, Any]] = []
    for code in sorted(tool_errors):
        policy = tool_errors[code]
        if outcome is not None and policy["outcome"] != outcome:
            continue
        properties: dict[str, Any] = {
            "code": {"const": code},
            "message": {"type": "string", "minLength": 1, "maxLength": 512},
            "retryable": {"const": policy["retryable"]},
        }
        if policy["retryable"]:
            properties["retryAfterMs"] = {
                "type": "integer",
                "minimum": 1,
                "maximum": 180000,
            }
        if details is not None:
            properties["details"] = details
        branches.append(
            {
                "type": "object",
                "additionalProperties": False,
                "required": ["code", "message", "retryable"],
                "properties": properties,
            }
        )
    require(branches, f"没有 outcome={outcome} 的 Tool Error")
    return {"oneOf": branches}


def output_schema(
    index: DescriptorIndex,
    overrides: dict[str, Any],
    result: str,
    tool_errors: dict[str, dict[str, Any]],
    capability_id: str,
) -> dict[str, Any]:
    generated = SchemaBuilder(index, overrides, "output").build(result)
    details = error_context_schema(index, overrides, generated["$defs"], capability_id)
    generated["$defs"]["StardewToolError"] = tool_error_schema(tool_errors, details)
    generated["$defs"]["FailedToolError"] = tool_error_schema(tool_errors, details, "failed")
    generated["$defs"]["UnknownToolError"] = tool_error_schema(tool_errors, details, "unknown")
    output_ref = f"#/$defs/{result}"
    branch_common = {
        "commandId": {"type": "string", "pattern": UUID_PATTERN},
    }
    generated.pop("$ref")
    generated.update(
        {
            "oneOf": [
                {
                    "type": "object",
                    "additionalProperties": False,
                    "required": ["status", "commandId", "output"],
                    "properties": {
                        "status": {"const": "succeeded"},
                        **branch_common,
                        "output": {"$ref": output_ref},
                    },
                },
                {
                    "type": "object",
                    "additionalProperties": False,
                    "required": ["status", "commandId", "error"],
                    "properties": {
                        "status": {"const": "failed"},
                        **branch_common,
                        "error": {"$ref": "#/$defs/FailedToolError"},
                    },
                },
                {
                    "type": "object",
                    "additionalProperties": False,
                    "required": ["status", "commandId", "error"],
                    "properties": {
                        "status": {"const": "unknown"},
                        **branch_common,
                        "error": {"$ref": "#/$defs/UnknownToolError"},
                    },
                },
            ]
        }
    )
    return generated


def validate_proto_projection(index: DescriptorIndex, capabilities: list[dict[str, Any]]) -> None:
    request_union = index.message("CommandRequest")
    result_union = index.message("CapabilityResult")
    request_fields = {field.name: field.type_name.lstrip(".") for field in request_union.field}
    result_fields = {field.name: field.type_name.lstrip(".") for field in result_union.field}
    for capability in capabilities:
        capability_id = capability["id"]
        require(capability_id in request_fields, f"CommandRequest 缺少 {capability_id}")
        require(capability_id in result_fields, f"CapabilityResult 缺少 {capability_id}")
        require(
            request_fields[capability_id] == f"{index.package}.{capability['request']}",
            f"{capability_id} Request 与 Manifest 不一致",
        )
        require(
            result_fields[capability_id] == f"{index.package}.{capability['result']}",
            f"{capability_id} Result 与 Manifest 不一致",
        )


def validate_override_section(index: DescriptorIndex, messages: Any, section: str) -> None:
    require(isinstance(messages, dict), f"override {section} 必须是对象")
    allowed_keys = {"required", "required_oneofs", "fields", "allOf"}
    for message_name, override in messages.items():
        require(isinstance(override, dict), f"{message_name} override 必须是对象")
        message = index.message(message_name)
        unknown_keys = set(override) - allowed_keys
        require(not unknown_keys, f"{message_name} 存在未知 override 键：{sorted(unknown_keys)}")
        fields = override.get("fields", {})
        require(isinstance(fields, dict), f"{message_name}.fields 必须是对象")
        proto_fields = {field.json_name or lower_camel(field.name) for field in message.field}
        unknown_fields = set(fields) - proto_fields
        require(not unknown_fields, f"{message_name} 存在未知字段 override：{sorted(unknown_fields)}")


def validate_overrides(index: DescriptorIndex, overrides: dict[str, Any]) -> None:
    validate_override_section(index, overrides.get("messages"), "messages")
    validate_override_section(index, overrides.get("output_messages", {}), "output_messages")


def enum_mappings(index: DescriptorIndex) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for full_name in sorted(name for name in index.enums if name.startswith(index.package + ".")):
        enum = index.enums[full_name]
        prefix = enum_prefix(enum)
        proto_to_json = {
            value.name: enum_json_value(value.name, prefix) for value in enum.value
        }
        result[full_name] = {
            "protoToJson": proto_to_json,
            "jsonToProto": {json_value: proto for proto, json_value in proto_to_json.items()},
        }
    return result


def generate() -> dict[str, Any]:
    manifest = load_yaml(MANIFEST_PATH)
    overrides = load_yaml(SCHEMA_POLICY_PATH)
    require(overrides.get("schema_version") == 1, "schema policy version 必须为 1")
    package = overrides.get("package")
    require(isinstance(package, str) and package, "schema policy package 必须是非空字符串")
    tool_name_prefix = overrides.get("tool_name_prefix")
    require(isinstance(tool_name_prefix, str) and re.fullmatch(r"[a-z][a-z0-9_]*_", tool_name_prefix) is not None, "tool_name_prefix 无效")
    capabilities = manifest.get("capabilities")
    require(isinstance(capabilities, list) and capabilities, "Manifest capabilities 必须是非空数组")

    index = DescriptorIndex(compile_descriptor_set(), package)
    validate_proto_projection(index, capabilities)
    validate_overrides(index, overrides)
    tool_errors = load_tool_errors()
    tools: list[dict[str, Any]] = []
    for capability in capabilities:
        input_schema = SchemaBuilder(index, overrides, "input").build(
            capability["request"], inline_root=True
        )
        tools.append(
            {
                "name": f"{tool_name_prefix}{capability['id']}",
                "title": capability["title"],
                "description": capability["description"],
                "capabilityId": capability["id"],
                "requestType": capability["request"],
                "resultType": capability["result"],
                "inputSchema": input_schema,
                "outputSchema": output_schema(
                    index, overrides, capability["result"], tool_errors, capability["id"]
                ),
                "annotations": {
                    "readOnlyHint": capability["side_effect"] == "read_only",
                    "destructiveHint": capability["destructive"],
                    "idempotentHint": capability["side_effect"] == "read_only",
                },
            }
        )

    return {
        "schemaVersion": 1,
        "protocolVersion": manifest["protocol_version"],
        "source": {
            "manifest": "../capabilities/manifest.yaml",
            "proto": "../proto/*.proto",
            "schemaPolicy": "schema-policy.yaml",
            "errorMap": "error-map.yaml",
        },
        "enumMappings": enum_mappings(index),
        "tools": tools,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="生成文件路径")
    parser.add_argument(
        "--check",
        action="store_true",
        help="不写文件；验证目标文件与重新生成结果逐字节一致",
    )
    arguments = parser.parse_args()
    generated = generate()
    tool_count = len(generated["tools"])
    content = json.dumps(generated, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    if arguments.check:
        if not arguments.output.exists() or arguments.output.read_text(encoding="utf-8") != content:
            print(f"MCP Tool Schema 已过期：{arguments.output}", file=sys.stderr)
            return 1
        print(f"mcp_tool_schemas_ok tools={tool_count}")
        return 0
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(content, encoding="utf-8")
    print(f"generated_mcp_tool_schemas tools={tool_count} output={arguments.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
