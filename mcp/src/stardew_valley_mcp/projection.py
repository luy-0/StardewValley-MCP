"""Descriptor 驱动的 Proto 到公开 JSON 投影。"""

from __future__ import annotations

import base64

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
        if field.has_presence and not message.HasField(field.name):
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
