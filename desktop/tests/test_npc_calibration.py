from __future__ import annotations

from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation import (
    NpcCalibrationCache,
    NpcCalibrationContext,
    NpcCalibrationSession,
    ProbeDesign,
    ProbeExperiment,
    build_probe_candidates,
)
from swsh_app.backend import PROTOCOL_VERSION
from swsh_app.localization import BASELINE


def advance_state(seed0: str, seed1: str, advances: int) -> tuple[str, str]:
    left = (int(seed0, 16) + advances) & ((1 << 64) - 1)
    right = int(seed1, 16) ^ advances
    return f"{left:016x}", f"{right:016x}"


def experiment(name: str, index: int, actual_advances: int, *, actions=None,
               source_kind="synthetic", evidence_ids=(), observation_advances=2):
    seed0 = f"{index + 1:016x}"
    seed1 = f"{index + 100:016x}"
    after0, after1 = advance_state(seed0, seed1, actual_advances)
    return ProbeExperiment(
        experiment_id=name,
        seed0=seed0,
        seed1=seed1,
        observed_seed0=after0,
        observed_seed1=after1,
        probe_actions=actions or [{"kind": "interference"}],
        source_kind=source_kind,
        evidence_ids=evidence_ids,
        observation_advances=observation_advances,
        observed_advances=actual_advances,
        action_log=[{"stage": "menu-close", "elapsedMs": 120}],
    )


class FakeProbeCalculator:
    def __init__(self):
        self.requests = []

    def execute(self, request, cancel_event):
        self.requests.append(dict(request))
        candidates = []
        for candidate in request["probeCandidates"]:
            total = 0
            for action in request["probeActions"]:
                kind = action["kind"].strip().lower()
                if kind == "interference":
                    total += candidate.get("interferenceAdvances", 0)
                elif kind == "menuclose":
                    total += action.get("menuCloseNonPlayerCharacters",
                                        candidate.get("menuCloseNonPlayerCharacters", 0))
                elif kind == "rain":
                    total += action.get("ticks", candidate.get("rainTicks", 0))
            state0, state1 = advance_state(request["seed0"], request["seed1"], total)
            observed_seed0 = request.get("observedSeed0")
            observed_seed1 = request.get("observedSeed1")
            observed_advances = request.get("observedAdvances")
            matches_state = None if observed_seed0 is None else (
                observed_seed0 == state0 and observed_seed1 == state1
            )
            matches_advances = None if observed_advances is None else observed_advances == total
            if matches_state is None and matches_advances is None:
                matches = None
            else:
                matches = matches_state is not False and matches_advances is not False
            candidates.append({
                "candidateId": candidate["candidateId"],
                "parameters": {key: value for key, value in candidate.items() if key != "candidateId"},
                "predictedAdvances": total,
                "predictedEndState": {"seed0": state0, "seed1": state1},
                "matchesState": matches_state,
                "matchesAdvances": matches_advances,
                "matches": matches,
                "actions": [],
            })
        event = {key: request[key] for key in (
            "protocolVersion", "requestId", "runId", "epochId", "contextRevision",
        )}
        event.update({
            "type": "result",
            "algorithmCommit": BASELINE["algorithm"]["commit"],
            "data": {"status": "Unique", "candidates": candidates},
        })
        return event


def make_context(**overrides):
    values = dict(
        game="Shield",
        region="Wild Area",
        subregion="Bridge Field",
        route_version="route-3",
        weather="clear",
        menu_action="close-and-reopen-camp",
        hold_direction=False,
        start_position={"x": 18, "y": 0, "facing": "north"},
        camera_state={"preset": "north-wall"},
        lead_setup={"ability": "Synchronize", "nature": "Timid"},
        algorithm_commit=BASELINE["algorithm"]["commit"],
    )
    values.update(overrides)
    return NpcCalibrationContext(**values)


