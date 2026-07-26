from __future__ import annotations

import json
import unittest
from pathlib import Path

from google.protobuf import json_format
from stardew_valley_mcp.protocol import capabilities_pb2, common_pb2, transport_pb2


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "spec" / "fixtures" / "v1" / "bootstrap"
DIGEST = "6c9c9fc8002032a8b4191e3d4809f74ae9c20abcfb26fbf579d7a329d7daf199"


def parse_frame(name: str) -> transport_pb2.TransportFrame:
    frame = transport_pb2.TransportFrame()
    json_format.ParseDict(json.loads((FIXTURES / name).read_text(encoding="utf-8")), frame)
    return frame


class BootstrapFixtureTests(unittest.TestCase):
    def test_query_runtime_lifecycle(self) -> None:
        ready = parse_frame("server-ready.json")
        self.assertEqual(ready.server_ready.capability_snapshot.digest, DIGEST)
        self.assertEqual([item.id for item in ready.server_ready.capability_snapshot.capabilities], ["query_runtime"])

        request = parse_frame("query-runtime.request.json")
        self.assertEqual(request.command_request.WhichOneof("operation"), "query_runtime")
        self.assertEqual(request.fence.capability_digest, DIGEST)

        accepted = parse_frame("query-runtime.accepted.json")
        self.assertEqual(accepted.command_event.state, capabilities_pb2.COMMAND_STATE_ACCEPTED)
        self.assertIsNone(accepted.command_event.WhichOneof("outcome"))

        succeeded = parse_frame("query-runtime.succeeded.json")
        self.assertEqual(succeeded.command_event.state, capabilities_pb2.COMMAND_STATE_SUCCEEDED)
        self.assertEqual(succeeded.command_event.result.WhichOneof("result"), "query_runtime")

        not_ready = parse_frame("query-runtime.not-ready.json")
        self.assertEqual(not_ready.command_event.state, capabilities_pb2.COMMAND_STATE_FAILED)
        self.assertEqual(not_ready.command_event.error.code, common_pb2.ERROR_CODE_NOT_READY)
        self.assertEqual(not_ready.command_event.WhichOneof("outcome"), "error")

    def test_hmac_vector_uses_singleton_digest(self) -> None:
        vector = json.loads((FIXTURES / "hmac-sha256.json").read_text(encoding="utf-8"))
        self.assertEqual(vector["capabilityDigest"], DIGEST)


if __name__ == "__main__":
    unittest.main()
