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
    _validate_effect_programs,
    _validate_runtime_foundation,
    render,
)


def _find_effect(programs: dict[str, object], effect_id: str) -> dict[str, object]:
    definitions = programs["definitions"]
    assert isinstance(definitions, list)
    for definition in definitions:
        assert isinstance(definition, dict)
        candidates = list(definition["effects"])
        for mode in definition["modes"]:
            candidates.extend(mode["effects"])
        for effect in candidates:
            if effect["effect_id"] == effect_id:
                return effect
    raise AssertionError(f"missing fixture effect {effect_id}")


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
        cls.effects = json.loads(
            (ROOT / "design/product-decks-v1/product-effects.lock.json").read_text(
                encoding="utf-8"
            )
        )
        cls.effects_schema = json.loads(
            (ROOT / "design/product-decks-v1/product-effects.schema.json").read_text(
                encoding="utf-8"
            )
        )

    def test_runtime_foundation_schema_and_cross_contract(self) -> None:
        validate_json_schema(self.runtime, self.runtime_schema)
        _validate_runtime_foundation(self.design, self.runtime)

    def test_effect_schema_and_cross_contract(self) -> None:
        validate_json_schema(self.effects, self.effects_schema)
        _validate_effect_programs(self.design, self.runtime, self.effects)

    def test_effect_schema_rejects_untyped_program_values(self) -> None:
        mutated = copy.deepcopy(self.effects)
        mutated["definitions"][0]["effects"][0]["value"]["source"] = "design-specific"
        with self.assertRaises(ManifestError):
            validate_json_schema(mutated, self.effects_schema)

    def test_effect_schema_rejects_missing_kind_operands(self) -> None:
        mutations = (
            ("lo04-seamless-barrier", "keyword", None),
            ("ap03-base-damage", "target", None),
            ("lo03-expire-summon", "parameter", ""),
            ("lo01-search", "card_filter", None),
            ("nt04-destroy", "target_filter", None),
            ("ap05-play-bottom", "selection_maximum", 0),
            ("lo07-cancel", "trigger", "on_play"),
        )
        for effect_id, field, replacement in mutations:
            with self.subTest(effect_id=effect_id, field=field):
                mutated = copy.deepcopy(self.effects)
                effect = _find_effect(mutated, effect_id)
                if replacement is None:
                    effect.pop(field)
                else:
                    effect[field] = replacement
                with self.assertRaises(ManifestError):
                    validate_json_schema(mutated, self.effects_schema)

    def test_cross_contract_rejects_missing_kind_operands_without_schema(self) -> None:
        mutations = (
            ("lo04-seamless-barrier", "keyword", None),
            ("ap03-base-damage", "target", None),
            ("lo03-expire-summon", "parameter", ""),
            ("lo01-search", "card_filter", None),
            ("nt04-destroy", "target_filter", None),
            ("ap05-play-bottom", "selection_maximum", 0),
            ("lo07-cancel", "trigger", "on_play"),
        )
        for effect_id, field, replacement in mutations:
            with self.subTest(effect_id=effect_id, field=field):
                mutated = copy.deepcopy(self.effects)
                effect = _find_effect(mutated, effect_id)
                if replacement is None:
                    effect.pop(field)
                else:
                    effect[field] = replacement
                with self.assertRaisesRegex(ManifestError, effect_id):
                    _validate_effect_programs(self.design, self.runtime, mutated)

    def test_render_contains_typed_executable_programs(self) -> None:
        generated = render(self.design, self.runtime, self.effects)
        self.assertNotIn("CardImplementationStatus::LockedNotImplemented, false", generated)
        self.assertEqual(35, generated.count("CardImplementationStatus::ExecutableProduct, true"))
        self.assertIn('{"AP-08", "repair", "修复2", TargetSpec::None', generated)
        self.assertIn('ConditionKind::MatchRepairToZeroAtLeast', generated)
        self.assertIn(
            '{"AP-S04", TargetSpec::FriendlyPermanent, 1, 1, '
            '{{{CardKind::Follower, CardKind::Amulet}}, 2, "pactmage", '
            '"abyssal_pact", true, false}',
            generated,
        )
        self.assertIn('effect.effect_id = "lo01-search";', generated)
        self.assertIn('effect.kind = EffectKind::SearchTop;', generated)
        self.assertIn('effect.card_filter.excluded_kinds = {CardKind::Follower};', generated)
        self.assertIn('effect.value = ValueSpec{AmountSource::Cracks, 0, 1, 5};', generated)
        self.assertIn('effect.dependency = EffectDependency::PreviousDrawEnteredHand;', generated)
        self.assertIn('effect.once_scope = OnceScope::SourceOwnerTurn;', generated)
        self.assertIn('effect.kind = EffectKind::CancelAttack;', generated)
        self.assertIn('effect.preserve_source_slot = true;', generated)
        self.assertIn('effect.target_from_effect_id = "lo10-first-repair-buff";', generated)
        self.assertIn('effect.uses_secondary_amount = true;', generated)
        self.assertIn('std::vector<ProductDeckDefinition> make_locked_product_decks()', generated)
        self.assertEqual(2, generated.count("deck.main_deck = {"))
        self.assertEqual(2, generated.count("deck.standby = {"))

    def test_every_definition_is_executable_and_program_identity_is_unique(self) -> None:
        definitions = self.effects["definitions"]
        self.assertEqual(35, len(definitions))
        self.assertEqual(35, len({definition["design_id"] for definition in definitions}))
        effect_ids = []
        for definition in definitions:
            effect_ids.extend(effect["effect_id"] for effect in definition["effects"])
            for mode in definition["modes"]:
                effect_ids.extend(effect["effect_id"] for effect in mode["effects"])
        self.assertEqual(56, len(effect_ids))
        self.assertEqual(len(effect_ids), len(set(effect_ids)))
        self.assertEqual(
            {"executable_product", True},
            {
                self.effects["default_definition"]["implementation_status"],
                self.effects["default_definition"]["effects_compiled"],
            },
        )

    def test_locked_product_decks_keep_exact_playable_counts(self) -> None:
        self.assertEqual(2, len(self.design["decks"]))
        definition_ids = {definition["design_id"] for definition in self.effects["definitions"]}
        for deck in self.design["decks"]:
            self.assertEqual(15, len(deck["main"]))
            self.assertEqual(30, sum(entry["copies"] for entry in deck["main"]))
            self.assertEqual(4, len(deck["standby"]))
            self.assertEqual(4, sum(entry["copies"] for entry in deck["standby"]))
            for entry in [*deck["main"], *deck["standby"]]:
                self.assertIn(entry["design_id"], definition_ids)

    def test_generator_rejects_non_product_deck_expansion(self) -> None:
        mutated = copy.deepcopy(self.design)
        mutated["decks"][0]["main"][0]["copies"] = 2
        with self.assertRaisesRegex(ManifestError, "exactly 30 main \\+ 4 standby"):
            render(mutated, self.runtime, self.effects)

    def test_dependencies_are_ordered_and_cannot_cross_programs(self) -> None:
        mutated = copy.deepcopy(self.effects)
        lo02 = next(card for card in mutated["definitions"] if card["design_id"] == "LO-02")
        lo02["effects"][1]["depends_on_effect_id"] = "ap05-play-draw"
        validate_json_schema(mutated, self.effects_schema)
        with self.assertRaisesRegex(ManifestError, "earlier effect in the same program"):
            _validate_effect_programs(self.design, self.runtime, mutated)

    def test_all_35_product_rows_are_required(self) -> None:
        mutated = copy.deepcopy(self.effects)
        mutated["definitions"][-1]["design_id"] = "LO-01"
        validate_json_schema(mutated, self.effects_schema)
        with self.assertRaisesRegex(ManifestError, "35 definitions exactly once"):
            _validate_effect_programs(self.design, self.runtime, mutated)

    def test_mode_effects_must_match_frozen_mode_contract(self) -> None:
        mutated = copy.deepcopy(self.effects)
        ap08 = next(card for card in mutated["definitions"] if card["design_id"] == "AP-08")
        ap08["modes"][0]["mode_id"] = "not-a-mode"
        validate_json_schema(mutated, self.effects_schema)
        with self.assertRaisesRegex(ManifestError, "frozen runtime mode contract"):
            _validate_effect_programs(self.design, self.runtime, mutated)

    def test_program_preserves_countdown_and_explicit_zero_health_semantics(self) -> None:
        effects_by_id = {}
        for definition in self.effects["definitions"]:
            for effect in definition["effects"]:
                effects_by_id[effect["effect_id"]] = effect
            for mode in definition["modes"]:
                for effect in mode["effects"]:
                    effects_by_id[effect["effect_id"]] = effect
        self.assertEqual(1, effects_by_id["lo03-zero-countdown"]["value"]["fixed"])
        self.assertEqual(1, effects_by_id["ap04-future-countdown"]["value"]["fixed"])
        self.assertIn("secondary_amount", effects_by_id["ap11-on-time-attack"])
        self.assertEqual(0, effects_by_id["ap11-on-time-attack"]["secondary_amount"])
        self.assertEqual(
            "lo10-first-repair-buff",
            effects_by_id["lo10-seamless-barrier"]["target_from_effect_id"],
        )
        once_effects = [effect for effect in effects_by_id.values() if effect.get("once")]
        self.assertTrue(once_effects)
        self.assertEqual(
            {"lo08-kill-repair", "los02-kill-repair"},
            {effect["effect_id"] for effect in once_effects
             if effect["once"]["scope"] == "source_turn"},
        )
        self.assertTrue(all(
            effect["once"]["scope"] == "source_owner_turn"
            for effect in once_effects
            if effect["effect_id"] not in {"lo08-kill-repair", "los02-kill-repair"}
        ))
        self.assertTrue(
            all(effect["once"]["consumption"] == "on_trigger" for effect in once_effects)
        )
        lo_s01 = effects_by_id["los01-seamless-buff"]
        self.assertEqual("", lo_s01["target_filter"]["profession_id"])
        self.assertEqual("", lo_s01["target_filter"]["series_id"])
        self.assertTrue(lo_s01["target_filter"]["exclude_source"])
        self.assertEqual(
            "opponent_of_source_controller",
            effects_by_id["lo07-repair"]["trigger_player_relation"],
        )
        for effect_id in (
            "lo03-zero-countdown", "lo10-first-repair-buff",
            "ap04-future-countdown", "ap05-future-draw",
        ):
            self.assertEqual(
                "source_controller", effects_by_id[effect_id]["trigger_player_relation"]
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
