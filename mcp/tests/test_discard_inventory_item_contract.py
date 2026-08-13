from __future__ import annotations

from jsonschema import Draft202012Validator

from stardew_valley_mcp.catalog import Catalog
from stardew_valley_mcp.command_runtime import _error, _failed_error_code_allowed
from stardew_valley_mcp.protocol import actions_pb2, capabilities_pb2, common_pb2


REVISION = "1" * 64
COMMAND_ID = "19191919-1919-4919-8919-191919191919"


def test_discard_proto_numbers_and_result_audit_fields_are_frozen() -> None:
    request_field = capabilities_pb2.CommandRequest.DESCRIPTOR.fields_by_name[
        "discard_inventory_item"
    ]
    result_field = capabilities_pb2.CapabilityResult.DESCRIPTOR.fields_by_name[
        "discard_inventory_item"
    ]

    assert request_field.number == result_field.number == 25
    assert request_field.message_type.name == "DiscardInventoryItemRequest"
    assert result_field.message_type.name == "DiscardInventoryItemResult"
    assert common_pb2.ERROR_CODE_ITEM_NOT_DISCARDABLE == 20
    assert common_pb2.ERROR_CODE_COMMIT_OUTCOME_UNKNOWN == 21
    assert {
        field.name: field.number
        for field in actions_pb2.DiscardInventoryItemResult.DESCRIPTOR.fields
    } == {
        "requested_quantity": 1,
        "discarded_quantity": 2,
        "source_slot_index": 3,
        "source_remaining_quantity": 4,
        "player_inventory_revision": 5,
        "money_before": 6,
        "money_after": 7,
        "money_refunded": 8,
    }


def test_discard_schema_annotations_and_stable_error_projection() -> None:
    tool = Catalog.load().tool("discard_inventory_item")
    assert tool.annotations.readOnlyHint is False
    assert tool.annotations.destructiveHint is True
    assert tool.annotations.idempotentHint is False

    input_validator = Draft202012Validator(tool.inputSchema)
    valid_input = {
        "itemRef": {"value": "player-item-ref"},
        "quantity": 1,
        "playerInventoryRevision": REVISION,
    }
    input_validator.validate(valid_input)
    for quantity in (0, -1, 2_147_483_648):
        assert not input_validator.is_valid({**valid_input, "quantity": quantity})

    output_validator = Draft202012Validator(tool.outputSchema)
    output_validator.validate(
        {
            "status": "succeeded",
            "commandId": COMMAND_ID,
            "output": {
                "requestedQuantity": 2,
                "discardedQuantity": 2,
                "sourceSlotIndex": 4,
                "sourceRemainingQuantity": 8,
                "playerInventoryRevision": REVISION,
                "moneyBefore": 1_000,
                "moneyAfter": 1_015,
                "moneyRefunded": 15,
            },
        }
    )
    output_validator.validate(
        {
            "status": "failed",
            "commandId": COMMAND_ID,
            "error": {
                "code": "item_not_discardable",
                "message": "该物品不能进入原生背包垃圾桶",
                "retryable": False,
            },
        }
    )

    assert _failed_error_code_allowed(
        "discard_inventory_item", common_pb2.ERROR_CODE_ITEM_NOT_DISCARDABLE
    )
    assert not _failed_error_code_allowed(
        "query_runtime", common_pb2.ERROR_CODE_ITEM_NOT_DISCARDABLE
    )
    assert _error(
        COMMAND_ID,
        common_pb2.Error(
            code=common_pb2.ERROR_CODE_ITEM_NOT_DISCARDABLE,
            message="该物品不能进入原生背包垃圾桶",
        ),
        "discard_inventory_item",
    ) == {
        "status": "failed",
        "commandId": COMMAND_ID,
        "error": {
            "code": "item_not_discardable",
            "message": "该物品不能进入原生背包垃圾桶",
            "retryable": False,
        },
    }

    assert _failed_error_code_allowed(
        "discard_inventory_item", common_pb2.ERROR_CODE_COMMIT_OUTCOME_UNKNOWN
    )
    assert not _failed_error_code_allowed(
        "query_runtime", common_pb2.ERROR_CODE_COMMIT_OUTCOME_UNKNOWN
    )
    assert _error(
        COMMAND_ID,
        common_pb2.Error(
            code=common_pb2.ERROR_CODE_COMMIT_OUTCOME_UNKNOWN,
            message="原生垃圾桶提交结果无法确认",
        ),
        "discard_inventory_item",
    ) == {
        "status": "unknown",
        "commandId": COMMAND_ID,
        "error": {
            "code": "unknown_outcome",
            "message": "原生垃圾桶提交结果无法确认",
            "retryable": False,
        },
    }
