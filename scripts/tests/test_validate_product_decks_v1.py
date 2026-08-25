"""Regression tests for the Gate 5A product deck design lock."""

# SPDX-License-Identifier: GPL-3.0-or-later

from __future__ import annotations

import copy
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from scripts.ci.validate_product_decks_v1 import ManifestError, validate


ROOT = Path(__file__).resolve().parents[2]
MANIFEST_PATH = ROOT / "design/product-decks-v1/card-pool.lock.json"
SCHEMA_PATH = ROOT / "design/product-decks-v1/card-pool.schema.json"


def _load(path: Path) -> object:
    return json.loads(path.read_text(encoding="utf-8"))


def _by_id(entries: list[dict[str, object]], field: str, identity: str) -> dict[str, object]:
    return next(entry for entry in entries if entry[field] == identity)


class ProductDeckV1ValidationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = _load(MANIFEST_PATH)
        cls.schema = _load(SCHEMA_PATH)

    def valid_copy(self) -> dict[str, object]:
        return copy.deepcopy(self.manifest)

    def assert_invalid(
        self,
        document: object,
        message: str,
        *,
        with_schema: bool = False,
    ) -> None:
        with self.assertRaisesRegex(ManifestError, message):
            validate(document, self.schema if with_schema else None)

    def test_checked_in_manifest_satisfies_schema_and_semantic_contract(self) -> None:
        validate(self.manifest, self.schema)

    def test_command_line_validates_an_independent_manifest_file(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            manifest_path = Path(temporary) / "copied-lock.json"
            manifest_path.write_text(
                json.dumps(self.manifest, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
            completed = subprocess.run(
                (
                    sys.executable,
                    "scripts/ci/validate_product_decks_v1.py",
                    "--manifest",
                    str(manifest_path),
                    "--schema",
                    str(SCHEMA_PATH),
                ),
                cwd=ROOT,
                capture_output=True,
                check=False,
                text=True,
                timeout=10,
            )
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertIn("validated Gate 5A product deck lock", completed.stdout)

    def test_cli_reports_readable_error_and_nonzero_exit(self) -> None:
        document = self.valid_copy()
        document["status"] = "implemented"
        with tempfile.TemporaryDirectory() as temporary:
            manifest_path = Path(temporary) / "bad-lock.json"
            manifest_path.write_text(json.dumps(document), encoding="utf-8")
            completed = subprocess.run(
                (
                    sys.executable,
                    "scripts/ci/validate_product_decks_v1.py",
                    "--manifest",
                    str(manifest_path),
                    "--schema",
                    str(SCHEMA_PATH),
                ),
                cwd=ROOT,
                capture_output=True,
                check=False,
                text=True,
                timeout=10,
            )
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("Gate 5A product deck validation failed", completed.stderr)
        self.assertIn("status", completed.stderr)

    def test_schema_rejects_an_unexpected_shape_before_semantics(self) -> None:
        document = self.valid_copy()
        document["runtime_card_ids"] = [1, 2, 3]
        self.assert_invalid(
            document,
            "schema validation failed.*unexpected property",
            with_schema=True,
        )

    def test_rejects_wrong_main_total_and_same_name_limit(self) -> None:
        document = self.valid_copy()
        deck = document["decks"][0]
        deck["main"][0]["copies"] = 4
        self.assert_invalid(document, "same-name limit")

        document = self.valid_copy()
        document["decks"][0]["main"][0]["copies"] -= 1
        self.assert_invalid(document, "exactly 30")

    def test_rejects_wrong_profession_and_unknown_card_references(self) -> None:
        document = self.valid_copy()
        oath = next(deck for deck in document["decks"] if deck["profession_id"] == "oathguard")
        oath["main"][0]["design_id"] = "AP-01"
        self.assert_invalid(document, "another profession")

        document = self.valid_copy()
        document["decks"][0]["main"][0]["design_id"] = "LO-99"
        self.assert_invalid(document, "unknown card")

    def test_rejects_standby_duplicates_copies_and_advance(self) -> None:
        document = self.valid_copy()
        document["decks"][0]["standby"][0]["copies"] = 2
        self.assert_invalid(document, "exactly once")

        document = self.valid_copy()
        standby_id = document["decks"][0]["standby"][0]["design_id"]
        _by_id(document["cards"], "design_id", standby_id)["can_advance"] = True
        self.assert_invalid(document, "standby cards must default")

    def test_rejects_missing_shared_neutral_and_wrong_trap_identity(self) -> None:
        document = self.valid_copy()
        neutral = _by_id(document["cards"], "design_id", "NT-04")
        neutral["neutral"] = False
        neutral["profession_id"] = "oathguard"
        self.assert_invalid(document, "neutral definitions")

        document = self.valid_copy()
        _by_id(document["cards"], "design_id", "LO-07")["card_type"] = "spell"
        self.assert_invalid(document, "trap copies")

    def test_rejects_definition_name_and_capability_reference_errors(self) -> None:
        document = self.valid_copy()
        document["cards"].pop()
        self.assert_invalid(document, "exactly 34")

        document = self.valid_copy()
        document["tokens"][0]["name"] = document["cards"][0]["name"]
        self.assert_invalid(document, "duplicate card/token name")

        document = self.valid_copy()
        document["cards"][0]["capability_requirements"][0]["capability_id"] = "missing_capability"
        self.assert_invalid(document, "unknown capability")

    def test_rejects_zero_cost_and_forbidden_safety_flags(self) -> None:
        document = self.valid_copy()
        _by_id(document["cards"], "design_id", "LO-01")["play_cost_pp"] = 0
        self.assert_invalid(document, "at least 1")

        document = self.valid_copy()
        safety = None
        for container_name in ("format", "implementation_scope"):
            container = document.get(container_name)
            if isinstance(container, dict):
                for field in (
                    "design_safety",
                    "forbidden_design_patterns",
                    "prohibited_mechanics",
                    "constraints",
                ):
                    if isinstance(container.get(field), dict):
                        safety = container[field]
                        break
            if safety is not None:
                break
        self.assertIsNotNone(safety)
        safety["current_pp_restoration"] = True
        self.assert_invalid(document, "current_pp_restoration")

    def test_repair_cards_cannot_advance_and_debt_scaling_is_capped(self) -> None:
        document = self.valid_copy()
        _by_id(document["cards"], "design_id", "LO-06")["can_advance"] = True
        self.assert_invalid(document, "repair card must not be advanceable")

        document = self.valid_copy()
        ability_words = document["rules"]["ability_words"]
        debtbound = (
            ability_words["debtbound"]
            if isinstance(ability_words, dict)
            else next(item for item in ability_words if item.get("ability_id", item.get("id")) == "debtbound")
        )
        cap_field = next(
            key for key in ("crack_scaling_cap", "scaling_cap", "maximum_cracks_read") if key in debtbound
        )
        debtbound[cap_field] = 6
        self.assert_invalid(document, "capped at 5")

    def test_lifesteal_and_zone_semantics_are_machine_checked(self) -> None:
        document = self.valid_copy()
        keywords = document["rules"]["keywords"]
        lifesteal = (
            keywords["lifesteal"]
            if isinstance(keywords, dict)
            else next(
                item
                for item in keywords
                if item.get("keyword", item.get("keyword_id", item.get("id")))
                == "lifesteal"
            )
        )
        if "defensive_damage_heals" in lifesteal:
            lifesteal["defensive_damage_heals"] = True
        else:
            for key in (
                "canonical_definition",
                "canonical_text",
                "canonical_rules_text",
                "description",
            ):
                if key in lifesteal:
                    lifesteal[key] = "造成伤害时回复。"
                    break
        self.assert_invalid(document, "active-attack|defensive|防守")

        document = self.valid_copy()
        semantics = document["rules"]["timing_and_zone_semantics"]
        capacity_field = next(
            key for key in ("main_board_capacity", "shared_main_board_capacity") if key in semantics
        )
        semantics[capacity_field] = 6
        self.assert_invalid(document, "capacity must be 5")

        document = self.valid_copy()
        semantics = document["rules"]["timing_and_zone_semantics"]
        replacement_field = next(
            key
            for key in ("field_replacement_counts_as_destroyed", "field_replacement_is_destruction")
            if key in semantics
        )
        semantics[replacement_field] = True
        self.assert_invalid(document, "must not count as destruction")

    def test_paper_balance_remains_an_unmeasured_next_gate_target(self) -> None:
        document = self.valid_copy()
        document["paper_balance_targets"]["validation_state"] = "playtested_and_passing"
        self.assert_invalid(document, "target_not_playtested")

        document = self.valid_copy()
        balance = document["paper_balance_targets"]
        if "future_playable_acceptance" in balance:
            balance["future_playable_acceptance"]["win_rate_range"][1] = 0.55
        else:
            key = next(
                key
                for key in ("swapped_seat_win_rate_max", "win_rate_max")
                if key in balance
            )
            balance[key] = 0.55
        self.assert_invalid(document, "0.48-0.52")

    def test_peak_crack_targets_are_locked_for_both_professions(self) -> None:
        for field, replacement, message in (
            ("oathguard_peak_cracks_per_game", [1, 4], "must remain 2-4"),
            ("pactmage_peak_cracks_per_game", [5, 9], "must remain 5-8"),
        ):
            with self.subTest(field=field):
                document = self.valid_copy()
                document["paper_balance_targets"][field] = replacement
                self.assert_invalid(document, message)

    def test_countdown_expiration_and_early_destruction_are_distinct(self) -> None:
        mutations = (
            (
                "countdown_zero_move_reason",
                "destroyed",
                "distinguish countdown expiration",
            ),
            (
                "countdown_zero_counts_as_destroyed",
                False,
                "must count as destruction",
            ),
            (
                "early_destruction_counts_as_countdown_end",
                True,
                "must not trigger countdown-end",
            ),
        )
        for field, replacement, message in mutations:
            with self.subTest(field=field):
                document = self.valid_copy()
                document["rules"]["timing_and_zone_semantics"][field] = replacement
                self.assert_invalid(document, message)

    def test_standby_history_scope_event_and_minimum_are_locked(self) -> None:
        mutations = (
            ("lo_s03", "scope", "current_owner_turn", "must be 'match'"),
            (
                "lo_s03",
                "event",
                "owned_amulet_destroyed",
                "owned_luminous_oath_amulet_left_on_countdown_zero",
            ),
            ("lo_s03", "minimum_occurrences", 2, "must be 1"),
            ("ap_s03", "scope", "match", "must be 'current_owner_turn'"),
            (
                "ap_s03",
                "event",
                "owned_amulet_left_play",
                "owned_abyssal_pact_amulet_destroyed_on_countdown_zero",
            ),
            ("ap_s03", "minimum_occurrences", 0, "must be 1"),
        )
        for history_id, field, replacement, message in mutations:
            with self.subTest(history_id=history_id, field=field):
                document = self.valid_copy()
                document["rules"]["history_semantics"][history_id][field] = replacement
                self.assert_invalid(document, message)

    def test_gameplay_roles_reject_cross_profession_wrong_role_and_missing_standby(
        self,
    ) -> None:
        document = self.valid_copy()
        oath = next(
            deck
            for deck in document["decks"]
            if deck["deck_id"] == "oathguard_luminous_oath_v1"
        )
        oath["gameplay_roles"]["starters"][0] = "AP-01"
        self.assert_invalid(document, "not in this deck's main or standby pool")

        document = self.valid_copy()
        oath = next(
            deck
            for deck in document["decks"]
            if deck["deck_id"] == "oathguard_luminous_oath_v1"
        )
        oath["gameplay_roles"]["connectors"][0] = "LO-01"
        self.assert_invalid(document, "locked ordered role list")

        document = self.valid_copy()
        oath = next(
            deck
            for deck in document["decks"]
            if deck["deck_id"] == "oathguard_luminous_oath_v1"
        )
        oath["gameplay_roles"]["payoffs"].remove("LO-S01")
        self.assert_invalid(document, "locked ordered role list")

    def test_future_visual_inventory_is_exact_and_not_generated(self) -> None:
        document = self.valid_copy()
        document["visual_assets"].pop()
        self.assert_invalid(document, "38-item")

        document = self.valid_copy()
        document["visual_assets"][0]["status"] = "generated"
        self.assert_invalid(document, "planned_not_generated")


if __name__ == "__main__":
    unittest.main()