class NpcCalibrationTests(unittest.TestCase):
    def make_session(self, candidates, *, cache=None, calculator=None, context=None,
                     min_training=3, min_validation=2, batch_size=512):
        return NpcCalibrationSession(
            context=context or make_context(),
            candidates=candidates,
            calculator=calculator or FakeProbeCalculator(),
            run_id="run-npc",
            epoch_id="epoch-npc",
            context_revision=4,
            cache=cache,
            minimum_training_experiments=min_training,
            minimum_validation_experiments=min_validation,
            candidate_batch_size=batch_size,
        )

    def test_training_converges_then_independent_states_validate_and_account_for_consumption(self):
        candidates = build_probe_candidates(interferenceAdvances=range(0, 6))
        with tempfile.TemporaryDirectory() as directory:
            cache = NpcCalibrationCache(Path(directory) / "npc-cache.json")
            session = self.make_session(candidates, cache=cache)
            training = [experiment(f"train-{i}", i, 3) for i in range(3)]
            validation = [experiment(f"check-{i}", i + 20, 3) for i in range(2)]

            result = session.calibrate(training, validation)

            self.assertEqual(result.status, "CalibratedOffline")
            self.assertEqual(result.candidate["interferenceAdvances"], 3)
            self.assertEqual(result.candidate_ids, (result.candidate["candidateId"],))
            self.assertEqual(result.hardware_status, "PendingHardwareValidation")
            self.assertEqual(len(result.accounting), 5)
            self.assertEqual(result.accounting[0].observation_advances, 2)
            self.assertEqual(result.accounting[0].candidate_advances[result.candidate_ids[0]], 3)
            self.assertEqual(result.accounting[0].action_log[0]["elapsedMs"], 120)
            self.assertIsNotNone(cache.get(make_context()))

    def test_ambiguous_training_keeps_all_candidates_and_picks_a_discriminating_design(self):
        candidates = build_probe_candidates(menuCloseNonPlayerCharacters=[2, 7])
        session = self.make_session(candidates, min_training=3)
        training = [experiment(f"amb-{i}", i, 0, actions=[{"kind": "rain", "ticks": 0}])
                    for i in range(3)]

        result = session.calibrate(training)
        choice = session.choose_discriminating_design([
            ProbeDesign("no-separation", "0000000000000001", "0000000000000002",
                        [{"kind": "rain", "ticks": 0}]),
            ProbeDesign("menu-close", "0000000000000003", "0000000000000004",
                        [{"kind": "menuclose"}]),
        ])

        self.assertEqual(result.status, "Ambiguous")
        self.assertEqual(len(result.candidate_ids), 2)
        self.assertIsNone(result.candidate)
        self.assertEqual(choice["status"], "DiscriminatingDesignFound")
        self.assertEqual(choice["recommendedDesignId"], "menu-close")

    def test_no_candidate_match_is_reported_as_model_mismatch(self):
        session = self.make_session(build_probe_candidates(interferenceAdvances=[1, 2]))
        result = session.calibrate([experiment("bad-model", 1, 3)])
        self.assertEqual(result.status, "ModelMismatch")
        self.assertEqual(result.candidate_ids, ())

    def test_cache_requires_exact_context_and_failed_revalidation_invalidates_it(self):
        context = make_context()
        with tempfile.TemporaryDirectory() as directory:
            cache = NpcCalibrationCache(Path(directory) / "npc-cache.json")
            candidates = build_probe_candidates(interferenceAdvances=[3])
            trained = self.make_session(candidates, cache=cache, context=context).calibrate(
                [experiment(f"train-{i}", i, 3) for i in range(3)],
                [experiment(f"check-{i}", i + 30, 3) for i in range(2)],
            )
            self.assertEqual(trained.status, "CalibratedOffline")
            self.assertEqual(
                self.make_session(candidates, cache=cache, context=make_context(weather="rain"))
                .revalidate_cached_candidate(experiment("wrong-context", 70, 3))["status"],
                "CacheMiss",
            )

            failed = self.make_session(candidates, cache=cache, context=context).revalidate_cached_candidate(
                experiment("changed-scene", 71, 4)
            )
            self.assertEqual(failed["status"], "CacheInvalidated")
            self.assertIsNone(cache.get(context))

    def test_candidate_search_is_batched_to_the_calculator_limit(self):
        candidates = build_probe_candidates(interferenceAdvances=range(513))
        calculator = FakeProbeCalculator()
        session = self.make_session(candidates, calculator=calculator, batch_size=512,
                                    min_training=1, min_validation=1)
        result = session.calibrate([experiment("batch-1", 1, 512)])
        probe_requests = [request for request in calculator.requests
                          if request["operation"] == "calibration.probe.evaluate"]
        self.assertEqual([len(item["probeCandidates"]) for item in probe_requests], [512, 1])
        self.assertEqual(result.status, "UniqueAwaitingIndependentValidation")
        self.assertEqual(len(result.candidate_ids), 1)

    def test_duplicate_start_states_cannot_be_claimed_as_independent_samples(self):
        session = self.make_session(build_probe_candidates(interferenceAdvances=[3]))
        sample = experiment("same-a", 1, 3)
        repeated = experiment("same-b", 1, 3)
        with self.assertRaisesRegex(ValueError, "distinct known RNG start states"):
            session.calibrate([sample, repeated])


if __name__ == "__main__":
    unittest.main()
