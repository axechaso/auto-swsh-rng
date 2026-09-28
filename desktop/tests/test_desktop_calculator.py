from __future__ import annotations

from pathlib import Path
import sys
import threading
import unittest
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation.desktop_calculator import DesktopJsonCalculator
from swsh_app.backend import PROTOCOL_VERSION, find_backend


@unittest.skipUnless(find_backend(), "Set SWSH_RNG_BACKEND to the built CLI for integration tests")
class DesktopJsonCalculatorTests(unittest.TestCase):
    def test_seed_solve_uses_the_shared_protocol_identity_and_version_checks(self):
        request = {
            "operation": "seed.solve",
            "observations": "01001110" * 16,
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": uuid.uuid4().hex,
            "runId": uuid.uuid4().hex,
            "epochId": uuid.uuid4().hex,
            "contextRevision": 12,
        }

        response = DesktopJsonCalculator(find_backend()).execute(request, threading.Event())

        self.assertEqual(response["requestId"], request["requestId"])
        self.assertEqual(response["contextRevision"], 12)
        self.assertEqual(response["type"], "result")
        self.assertEqual(response["data"]["observationCount"], 128)
        self.assertEqual(response["data"]["boundarySemanticsVersion"], "retail-seed-observation-v1")


if __name__ == "__main__":
    unittest.main()
