from __future__ import annotations

import os
from pathlib import Path
import sys
import threading
import unittest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtWidgets import QApplication

from swsh_app.automation import SeedObserver, SimulationDevicePort
from swsh_app.automation.synthetic_replay import build_synthetic_seed_replay


APP = QApplication.instance() or QApplication([])


class SyntheticReplayTests(unittest.TestCase):
    def test_production_observer_reads_solve_and_verify_bits_from_generated_frames(self):
        observations = ("01001110" * 16)
        source, classify = build_synthetic_seed_replay(observations)
        device = SimulationDevicePort(frame_source=source)
        observer = SeedObserver(
            source, classify, clock=source.clock, timeout_seconds=1,
            poll_seconds=0.001,
        )
        cancel = threading.Event()
        lease = device.acquire_task_lease()

        solve = observer.observe_sequence(
            128,
            lambda index: device.trigger_seed_bit(None, lease, "epoch", "solve", index, cancel),
            cancel_event=cancel,
        )
        verify = observer.observe_sequence(
            128,
            lambda index: device.trigger_seed_bit(None, lease, "epoch", "verify", index, cancel),
            cancel_event=cancel,
        )

        self.assertTrue(solve.complete)
        self.assertTrue(verify.complete)
        self.assertEqual(solve.observations, observations)
        self.assertEqual(verify.observations, observations)
        self.assertEqual(source.remaining_actions, 0)
        device.stop_scripts()
        device.release_task_lease(lease)
        source.close()

    def test_synthetic_replay_rejects_malformed_bit_stream(self):
        for observations in ("1" * 127, "0" * 127 + "x"):
            with self.assertRaises(ValueError):
                build_synthetic_seed_replay(observations)


if __name__ == "__main__":
    unittest.main()
