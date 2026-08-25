#!/usr/bin/env python3
"""Generate the committed Gate 5B C++ base catalog from the Gate 5A lock.

The ordinary CMake build compiles the committed generated source and never
invokes Python. Use this tool explicitly to regenerate it, or pass ``--check``
to verify that the checked-in source still matches the validated design lock.
"""

from __future__ import annotations

import argparse
import difflib
import json
import sys
from pathlib import Path
from typing import Mapping, Sequence


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MANIFEST = ROOT / "design/product-decks-v1/card-pool.lock.json"
DEFAULT_SCHEMA = ROOT / "design/product-decks-v1/card-pool.schema.json"
DEFAULT_OUTPUT = ROOT / "engine/src/generated/product_catalog_v2.generated.cpp"
DEFAULT_RUNTIME_MANIFEST = ROOT / "design/product-decks-v1/runtime-foundation.lock.json"
DEFAULT_RUNTIME_SCHEMA = ROOT / "design/product-decks-v1/runtime-foundation.schema.json"

sys.path.insert(0, str(ROOT))
from scripts.ci.validate_product_decks_v1 import (  # noqa: E402
    ManifestError,
    validate,
    validate_json_schema,
)


KIND = {
    "follower": "CardKind::Follower",
    "spell": "CardKind::Spell",
    "amulet": "CardKind::Amulet",
    "trap": "CardKind::Trap",
    "field": "CardKind::Field",
}
AVAILABILITY = {
    "main": "CardAvailability::MainDeck",
    "standby": "CardAvailability::Standby",
    "token": "CardAvailability::Token",
}
KEYWORD = {
    "ward": "Keyword::Ward",
    "rush": "Keyword::Rush",
    "storm": "Keyword::Storm",
    "barrier": "Keyword::Barrier",
    "bane": "Keyword::Bane",
    "lifesteal": "Keyword::Lifesteal",
}
TARGET = {
    "none": "TargetSpec::None",
    "self": "TargetSpec::Self",
    "friendly_follower": "TargetSpec::FriendlyFollower",
    "enemy_follower": "TargetSpec::EnemyFollower",
    "friendly_permanent": "TargetSpec::FriendlyPermanent",
    "enemy_permanent": "TargetSpec::EnemyPermanent",
}
CONDITION = {
    "cracks_at_least": "ConditionKind::CracksAtLeast",
    "cracks_at_most": "ConditionKind::CracksAtMost",
    "turn_repair_at_least": "ConditionKind::TurnRepairAtLeast",
    "turn_future_use_at_least": "ConditionKind::TurnFutureUseAtLeast",
    "turn_barrier_granted": "ConditionKind::TurnBarrierGranted",
    "turn_countdown_expired": "ConditionKind::TurnCountdownExpired",
    "match_repair_to_zero_at_least": "ConditionKind::MatchRepairToZeroAtLeast",
    "match_countdown_expired_at_least": "ConditionKind::MatchCountdownExpiredAtLeast",
    "leader_health_at_most": "ConditionKind::LeaderHealthAtMost",
    "controls_series_permanent": "ConditionKind::ControlsSeriesPermanent",
}


def _load_json(path: Path) -> object:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ManifestError(f"cannot read {path}: {error}") from error


def _cpp_string(value: str) -> str:
    # JSON string escaping is a strict subset of the escaping accepted by a
    # UTF-8 C++ string literal for the locked content.
    return json.dumps(value, ensure_ascii=False)


def _keyword_mask(keywords: Sequence[str]) -> str:
    if not keywords:
        return "mask(Keyword::None)"
    return " | ".join(f"mask({KEYWORD[keyword]})" for keyword in keywords)


