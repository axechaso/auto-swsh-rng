import unittest
import sys
import hashlib
from types import SimpleNamespace
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation import (
    AutomationConfig,
    AutomationRunStatus,
    AutomationRunner,
)
from swsh_app.backend import PROTOCOL_VERSION
from swsh_app.localization import BASELINE


BITS_ZERO = "0" * 128
BITS_ONE = "1" * 128
STATE_A = {"seed0": "0000000000000001", "seed1": "0000000000000002"}
STATE_B = {"seed0": "0000000000000011", "seed1": "0000000000000022"}
ANCHOR = {"seed0": "00000000000000A1", "seed1": "00000000000000B2"}


class FakeObserver:
    def __init__(self, samples):
        self.samples = list(samples)
        self.calls = []

    def observe_sequence(self, count, trigger, *, cancel_event=None):
        self.calls.append(count)
        for index in range(count):
            if cancel_event is not None and cancel_event.is_set():
                return SimpleNamespace(observations="", complete=False, unknown_index=index)
            trigger(index)
        if not self.samples:
            raise AssertionError("unexpected observer call")
        return self.samples.pop(0)


class FakeDevice:
    def __init__(self):
        self.lease = object()
        self.calls = []
        self.runner = None
        self.cancelled = False
        self.cleanup = []

    def acquire_task_lease(self):
        self.calls.append(("acquire",))
        return self.lease

    def preflight(self, config, lease, cancel_event):
        self.calls.append(("preflight", lease))

    def restore_scene(self, config, lease, epoch_id, *, after_restart, cancel_event):
        self.calls.append(("restore", epoch_id, after_restart))
        return True

    def trigger_seed_bit(self, config, lease, epoch_id, purpose, index, cancel_event):
        if index == 0:
            self.calls.append(("seed", epoch_id, purpose))
            if self.runner is not None and self.cancelled:
                self.runner.cancel()

    def restart_game(self, config, lease, epoch_id, cancel_event):
        self.calls.append(("restart", epoch_id))
        return True

    def cancel(self):
        self.calls.append(("cancel",))

    def stop_scripts(self):
        self.cleanup.append("stop")

    def release_task_lease(self, lease):
        self.cleanup.append(("release", lease))


class FakeCalculator:
    def __init__(self, *, search=None, verify=None):
        self.requests = []
        self.search = search or (lambda request: search_result(request, 0, []))
        self.verify = list([True] * 20 if verify is None else verify)
        self.solve_index = 0

    def execute(self, request, cancel_event):
        self.requests.append(dict(request))
        operation = request["operation"]
        if operation == "seed.solve":
            self.solve_index += 1
            state = STATE_A if self.solve_index % 2 else STATE_B
            data = {
                "rawAlgorithmState": state,
                "stateBeforeObservations": state,
                "stateAfterObservations": state,
                "observationCount": 128,
                "rawStateBoundary": "after_observations",
                "boundarySemanticsVersion": "retail-seed-observation-v1",
            }
        elif operation == "seed.verify":
            matches = self.verify.pop(0)
            data = {
                "matches": matches,
                "predictedObservations": request["observations"] if matches else BITS_ONE,
                "firstMismatchIndex": None if matches else 0,
                "stateAfterObservations": ANCHOR,
            }
        elif operation == "encounter.search":
            data = self.search(request)
        else:
            raise AssertionError(f"unexpected operation: {operation}")
        return {
            "type": "result",
            "protocolVersion": PROTOCOL_VERSION,
            "algorithmCommit": BASELINE["algorithm"]["commit"],
            "requestId": request["requestId"],
            "runId": request["runId"],
            "epochId": request["epochId"],
            "contextRevision": request["contextRevision"],
            "data": data,
        }


def sample(bits, *, complete=True, unknown_index=None):
    return SimpleNamespace(observations=bits if complete else bits[:17], complete=complete,
                           unknown_index=unknown_index)


def search_result(request, total, rows, *, complete=True, start=None, end=None, truncated=False):
    del truncated
    scan_start = request["scanCursor"]
    searched_end = min(request["end"], scan_start + request["scanLimit"] - 1)
    candidate_cursor = request["candidateCursor"]
    page_rows = rows[candidate_cursor:candidate_cursor + request["candidatePageSize"]]
    page_complete = candidate_cursor + len(page_rows) >= total
    digest = hashlib.sha256(
        f"{request['runId']}:{request['epochId']}:{scan_start}:{searched_end}".encode()
    ).hexdigest()
    return {
        "searchedStart": scan_start if start is None else start,
        "searchedEnd": searched_end if end is None else end,
        "candidateStart": candidate_cursor,
        "searchedPositions": searched_end - scan_start + 1,
        "scanComplete": complete and searched_end == request["end"],
        "nextScanCursor": None if searched_end == request["end"] else searched_end + 1,
        "candidateTotal": total,
        "candidatePageComplete": page_complete,
        "nextCandidateCursor": None if page_complete else candidate_cursor + len(page_rows),
        "snapshotId": f"snapshot-{digest[:32]}",
        "requestDigest": digest,
        "orderVersion": "advance-fields-v1",
        "rows": page_rows,
    }


