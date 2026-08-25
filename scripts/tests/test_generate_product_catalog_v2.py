# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import copy
import json
import subprocess
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from scripts.ci.validate_product_decks_v1 import (  # noqa: E402
    ManifestError,
    validate_json_schema,
)
from scripts.design.generate_product_catalog_v2 import (  # noqa: E402
    _validate_runtime_foundation,
    render,
)


class ProductCatalogV2GeneratorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.design = json.loads(
            (ROOT / "design/product-decks-v1/card-pool.lock.json").read_text(encoding="utf-8")
        )
        cls.runtime = json.loads(
            (ROOT / "design/product-decks-v1/runtime-foundation.lock.json").read_text(
                encoding="utf-8"
            )
        )
        cls.runtime_schema = json.loads(
            (ROOT / "design/product-decks-v1/runtime-foundation.schema.json").read_text(
                encoding="utf-8"
            )
        )

    def test_runtime_foundation_schema_and_cross_contract(self) -> None:
        validate_json_schema(self.runtime, self.runtime_schema)
        _validate_runtime_foundation(self.design, self.runtime)

    def test_render_contains_typed_shapes_and_locked_status(self) -> None:
        generated = render(self.design, self.runtime)
        self.assertIn('CardImplementationStatus::LockedNotImplemented, false', generated)
        self.assertIn('{"AP-08", "repair", "修复2", TargetSpec::None', generated)
        self.assertIn('ConditionKind::MatchRepairToZeroAtLeast', generated)
        self.assertIn(
            '{"AP-S04", TargetSpec::FriendlyPermanent, 1, 1, '
            '{{{CardKind::Follower, CardKind::Amulet}}, 2, "pactmage", '
            '"abyssal_pact", true, false}',
            generated,
        )

    def test_every_standby_requires_typed_condition(self) -> None:
        mutated = copy.deepcopy(self.runtime)
        mutated["standby_cards"][0]["conditions"] = []
        with self.assertRaises(ManifestError):
            validate_json_schema(mutated, self.runtime_schema)

    def test_ap_s04_filter_cannot_be_broadened(self) -> None:
        mutated = copy.deepcopy(self.runtime)
        ap_s04 = next(
            card for card in mutated["standby_cards"] if card["design_id"] == "AP-S04"
        )
        ap_s04["additional_cost"]["filter"]["series_id"] = "neutral"
        validate_json_schema(mutated, self.runtime_schema)
        with self.assertRaisesRegex(ManifestError, "AP-S04 additional cost"):
            _validate_runtime_foundation(self.design, mutated)

    def test_committed_generated_catalog_is_current(self) -> None:
        result = subprocess.run(
            [sys.executable, "scripts/design/generate_product_catalog_v2.py", "--check"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("verified generated product catalog", result.stdout)

    def test_v04_core_regressions_do_not_depend_on_legacy_product_cards(self) -> None:
        core_regressions = (
            ROOT / "engine/tests/test_main.cpp",
            ROOT / "engine/tests/test_client_api.cpp",
            ROOT / "engine/tests/test_wire.cpp",
        )
        forbidden = (
            "make_v04_catalog",
            "make_midrange_deck",
            "make_advance_deck",
            "cards::midrange",
            "cards::advance",
        )
        for path in core_regressions:
            source = path.read_text(encoding="utf-8")
            for token in forbidden:
                self.assertNotIn(token, source, f"{path.name} regained legacy dependency: {token}")

        for path in core_regressions[:2]:
            self.assertIn(
                '#include "v04_synthetic_fixture.hpp"',
                path.read_text(encoding="utf-8"),
                f"{path.name} must keep using the independent synthetic fixture",
            )


if __name__ == "__main__":
    unittest.main()
