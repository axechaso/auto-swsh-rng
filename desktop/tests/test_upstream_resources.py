import copy
import hashlib
import json
from pathlib import Path
import re
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "desktop"))
from swsh_app.backend import PROTOCOL_VERSION, validate_event
from swsh_app.localization import BASELINE, RESOURCE_ROOT, translate
from swsh_app.storage import result_values


class UpstreamResourcesTests(unittest.TestCase):
    def test_resource_provenance_and_hash(self):
        raw = (RESOURCE_ROOT / "owoow.zh-Hans.json").read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(), BASELINE["localization"]["resourceSha256"])
        self.assertEqual(json.loads(raw)["source"]["commit"], BASELINE["localization"]["commit"])

    def test_localized_forms_abilities_marks_and_unknown_keys(self):
        for kind, original, expected in [
            ("species", "Skwovet", "贪心栗鼠"),
            ("species", "Articuno-1", "急冻鸟（伽勒尔的样子）"),
            ("area", "Route 9 (Surfing - Ocean)", "９号道路 （水上－海）"),
            ("ability", "Synchronize (1)", "同步（1）"),
            ("mark", "AbsentMinded", "无虑之证"),
            ("mark", "Uncommon", "偶遇之证"),
            ("mark", "Fishing", "上钩之证"),
            ("species", "FuturePokemon-9", "FuturePokemon-9"),
        ]:
            with self.subTest(original=original):
                self.assertEqual(translate(original, kind), expected)

    def test_all_pinned_encounter_resources_have_translations(self):
        core = ROOT / "third_party/owoow/owoow.Core/Resources"
        if not core.is_dir():
            self.skipTest("源代码覆盖检查需要 owoow 子模块")
        species = set()
        def visit(value):
            if isinstance(value, dict):
                if "Species" in value:
                    species.add(value["Species"])
                for child in value.values():
                    visit(child)
            elif isinstance(value, list):
                for child in value:
                    visit(child)
        for game in ("Sword", "Shield"):
            prefix = "sw" if game == "Sword" else "sh"
            for kind in ("symbol", "static", "hidden", "fishing"):
                data = json.loads((core / game / f"{prefix}_{kind}.json").read_text(encoding="utf-8-sig"))
                for area, weathers in data.items():
                    self.assertNotRegex(translate(area, "area"), r"[A-Za-z]", area)
                    for weather in weathers:
                        self.assertNotRegex(translate(weather, "ui"), r"[A-Za-z]", weather)
                visit(data)
        self.assertGreater(len(species), 100)
        for name in species:
            self.assertNotRegex(translate(name, "species"), r"[A-Za-z]", name)

    def test_display_does_not_change_calculation_data(self):
        fixture = json.loads((ROOT / "tests/AutoSwshRng.Cli.Tests/Fixtures/owoow-baseline.json").read_text())
        row = fixture["cases"][0]["expected"]["rows"][0]
        original = copy.deepcopy(row)
        values = result_values(row)
        self.assertEqual(row, original)
        self.assertEqual(values[1], translate(row["species"], "species"))
        self.assertEqual(values[-4:], [row["ec"], row["pid"], row["seed0"], row["seed1"]])

    def test_rejects_mixed_backend_versions(self):
        valid = {"type": "result", "protocolVersion": PROTOCOL_VERSION,
                 "algorithmCommit": BASELINE["algorithm"]["commit"]}
        validate_event(valid)
        for invalid in [{"type": "result"}, {**valid, "protocolVersion": 99},
                        {**valid, "algorithmCommit": "0" * 40}, []]:
            with self.assertRaisesRegex(ValueError, "版本"):
                validate_event(invalid)


if __name__ == "__main__":
    unittest.main()