def make_config(**kwargs):
    values = dict(
        run_id="run-test",
        context_revision=7,
        scenario_id="route-1-clear-weather",
        min_advance=1,
        max_advance=5,
        search_request={"game": "Shield", "kind": "overworld", "species": "Skwovet"},
        chunk_size=3,
        max_seed_rounds=2,
        search_retries=0,
    )
    values.update(kwargs)
    return AutomationConfig(**values)


class AutomationRunnerTests(unittest.TestCase):
    def run_flow(self, config, *, samples, calculator, device=None):
        device = device or FakeDevice()
        runner = AutomationRunner(config, device=device, observer=FakeObserver(samples), calculator=calculator)
        device.runner = runner
        return runner.run(), device, calculator

    def test_only_complete_empty_range_restarts_then_finds_candidates(self):
        template = {"game": "Shield", "kind": "overworld", "filters": {"shiny": True}}
        config = make_config(search_request=template)
        template["filters"]["shiny"] = False
        calculator = FakeCalculator(search=lambda req: search_result(
            req,
            0 if req["epochId"] == first_epoch_id[0] else (1 if req["scanCursor"] == 4 else 0),
            [] if req["epochId"] == first_epoch_id[0] or req["scanCursor"] == 1 else [{"advance": 4}],
        ))
        first_epoch_id = [None]
        # The callback captures the first epoch lazily after the first seed solve.
        original_execute = calculator.execute

        def execute(request, cancel_event):
            if request["operation"] == "seed.solve" and first_epoch_id[0] is None:
                first_epoch_id[0] = request["epochId"]
            return original_execute(request, cancel_event)

        calculator.execute = execute
        samples = [sample(BITS_ZERO), sample(BITS_ZERO), sample(BITS_ONE), sample(BITS_ONE)]
        result, device, calculator = self.run_flow(
            config,
            samples=samples,
            calculator=calculator,
        )

        self.assertEqual(result.status, AutomationRunStatus.TARGETS_FOUND, result.reason)
        self.assertEqual(result.epochs[-1].candidate_count, 1)
        self.assertEqual(result.candidate_rows[0]["advance"], 4)
        self.assertEqual(len(result.epochs), 2)
        self.assertEqual(sum(call[0] == "restart" for call in device.calls), 1)
        search_calls = [request for request in calculator.requests if request["operation"] == "encounter.search"]
        self.assertEqual(
            [(r["scanCursor"], r["end"]) for r in search_calls],
            [(1, 5), (4, 5), (1, 5), (4, 5)],
        )
        self.assertTrue(all(r["filters"]["shiny"] is True for r in search_calls))
        self.assertEqual(device.cleanup[0], "stop")
        self.assertEqual(device.cleanup[1], ("release", device.lease))

    def test_incomplete_search_or_wrong_bounds_never_restart(self):
        for response in (
            lambda req: search_result(req, 0, [], complete=False),
            lambda req: search_result(req, 0, [], start=req["start"] + 1),
        ):
            with self.subTest(response=response):
                calculator = FakeCalculator(search=response)
                result, device, _ = self.run_flow(
                    make_config(max_advance=2, chunk_size=3),
                    samples=[sample(BITS_ZERO), sample(BITS_ZERO)],
                    calculator=calculator,
                )
                self.assertEqual(result.status, AutomationRunStatus.NEEDS_ATTENTION)
                self.assertIn("SEARCH_INCOMPLETE", result.reason)
                self.assertFalse(any(call[0] == "restart" for call in device.calls))

    def test_transient_search_failure_retries_with_the_requested_bounds(self):
        attempts = [0]

        def search(request):
            attempts[0] += 1
            if attempts[0] == 1:
                raise RuntimeError("temporary calculator failure")
            return search_result(request, 1, [{"advance": 2}])

        calculator = FakeCalculator(search=search)
        result, _, _ = self.run_flow(
            make_config(max_advance=2, chunk_size=3, search_retries=1),
            samples=[sample(BITS_ZERO), sample(BITS_ZERO)],
            calculator=calculator,
        )

        self.assertEqual(result.status, AutomationRunStatus.TARGETS_FOUND, result.reason)
        self.assertEqual(attempts[0], 2)
        retry_events = [event for event in result.events if "未确认完整，重试" in event.message]
        self.assertEqual(len(retry_events), 1)
        self.assertIn("[1, 2]", retry_events[0].message)

    def test_seed_mismatch_discards_epoch_and_remeasures_before_search(self):
        calculator = FakeCalculator(verify=[False, True], search=lambda req: search_result(req, 1, [{"advance": 2}]))
        result, device, calculator = self.run_flow(
            make_config(max_advance=2, chunk_size=3),
            samples=[sample(BITS_ZERO), sample(BITS_ZERO), sample(BITS_ONE), sample(BITS_ONE)],
            calculator=calculator,
        )
        self.assertEqual(result.status, AutomationRunStatus.TARGETS_FOUND)
        self.assertEqual(sum(req["operation"] == "seed.solve" for req in calculator.requests), 2)
        verify_requests = [req for req in calculator.requests if req["operation"] == "seed.verify"]
        self.assertEqual(verify_requests[0]["seed0"], STATE_A["seed0"])
        self.assertEqual(verify_requests[1]["seed0"], STATE_B["seed0"])
        self.assertEqual(sum(call[0] == "restore" for call in device.calls), 2)

    def test_cancel_stops_actions_and_releases_task_lease(self):
        device = FakeDevice()
        device.cancelled = True
        calculator = FakeCalculator()
        runner = AutomationRunner(
            make_config(), device=device,
            observer=FakeObserver([sample(BITS_ZERO), sample(BITS_ZERO)]),
            calculator=calculator,
        )
        device.runner = runner
        result = runner.run()
        self.assertEqual(result.status, AutomationRunStatus.CANCELLED)
        self.assertFalse(any(req["operation"] == "encounter.search" for req in calculator.requests))
        self.assertEqual(device.cleanup[0], "stop")
        self.assertEqual(device.cleanup[1], ("release", device.lease))

    def test_pending_hardware_scenario_cannot_start_formal_run(self):
        device = FakeDevice()
        runner = AutomationRunner(
            make_config(execution_mode="formal"),
            device=device,
            observer=FakeObserver([]),
            calculator=FakeCalculator(),
        )

        result = runner.run()

        self.assertEqual(result.status, AutomationRunStatus.NEEDS_ATTENTION)
        self.assertIn("FORMAL_START_BLOCKED", result.reason)
        self.assertEqual(device.calls, [])
        self.assertEqual(device.cleanup, [])

    def test_range_endpoints_are_inclusive_and_epoch_limit_does_not_restart(self):
        calculator = FakeCalculator(search=lambda req: search_result(req, 0, []))
        result, device, _ = self.run_flow(
            make_config(min_advance=3, max_advance=3, chunk_size=1, max_epochs=1),
            samples=[sample(BITS_ZERO), sample(BITS_ZERO)],
            calculator=calculator,
        )
        search = next(req for req in calculator.requests if req["operation"] == "encounter.search")
        self.assertEqual((search["scanCursor"], search["end"]), (3, 3))
        self.assertEqual(result.status, AutomationRunStatus.NEEDS_ATTENTION)
        self.assertIn("EPOCH_LIMIT_REACHED", result.reason)
        self.assertFalse(any(call[0] == "restart" for call in device.calls))

    def test_t39_candidate_cursor_enumerates_every_row_in_stable_snapshot(self):
        all_rows = [{"advance": 1, "candidateOrdinal": index} for index in range(10_005)]
        calculator = FakeCalculator(search=lambda req: search_result(req, len(all_rows), all_rows))
        result, _, calculator = self.run_flow(
            make_config(min_advance=1, max_advance=1, chunk_size=1),
            samples=[sample(BITS_ZERO), sample(BITS_ZERO)],
            calculator=calculator,
        )
        page_requests = [
            request for request in calculator.requests
            if request["operation"] == "encounter.search"
        ]

        self.assertEqual(result.status, AutomationRunStatus.TARGETS_FOUND, result.reason)
        self.assertEqual(result.epochs[0].candidate_count, 10_005)
        self.assertFalse(result.epochs[0].candidate_rows_truncated)
        self.assertEqual(len(result.candidate_rows), 10_005)
        self.assertEqual([request["candidateCursor"] for request in page_requests], [0, 10_000])
        self.assertEqual(page_requests[0]["scanCursor"], page_requests[1]["scanCursor"])
        expected_digest = hashlib.sha256(
            f"{page_requests[0]['runId']}:{page_requests[0]['epochId']}:1:1".encode()
        ).hexdigest()
        self.assertEqual(page_requests[1]["requestDigest"], expected_digest)
        self.assertEqual(page_requests[1]["snapshotId"], f"snapshot-{expected_digest[:32]}")

    def test_configuration_rejects_unsafe_ranges_and_bad_limits(self):
        for overrides in (
            {"max_advance": 0},
            {"chunk_size": 100_001},
            {"context_revision": True},
            {"max_epochs": 0},
        ):
            with self.subTest(overrides=overrides), self.assertRaises(ValueError):
                make_config(**overrides)


if __name__ == "__main__":
    unittest.main()
