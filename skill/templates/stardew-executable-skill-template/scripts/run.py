"""可执行 Skill 最小入口示例。"""

from __future__ import annotations


async def run(ctx, arguments):
    del arguments
    result = await ctx.call_tool("stardew_query_runtime", {})
    if result.get("status") != "succeeded":
        return {
            "status": result.get("status", "failed"),
            "error": result.get(
                "error",
                {
                    "code": "runtime_query_failed",
                    "message": "无法查询游戏运行状态",
                    "retryable": False,
                },
            ),
        }
    return {"status": "succeeded", "output": {"observed": True}}