def _row(
    card: Mapping[str, object],
    *,
    token: bool,
    implementation_status: str,
    effects_compiled: bool,
) -> str:
    stats = card.get("stats") or {}
    assert isinstance(stats, dict)
    deployment = card.get("deployment")
    if deployment is None:
        deployment_pp = 0
        condition_text = ""
        additional_cost_text = ""
    else:
        assert isinstance(deployment, dict)
        deployment_pp = int(deployment["pp_cost"])
        condition_text = str(deployment["condition_text"])
        additional_cost_text = str(deployment["additional_cost_text"] or "")

    availability = "token" if token else str(card["availability"])
    values = [
        _cpp_string(str(card["design_id"])),
        _cpp_string(str(card["name"])),
        _cpp_string(str(card["profession_id"])),
        _cpp_string(str(card["series_id"])),
        "true" if bool(card.get("neutral", False)) else "false",
        AVAILABILITY[availability],
        KIND[str(card["card_type"])],
        str(int(card.get("play_cost_pp") or 0)),
        str(int(stats.get("attack", 0))),
        str(int(stats.get("health", 0))),
        str(int(card.get("countdown") or 0)),
        "true" if bool(card.get("can_advance", False)) else "false",
        str(int(card.get("burn_pp_capacity", 0))),
        _keyword_mask(card.get("keywords", [])),
        "CardImplementationStatus::LockedNotImplemented"
        if implementation_status == "locked_not_implemented"
        else "CardImplementationStatus::ExecutableProduct",
        "true" if effects_compiled else "false",
        str(deployment_pp),
        _cpp_string(condition_text),
        _cpp_string(additional_cost_text),
        _cpp_string(str(card["canonical_rules_text"])),
    ]
    return "    {" + ", ".join(values) + "},"


def _selector(value: object) -> str:
    if value is None:
        return "{{CardKind::Follower, CardKind::Follower}}, 0, \"\", \"\", true, true"
    assert isinstance(value, dict)
    kinds = value["allowed_card_types"]
    assert isinstance(kinds, list) and 1 <= len(kinds) <= 2
    padded = [KIND[str(kind)] for kind in kinds]
    while len(padded) < 2:
        padded.append("CardKind::Follower")
    return ", ".join([
        "{{" + ", ".join(padded) + "}}",
        str(len(kinds)),
        _cpp_string(str(value["profession_id"])),
        _cpp_string(str(value["series_id"])),
        "true" if bool(value["include_main_board"]) else "false",
        "true" if bool(value["include_field"]) else "false",
    ])


def _validate_runtime_foundation(
    design: Mapping[str, object],
    runtime: Mapping[str, object],
) -> None:
    cards = design["cards"]
    tokens = design["tokens"]
    assert isinstance(cards, list) and isinstance(tokens, list)
    design_ids = {str(card["design_id"]) for card in [*cards, *tokens]}
    mode_cards = runtime["mode_cards"]
    standby_cards = runtime["standby_cards"]
    assert isinstance(mode_cards, list) and isinstance(standby_cards, list)

    expected_modes = {
        "AP-08": {"repair", "empower"},
        "NT-04": {"damage_follower", "destroy_amulet_or_field"},
    }
    actual_modes: dict[str, set[str]] = {}
    for entry in mode_cards:
        design_id = str(entry["design_id"])
        if design_id not in design_ids or design_id in actual_modes:
            raise ManifestError(f"invalid or duplicate runtime mode card {design_id!r}")
        modes = entry["modes"]
        actual_modes[design_id] = {str(mode["mode_id"]) for mode in modes}
        if len(actual_modes[design_id]) != len(modes):
            raise ManifestError(f"runtime mode IDs must be unique for {design_id}")
    if actual_modes != expected_modes:
        raise ManifestError(f"runtime modal contract mismatch: {actual_modes!r}")

    expected_standby = {
        "LO-S01", "LO-S02", "LO-S03", "LO-S04",
        "AP-S01", "AP-S02", "AP-S03", "AP-S04",
    }
    actual_standby = {str(entry["design_id"]) for entry in standby_cards}
    if actual_standby != expected_standby or len(actual_standby) != len(standby_cards):
        raise ManifestError("runtime standby contract must describe each of the eight locked cards once")
    for entry in standby_cards:
        if not entry["conditions"]:
            raise ManifestError(f"standby card {entry['design_id']} requires typed conditions")
        if entry["design_id"] != "AP-S04" and entry["additional_cost"] is not None:
            raise ManifestError(f"only AP-S04 has a locked additional deployment cost")

    ap_s04 = next(entry for entry in standby_cards if entry["design_id"] == "AP-S04")
    cost = ap_s04["additional_cost"]
    if cost is None:
        raise ManifestError("AP-S04 requires its locked additional cost")
    filter_spec = cost["filter"]
    if (
        set(filter_spec["allowed_card_types"]) != {"follower", "amulet"}
        or filter_spec["profession_id"] != "pactmage"
        or filter_spec["series_id"] != "abyssal_pact"
        or not filter_spec["include_main_board"]
        or filter_spec["include_field"]
    ):
        raise ManifestError("AP-S04 additional cost must select one own abyssal-pact follower or amulet")


