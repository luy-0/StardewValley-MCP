#!/usr/bin/env python3
"""验证公开 V1 Spec 的机器可读一致性。"""

from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import importlib
import json
import re
import struct
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path
from typing import Any

import yaml
from google.protobuf import descriptor_pb2, json_format
from jsonschema import Draft202012Validator


SPEC = Path(__file__).resolve().parents[1]
PROTO = SPEC / "proto"
MANIFEST_PATH = SPEC / "capabilities" / "manifest.yaml"
ADJUDICATION_PATH = SPEC / "capabilities" / "adjudication.yaml"
ERROR_MAP_PATH = SPEC / "mcp" / "error-map.yaml"
FIXTURE_ROOT = SPEC / "fixtures" / "v1"
PACKAGE = "stardew_valley.mcp.v1"
UUID_RE = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")
REVISION_RE = re.compile(r"^[0-9a-f]{64}$")

LEGACY_CANDIDATES = {
    "say", "emote", "face", "move_to", "go_to", "interact", "use_tool", "equip",
    "open_menu", "menu_click", "menu_close", "query_runtime_snapshot", "query_world_region",
    "query_inventory_snapshot", "query_ui_snapshot", "query_menu", "query_inspect_refs", "go_to_bed",
}


class VerificationError(RuntimeError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise VerificationError(message)


def run(command: list[str], *, cwd: Path | None = None) -> None:
    subprocess.run(command, cwd=cwd, check=True)


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def load_yaml(path: Path) -> Any:
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def lp(value: str | bytes) -> bytes:
    raw = value.encode("utf-8") if isinstance(value, str) else value
    return struct.pack(">I", len(raw)) + raw


def capability_digest(capabilities: list[dict[str, Any]]) -> str:
    side_effect = {"read_only": 1, "mutating": 2}
    execution = {"immediate": 1, "long_running": 2}
    encoded = bytearray()
    for item in sorted(capabilities, key=lambda value: value["id"].encode("utf-8")):
        risks = sorted(item["risk"], key=lambda value: value.encode("utf-8"))
        encoded.extend(lp(item["id"]))
        encoded.extend(lp(item["contract_version"]))
        encoded.extend(bytes([
            side_effect[item["side_effect"]], execution[item["execution"]],
            1 if item["cancellable"] else 0,
        ]))
        encoded.extend(struct.pack(">II", item["default_timeout_ms"], item["max_timeout_ms"]))
        encoded.extend(lp(item["request"]))
        encoded.extend(lp(item["result"]))
        encoded.extend(lp(item["required_scope"]))
        encoded.extend(struct.pack(">I", len(risks)))
        for risk in risks:
            encoded.extend(lp(risk))
        encoded.append(1 if item["destructive"] else 0)
    return hashlib.sha256(encoded).hexdigest()


def verify_json_schemas(manifest: dict[str, Any]) -> None:
    for relative in ["capabilities/manifest.schema.json", "skill/skill.schema.json"]:
        Draft202012Validator.check_schema(load_json(SPEC / relative))
    Draft202012Validator(load_json(SPEC / "capabilities/manifest.schema.json")).validate(manifest)


def generate(tmp: Path) -> tuple[Path, Path, Path]:
    python_out = tmp / "python"
    csharp_out = tmp / "csharp"
    python_out.mkdir()
    csharp_out.mkdir()
    descriptor = tmp / "v1.pb"
    sources = sorted(str(path) for path in PROTO.glob("*.proto"))
    run([
        "protoc", "-I", str(PROTO), f"--descriptor_set_out={descriptor}", "--include_imports",
        f"--python_out={python_out}", f"--csharp_out={csharp_out}", *sources,
    ])
    return descriptor, python_out, csharp_out


def descriptor_index(
    descriptor: Path,
) -> tuple[descriptor_pb2.FileDescriptorSet, dict[str, Any], dict[str, Any]]:
    file_set = descriptor_pb2.FileDescriptorSet.FromString(descriptor.read_bytes())
    messages: dict[str, Any] = {}
    enums: dict[str, Any] = {}
    for file in file_set.file:
        require(file.package == PACKAGE, f"错误 package: {file.name} -> {file.package}")
        for message in file.message_type:
            require(message.name not in messages, f"顶层消息重名: {message.name}")
            messages[message.name] = message
        for enum in file.enum_type:
            require(enum.name not in enums, f"顶层枚举重名: {enum.name}")
            require(bool(enum.value) and enum.value[0].number == 0, f"枚举缺少 0 值: {enum.name}")
            enums[enum.name] = enum
    return file_set, messages, enums


def oneof_fields(message: Any, oneof_name: str) -> dict[str, str]:
    indexes = [i for i, value in enumerate(message.oneof_decl) if value.name == oneof_name]
    require(len(indexes) == 1, f"消息 {message.name} 缺少唯一 oneof {oneof_name}")
    index = indexes[0]
    return {
        field.name: field.type_name.rsplit(".", 1)[-1]
        for field in message.field
        if field.HasField("oneof_index") and field.oneof_index == index
    }


def verify_manifest_against_proto(manifest: dict[str, Any], messages: dict[str, Any]) -> None:
    capabilities = manifest["capabilities"]
    ids = [item["id"] for item in capabilities]
    require(len(ids) == len(set(ids)), "Manifest capability ID 重复")
    require(len(ids) == 15, f"V1 候选能力数量应为 15，实际为 {len(ids)}")
    requests = oneof_fields(messages["CommandRequest"], "operation")
    results = oneof_fields(messages["CapabilityResult"], "result")
    require(set(ids) == set(requests) == set(results), "Manifest、Request、Result 能力集合不一致")
    for item in capabilities:
        require(item["request"] in messages, f"Request 消息不存在: {item['request']}")
        require(item["result"] in messages, f"Result 消息不存在: {item['result']}")
        require(requests[item["id"]] == item["request"], f"Request 分支不一致: {item['id']}")
        require(results[item["id"]] == item["result"], f"Result 分支不一致: {item['id']}")
        require(item["default_timeout_ms"] <= item["max_timeout_ms"], f"Timeout 倒置: {item['id']}")
        expected_scope = "game:read" if item["side_effect"] == "read_only" else "game:write"
        require(item["required_scope"] == expected_scope, f"Scope 与副作用不一致: {item['id']}")


def verify_adjudication(manifest: dict[str, Any]) -> None:
    adjudication = load_yaml(ADJUDICATION_PATH)
    require(adjudication["schema_version"] == 1, "能力裁决 schema_version 错误")
    rows = adjudication["candidates"]
    old_ids = [row["old_id"] for row in rows]
    require(len(old_ids) == len(set(old_ids)), "历史能力裁决存在重复项")
    require(set(old_ids) == LEGACY_CANDIDATES, "18 项历史能力裁决存在遗漏或额外项")
    final_ids = {item["id"] for item in manifest["capabilities"]}
    projected = {row["v1_id"] for row in rows if row["v1_id"] is not None}
    require(projected == final_ids, "历史裁决投影与 V1 Manifest 不一致")
    require(all(row["decision"] != "pending" for row in rows), "存在未裁决历史能力")


def verify_error_map(enums: dict[str, Any]) -> None:
    error_map = load_yaml(ERROR_MAP_PATH)
    require(error_map["source_enum"] == f"{PACKAGE}.ErrorCode", "ErrorMap 源枚举错误")
    enum_names = {value.name for value in enums["ErrorCode"].value}
    mappings = error_map["mappings"]
    require(set(mappings) == enum_names, "ErrorCode 到 MCP Error 映射必须穷尽且无额外项")
    valid_outcomes = {"failed", "unknown"}
    for source, target in mappings.items():
        require(target["outcome"] in valid_outcomes, f"错误 outcome 无效: {source}")
        require(isinstance(target["retryable"], bool), f"retryable 非布尔值: {source}")
        require(re.fullmatch(r"[a-z][a-z0-9_]*", target["tool_code"]) is not None, f"Tool 错误码无效: {source}")
    require(mappings["ERROR_CODE_DEADLINE_EXCEEDED"]["tool_code"] == "command_timeout", "Deadline 映射错误")
    require(mappings["ERROR_CODE_CANCELLED"]["tool_code"] == "command_cancelled", "Cancel 映射错误")
    require(mappings["ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED"]["outcome"] == "unknown", "幂等过期必须投影为 unknown")
    local_errors = error_map.get("local_errors", {})
    require(isinstance(local_errors, dict), "MCP local_errors 必须是对象")
    require("route_unavailable" in local_errors, "MCP 本地路由错误未显式定义")
    for code, policy in local_errors.items():
        require(re.fullmatch(r"[a-z][a-z0-9_]*", code) is not None, f"本地 Tool Error 名称无效: {code}")
        require(policy["outcome"] in valid_outcomes and isinstance(policy["retryable"], bool), f"本地 Tool Error 策略无效: {code}")


def verify_tool_schema_catalog(manifest: dict[str, Any]) -> None:
    catalog = load_json(SPEC / "mcp" / "tool-schemas.json")
    tools = {tool["name"]: tool for tool in catalog["tools"]}
    projection = load_yaml(SPEC / "mcp" / "schema-policy.yaml")
    tool_prefix = projection["tool_name_prefix"]
    expected = {f"{tool_prefix}{item['id']}" for item in manifest["capabilities"]}
    require(set(tools) == expected, "MCP Tool Schema 集合与 Manifest 不一致")
    for name, tool in tools.items():
        Draft202012Validator.check_schema(tool["inputSchema"])
        Draft202012Validator.check_schema(tool["outputSchema"])
        read_only = next(item["side_effect"] == "read_only" for item in manifest["capabilities"] if f"stardew_{item['id']}" == name)
        require(tool["annotations"]["idempotentHint"] == read_only, f"Tool 幂等 Annotation 错误: {name}")

    command_id = "a3e96c3f-5e71-4f43-8053-111111111111"
    invalid_cases = [
        (tools["stardew_say"]["inputSchema"], {"content": "a\0b"}, "say 输入接受 NUL"),
        (tools["stardew_inspect"]["inputSchema"], {"refs": [{"value": "\0"}]}, "Ref 接受 NUL"),
        (tools["stardew_emote"]["outputSchema"], {"status": "succeeded", "commandId": command_id, "output": {"emote": "unspecified"}}, "成功 Emote 接受 UNSPECIFIED"),
        (tools["stardew_say"]["outputSchema"], {"status": "succeeded", "commandId": command_id, "output": {"contentLength": 0}}, "成功 Say 接受零长度"),
        (tools["stardew_say"]["outputSchema"], {"status": "succeeded", "commandId": "a3e96c3f-5e71-1f43-8053-111111111111", "output": {"contentLength": 1}}, "Output 接受非 UUIDv4"),
        (
            tools["stardew_inspect"]["outputSchema"],
            {"status": "succeeded", "commandId": command_id, "output": {"items": [{"resolution": {"ref": {"value": "ref-1"}, "status": "resolved", "kind": "world_entity"}}], "warnings": []}},
            "Resolved Inspect 接受缺失 Fact",
        ),
    ]
    for schema, instance, message in invalid_cases:
        require(not Draft202012Validator(schema).is_valid(instance), message)

    revision = "0" * 64
    valid_inputs = {
        "stardew_say": {"content": "你好"},
        "stardew_emote": {"emote": "happy"},
        "stardew_face": {"direction": "up"},
        "stardew_navigate": {"position": {"locationId": "Farm", "x": 1, "y": 2}, "arrival": "exact"},
        "stardew_interact": {"position": {"locationId": "Farm", "x": 1, "y": 2}},
        "stardew_use_tool": {"position": {"locationId": "Farm", "x": 1, "y": 2}, "chargeLevel": 0},
        "stardew_equip": {"slotIndex": 0},
        "stardew_open_menu": {"menu": "inventory"},
        "stardew_activate_ui": {"elementRef": {"value": "ui-1"}, "uiRevision": revision},
        "stardew_close_menu": {},
        "stardew_query_runtime": {},
        "stardew_query_world": {},
        "stardew_query_inventory": {},
        "stardew_query_ui": {},
        "stardew_inspect": {"refs": [{"value": "ref-1"}]},
    }
    require(set(valid_inputs) == expected, "MCP Input 正例集合不完整")
    for name, instance in valid_inputs.items():
        Draft202012Validator(tools[name]["inputSchema"]).validate(instance)

    failed = {"status": "failed", "commandId": command_id, "error": {"code": "execution_failed", "message": "执行失败", "retryable": False}}
    unknown = {"status": "unknown", "commandId": command_id, "error": {"code": "unknown_outcome", "message": "结果未知", "retryable": False}}
    for name, tool in tools.items():
        validator = Draft202012Validator(tool["outputSchema"])
        validator.validate(failed)
        validator.validate(unknown)

    def synthesize(schema: dict[str, Any], root: dict[str, Any]) -> Any:
        if "$ref" in schema:
            target: Any = root
            for part in schema["$ref"].removeprefix("#/").split("/"):
                target = target[part]
            return synthesize(target, root)
        if "const" in schema:
            return schema["const"]
        if "enum" in schema:
            return schema["enum"][0]
        if "oneOf" in schema:
            return synthesize(schema["oneOf"][0], root)
        value_type = schema.get("type")
        if value_type == "object" or "properties" in schema:
            return {key: synthesize(schema["properties"][key], root) for key in schema.get("required", [])}
        if value_type == "array":
            return [synthesize(schema["items"], root) for _ in range(schema.get("minItems", 0))]
        if value_type == "string":
            pattern = schema.get("pattern", "")
            if pattern == "^[0-9a-f]{64}$":
                return revision
            if "4[0-9a-f]{3}" in pattern:
                return command_id
            if "[0-9]*" in pattern:
                return "0"
            return "x" * max(0, schema.get("minLength", 0))
        if value_type in {"integer", "number"}:
            return max(0, schema.get("minimum", 0))
        if value_type == "boolean":
            return False
        raise VerificationError(f"无法合成 Schema 正例: {schema}")

    for name, tool in tools.items():
        success = synthesize(tool["outputSchema"], tool["outputSchema"])
        Draft202012Validator(tool["outputSchema"]).validate(success)


def verify_action_fixtures() -> None:
    index = load_json(FIXTURE_ROOT / "index.json")
    catalog = load_json(SPEC / "mcp" / "tool-schemas.json")
    tools = {tool["capabilityId"]: tool for tool in catalog["tools"]}
    expected = {
        "say", "emote", "face", "navigate", "interact",
        "use_tool", "equip", "open_menu", "activate_ui", "close_menu",
    }
    paths = index.get("actionFixtures", [])
    require(len(paths) == len(set(paths)), "动作 Fixture 路径重复")
    documents = [load_json(FIXTURE_ROOT / path) for path in paths]
    require({document.get("capability") for document in documents} == expected, "V1 变更能力 Fixture 集合不完整")
    for document in documents:
        capability = document["capability"]
        require(
            set(document) == {"capability", "minimalInput", "fullInput", "invalidInput", "accepted", "succeeded", "failed"},
            f"动作 Fixture 字段不完整: {capability}",
        )
        input_validator = Draft202012Validator(tools[capability]["inputSchema"])
        output_validator = Draft202012Validator(tools[capability]["outputSchema"])
        input_validator.validate(document["minimalInput"])
        input_validator.validate(document["fullInput"])
        require(not input_validator.is_valid(document["invalidInput"]), f"非法动作输入被 Schema 接受: {capability}")
        require(document["accepted"] == {"state": "accepted", "phase": "queued"}, f"动作 ACCEPTED Fixture 无效: {capability}")
        output_validator.validate(document["succeeded"])
        output_validator.validate(document["failed"])
        if capability in {"open_menu", "activate_ui", "close_menu"}:
            transition = document["succeeded"]["output"]["transition"]
            require(
                REVISION_RE.fullmatch(transition["uiRevisionBefore"]) is not None,
                f"动作 Fixture 的 uiRevisionBefore 无效: {capability}",
            )
            require(
                REVISION_RE.fullmatch(transition["uiRevisionAfter"]) is not None,
                f"动作 Fixture 的 uiRevisionAfter 无效: {capability}",
            )
        if capability == "navigate":
            output = document["succeeded"]["output"]
            require(output["final"] == output["resolvedDestination"], "navigate Fixture 未严格到达 resolvedDestination")
            require(
                output["routeLocationIds"][0] == output["start"]["locationId"]
                and output["routeLocationIds"][-1] == output["final"]["locationId"],
                "navigate Fixture 的实际 Location 路线首尾不一致",
            )
        if capability == "interact":
            require(
                document["failed"]["error"]["code"] == "not_ready",
                "interact Fixture 未固定非工具手持物门禁错误",
            )
        if capability == "use_tool":
            output = document["succeeded"]["output"]
            require(output["toolQualifiedItemId"] == "(T)WateringCan", "use_tool Fixture 未使用首版支持工具")
            require(output["chargeLevel"] == document["fullInput"]["chargeLevel"] == 5, "use_tool Fixture 的实际蓄力不一致")
            require(
                document["failed"]["error"]["code"] == "invalid_arguments",
                "use_tool Fixture 未固定不支持工具错误",
            )


def verify_phase5_contract_cases() -> None:
    index = load_json(FIXTURE_ROOT / "index.json")
    path = index.get("phase5ContractCases")
    require(path == "actions/phase5-contract-cases.json", "阶段 5 行为向量路径无效")
    document = load_json(FIXTURE_ROOT / path)
    require(set(document) == {"schemaVersion", "cases"} and document["schemaVersion"] == 1, "阶段 5 行为向量结构无效")
    cases = {case["id"]: case for case in document["cases"]}
    expected = {
        "navigate_character_locked_success",
        "navigate_character_moved",
        "interact_held_non_tool",
        "unfocused_player_action_supported",
        "use_tool_unsupported",
        "use_tool_charge_policy",
        "cancel_after_commit",
    }
    require(set(cases) == expected, "阶段 5 行为向量集合不完整")
    locked = cases["navigate_character_locked_success"]
    require(locked["lockedDestination"] == locked["expected"]["resolvedDestination"], "锁定导航落脚格不一致")
    require(cases["navigate_character_moved"]["expected"] == {"state": "failed", "errorCode": "execution_failed"}, "移动目标错误语义无效")
    require(cases["interact_held_non_tool"]["expected"] == {"state": "failed", "errorCode": "not_ready"}, "交互手持物错误语义无效")
    require(
        cases["unfocused_player_action_supported"]["expected"]
        == {"focusGate": "allowed", "globalInputBridge": False},
        "失焦玩家动作支持语义无效",
    )
    unsupported = cases["use_tool_unsupported"]
    require(len(unsupported["toolKinds"]) == 7 and unsupported["expected"]["errorCode"] == "invalid_arguments", "工具白名单反例无效")
    charges = cases["use_tool_charge_policy"]
    require(all(case["chargeLevel"] > 0 for case in charges["invalidCases"][:3]), "非蓄力工具反例无效")
    require(
        all(case["chargeLevel"] > case["supportedLevel"] for case in charges["invalidCases"][3:]),
        "蓄力工具支持等级反例无效",
    )
    require(charges["expected"] == {"state": "failed", "errorCode": "invalid_arguments"}, "工具蓄力错误语义无效")
    cancelled = cases["cancel_after_commit"]["expected"]
    require(cancelled == {"accepted": False, "errorCode": "conflict", "terminalStateNot": "cancelled"}, "提交后取消语义无效")


def import_generated_python(python_out: Path) -> dict[str, Any]:
    modules: dict[str, Any] = {}
    sys.path.insert(0, str(python_out))
    try:
        for name in ["common", "refs", "facts", "actions", "queries", "capabilities", "transport"]:
            modules[name] = importlib.import_module(f"{name}_pb2")
        return modules
    finally:
        sys.path.pop(0)


def is_uuid_v4(value: str) -> bool:
    if UUID_RE.fullmatch(value) is None:
        return False
    try:
        parsed = uuid.UUID(value)
    except ValueError:
        return False
    return parsed.version == 4 and parsed.variant == uuid.RFC_4122 and str(parsed) == value


def descriptor_dict(descriptor: Any) -> dict[str, Any]:
    side_effect = {1: "read_only", 2: "mutating"}
    execution = {1: "immediate", 2: "long_running"}
    require(descriptor.side_effect in side_effect, f"Descriptor side_effect 无效: {descriptor.id}")
    require(descriptor.execution in execution, f"Descriptor execution 无效: {descriptor.id}")
    return {
        "id": descriptor.id,
        "contract_version": descriptor.contract_version,
        "request": descriptor.request_type,
        "result": descriptor.result_type,
        "side_effect": side_effect[descriptor.side_effect],
        "execution": execution[descriptor.execution],
        "cancellable": descriptor.cancellable,
        "default_timeout_ms": descriptor.default_timeout_ms,
        "max_timeout_ms": descriptor.max_timeout_ms,
        "required_scope": descriptor.required_scope,
        "risk": list(descriptor.risks),
        "destructive": descriptor.destructive,
    }


def verify_event_shape(event: Any, expected_capability: str | None = None) -> None:
    outcome = event.WhichOneof("outcome")
    if event.state == 3:
        require(outcome == "result", "SUCCEEDED 必须携带 Result")
        if expected_capability is not None:
            require(event.result.WhichOneof("result") == expected_capability, "成功 Result 分支与请求不一致")
    elif event.state in {4, 5, 6}:
        require(outcome == "error", "失败终态必须携带 Error")
    else:
        require(outcome is None, "非终态不得携带 Outcome")
    if event.HasField("progress_percent"):
        require(event.progress_percent <= 100, "progress_percent 超过 100")
    if event.state == 5:
        require(event.error.code == 13, "CANCELLED 必须携带 ERROR_CODE_CANCELLED")
    elif event.state == 6:
        require(event.error.code == 12, "TIMED_OUT 必须携带 ERROR_CODE_DEADLINE_EXCEEDED")
    elif event.state == 4:
        allowed_failed = {1, 10, 11, 14, 15, 17, 19}
        require(event.error.code in allowed_failed, "FAILED 使用了非业务终态错误码")


def verify_cancel_response_shape(response: Any) -> None:
    if response.accepted:
        require(response.HasField("current") and not response.HasField("error"), "接受取消时必须有 Current 且不得有 Error")
    else:
        require(response.HasField("error"), "拒绝取消时必须有 Error")
    if response.HasField("current"):
        require(response.current.command_id == response.command_id, "取消响应 Current Command ID 不一致")
        verify_event_shape(response.current)


def verify_status_response_shape(response: Any) -> None:
    require(response.found == response.HasField("current"), "状态响应 found/current 不一致")
    if response.HasField("current"):
        require(response.current.command_id == response.command_id, "状态响应 Current Command ID 不一致")
        verify_event_shape(response.current)


def verify_fixture_semantics(
    frames: list[Any],
    manifest: dict[str, Any],
    digest: str,
    advertised_capabilities: list[dict[str, Any]] | None = None,
) -> None:
    message_ids: set[str] = set()
    frames_by_body: dict[str, list[Any]] = {}
    capability_by_id = {item["id"]: item for item in manifest["capabilities"]}
    command_capability: dict[str, str] = {}
    command_states: dict[str, list[int]] = {}
    handshake_bodies = {"server_hello", "client_hello", "server_ready", "handshake_rejected"}
    request_bodies = {"command_request", "cancel_command_request", "get_command_status_request"}
    response_bodies = {"command_event", "cancel_command_response", "get_command_status_response"}

    for frame in frames:
        require(1 <= len(frame.message_id) <= 64, "message_id 长度非法")
        require(all(0x20 <= ord(char) <= 0x7E for char in frame.message_id), f"message_id 不是可打印 ASCII: {frame.message_id!r}")
        require(frame.message_id not in message_ids, f"Fixture message_id 重复: {frame.message_id}")
        message_ids.add(frame.message_id)
        body = frame.WhichOneof("body")
        require(body is not None, f"Fixture 缺少 body: {frame.message_id}")
        frames_by_body.setdefault(body, []).append(frame)
        handshake = body in handshake_bodies
        require(frame.HasField("fence") != handshake, f"Fence presence 错误: {body}")
        if body in request_bodies:
            require(not frame.reply_to, f"请求帧不得填写 reply_to: {body}")
        if body in response_bodies and frame.reply_to:
            require(frame.reply_to in message_ids, f"reply_to 尚未出现: {frame.reply_to}")
        if not handshake:
            require(frame.fence.capability_digest == digest, f"Fence Digest 错误: {frame.message_id}")
            require(frame.fence.lease_epoch == 7, f"Fence Lease 错误: {frame.message_id}")
        if body == "command_request":
            request = frame.command_request
            require(is_uuid_v4(request.command_id), f"Command ID 不是规范 UUIDv4: {request.command_id}")
            capability_id = request.WhichOneof("operation")
            require(capability_id in capability_by_id, f"未知能力 Fixture: {capability_id}")
            require(request.command_id not in command_capability, f"重复 CommandRequest: {request.command_id}")
            command_capability[request.command_id] = capability_id
            spec = capability_by_id[capability_id]
            require(request.timeout_ms == 0 or request.timeout_ms <= spec["max_timeout_ms"], f"超出最大 Timeout: {capability_id}")
        if body == "command_event":
            event = frame.command_event
            require(event.command_id in command_capability, f"Event 引用了未知命令: {event.command_id}")
            command_states.setdefault(event.command_id, []).append(event.state)
            verify_event_shape(event, command_capability[event.command_id])
        if body in {"cancel_command_request", "get_command_status_request"}:
            command_id = getattr(frame, body).command_id
            require(command_id in command_capability, f"控制请求引用未知命令: {command_id}")
        if body == "cancel_command_response":
            response = frame.cancel_command_response
            require(response.command_id in command_capability, "取消响应引用未知命令")
            verify_cancel_response_shape(response)
        if body == "get_command_status_response":
            response = frame.get_command_status_response
            require(response.command_id in command_capability, "状态响应引用未知命令")
            verify_status_response_shape(response)

    legal = {(1, 2), (1, 3), (1, 4), (1, 5), (1, 6), (2, 3), (2, 4), (2, 5), (2, 6)}
    terminal = {3, 4, 5, 6}
    for command_id, states in command_states.items():
        for before, after in zip(states, states[1:]):
            require(before not in terminal and (before, after) in legal, f"非法状态转换: {command_id} {before}->{after}")

    hello = frames_by_body["server_hello"][0].server_hello
    client = frames_by_body["client_hello"][0].client_hello
    ready = frames_by_body["server_ready"][0].server_ready
    require(is_uuid_v4(hello.mod_instance_id), "mod_instance_id 不是规范 UUIDv4")
    require(is_uuid_v4(client.client_instance_id), "client_instance_id 不是规范 UUIDv4")
    require(is_uuid_v4(ready.session_id), "session_id 不是规范 UUIDv4")
    require(len(hello.server_nonce) == 32 and len(client.client_nonce) == 32, "握手 Nonce 必须为 32 字节")
    require((hello.version.major, hello.version.minor) == (1, 0), "ServerHello 版本错误")
    require((client.requested_version.major, client.requested_version.minor) == (1, 0), "ClientHello 版本错误")
    require((ready.selected_version.major, ready.selected_version.minor) == (1, 0), "ServerReady 版本错误")
    server_hello_frame = frames_by_body["server_hello"][0]
    client_hello_frame = frames_by_body["client_hello"][0]
    server_ready_frame = frames_by_body["server_ready"][0]
    require(not server_hello_frame.reply_to, "ServerHello reply_to 必须为空")
    require(client_hello_frame.reply_to == server_hello_frame.message_id, "ClientHello reply_to 不匹配 ServerHello")
    require(server_ready_frame.reply_to == client_hello_frame.message_id, "ServerReady reply_to 不匹配 ClientHello")
    require(ready.capability_snapshot.digest == digest, "ServerReady 声明 Digest 错误")
    require(ready.result_retention_ms >= 300_000, "Result Retention 低于 V1 下限")
    require(ready.reconnect_grace_ms >= 10_000, "Reconnect Grace 低于 V1 下限")
    descriptors = list(ready.capability_snapshot.capabilities)
    ids = [value.id for value in descriptors]
    require(ids == sorted(ids, key=lambda value: value.encode("utf-8")), "Descriptor 未按 ID 规范排序")
    require(len(ids) == len(set(ids)), "Descriptor ID 重复")
    projected = [descriptor_dict(value) for value in descriptors]
    require(capability_digest(projected) == digest, "收到的 Descriptor 重新计算 Digest 不一致")
    descriptor_fields = {
        "id", "contract_version", "request", "result", "side_effect", "execution", "cancellable",
        "default_timeout_ms", "max_timeout_ms", "required_scope", "risk", "destructive",
    }
    expected_source = manifest["capabilities"] if advertised_capabilities is None else advertised_capabilities
    expected = {
        item["id"]: {key: value for key, value in item.items() if key in descriptor_fields}
        for item in expected_source
    }
    for item in expected.values():
        item["risk"] = sorted(item["risk"], key=lambda value: value.encode("utf-8"))
    require({item["id"]: item for item in projected} == expected, "Descriptor 完整语义与 Manifest 不一致")
    for value in descriptors:
        require(list(value.risks) == sorted(set(value.risks), key=lambda risk: risk.encode("utf-8")), f"Risk 未规范排序或重复: {value.id}")
    for frame in frames:
        if frame.HasField("fence"):
            require(frame.fence.session_id == ready.session_id, f"Fence Session 不一致: {frame.message_id}")


def load_fixture_frames(transport_pb2: Any, entries: list[dict[str, Any]]) -> tuple[list[Any], list[Path]]:
    paths: list[Path] = []
    frames = []
    for entry in entries:
        require(entry["message"] == "TransportFrame", "未知 Fixture 消息类型")
        path = FIXTURE_ROOT / entry["path"]
        require(path.is_file(), f"Fixture 不存在: {path}")
        message = transport_pb2.TransportFrame()
        json_format.ParseDict(load_json(path), message, ignore_unknown_fields=False)
        body = message.WhichOneof("body")
        expected_sender = {
            "server_hello": "mod", "server_ready": "mod", "handshake_rejected": "mod",
            "command_event": "mod", "cancel_command_response": "mod", "get_command_status_response": "mod",
            "client_hello": "mcp", "command_request": "mcp", "cancel_command_request": "mcp",
            "get_command_status_request": "mcp",
        }.get(body)
        require(entry.get("sender") == expected_sender, f"Fixture sender 与 Body 方向不一致: {path}")
        binary = message.SerializeToString(deterministic=True)
        reparsed = transport_pb2.TransportFrame.FromString(binary)
        require(message == reparsed, f"Proto 往返不一致: {path}")
        frames.append(message)
        paths.append(path)
    return frames, paths


def verify_fixtures(transport_pb2: Any, manifest: dict[str, Any], digest: str) -> list[Path]:
    index = load_json(FIXTURE_ROOT / "index.json")
    require(index["schemaVersion"] == 1, "Fixture index schemaVersion 错误")
    frames, paths = load_fixture_frames(transport_pb2, index["protoJson"])
    verify_fixture_semantics(frames, manifest, digest)
    return paths


def verify_lifecycle_fixtures(transport_pb2: Any, index: dict[str, Any], top_level_frames: list[Any]) -> list[Path]:
    """独立验证互斥的生命周期向量，不把它们误当成同一事件流。"""
    entries = index.get("lifecycleFixtures")
    require(isinstance(entries, list) and len(entries) == 4, "lifecycleFixtures 必须恰含四个向量")

    top_by_id = {frame.message_id: frame for frame in top_level_frames}
    navigate_request = top_by_id.get("cmd-msg-2")
    status_request = top_by_id.get("status-msg-1")
    require(navigate_request is not None and navigate_request.WhichOneof("body") == "command_request", "缺少 navigate 请求 fixture")
    require(status_request is not None and status_request.WhichOneof("body") == "get_command_status_request", "缺少 navigate 状态请求 fixture")
    command_id = navigate_request.command_request.command_id
    require(status_request.get_command_status_request.command_id == command_id, "navigate 状态请求 Command ID 不一致")

    expected_paths = {
        "commands/navigate.running.json",
        "commands/navigate.cancelled.json",
        "commands/navigate.timed-out.json",
        "commands/navigate.status-expired.json",
    }
    require({entry.get("path") for entry in entries} == expected_paths, "lifecycleFixtures 路径集合错误")

    seen_message_ids = set(top_by_id)
    frames_by_path: dict[str, Any] = {}
    paths: list[Path] = []
    for entry in entries:
        require(entry.get("message") == "TransportFrame", "lifecycle fixture 消息类型错误")
        require(entry.get("sender") == "mod", "lifecycle fixture 发送方必须为 mod")
        path = FIXTURE_ROOT / entry["path"]
        require(path.is_file(), f"lifecycle fixture 不存在: {path}")
        frame = transport_pb2.TransportFrame()
        json_format.ParseDict(load_json(path), frame, ignore_unknown_fields=False)
        require(frame.WhichOneof("body") is not None, f"lifecycle fixture 缺少 body: {path}")
        require(frame.HasField("fence"), f"lifecycle fixture 缺少 Fence: {path}")
        require(frame.fence == navigate_request.fence, f"lifecycle fixture Fence 不一致: {path}")
        require(1 <= len(frame.message_id) <= 64, f"lifecycle fixture message_id 长度非法: {path}")
        require(all(0x20 <= ord(char) <= 0x7E for char in frame.message_id), f"lifecycle fixture message_id 不是可打印 ASCII: {path}")
        require(frame.message_id not in seen_message_ids, f"lifecycle fixture message_id 重复: {frame.message_id}")
        seen_message_ids.add(frame.message_id)
        binary = frame.SerializeToString(deterministic=True)
        require(frame == transport_pb2.TransportFrame.FromString(binary), f"lifecycle fixture Proto 往返不一致: {path}")
        frames_by_path[entry["path"]] = frame
        paths.append(path)

    running = frames_by_path["commands/navigate.running.json"]
    require(running.WhichOneof("body") == "command_event", "running fixture 必须为 CommandEvent")
    require(running.reply_to == navigate_request.message_id, "RUNNING 必须直接关联 navigate 请求")
    require(running.command_event.command_id == command_id, "RUNNING Command ID 不一致")
    require(running.command_event.state == 2 and running.command_event.phase == "walking", "RUNNING state/phase 错误")
    require(running.command_event.WhichOneof("outcome") is None, "RUNNING 不得携带 Outcome")
    verify_event_shape(running.command_event, "navigate")

    cancelled = frames_by_path["commands/navigate.cancelled.json"]
    require(cancelled.WhichOneof("body") == "command_event", "cancelled fixture 必须为 CommandEvent")
    require(not cancelled.reply_to, "CANCELLED 终态事件必须作为主动事件发送")
    require(cancelled.command_event.command_id == command_id, "CANCELLED Command ID 不一致")
    require(cancelled.command_event.state == 5, "CANCELLED state 错误")
    require(cancelled.command_event.WhichOneof("outcome") == "error", "CANCELLED 必须仅携带 Error")
    require(cancelled.command_event.error.code == 13, "CANCELLED 必须使用 ERROR_CODE_CANCELLED")
    verify_event_shape(cancelled.command_event, "navigate")

    timed_out = frames_by_path["commands/navigate.timed-out.json"]
    require(timed_out.WhichOneof("body") == "command_event", "timed-out fixture 必须为 CommandEvent")
    require(not timed_out.reply_to, "TIMED_OUT 终态事件必须作为主动事件发送")
    require(timed_out.command_event.command_id == command_id, "TIMED_OUT Command ID 不一致")
    require(timed_out.command_event.state == 6, "TIMED_OUT state 错误")
    require(timed_out.command_event.WhichOneof("outcome") == "error", "TIMED_OUT 必须仅携带 Error")
    require(timed_out.command_event.error.code == 12, "TIMED_OUT 必须使用 ERROR_CODE_DEADLINE_EXCEEDED")
    verify_event_shape(timed_out.command_event, "navigate")

    expired = frames_by_path["commands/navigate.status-expired.json"]
    require(expired.WhichOneof("body") == "protocol_error", "status-expired fixture 必须为 ProtocolError")
    require(expired.reply_to == status_request.message_id, "过期 tombstone 必须关联 status request")
    require(expired.protocol_error.error.code == 16, "过期 tombstone 必须使用 ERROR_CODE_IDEMPOTENCY_RECORD_EXPIRED")
    return paths


def client_auth_data(vector: dict[str, Any]) -> bytes:
    return b"".join([
        lp("stardew-valley-mcp/v1/client-auth"), lp(vector["modInstanceId"]), lp(vector["clientInstanceId"]),
        lp(base64.b64decode(vector["serverNonceBase64"])), lp(base64.b64decode(vector["clientNonceBase64"])),
        struct.pack(">II", vector["requestedMajor"], vector["requestedMinor"]), lp(vector["resumeSessionId"]),
    ])


def server_auth_data(vector: dict[str, Any]) -> bytes:
    return b"".join([
        lp("stardew-valley-mcp/v1/server-auth"), lp(vector["modInstanceId"]), lp(vector["clientInstanceId"]),
        lp(base64.b64decode(vector["serverNonceBase64"])), lp(base64.b64decode(vector["clientNonceBase64"])),
        struct.pack(">II", vector["selectedMajor"], vector["selectedMinor"]), lp(vector["sessionId"]),
        struct.pack(">Q", vector["leaseEpoch"]), lp(vector["capabilityDigest"]),
        struct.pack(">II", vector["resultRetentionMs"], vector["reconnectGraceMs"]),
    ])


def mutate_vector(vector: dict[str, Any], key: str) -> dict[str, Any]:
    mutated = dict(vector)
    if key.endswith("NonceBase64"):
        raw = bytearray(base64.b64decode(mutated[key]))
        raw[0] ^= 1
        mutated[key] = base64.b64encode(raw).decode("ascii")
    elif isinstance(mutated[key], int):
        mutated[key] += 1
    else:
        mutated[key] = mutated[key] + "x"
    return mutated


def verify_auth_vector(
    digest: str,
    frames: list[Any],
    vector_path: Path | None = None,
    vector: dict[str, Any] | None = None,
) -> dict[str, Any]:
    vector = vector if vector is not None else load_json(vector_path or FIXTURE_ROOT / "auth" / "hmac-sha256.json")
    require(vector["capabilityDigest"] == digest, "Auth Vector Digest 过期")
    secret = base64.b64decode(vector["secretBase64"], validate=True)
    require(len(secret) >= 32, "测试共享秘密短于 32 字节")
    require(len(base64.b64decode(vector["serverNonceBase64"], validate=True)) == 32, "Server Nonce 长度错误")
    require(len(base64.b64decode(vector["clientNonceBase64"], validate=True)) == 32, "Client Nonce 长度错误")
    client_tag = hmac.new(secret, client_auth_data(vector), hashlib.sha256).digest()
    server_tag = hmac.new(secret, server_auth_data(vector), hashlib.sha256).digest()
    require(hmac.compare_digest(client_tag, base64.b64decode(vector["clientAuthTagBase64"])), "Client HMAC 不一致")
    require(hmac.compare_digest(server_tag, base64.b64decode(vector["serverAuthTagBase64"])), "Server HMAC 不一致")
    client_fields = ["modInstanceId", "clientInstanceId", "serverNonceBase64", "clientNonceBase64", "requestedMajor", "requestedMinor", "resumeSessionId"]
    server_fields = ["modInstanceId", "clientInstanceId", "serverNonceBase64", "clientNonceBase64", "selectedMajor", "selectedMinor", "sessionId", "leaseEpoch", "capabilityDigest", "resultRetentionMs", "reconnectGraceMs"]
    for key in client_fields:
        candidate = hmac.new(secret, client_auth_data(mutate_vector(vector, key)), hashlib.sha256).digest()
        require(not hmac.compare_digest(candidate, client_tag), f"Client HMAC 未覆盖: {key}")
    for key in server_fields:
        candidate = hmac.new(secret, server_auth_data(mutate_vector(vector, key)), hashlib.sha256).digest()
        require(not hmac.compare_digest(candidate, server_tag), f"Server HMAC 未覆盖: {key}")

    by_body = {frame.WhichOneof("body"): frame for frame in frames if frame.WhichOneof("body") in {"server_hello", "client_hello", "server_ready"}}
    hello, client, ready = by_body["server_hello"].server_hello, by_body["client_hello"].client_hello, by_body["server_ready"].server_ready
    require(hello.mod_instance_id == vector["modInstanceId"], "ServerHello 与 Auth Vector Mod ID 不一致")
    require(client.client_instance_id == vector["clientInstanceId"], "ClientHello 与 Auth Vector Client ID 不一致")
    require(base64.b64encode(hello.server_nonce).decode() == vector["serverNonceBase64"], "Server Nonce Fixture 不一致")
    require(base64.b64encode(client.client_nonce).decode() == vector["clientNonceBase64"], "Client Nonce Fixture 不一致")
    require(client.auth_tag == client_tag, "ClientHello Tag 与 Vector 不一致")
    require(ready.auth_tag == server_tag, "ServerReady Tag 与 Vector 不一致")
    require(ready.session_id == vector["sessionId"] and ready.lease_epoch == vector["leaseEpoch"], "ServerReady Session 与 Vector 不一致")
    require(ready.result_retention_ms == vector["resultRetentionMs"] and ready.reconnect_grace_ms == vector["reconnectGraceMs"], "ServerReady 保留期与 Vector 不一致")

    downgrade = load_json(FIXTURE_ROOT / "auth" / "hmac-minor-downgrade.synthetic.json")
    require((downgrade["requestedMajor"], downgrade["requestedMinor"]) == (1, 1), "合成降级向量请求版本错误")
    require((downgrade["selectedMajor"], downgrade["selectedMinor"]) == (1, 0), "合成降级向量选择版本错误")
    downgrade_client = hmac.new(secret, client_auth_data(downgrade), hashlib.sha256).digest()
    downgrade_server = hmac.new(secret, server_auth_data(downgrade), hashlib.sha256).digest()
    require(hmac.compare_digest(downgrade_client, base64.b64decode(downgrade["clientAuthTagBase64"])), "合成降级 Client HMAC 不一致")
    require(hmac.compare_digest(downgrade_server, base64.b64decode(downgrade["serverAuthTagBase64"])), "合成降级 Server HMAC 不一致")
    return vector


def verify_bootstrap_fixtures(transport_pb2: Any, manifest: dict[str, Any]) -> tuple[list[Path], dict[str, Any], str]:
    index = load_json(FIXTURE_ROOT / "index.json")
    profile = index.get("profiles", {}).get("bootstrap")
    require(isinstance(profile, dict), "Fixture index 缺少 bootstrap profile")
    bootstrap_capability = next(
        (item for item in manifest["capabilities"] if item["id"] == "query_runtime"), None,
    )
    require(bootstrap_capability is not None, "Manifest 缺少 query_runtime")
    bootstrap_digest = capability_digest([bootstrap_capability])
    vectors = profile.get("authVectors")
    require(isinstance(vectors, list) and len(vectors) == 1, "bootstrap 必须声明唯一 HMAC 向量")
    vector_entry = vectors[0]
    require(vector_entry == {"path": "bootstrap/hmac-sha256.json", "algorithm": "v1_hmac_sha256"}, "bootstrap HMAC 向量声明错误")
    vector_path = FIXTURE_ROOT / vector_entry["path"]
    require(vector_path.is_file(), f"bootstrap HMAC Fixture 不存在: {vector_path}")

    scenarios = profile.get("scenarios")
    require(isinstance(scenarios, list) and len(scenarios) == 2, "bootstrap 必须包含成功与 NOT_READY 两个场景")
    expected_terminal = {
        "query-runtime-succeeded": 3,
        "query-runtime-not-ready": 4,
    }

    def verify_scenario_terminal(scenario_id: str, declared_terminal: Any, events: list[Any]) -> None:
        expected = expected_terminal[scenario_id]
        require([event.state for event in events] == [1, expected], f"bootstrap 状态序列错误: {scenario_id}")
        require(declared_terminal == {3: "COMMAND_STATE_SUCCEEDED", 4: "COMMAND_STATE_FAILED"}[expected], f"bootstrap 终态声明错误: {scenario_id}")
        if expected == 3:
            require(events[-1].WhichOneof("outcome") == "result", "bootstrap 成功终态必须只有 Result")
        else:
            require(events[-1].WhichOneof("outcome") == "error", "bootstrap NOT_READY 必须只有 Error")
            require(events[-1].error.code == 10, "bootstrap FAILED 必须使用 ERROR_CODE_NOT_READY")

    success_paths: list[Path] | None = None
    success_frames: list[Any] | None = None
    not_ready_paths: list[Path] | None = None
    not_ready_frames: list[Any] | None = None
    for scenario in scenarios:
        scenario_id = scenario.get("id")
        require(scenario_id in expected_terminal, f"未知 bootstrap 场景: {scenario_id}")
        frames, paths = load_fixture_frames(transport_pb2, scenario["protoJson"])
        verify_fixture_semantics(frames, manifest, bootstrap_digest, [bootstrap_capability])
        events = [frame.command_event for frame in frames if frame.WhichOneof("body") == "command_event"]
        verify_scenario_terminal(scenario_id, scenario.get("expectedTerminalState"), events)
        if scenario_id == "query-runtime-succeeded":
            success_paths, success_frames = paths, frames
        else:
            not_ready_paths = paths
            not_ready_frames = frames

    require(success_paths is not None and success_frames is not None and not_ready_paths is not None and not_ready_frames is not None, "bootstrap 场景不完整")
    vector = verify_auth_vector(bootstrap_digest, success_frames, vector_path)

    def rejected(check: Any, label: str) -> None:
        try:
            check()
        except VerificationError:
            return
        raise VerificationError(f"bootstrap 篡改未被拒绝: {label}")

    def clone_frames(source: list[Any]) -> list[Any]:
        cloned: list[Any] = []
        for frame in source:
            copy = type(frame)()
            copy.CopyFrom(frame)
            cloned.append(copy)
        return cloned

    changed_descriptor = clone_frames(success_frames)
    changed_descriptor[2].server_ready.capability_snapshot.capabilities[0].default_timeout_ms += 1
    rejected(lambda: verify_fixture_semantics(changed_descriptor, manifest, bootstrap_digest, [bootstrap_capability]), "singleton Descriptor")
    changed_digest = clone_frames(success_frames)
    changed_digest[2].server_ready.capability_snapshot.digest = "0" * 64
    rejected(lambda: verify_fixture_semantics(changed_digest, manifest, bootstrap_digest, [bootstrap_capability]), "Snapshot digest")
    changed_fence = clone_frames(success_frames)
    changed_fence[3].fence.capability_digest = "0" * 64
    rejected(lambda: verify_fixture_semantics(changed_fence, manifest, bootstrap_digest, [bootstrap_capability]), "Fence digest")
    changed_hmac = dict(vector)
    changed_hmac["serverAuthTagBase64"] = "A" * 44
    rejected(lambda: verify_auth_vector(bootstrap_digest, success_frames, vector=changed_hmac), "HMAC")
    changed_not_ready = clone_frames(not_ready_frames)
    changed_not_ready[-1].command_event.error.code = 11
    rejected(lambda: verify_scenario_terminal("query-runtime-not-ready", "COMMAND_STATE_FAILED", [frame.command_event for frame in changed_not_ready if frame.WhichOneof("body") == "command_event"]), "FAILED/NOT_READY 状态")
    return [*success_paths, not_ready_paths[-1]], vector, bootstrap_digest


def verify_observation_fixtures(transport_pb2: Any, manifest: dict[str, Any]) -> tuple[list[Path], dict[str, Any], str]:
    index = load_json(FIXTURE_ROOT / "index.json")
    profile = index.get("profiles", {}).get("observation")
    require(isinstance(profile, dict), "Fixture index 缺少 observation profile")
    observation_ids = ["inspect", "query_inventory", "query_runtime", "query_ui", "query_world"]
    capabilities = [item for item in manifest["capabilities"] if item["id"] in observation_ids]
    require([item["id"] for item in sorted(capabilities, key=lambda item: item["id"].encode())] == observation_ids, "observation capability 集合错误")
    digest = capability_digest(capabilities)
    vectors = profile.get("authVectors")
    require(vectors == [{"path": "observation/hmac-sha256.json", "algorithm": "v1_hmac_sha256"}], "observation HMAC 向量声明错误")
    scenarios = profile.get("scenarios")
    require(isinstance(scenarios, list) and len(scenarios) == 10, "observation 必须有五项成功和失败场景")
    seen: set[str] = set()
    all_paths: list[Path] = []
    success_frames: list[Any] | None = None
    inspect_success_frames: list[Any] | None = None
    for scenario in scenarios:
        scenario_id = scenario.get("id")
        require(isinstance(scenario_id, str) and scenario_id not in seen, "observation scenario ID 重复或非法")
        seen.add(scenario_id)
        frames, paths = load_fixture_frames(transport_pb2, scenario["protoJson"])
        verify_fixture_semantics(frames, manifest, digest, capabilities)
        events = [frame.command_event for frame in frames if frame.WhichOneof("body") == "command_event"]
        require([event.state for event in events] == [1, 3 if scenario_id.endswith("-succeeded") else 4], f"observation 状态序列错误: {scenario_id}")
        require(scenario.get("expectedTerminalState") == ("COMMAND_STATE_SUCCEEDED" if scenario_id.endswith("-succeeded") else "COMMAND_STATE_FAILED"), f"observation 终态声明错误: {scenario_id}")
        all_paths.extend(paths)
        if scenario_id == "query-world-succeeded":
            success_frames = frames
        elif scenario_id == "inspect-succeeded":
            inspect_success_frames = frames
    require(success_frames is not None, "observation 缺少 query-world 成功场景")
    require(inspect_success_frames is not None, "observation 缺少 inspect 成功场景")
    inspect_request = next(
        frame.command_request.inspect
        for frame in inspect_success_frames
        if frame.WhichOneof("body") == "command_request"
    )
    inspect_result = next(
        frame.command_event.result.inspect
        for frame in inspect_success_frames
        if frame.WhichOneof("body") == "command_event" and frame.command_event.state == 3
    )
    require(
        [reference.value for reference in inspect_request.refs]
        == [item.resolution.ref.value for item in inspect_result.items],
        "Inspect Fixture 未保持请求顺序或等长",
    )
    require(
        [item.WhichOneof("fact") for item in inspect_result.items if item.resolution.status == 1]
        == ["world_entity", "character", "inventory_item", "inventory", "ui_element"],
        "Inspect Fixture 未覆盖五种 resolved Fact",
    )
    unavailable = inspect_result.items[5]
    require(unavailable.resolution.status == 5, "Inspect Fixture 缺少 FACT_UNAVAILABLE")
    require(unavailable.resolution.kind == 3, "FACT_UNAVAILABLE 必须保留已知 Kind")
    require(unavailable.WhichOneof("fact") is None, "FACT_UNAVAILABLE 不得携带 Fact")
    require(
        unavailable.resolution.error.code == 19
        and unavailable.resolution.error.message == "当前 Ref 事实不可用",
        "FACT_UNAVAILABLE Error 不符合固定契约",
    )
    require(
        inspect_result.items[-1].resolution.status == 1
        and inspect_result.items[-1].WhichOneof("fact") == "ui_element",
        "FACT_UNAVAILABLE 后续项未继续 resolved",
    )
    vector_path = FIXTURE_ROOT / "observation" / "hmac-sha256.json"
    vector = verify_auth_vector(digest, success_frames, vector_path)
    standalone = [
        "query-world.success-minimal.json", "query-world.success-complete.json",
        "query-inventory.success-minimal.json", "query-inventory.success-complete.json",
        "query-ui.success-no-menu.json", "query-ui.success-menu.json",
        "query-ui.success-unsupported-menu.json",
        "inspect.success-minimal.json", "inspect.success-complete.json",
    ]
    for name in standalone:
        frame = transport_pb2.TransportFrame()
        json_format.ParseDict(load_json(FIXTURE_ROOT / "observation" / name), frame, ignore_unknown_fields=False)
        require(frame.command_event.state == 3, f"observation 最小/完整 Fixture 非成功: {name}")
        verify_event_shape(frame.command_event)
    invalid = load_json(FIXTURE_ROOT / "observation" / "invalid-inputs.json")
    require(invalid.get("schemaVersion") == 1 and len(invalid.get("cases", [])) >= 5, "observation invalid-inputs 覆盖不足")
    require({item["capability"] for item in invalid["cases"]} == {"query_world", "query_inventory", "query_ui", "inspect"}, "observation invalid-inputs 能力覆盖错误")
    return all_paths, vector, digest


def verify_framing_model(transport_pb2: Any) -> None:
    frame = transport_pb2.TransportFrame(message_id="frame-smoke", ping=transport_pb2.Ping(sequence=7))
    payload = frame.SerializeToString(deterministic=True)
    wire = struct.pack(">I", len(payload)) + payload

    def decode(data: bytes) -> list[bytes]:
        values: list[bytes] = []
        offset = 0
        while offset < len(data):
            require(len(data) - offset >= 4, "EOF 位于长度头中")
            size = struct.unpack_from(">I", data, offset)[0]
            require(1 <= size <= 1_048_576, f"非法帧长: {size}")
            offset += 4
            require(len(data) - offset >= size, "EOF 位于 Payload 中")
            values.append(data[offset:offset + size])
            offset += size
        return values

    require(decode(wire + wire) == [payload, payload], "粘包拆帧失败")
    for invalid in [struct.pack(">I", 0), struct.pack(">I", 1_048_577), wire[:2], wire[:-1]]:
        try:
            decode(invalid)
        except VerificationError:
            pass
        else:
            raise VerificationError("非法长度或短读未被拒绝")


def verify_negative_message_models(modules: dict[str, Any]) -> None:
    capability_pb2 = modules["capabilities"]
    common_pb2 = modules["common"]
    command_id = "b4fa7d40-6f82-4054-9164-222222222222"

    def rejected(check: Any, label: str) -> None:
        try:
            check()
        except VerificationError:
            return
        raise VerificationError(f"负例未被拒绝: {label}")

    rejected(lambda: verify_cancel_response_shape(capability_pb2.CancelCommandResponse(command_id=command_id, accepted=False)), "拒绝取消但无 Error")
    invalid_accepted = capability_pb2.CancelCommandResponse(command_id=command_id, accepted=True)
    invalid_accepted.current.command_id = command_id
    invalid_accepted.current.state = 2
    invalid_accepted.error.code = 9
    rejected(lambda: verify_cancel_response_shape(invalid_accepted), "接受取消同时携带 Error")
    rejected(lambda: verify_status_response_shape(capability_pb2.GetCommandStatusResponse(command_id=command_id, found=True)), "found=true 但无 Current")
    invalid_not_found = capability_pb2.GetCommandStatusResponse(command_id=command_id, found=False)
    invalid_not_found.current.command_id = command_id
    invalid_not_found.current.state = 2
    rejected(lambda: verify_status_response_shape(invalid_not_found), "found=false 但携带 Current")

    cancelled = capability_pb2.CommandEvent(command_id=command_id, state=5)
    cancelled.error.CopyFrom(common_pb2.Error(code=12, message="错误码错配"))
    rejected(lambda: verify_event_shape(cancelled), "CANCELLED 携带 Deadline Error")
    timed_out = capability_pb2.CommandEvent(command_id=command_id, state=6)
    timed_out.error.CopyFrom(common_pb2.Error(code=13, message="错误码错配"))
    rejected(lambda: verify_event_shape(timed_out), "TIMED_OUT 携带 Cancel Error")
    failed = capability_pb2.CommandEvent(command_id=command_id, state=4)
    failed.error.CopyFrom(common_pb2.Error(code=13, message="错误码错配"))
    rejected(lambda: verify_event_shape(failed), "FAILED 携带 Cancel Error")
    failed_unspecified = capability_pb2.CommandEvent(command_id=command_id, state=4)
    failed_unspecified.error.CopyFrom(common_pb2.Error(code=0, message="错误码错配"))
    rejected(lambda: verify_event_shape(failed_unspecified), "FAILED 携带 UNSPECIFIED Error")
    failed_expired = capability_pb2.CommandEvent(command_id=command_id, state=4)
    failed_expired.error.CopyFrom(common_pb2.Error(code=16, message="错误码错配"))
    rejected(lambda: verify_event_shape(failed_expired), "FAILED 携带幂等过期 Error")

    # 阶段 0 的状态规则模型：实现期 Harness 必须用真实连接和 Registry 重跑同一判决表。
    def can_resume(bound_session: str, bound_client: str, requested_session: str, client: str, in_grace: bool, authenticated: bool) -> bool:
        return in_grace and authenticated and bound_session == requested_session and bound_client == client

    require(can_resume("session-a", "client-a", "session-a", "client-a", True, True), "合法 Resume 被拒绝")
    require(not can_resume("session-a", "client-a", "session-a", "client-b", True, True), "Resume 未绑定原 Client ID")
    require(not can_resume("session-a", "client-a", "session-a", "client-a", False, True), "Grace 过期仍允许 Resume")
    require(not can_resume("session-a", "client-a", "session-a", "client-a", True, False), "未认证 Resume 被接受")

    def fence_valid(session: str, epoch: int, digest: str) -> bool:
        return (session, epoch, digest) == ("session-a", 7, "digest-a")

    require(fence_valid("session-a", 7, "digest-a"), "当前 Fence 被拒绝")
    require(not fence_valid("session-a", 6, "digest-a"), "旧 Lease Fence 被接受")
    require(not fence_valid("session-a", 7, "digest-b"), "旧 Capability Digest 被接受")

    def duplicate_resolution(record_state: str, same_request: bool) -> str:
        if record_state == "none":
            return "new"
        if not same_request:
            return "conflict"
        if record_state in {"active", "result"}:
            return "cached"
        if record_state == "tombstone":
            return "idempotency_record_expired"
        raise VerificationError(f"未知幂等记录状态: {record_state}")

    require(duplicate_resolution("none", True) == "new", "新 Command ID 未被接受")
    require(duplicate_resolution("active", True) == "cached", "活动命令重复提交未复用状态")
    require(duplicate_resolution("result", True) == "cached", "保留期内重复命令未复用结果")
    require(duplicate_resolution("tombstone", True) == "idempotency_record_expired", "Tombstone 命中后允许重新执行")
    require(duplicate_resolution("active", False) == "conflict", "活动命令同 ID 不同请求未冲突")
    require(duplicate_resolution("result", False) == "conflict", "终态命令同 ID 不同请求未冲突")
    require(duplicate_resolution("tombstone", False) == "conflict", "Tombstone 同 ID 不同请求未冲突")


def verify_csharp(
    csharp_out: Path,
    fixture_paths: list[Path],
    vector: dict[str, Any],
    python_modules: dict[str, Any],
    digest: str,
    tmp: Path,
    ready_path: Path,
    vector_path: Path,
    label: str,
) -> None:
    harness = tmp / f"csharp-harness-{label}"
    harness.mkdir()
    for source in csharp_out.glob("*.cs"):
        (harness / source.name).write_bytes(source.read_bytes())
    (harness / "Harness.csproj").write_text(
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net6.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><PackageReference Include="Google.Protobuf" Version="3.34.1" /></ItemGroup>
</Project>
""", encoding="utf-8")
    (harness / "Program.cs").write_text(
        r'''using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using StardewValleyMcp.Protocol.V1;

static byte[] LPBytes(byte[] value) { var b = new byte[4 + value.Length]; BinaryPrimitives.WriteUInt32BigEndian(b, (uint)value.Length); value.CopyTo(b, 4); return b; }
static byte[] LPText(string value) => LPBytes(Encoding.UTF8.GetBytes(value));
static byte[] U32(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); return b; }
static byte[] U64(ulong value) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); return b; }
static byte[] Join(params byte[][] values) { var length = values.Sum(x => x.Length); var b = new byte[length]; var at = 0; foreach (var value in values) { value.CopyTo(b, at); at += value.Length; } return b; }
static string Sha(byte[] value) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(value)).ToLowerInvariant(); }
static string Prop(JsonElement root, string name) => root.GetProperty(name).GetString()!;
static uint U32Prop(JsonElement root, string name) => root.GetProperty(name).GetUInt32();
static ulong U64Prop(JsonElement root, string name) => root.GetProperty(name).GetUInt64();
static string Tag(byte[] secret, byte[] data) { using var mac = new HMACSHA256(secret); return Convert.ToBase64String(mac.ComputeHash(data)); }

var metadataPath = args[0]; var readyPath = args[1]; var vectorPath = args[2];
var fixturePaths = args.Skip(3).ToArray();
foreach (var path in fixturePaths) { var parsed = TransportFrame.Parser.ParseJson(File.ReadAllText(path)); if (parsed.BodyCase == TransportFrame.BodyOneofCase.None) throw new Exception(path); if (!parsed.Equals(TransportFrame.Parser.ParseFrom(parsed.ToByteArray()))) throw new Exception($"roundtrip: {path}"); }
var ready = TransportFrame.Parser.ParseJson(File.ReadAllText(readyPath)).ServerReady;
var encoded = new List<byte>();
foreach (var item in ready.CapabilitySnapshot.Capabilities.OrderBy(x => x.Id, StringComparer.Ordinal)) {
  encoded.AddRange(LPText(item.Id)); encoded.AddRange(LPText(item.ContractVersion)); encoded.Add((byte)item.SideEffect); encoded.Add((byte)item.Execution); encoded.Add(item.Cancellable ? (byte)1 : (byte)0);
  encoded.AddRange(U32(item.DefaultTimeoutMs)); encoded.AddRange(U32(item.MaxTimeoutMs)); encoded.AddRange(LPText(item.RequestType)); encoded.AddRange(LPText(item.ResultType)); encoded.AddRange(LPText(item.RequiredScope));
  var risks = item.Risks.OrderBy(x => x, StringComparer.Ordinal).ToArray(); encoded.AddRange(U32((uint)risks.Length)); foreach (var risk in risks) encoded.AddRange(LPText(risk)); encoded.Add(item.Destructive ? (byte)1 : (byte)0);
}
var digest = Sha(encoded.ToArray());
using var vectorDoc = JsonDocument.Parse(File.ReadAllText(vectorPath)); var v = vectorDoc.RootElement; var secret = Convert.FromBase64String(Prop(v, "secretBase64"));
var clientData = Join(LPText("stardew-valley-mcp/v1/client-auth"), LPText(Prop(v,"modInstanceId")), LPText(Prop(v,"clientInstanceId")), LPBytes(Convert.FromBase64String(Prop(v,"serverNonceBase64"))), LPBytes(Convert.FromBase64String(Prop(v,"clientNonceBase64"))), U32(U32Prop(v,"requestedMajor")), U32(U32Prop(v,"requestedMinor")), LPText(Prop(v,"resumeSessionId")));
var serverData = Join(LPText("stardew-valley-mcp/v1/server-auth"), LPText(Prop(v,"modInstanceId")), LPText(Prop(v,"clientInstanceId")), LPBytes(Convert.FromBase64String(Prop(v,"serverNonceBase64"))), LPBytes(Convert.FromBase64String(Prop(v,"clientNonceBase64"))), U32(U32Prop(v,"selectedMajor")), U32(U32Prop(v,"selectedMinor")), LPText(Prop(v,"sessionId")), U64(U64Prop(v,"leaseEpoch")), LPText(Prop(v,"capabilityDigest")), U32(U32Prop(v,"resultRetentionMs")), U32(U32Prop(v,"reconnectGraceMs")));
var descriptors = new FileDescriptor[] { CommonReflection.Descriptor, RefsReflection.Descriptor, FactsReflection.Descriptor, ActionsReflection.Descriptor, QueriesReflection.Descriptor, CapabilitiesReflection.Descriptor, TransportReflection.Descriptor }.ToDictionary(x => x.Name, x => Sha(x.SerializedData.ToByteArray()));
File.WriteAllText(metadataPath, JsonSerializer.Serialize(new { fixtureCount = fixturePaths.Length, digest, clientTag = Tag(secret, clientData), serverTag = Tag(secret, serverData), descriptors }));
Console.WriteLine($"csharp_contract_ok count={fixturePaths.Length} digest={digest}");
''', encoding="utf-8")
    metadata = tmp / "csharp-metadata.json"
    run(["dotnet", "run", "--project", str(harness / "Harness.csproj"), "--", str(metadata), str(ready_path), str(vector_path), *map(str, fixture_paths)])
    actual = load_json(metadata)
    require(actual["digest"] == digest, "C# Capability Digest 与 Python 不一致")
    require(actual["clientTag"] == vector["clientAuthTagBase64"], "C# Client HMAC 不一致")
    require(actual["serverTag"] == vector["serverAuthTagBase64"], "C# Server HMAC 不一致")
    python_hashes = {
        module.DESCRIPTOR.name: hashlib.sha256(module.DESCRIPTOR.serialized_pb).hexdigest()
        for module in python_modules.values()
    }
    require(actual["descriptors"] == python_hashes, "C# 与 Python 生成 Descriptor 不一致")


def verify_forbidden_proto_terms() -> None:
    forbidden = re.compile(r"AdapterV2|CommandProcessor|CompoundDispatcher|FallbackToLegacyMapper|v2-json|starcoplay\.protocol\.v245|runtime_manager|agent\.protocol", re.IGNORECASE)
    for path in PROTO.glob("*.proto"):
        require(forbidden.search(path.read_text(encoding="utf-8")) is None, f"Proto 含历史依赖: {path}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--skip-csharp", action="store_true", help="只在没有 .NET SDK 时用于本地草稿检查；冻结验收不得跳过")
    args = parser.parse_args()
    manifest = load_yaml(MANIFEST_PATH)
    verify_json_schemas(manifest)
    verify_adjudication(manifest)
    verify_forbidden_proto_terms()
    digest = capability_digest(manifest["capabilities"])
    with tempfile.TemporaryDirectory(prefix="sdvmcp-spec-") as raw_tmp:
        tmp = Path(raw_tmp)
        descriptor, python_out, csharp_out = generate(tmp)
        _, messages, enums = descriptor_index(descriptor)
        verify_manifest_against_proto(manifest, messages)
        verify_error_map(enums)
        run([sys.executable, str(SPEC / "conformance" / "generate_mcp_tool_schemas.py"), "--check"])
        verify_tool_schema_catalog(manifest)
        verify_action_fixtures()
        verify_phase5_contract_cases()
        modules = import_generated_python(python_out)
        fixture_paths = verify_fixtures(modules["transport"], manifest, digest)
        index = load_json(FIXTURE_ROOT / "index.json")
        frames = []
        for entry in index["protoJson"]:
            frame = modules["transport"].TransportFrame()
            json_format.ParseDict(load_json(FIXTURE_ROOT / entry["path"]), frame, ignore_unknown_fields=False)
            frames.append(frame)
        lifecycle_paths = verify_lifecycle_fixtures(modules["transport"], index, frames)
        vector = verify_auth_vector(digest, frames)
        bootstrap_paths, bootstrap_vector, bootstrap_digest = verify_bootstrap_fixtures(modules["transport"], manifest)
        observation_paths, observation_vector, observation_digest = verify_observation_fixtures(modules["transport"], manifest)
        verify_framing_model(modules["transport"])
        verify_negative_message_models(modules)
        if not args.skip_csharp:
            verify_csharp(
                csharp_out, fixture_paths + lifecycle_paths, vector, modules, digest, tmp,
                FIXTURE_ROOT / "transport" / "server-ready.json",
                FIXTURE_ROOT / "auth" / "hmac-sha256.json", "full",
            )
            verify_csharp(
                csharp_out, bootstrap_paths, bootstrap_vector, modules, bootstrap_digest, tmp,
                FIXTURE_ROOT / "bootstrap" / "server-ready.json",
                FIXTURE_ROOT / "bootstrap" / "hmac-sha256.json", "bootstrap",
            )
            verify_csharp(
                csharp_out, observation_paths, observation_vector, modules, observation_digest, tmp,
                FIXTURE_ROOT / "observation" / "server-ready.json",
                FIXTURE_ROOT / "observation" / "hmac-sha256.json", "observation",
            )
    print(f"spec_v1_conformance_ok capabilities={len(manifest['capabilities'])} digest={digest}")


if __name__ == "__main__":
    main()
