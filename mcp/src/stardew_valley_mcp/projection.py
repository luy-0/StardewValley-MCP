"""Descriptor 驱动的 Proto 到公开 JSON 投影。"""

from __future__ import annotations

import base64

from google.protobuf import json_format
from google.protobuf.descriptor import FieldDescriptor
from google.protobuf.message import Message


def _enum_value(field: FieldDescriptor, value: int) -> str:
    try:
        name = field.enum_type.values_by_number[value].name
    except KeyError as error:
        raise ValueError(f"未知 enum number: {field.enum_type.full_name}={value}") from error
    names = [item.name for item in field.enum_type.values]
    prefix = names[0]
    for other in names[1:]:
        while prefix and not other.startswith(prefix):
            prefix = prefix[:-1]
    prefix = prefix[: prefix.rfind("_") + 1]
    return name.removeprefix(prefix).lower()


def project_message(message: Message) -> dict[str, object]:
    output: dict[str, object] = {}
    for field in message.DESCRIPTOR.fields:
        key = field.json_name
        if field.is_repeated:
            values = getattr(message, field.name)
            if field.message_type and field.message_type.GetOptions().map_entry:
                output[key] = {str(k): _project_value(field.message_type.fields_by_name["value"], v) for k, v in values.items()}
            else:
                output[key] = [_project_value(field, value) for value in values]
            continue
        if field.containing_oneof is not None and not message.HasField(field.name):
            continue
        output[key] = _project_value(field, getattr(message, field.name))
    return output


def _project_value(field: FieldDescriptor, value: object) -> object:
    if field.type == FieldDescriptor.TYPE_MESSAGE:
        return project_message(value)  # type: ignore[arg-type]
    if field.type == FieldDescriptor.TYPE_ENUM:
        return _enum_value(field, int(value))
    if field.type in {FieldDescriptor.TYPE_INT64, FieldDescriptor.TYPE_UINT64, FieldDescriptor.TYPE_SINT64, FieldDescriptor.TYPE_FIXED64, FieldDescriptor.TYPE_SFIXED64}:
        return str(value)
    if field.type == FieldDescriptor.TYPE_BYTES:
        return base64.b64encode(bytes(value)).decode("ascii")
    return value


def parse_message(document: dict[str, object], message_class: type[Message]) -> Message:
    """按 Descriptor 将公开 JSON 参数转换为 Proto 消息。"""
    message = message_class()
    normalized = _normalize_input(document, message.DESCRIPTOR)
    return json_format.ParseDict(normalized, message, ignore_unknown_fields=False)


def _normalize_input(document: dict[str, object], descriptor: object) -> dict[str, object]:
    output: dict[str, object] = {}
    fields = {field.json_name: field for field in descriptor.fields}
    for key, value in document.items():
        field = fields.get(key)
        if field is None:
            output[key] = value
            continue
        if field.is_repeated and isinstance(value, list):
            output[key] = [_normalize_field_input(field, item) for item in value]
        else:
            output[key] = _normalize_field_input(field, value)
    return output


def _normalize_field_input(field: FieldDescriptor, value: object) -> object:
    if field.type == FieldDescriptor.TYPE_MESSAGE and isinstance(value, dict):
        return _normalize_input(value, field.message_type)
    if field.type == FieldDescriptor.TYPE_ENUM and isinstance(value, str):
        for enum_value in field.enum_type.values:
            if _enum_value(field, enum_value.number) == value:
                return enum_value.name
    return value