def render(
    document: Mapping[str, object],
    runtime: Mapping[str, object],
) -> str:
    cards = document["cards"]
    tokens = document["tokens"]
    capabilities = document["capability_catalog"]
    assert isinstance(cards, list) and isinstance(tokens, list) and isinstance(capabilities, list)
    defaults = runtime["default_definition"]
    assert isinstance(defaults, dict)
    implementation_status = str(defaults["implementation_status"])
    effects_compiled = bool(defaults["effects_compiled"])
    rows = [
        _row(
            card,
            token=False,
            implementation_status=implementation_status,
            effects_compiled=effects_compiled,
        )
        for card in cards
    ]
    rows.extend(
        _row(
            token,
            token=True,
            implementation_status=implementation_status,
            effects_compiled=effects_compiled,
        )
        for token in tokens
    )
    body = "\n".join(rows)

    mode_rows: list[str] = []
    for card in runtime["mode_cards"]:
        for mode in card["modes"]:
            mode_rows.append(
                "    {" + ", ".join([
                    _cpp_string(str(card["design_id"])),
                    _cpp_string(str(mode["mode_id"])),
                    _cpp_string(str(mode["label"])),
                    TARGET[str(mode["target"])],
                    "{" + _selector(mode["target_filter"]) + "}",
                ]) + "},"
            )
    mode_body = "\n".join(mode_rows)

    condition_rows: list[str] = []
    cost_rows: list[str] = []
    for card in runtime["standby_cards"]:
        for condition in card["conditions"]:
            condition_rows.append(
                "    {" + ", ".join([
                    _cpp_string(str(card["design_id"])),
                    CONDITION[str(condition["kind"])],
                    _cpp_string(str(condition["condition_id"])),
                    str(int(condition["threshold"])),
                    str(int(condition["read_cap"])),
                    _cpp_string(str(condition["parameter"])),
                    "{" + _selector(condition["permanent_filter"]) + "}",
                ]) + "},"
            )
        additional_cost = card["additional_cost"]
        if additional_cost is not None:
            cost_rows.append(
                "    {" + ", ".join([
                    _cpp_string(str(card["design_id"])),
                    TARGET[str(additional_cost["target"])],
                    str(int(additional_cost["minimum"])),
                    str(int(additional_cost["maximum"])),
                    "{" + _selector(additional_cost["filter"]) + "}",
                ]) + "},"
            )
    condition_body = "\n".join(condition_rows)
    cost_body = "\n".join(cost_rows)
    required_capabilities = [
        str(capability["capability_id"])
        for capability in capabilities
        if capability["default_status"] in {"fix", "new"}
    ]
    fix_count = sum(capability["default_status"] == "fix" for capability in capabilities)
    new_count = sum(capability["default_status"] == "new" for capability in capabilities)
    if fix_count != 9 or new_count != 33 or len(required_capabilities) != 42:
        raise ManifestError(
            "Gate 5B executable capability registry must contain exactly 9 fix + 33 new entries"
        )
    capability_body = "\n".join(
        f"    {_cpp_string(capability_id)}," for capability_id in required_capabilities
    )
    return f'''// SPDX-License-Identifier: GPL-3.0-or-later
// GENERATED FILE. Sources: design/product-decks-v1/card-pool.lock.json and
// design/product-decks-v1/runtime-foundation.lock.json.
// Regenerate with scripts/design/generate_product_catalog_v2.py.
#include "scgs/product_runtime.hpp"

#include <array>
#include <string_view>

namespace scgs::v2 {{
namespace {{

struct GeneratedCardRow {{
    std::string_view design_id;
    std::string_view name;
    std::string_view profession_id;
    std::string_view series_id;
    bool neutral;
    CardAvailability availability;
    CardKind kind;
    int cost;
    int attack;
    int health;
    int countdown;
    bool can_advance;
    int burn_pp_capacity;
    KeywordMask printed_keywords;
    CardImplementationStatus implementation_status;
    bool effects_compiled;
    int standby_pp_cost;
    std::string_view standby_condition_text;
    std::string_view standby_additional_cost_text;
    std::string_view canonical_rules_text;
}};

struct GeneratedSelectorRow {{
    std::array<CardKind, 2> allowed_kinds;
    std::size_t allowed_kind_count;
    std::string_view profession_id;
    std::string_view series_id;
    bool include_main_board;
    bool include_field;
}};

struct GeneratedModeRow {{
    std::string_view design_id;
    std::string_view mode_id;
    std::string_view label;
    TargetSpec target;
    GeneratedSelectorRow target_filter;
}};

struct GeneratedConditionRow {{
    std::string_view design_id;
    ConditionKind kind;
    std::string_view condition_id;
    int threshold;
    int read_cap;
    std::string_view parameter;
    GeneratedSelectorRow permanent_filter;
}};

struct GeneratedAdditionalCostRow {{
    std::string_view design_id;
    TargetSpec target;
    std::size_t minimum;
    std::size_t maximum;
    GeneratedSelectorRow filter;
}};

inline constexpr std::array<GeneratedCardRow, {len(rows)}> kGeneratedCards = {{{{
{body}
}}}};

inline constexpr std::array<GeneratedModeRow, {len(mode_rows)}> kGeneratedModes = {{{{
{mode_body}
}}}};

inline constexpr std::array<GeneratedConditionRow, {len(condition_rows)}> kGeneratedConditions = {{{{
{condition_body}
}}}};

inline constexpr std::array<GeneratedAdditionalCostRow, {len(cost_rows)}> kGeneratedAdditionalCosts = {{{{
{cost_body}
}}}};

inline constexpr std::array<std::string_view, {len(required_capabilities)}> kRequiredCapabilities = {{{{
{capability_body}
}}}};

PermanentSelectorSpec make_selector(const GeneratedSelectorRow& row) {{
    PermanentSelectorSpec selector;
    selector.allowed_kinds.assign(
        row.allowed_kinds.begin(),
        row.allowed_kinds.begin() + static_cast<std::ptrdiff_t>(row.allowed_kind_count));
    selector.profession_id = row.profession_id;
    selector.series_id = row.series_id;
    selector.include_main_board = row.include_main_board;
    selector.include_field = row.include_field;
    return selector;
}}

}} // namespace

CardCatalog make_locked_product_catalog() {{
    CardCatalog catalog;
    for (const GeneratedCardRow& row : kGeneratedCards) {{
        CardDefinition definition;
        definition.identity = CardIdentity{{
            std::string(row.design_id),
            std::string(row.profession_id),
            std::string(row.series_id),
            row.neutral,
        }};
        definition.name = row.name;
        definition.availability = row.availability;
        definition.kind = row.kind;
        definition.cost = row.cost;
        definition.attack = row.attack;
        definition.health = row.health;
        definition.countdown = row.countdown;
        definition.can_advance = row.can_advance;
        definition.burn_pp_capacity = row.burn_pp_capacity;
        definition.printed_keywords = row.printed_keywords;
        definition.implementation_status = row.implementation_status;
        definition.effects_compiled = row.effects_compiled;
        definition.canonical_rules_text = row.canonical_rules_text;
        for (const GeneratedModeRow& generated_mode : kGeneratedModes) {{
            if (generated_mode.design_id != row.design_id) {{
                continue;
            }}
            ModeSpec mode;
            mode.mode_id = generated_mode.mode_id;
            mode.label = generated_mode.label;
            mode.target = generated_mode.target;
            mode.target_filter = make_selector(generated_mode.target_filter);
            // Gate 5B deliberately generates shape-only modes. Their effect
            // programs remain empty until Gate 5C marks the card executable.
            definition.modes.push_back(std::move(mode));
        }}
        if (row.availability == CardAvailability::Standby) {{
            StandbySpec standby;
            standby.pp_cost = row.standby_pp_cost;
            for (const GeneratedConditionRow& generated_condition : kGeneratedConditions) {{
                if (generated_condition.design_id != row.design_id) {{
                    continue;
                }}
                ConditionSpec condition;
                condition.kind = generated_condition.kind;
                condition.condition_id = generated_condition.condition_id;
                condition.threshold = generated_condition.threshold;
                condition.read_cap = generated_condition.read_cap;
                condition.parameter = generated_condition.parameter;
                condition.permanent_filter = make_selector(generated_condition.permanent_filter);
                standby.conditions.push_back(std::move(condition));
            }}
            for (const GeneratedAdditionalCostRow& generated_cost : kGeneratedAdditionalCosts) {{
                if (generated_cost.design_id != row.design_id) {{
                    continue;
                }}
                standby.requires_additional_cost = true;
                standby.additional_cost_target = generated_cost.target;
                standby.additional_cost_filter = make_selector(generated_cost.filter);
                standby.additional_cost_minimum = generated_cost.minimum;
                standby.additional_cost_maximum = generated_cost.maximum;
            }}
            standby.condition_text = row.standby_condition_text;
            standby.additional_cost_text = row.standby_additional_cost_text;
            definition.standby = std::move(standby);
        }}
        catalog.add(std::move(definition));
    }}
    return catalog;
}}

std::span<const std::string_view> required_product_capability_ids() noexcept {{
    return kRequiredCapabilities;
}}

}} // namespace scgs::v2
'''


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--schema", type=Path, default=DEFAULT_SCHEMA)
    parser.add_argument("--runtime-manifest", type=Path, default=DEFAULT_RUNTIME_MANIFEST)
    parser.add_argument("--runtime-schema", type=Path, default=DEFAULT_RUNTIME_SCHEMA)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args(argv)

    try:
        document = _load_json(args.manifest.resolve(strict=True))
        schema = _load_json(args.schema.resolve(strict=True))
        validate(document, schema)
        if not isinstance(document, dict):
            raise ManifestError("the validated product manifest root is not an object")
        runtime = _load_json(args.runtime_manifest.resolve(strict=True))
        runtime_schema = _load_json(args.runtime_schema.resolve(strict=True))
        validate_json_schema(runtime, runtime_schema)
        if not isinstance(runtime, dict):
            raise ManifestError("the validated runtime foundation root is not an object")
        _validate_runtime_foundation(document, runtime)
        generated = render(document, runtime)
        output = args.output.resolve()
        if args.check:
            current = output.read_text(encoding="utf-8")
            if current != generated:
                diff = "".join(difflib.unified_diff(
                    current.splitlines(keepends=True),
                    generated.splitlines(keepends=True),
                    fromfile=str(output),
                    tofile="regenerated",
                ))
                print("committed product catalog is stale:\n" + diff, file=sys.stderr)
                return 1
            print(f"verified generated product catalog: {output}")
            return 0
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(generated, encoding="utf-8", newline="\n")
        print(f"generated product catalog: {output}")
        return 0
    except (OSError, ManifestError, KeyError, TypeError, ValueError) as error:
        print(f"product catalog generation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
