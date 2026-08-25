#!/usr/bin/env python3
"""Validate the locked, design-only Gate 5A product deck manifest.

The repository intentionally does not add a third-party JSON Schema package for
this design gate.  The small evaluator below implements the draft-2020-12
keywords used by ``card-pool.schema.json`` and is followed by cross-document
checks which JSON Schema cannot express clearly (deck totals, profession
legality, reference integrity, and the locked rules/balance contracts).
"""

# SPDX-License-Identifier: GPL-3.0-or-later

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import sys
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MANIFEST = ROOT / "design/product-decks-v1/card-pool.lock.json"
DEFAULT_SCHEMA = ROOT / "design/product-decks-v1/card-pool.schema.json"

EXPECTED_NEUTRALS = {"NT-01", "NT-02", "NT-03", "NT-04"}
EXPECTED_PROFESSIONS = {"oathguard", "pactmage"}
EXPECTED_KEYWORDS = {"ward", "rush", "storm", "barrier", "bane", "lifesteal"}
REPAIR_CARDS_THAT_CANNOT_ADVANCE = {
    "LO-02",
    "LO-03",
    "LO-06",
    "LO-07",
    "LO-09",
    "AP-08",
}
REQUIRED_NEXT_GATE_CAPABILITIES = {
    "profession_series_neutral_tags",
    "amulet_main_board",
    "field_zone",
    "field_replacement_without_destroy",
    "filtered_top_deck_search",
    "randomized_deck_bottom",
    "hand_to_deck_bottom",
    "discard_from_hand",
    "summon_token_original_slot",
    "modal_choice",
    "dynamic_crack_threshold",
    "repair_to_zero_trigger",
    "future_use_trigger",
    "combat_kill_survive_trigger",
    "permanent_keyword_grant",
    "permanent_targeting",
}
EXPECTED_GAMEPLAY_ROLES = {
    "oathguard_luminous_oath_v1": {
        "starters": ["LO-01", "LO-03", "LO-04", "NT-01"],
        "connectors": ["LO-02", "LO-05", "LO-06", "LO-07", "LO-10"],
        "payoffs": ["LO-04", "LO-08", "LO-10", "LO-S01", "LO-S02", "LO-S03"],
        "recovery": ["LO-02", "LO-06", "LO-07", "LO-08", "LO-09", "NT-03"],
        "finishers": ["LO-11", "LO-S04"],
    },
    "pactmage_abyssal_pact_v1": {
        "starters": ["AP-01", "AP-04", "AP-02"],
        "connectors": ["AP-03", "AP-05", "AP-06", "AP-08"],
        "payoffs": ["AP-06", "AP-07", "AP-09", "AP-10", "AP-S01", "AP-S03"],
        "recovery": ["AP-07", "AP-08", "AP-09", "AP-S02", "AP-S03", "NT-03"],
        "finishers": ["AP-11", "AP-S04"],
    },
}

# Canonical SHA-256 digests make the design lock enforce content, not merely
# JSON shape.  Each partition is deliberately small enough that drift reports
# the product area which changed.  Update a digest only as part of an explicit
# design-lock revision which updates the manifest, design documents, and tests
# together.
LOCKED_PRODUCT_SECTION_SHA256 = {
    "metadata": "643e33a1bf15d5828e040337fa79aa9af3a0ea9a76edcb88f2fd69acaa437ee3",
    "rules": "286c567f3bf79446955100ca2fdf56836409a6e9ae04114f0ea56a6e469145cd",
    "classes": "82b8224ae7fbec1f00c81293b7ccd596543611e3405fdb1c60bde8b0360367e6",
    "leaders": "92967f864605382512f1753bc7dbc19454e8c726bbf5b061f13924fbf2cbdeef",
    "card_types": "2916268768717db253be311f204373e633ee50b3181ca843a7df263f68e6e8a0",
    "keywords": "0ffd3429d7a43047474f976aca22b04a680f39907fcf81192237214ff8ea93d4",
    "capability_catalog": "1c5a5ed33e6622ca5717f7efcfe5ed705fdb57a50fe474140f69ccef6f507e2c",
    "tokens": "89188ca2afdfcf1c2a77ff75b7971e091b7a6a824ea62ea4649c1012fdb4b0d3",
    "cards": "375f8d40cc065214f99908d5869cacade66e51f28e579334dcbacad94023f672",
    "decks": "de2490603bdedcfe7a1c0930845cbb3d437cea660c9503e42b87b3ff1f615102",
    "visual_assets": "a41640165fd4b5656d22ab5657c138aa178519d6c37c435a102e42564fdde427",
    "paper_balance_targets": "be34c98f5bb2eb9afaba4e4e350a1bbc70705ca5ad68b04c1596bbc590c9e8bb",
    "art_direction": "5f826b1b79436796c02c8cc3ff5390d3a891be8416af6253c51157cfa4d44d17",
    "migration_policy": "5ea3287b955f474ee7a208ccf3c03a6e82163c12f66471fda7c2860cdd8ddb8e",
}


class ManifestError(ValueError):
    """Raised when the Gate 5A design contract is not satisfied."""


def _fail(path: str, message: str) -> None:
    raise ManifestError(f"{path}: {message}")


def _json_type_matches(value: object, expected: str) -> bool:
    if expected == "null":
        return value is None
    if expected == "boolean":
        return isinstance(value, bool)
    if expected == "integer":
        return isinstance(value, int) and not isinstance(value, bool)
    if expected == "number":
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    if expected == "string":
        return isinstance(value, str)
    if expected == "array":
        return isinstance(value, list)
    if expected == "object":
        return isinstance(value, dict)
    return False


def _resolve_pointer(root: object, pointer: str) -> object:
    if not pointer.startswith("#/"):
        raise ManifestError(f"schema uses unsupported non-local $ref {pointer!r}")
    current = root
    for raw_part in pointer[2:].split("/"):
        part = raw_part.replace("~1", "/").replace("~0", "~")
        if not isinstance(current, dict) or part not in current:
            raise ManifestError(f"schema contains unresolved $ref {pointer!r}")
        current = current[part]
    return current


def _schema_error(instance: object, schema: object, root: object, path: str) -> str | None:
    """Return the first schema mismatch for the supported schema subset."""

    if isinstance(schema, bool):
        return None if schema else f"{path}: rejected by false schema"
    if not isinstance(schema, dict):
        return f"{path}: schema node must be an object or boolean"

    if "$ref" in schema:
        referenced = _resolve_pointer(root, schema["$ref"])
        error = _schema_error(instance, referenced, root, path)
        if error is not None:
            return error

    for child in schema.get("allOf", []):
        error = _schema_error(instance, child, root, path)
        if error is not None:
            return error
    if "anyOf" in schema:
        errors = [_schema_error(instance, child, root, path) for child in schema["anyOf"]]
        if all(error is not None for error in errors):
            return f"{path}: does not satisfy anyOf ({'; '.join(str(e) for e in errors)})"
    if "oneOf" in schema:
        matches = sum(
            _schema_error(instance, child, root, path) is None
            for child in schema["oneOf"]
        )
        if matches != 1:
            return f"{path}: must satisfy exactly one oneOf branch (matched {matches})"
    if "not" in schema and _schema_error(instance, schema["not"], root, path) is None:
        return f"{path}: matches a forbidden schema"
    if "if" in schema:
        branch = "then" if _schema_error(instance, schema["if"], root, path) is None else "else"
        if branch in schema:
            error = _schema_error(instance, schema[branch], root, path)
            if error is not None:
                return error

    expected_type = schema.get("type")
    if expected_type is not None:
        type_names = [expected_type] if isinstance(expected_type, str) else expected_type
        if not isinstance(type_names, list) or not all(isinstance(item, str) for item in type_names):
            return f"{path}: schema type must be a string or string array"
        if not any(_json_type_matches(instance, item) for item in type_names):
            return f"{path}: expected type {' or '.join(type_names)}, found {type(instance).__name__}"

    if "const" in schema and instance != schema["const"]:
        return f"{path}: expected constant {schema['const']!r}"
    if "enum" in schema and instance not in schema["enum"]:
        return f"{path}: value {instance!r} is not in the allowed enum"

    if isinstance(instance, dict):
        required = schema.get("required", [])
        for key in required:
            if key not in instance:
                return f"{path}: missing required property {key!r}"
        properties = schema.get("properties", {})
        pattern_properties = schema.get("patternProperties", {})
        for key, value in instance.items():
            child_schemas: list[object] = []
            if key in properties:
                child_schemas.append(properties[key])
            for pattern, child in pattern_properties.items():
                if re.search(pattern, key):
                    child_schemas.append(child)
            if not child_schemas:
                additional = schema.get("additionalProperties", True)
                if additional is False:
                    return f"{path}: unexpected property {key!r}"
                if isinstance(additional, dict):
                    child_schemas.append(additional)
            for child in child_schemas:
                error = _schema_error(value, child, root, f"{path}.{key}")
                if error is not None:
                    return error
        if len(instance) < schema.get("minProperties", 0):
            return f"{path}: has too few properties"
        if "maxProperties" in schema and len(instance) > schema["maxProperties"]:
            return f"{path}: has too many properties"
        for key, dependencies in schema.get("dependentRequired", {}).items():
            if key in instance:
                for dependency in dependencies:
                    if dependency not in instance:
                        return f"{path}: property {key!r} requires {dependency!r}"

    if isinstance(instance, list):
        if len(instance) < schema.get("minItems", 0):
            return f"{path}: has too few items"
        if "maxItems" in schema and len(instance) > schema["maxItems"]:
            return f"{path}: has too many items"
        if schema.get("uniqueItems"):
            serialized = [json.dumps(item, ensure_ascii=False, sort_keys=True) for item in instance]
            if len(serialized) != len(set(serialized)):
                return f"{path}: items must be unique"
        prefix_items = schema.get("prefixItems", [])
        for index, child in enumerate(prefix_items[: len(instance)]):
            error = _schema_error(instance[index], child, root, f"{path}[{index}]")
            if error is not None:
                return error
        items = schema.get("items")
        if items is not None:
            start = len(prefix_items) if prefix_items else 0
            for index in range(start, len(instance)):
                error = _schema_error(instance[index], items, root, f"{path}[{index}]")
                if error is not None:
                    return error
        if "contains" in schema:
            matches = sum(
                _schema_error(item, schema["contains"], root, f"{path}[{index}]") is None
                for index, item in enumerate(instance)
            )
            minimum = schema.get("minContains", 1)
            maximum = schema.get("maxContains", math.inf)
            if matches < minimum or matches > maximum:
                return f"{path}: contains matched {matches}, expected [{minimum}, {maximum}]"

    if isinstance(instance, str):
        if len(instance) < schema.get("minLength", 0):
            return f"{path}: string is too short"
        if "maxLength" in schema and len(instance) > schema["maxLength"]:
            return f"{path}: string is too long"
        if "pattern" in schema and re.search(schema["pattern"], instance) is None:
            return f"{path}: string does not match {schema['pattern']!r}"

    if isinstance(instance, (int, float)) and not isinstance(instance, bool):
        if "minimum" in schema and instance < schema["minimum"]:
            return f"{path}: number is below minimum {schema['minimum']}"
        if "maximum" in schema and instance > schema["maximum"]:
            return f"{path}: number is above maximum {schema['maximum']}"
        if "exclusiveMinimum" in schema and instance <= schema["exclusiveMinimum"]:
            return f"{path}: number must be greater than {schema['exclusiveMinimum']}"
        if "exclusiveMaximum" in schema and instance >= schema["exclusiveMaximum"]:
            return f"{path}: number must be less than {schema['exclusiveMaximum']}"

    return None


def validate_json_schema(document: object, schema: object) -> None:
    """Validate *document* against the checked-in standard-library subset."""

    if not isinstance(schema, dict):
        _fail("schema", "root must be an object")
    dialect = schema.get("$schema")
    if dialect is not None and dialect != "https://json-schema.org/draft/2020-12/schema":
        _fail("schema.$schema", "must use JSON Schema draft 2020-12")
    error = _schema_error(document, schema, schema, "$")
    if error is not None:
        raise ManifestError(f"schema validation failed: {error}")


def _object(value: object, path: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        _fail(path, "must be an object")
    return value


def _array(value: object, path: str) -> list[Any]:
    if not isinstance(value, list):
        _fail(path, "must be an array")
    return value


def _string(value: object, path: str) -> str:
    if not isinstance(value, str) or not value.strip():
        _fail(path, "must be a non-empty string")
    return value


def _integer(value: object, path: str, minimum: int | None = None) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        _fail(path, "must be an integer")
    if minimum is not None and value < minimum:
        _fail(path, f"must be at least {minimum}")
    return value


def _required_value(mapping: Mapping[str, object], key: str, path: str) -> object:
    if key not in mapping:
        _fail(path, f"missing locked property {key!r}")
    return mapping[key]


def _canonical_json_sha256(value: object) -> str:
    canonical = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def _product_design_sections(document: Mapping[str, object]) -> dict[str, object]:
    metadata_fields = (
        "$schema",
        "schema_version",
        "status",
        "design_id_policy",
        "implementation_scope",
        "format",
    )
    metadata = {
        field: _required_value(document, field, "$.metadata")
        for field in metadata_fields
    }
    rules = _object(
        _required_value(document, "rules", "$.rules"),
        "$.rules",
    )
    card_types = _required_value(rules, "card_types", "$.rules.card_types")
    keywords = _required_value(rules, "keywords", "$.rules.keywords")
    remaining_rules = {
        key: value
        for key, value in rules.items()
        if key not in {"card_types", "keywords"}
    }

    return {
        "metadata": metadata,
        "rules": remaining_rules,
        "classes": _required_value(document, "professions", "$.professions"),
        "leaders": _required_value(document, "leaders", "$.leaders"),
        "card_types": card_types,
        "keywords": keywords,
        "capability_catalog": _required_value(
            document,
            "capability_catalog",
            "$.capability_catalog",
        ),
        "tokens": _required_value(document, "tokens", "$.tokens"),
        "cards": _required_value(document, "cards", "$.cards"),
        "decks": _required_value(document, "decks", "$.decks"),
        "visual_assets": _required_value(
            document,
            "visual_assets",
            "$.visual_assets",
        ),
        "paper_balance_targets": _required_value(
            document,
            "paper_balance_targets",
            "$.paper_balance_targets",
        ),
        "art_direction": _required_value(
            document,
            "art_direction",
            "$.art_direction",
        ),
        "migration_policy": _required_value(
            document,
            "legacy_product_migration",
            "$.legacy_product_migration",
        ),
    }


def _validate_locked_product_sections(document: Mapping[str, object]) -> None:
    sections = _product_design_sections(document)
    if set(sections) != set(LOCKED_PRODUCT_SECTION_SHA256):
        _fail(
            "$.locked_product_sections",
            "internal section map and SHA-256 table differ",
        )
    for section_name, expected_digest in LOCKED_PRODUCT_SECTION_SHA256.items():
        actual_digest = _canonical_json_sha256(sections[section_name])
        if actual_digest != expected_digest:
            _fail(
                f"$.locked_product_sections.{section_name}",
                f"locked product design section {section_name!r} drifted: "
                f"expected SHA-256 {expected_digest}, got {actual_digest}",
            )


def _id_map(entries: object, id_field: str, path: str) -> dict[str, dict[str, Any]]:
    result: dict[str, dict[str, Any]] = {}
    for index, raw_entry in enumerate(_array(entries, path)):
        entry = _object(raw_entry, f"{path}[{index}]")
        identity = _string(entry.get(id_field), f"{path}[{index}].{id_field}")
        if identity in result:
            _fail(path, f"duplicate {id_field} {identity!r}")
        result[identity] = entry
    return result


def _entry_id(entry: Mapping[str, object], candidates: Sequence[str], path: str) -> str:
    for candidate in candidates:
        value = entry.get(candidate)
        if isinstance(value, str) and value:
            return value
    _fail(path, f"must contain one of {', '.join(candidates)}")


def _rule_entries(value: object, path: str, id_candidates: Sequence[str]) -> dict[str, dict[str, Any]]:
    if isinstance(value, dict):
        result: dict[str, dict[str, Any]] = {}
        for key, raw_entry in value.items():
            entry = _object(raw_entry, f"{path}.{key}") if isinstance(raw_entry, dict) else {"canonical_text": raw_entry}
            entry = dict(entry)
            entry.setdefault(id_candidates[0], key)
            result[key] = entry
        return result
    result = {}
    for index, raw_entry in enumerate(_array(value, path)):
        entry = _object(raw_entry, f"{path}[{index}]")
        identity = _entry_id(entry, id_candidates, f"{path}[{index}]")
        if identity in result:
            _fail(path, f"duplicate rule id {identity!r}")
        result[identity] = entry
    return result


def _require_bool(entry: Mapping[str, object], field: str, expected: bool, path: str) -> None:
    value = entry.get(field)
    if value is not expected:
        _fail(f"{path}.{field}", f"must be {str(expected).lower()}")


def _contains_normalized(value: object, expected: str) -> bool:
    if isinstance(value, str):
        return value.casefold() == expected.casefold()
    if isinstance(value, list):
        return any(_contains_normalized(item, expected) for item in value)
    return False


def _validate_cards_and_decks(document: Mapping[str, object]) -> tuple[dict[str, dict[str, Any]], dict[str, dict[str, Any]]]:
    cards = _id_map(document.get("cards"), "design_id", "$.cards")
    tokens = _id_map(document.get("tokens"), "design_id", "$.tokens")
    if len(cards) != 34:
        _fail("$.cards", f"must contain exactly 34 constructible definitions, found {len(cards)}")
    if len(tokens) != 1:
        _fail("$.tokens", f"must contain exactly one derived token, found {len(tokens)}")
    overlap = set(cards) & set(tokens)
    if overlap:
        _fail("$.cards", f"card and token design IDs overlap: {sorted(overlap)}")

    names: dict[str, str] = {}
    for identity, card in [*cards.items(), *tokens.items()]:
        name = _string(card.get("name"), f"card[{identity}].name")
        if name in names:
            _fail("$.cards", f"duplicate card/token name {name!r} ({names[name]}, {identity})")
        names[name] = identity

    neutral_ids = {identity for identity, card in cards.items() if card.get("neutral") is True}
    if neutral_ids != EXPECTED_NEUTRALS:
        _fail("$.cards", f"neutral definitions must be {sorted(EXPECTED_NEUTRALS)}, found {sorted(neutral_ids)}")

    for identity, card in cards.items():
        availability = card.get("availability")
        if availability not in {"main", "standby"}:
            _fail(f"card[{identity}].availability", "must be main or standby")
        play_cost = card.get("play_cost_pp")
        if availability == "main":
            _integer(play_cost, f"card[{identity}].play_cost_pp", 1)
        elif play_cost is not None:
            _integer(play_cost, f"card[{identity}].play_cost_pp", 1)
        burn = card.get("burn_pp_capacity", 0)
        _integer(burn, f"card[{identity}].burn_pp_capacity", 0)

        if availability == "standby":
            if card.get("can_advance") is not False:
                _fail(f"card[{identity}].can_advance", "standby cards must default to not advanceable")
            deployment = _object(card.get("deployment"), f"card[{identity}].deployment")
            cost_field = next(
                (
                    field
                    for field in ("pp_cost", "deployment_cost_pp", "cost")
                    if field in deployment
                ),
                "pp_cost",
            )
            _integer(deployment.get(cost_field), f"card[{identity}].deployment.{cost_field}", 1)

        rules_text = _string(card.get("canonical_rules_text"), f"card[{identity}].canonical_rules_text")
        folded = rules_text.casefold().replace(" ", "")
        forbidden_text = {
            "恢复当前pp": "current PP restoration",
            "回复当前pp": "current PP restoration",
            "额外战备次数": "extra standby uses",
            "无限检索": "infinite search",
        }
        for needle, description in forbidden_text.items():
            if needle in folded:
                _fail(f"card[{identity}].canonical_rules_text", f"contains forbidden {description}")

    for identity in REPAIR_CARDS_THAT_CANNOT_ADVANCE:
        if identity not in cards:
            _fail("$.cards", f"missing locked repair card {identity}")
        if cards[identity].get("can_advance") is not False:
            _fail(f"card[{identity}].can_advance", "locked repair card must not be advanceable")

    decks = _id_map(document.get("decks"), "deck_id", "$.decks")
    if len(decks) != 2:
        _fail("$.decks", f"must contain exactly two product decks, found {len(decks)}")
    seen_professions: set[str] = set()
    referenced_cards: set[str] = set()
    for deck_id, deck in decks.items():
        profession_id = _string(deck.get("profession_id"), f"deck[{deck_id}].profession_id")
        if profession_id in seen_professions:
            _fail("$.decks", f"duplicate deck profession {profession_id!r}")
        seen_professions.add(profession_id)

        main = _array(deck.get("main"), f"deck[{deck_id}].main")
        if len(main) != 15:
            _fail(f"deck[{deck_id}].main", f"must contain exactly 15 distinct entries, found {len(main)}")
        main_ids: set[str] = set()
        main_total = 0
        main_neutrals: set[str] = set()
        trap_copies = 0
        for index, raw_line in enumerate(main):
            line = _object(raw_line, f"deck[{deck_id}].main[{index}]")
            identity = _string(line.get("design_id"), f"deck[{deck_id}].main[{index}].design_id")
            copies = _integer(line.get("copies"), f"deck[{deck_id}].main[{index}].copies", 1)
            if copies > 3:
                _fail(f"deck[{deck_id}].main[{index}].copies", "same-name limit is three")
            if identity in main_ids:
                _fail(f"deck[{deck_id}].main", f"duplicate line for {identity}")
            if identity not in cards:
                _fail(f"deck[{deck_id}].main", f"references unknown card {identity}")
            card = cards[identity]
            if card.get("availability") != "main":
                _fail(f"deck[{deck_id}].main", f"{identity} is not a main-deck card")
            if card.get("neutral") is True:
                main_neutrals.add(identity)
            elif card.get("profession_id") != profession_id:
                _fail(f"deck[{deck_id}].main", f"{identity} belongs to another profession")
            if card.get("card_type") == "trap":
                trap_copies += copies
            main_ids.add(identity)
            referenced_cards.add(identity)
            main_total += copies
        if main_total != 30:
            _fail(f"deck[{deck_id}].main", f"must total exactly 30 cards, found {main_total}")
        if main_neutrals != EXPECTED_NEUTRALS:
            _fail(f"deck[{deck_id}].main", f"must include all four shared neutrals, found {sorted(main_neutrals)}")

        expected_traps = 2 if profession_id == "oathguard" else 0
        if trap_copies != expected_traps:
            _fail(f"deck[{deck_id}].main", f"{profession_id} must contain exactly {expected_traps} trap copies, found {trap_copies}")

        standby = _array(deck.get("standby"), f"deck[{deck_id}].standby")
        if len(standby) != 4:
            _fail(f"deck[{deck_id}].standby", f"must contain exactly four entries, found {len(standby)}")
        standby_ids: set[str] = set()
        for index, raw_line in enumerate(standby):
            line = _object(raw_line, f"deck[{deck_id}].standby[{index}]")
            identity = _string(line.get("design_id"), f"deck[{deck_id}].standby[{index}].design_id")
            copies = _integer(line.get("copies"), f"deck[{deck_id}].standby[{index}].copies", 1)
            if copies != 1:
                _fail(f"deck[{deck_id}].standby[{index}].copies", "each public standby card must appear exactly once")
            if identity in standby_ids:
                _fail(f"deck[{deck_id}].standby", f"duplicate standby {identity}")
            if identity not in cards:
                _fail(f"deck[{deck_id}].standby", f"references unknown card {identity}")
            card = cards[identity]
            if card.get("availability") != "standby":
                _fail(f"deck[{deck_id}].standby", f"{identity} is not a standby card")
            if card.get("profession_id") != profession_id:
                _fail(f"deck[{deck_id}].standby", f"{identity} belongs to another profession")
            standby_ids.add(identity)
            referenced_cards.add(identity)

    if seen_professions != EXPECTED_PROFESSIONS:
        _fail("$.decks", f"deck professions must be {sorted(EXPECTED_PROFESSIONS)}, found {sorted(seen_professions)}")
    if referenced_cards != set(cards):
        missing = sorted(set(cards) - referenced_cards)
        _fail("$.decks", f"constructible definitions are not referenced exactly by the locked decks: {missing}")
    return cards, tokens


def _lookup_semantic(entry: Mapping[str, object], keys: Iterable[str]) -> object | None:
    for key in keys:
        if key in entry:
            return entry[key]
    return None


def _validate_rules(document: Mapping[str, object]) -> None:
    rules = _object(document.get("rules"), "$.rules")
    keywords = _rule_entries(
        rules.get("keywords"),
        "$.rules.keywords",
        ("keyword", "keyword_id", "id"),
    )
    if set(keywords) != EXPECTED_KEYWORDS:
        _fail("$.rules.keywords", f"must define exactly {sorted(EXPECTED_KEYWORDS)}, found {sorted(keywords)}")

    expected_keyword_definitions = {
        "ward": "必须优先成为攻击目标。",
        "rush": "登场回合只能攻击随从。",
        "storm": "登场回合可攻击随从或主战者。",
        "barrier": "下一次正数伤害变为0并移除，不叠层。",
        "bane": "战斗中实际造成伤害后破坏对方随从。",
        "lifesteal": "仅主动攻击造成的实际伤害治疗主战者；防守反击和效果伤害不治疗。",
    }
    for identity, expected_definition in expected_keyword_definitions.items():
        if keywords[identity].get("canonical_definition") != expected_definition:
            _fail(
                f"$.rules.keywords.{identity}.canonical_definition",
                "differs from the locked, unambiguous keyword definition",
            )

    lifesteal = keywords["lifesteal"]
    structured_lifesteal = all(
        key in lifesteal
        for key in ("active_attack_only", "actual_damage_only", "defensive_damage_heals")
    )
    if structured_lifesteal:
        _require_bool(lifesteal, "active_attack_only", True, "$.rules.keywords.lifesteal")
        _require_bool(lifesteal, "actual_damage_only", True, "$.rules.keywords.lifesteal")
        _require_bool(lifesteal, "defensive_damage_heals", False, "$.rules.keywords.lifesteal")
    else:
        text = str(
            _lookup_semantic(
                lifesteal,
                (
                    "canonical_definition",
                    "canonical_text",
                    "canonical_rules_text",
                    "description",
                ),
            )
            or ""
        )
        if not all(fragment in text for fragment in ("主动攻击", "实际伤害", "防守")):
            _fail("$.rules.keywords.lifesteal", "must explicitly limit healing to actual active-attack damage and exclude defensive damage")

    ability_words = _rule_entries(rules.get("ability_words"), "$.rules.ability_words", ("ability_id", "id"))
    for identity in ("flawless", "debtbound"):
        if identity not in ability_words:
            _fail("$.rules.ability_words", f"missing {identity}")
        timing = _lookup_semantic(ability_words[identity], ("check_timing", "evaluation_timing", "timing"))
        if timing not in {"after_payment_and_burn", "payment_and_burn_before_check"}:
            text = str(
                _lookup_semantic(
                    ability_words[identity],
                    ("canonical_definition", "canonical_text", "description"),
                )
                or ""
            )
            if not all(fragment in text for fragment in ("支付", "燃耗", "后")):
                _fail(f"$.rules.ability_words.{identity}", "must be evaluated after payment and burn")
    debtbound = ability_words["debtbound"]
    cap = _lookup_semantic(debtbound, ("crack_scaling_cap", "scaling_cap", "maximum_cracks_read"))
    if cap != 5:
        _fail("$.rules.ability_words.debtbound", "crack-based scaling must be capped at 5")

    semantics = _object(rules.get("timing_and_zone_semantics"), "$.rules.timing_and_zone_semantics")
    board_capacity = _lookup_semantic(semantics, ("main_board_capacity", "shared_main_board_capacity"))
    if board_capacity != 5:
        _fail("$.rules.timing_and_zone_semantics", "mixed main board capacity must be 5")
    board_types = _lookup_semantic(semantics, ("main_board_types", "shared_main_board_types"))
    if not (_contains_normalized(board_types, "follower") and _contains_normalized(board_types, "amulet")):
        _fail("$.rules.timing_and_zone_semantics", "followers and amulets must share the five-slot main board")
    field_capacity = _lookup_semantic(semantics, ("field_zone_capacity_per_player", "field_slots_per_player"))
    if field_capacity != 1:
        _fail("$.rules.timing_and_zone_semantics", "each player must have one independent field slot")
    replacement_destroyed = _lookup_semantic(
        semantics,
        ("field_replacement_counts_as_destroyed", "field_replacement_is_destruction"),
    )
    if replacement_destroyed is not False:
        _fail("$.rules.timing_and_zone_semantics", "field replacement must not count as destruction")
    if semantics.get("countdown_zero_move_reason") != "countdown_expired":
        _fail(
            "$.rules.timing_and_zone_semantics.countdown_zero_move_reason",
            "must distinguish countdown expiration from ordinary destruction",
        )
    if semantics.get("countdown_zero_counts_as_destroyed") is not True:
        _fail(
            "$.rules.timing_and_zone_semantics.countdown_zero_counts_as_destroyed",
            "countdown-zero departure must count as destruction",
        )
    if semantics.get("early_destruction_counts_as_countdown_end") is not False:
        _fail(
            "$.rules.timing_and_zone_semantics.early_destruction_counts_as_countdown_end",
            "early destruction must not trigger countdown-end abilities",
        )

    history = _object(rules.get("history_semantics"), "$.rules.history_semantics")
    expected_history = {
        "lo_s03": {
            "card_id": "LO-S03",
            "scope": "match",
            "event": "owned_luminous_oath_amulet_left_on_countdown_zero",
            "minimum_occurrences": 1,
        },
        "ap_s03": {
            "card_id": "AP-S03",
            "scope": "current_owner_turn",
            "event": "owned_abyssal_pact_amulet_destroyed_on_countdown_zero",
            "minimum_occurrences": 1,
        },
    }
    for history_id, expected in expected_history.items():
        actual = _object(
            history.get(history_id),
            f"$.rules.history_semantics.{history_id}",
        )
        for field, value in expected.items():
            if actual.get(field) != value:
                _fail(
                    f"$.rules.history_semantics.{history_id}.{field}",
                    f"must be {value!r}",
                )

    evolution_value = rules.get("evolution_charge")
    if isinstance(evolution_value, list):
        evolution_entries = _rule_entries(
            evolution_value,
            "$.rules.evolution_charge",
            ("profession_id",),
        )
        oath = evolution_entries.get("oathguard")
        pact = evolution_entries.get("pactmage")
        if oath is None or pact is None:
            _fail("$.rules.evolution_charge", "must define oathguard and pactmage")
    else:
        evolution = _object(evolution_value, "$.rules.evolution_charge")
        oath = _object(evolution.get("oathguard"), "$.rules.evolution_charge.oathguard")
        pact = _object(evolution.get("pactmage"), "$.rules.evolution_charge.pactmage")
    for path, entry in (("oathguard", oath), ("pactmage", pact)):
        limit = _lookup_semantic(
            entry,
            (
                "limit_per_turn_cycle",
                "once_per_turn_cycle",
                "maximum_triggers_per_turn_cycle",
            ),
        )
        if limit not in {True, 1}:
            _fail(f"$.rules.evolution_charge.{path}", "must trigger at most once per turn cycle")
    oath_requires_actual = _lookup_semantic(
        oath,
        ("requires_actual_repair", "zero_crack_state_alone_triggers"),
    )
    oath_text = str(oath.get("canonical_definition", "")).replace(" ", "")
    describes_last_crack = "实际修复" in oath_text and (
        "归零" in oath_text or "最后一道裂痕" in oath_text
    )
    if oath_requires_actual not in {True, False} and not describes_last_crack:
        _fail("$.rules.evolution_charge.oathguard", "must encode actual-repair gating")
    if "zero_crack_state_alone_triggers" in oath and oath["zero_crack_state_alone_triggers"] is not False:
        _fail("$.rules.evolution_charge.oathguard", "remaining at zero cracks must not charge evolution")
    if "requires_actual_repair" in oath and oath["requires_actual_repair"] is not True:
        _fail("$.rules.evolution_charge.oathguard", "must require an actual repair")
    minimum = _lookup_semantic(
        pact,
        ("minimum_cracks_from_single_hand_action", "single_action_crack_minimum"),
    )
    pact_text = str(pact.get("canonical_definition", "")).replace(" ", "")
    if minimum != 2 and not all(fragment in pact_text for fragment in ("单次手牌行动", "至少2")):
        _fail("$.rules.evolution_charge.pactmage", "must require at least two cracks from one hand action")


def _validate_safety_and_references(document: Mapping[str, object], cards: Mapping[str, Mapping[str, object]], tokens: Mapping[str, Mapping[str, object]]) -> None:
    capability_catalog = _id_map(document.get("capability_catalog"), "capability_id", "$.capability_catalog")
    missing_capabilities = REQUIRED_NEXT_GATE_CAPABILITIES - set(capability_catalog)
    if missing_capabilities:
        _fail(
            "$.capability_catalog",
            f"missing required next-Gate capabilities {sorted(missing_capabilities)}",
        )
    for identity, card in [*cards.items(), *tokens.items()]:
        requirements = _array(card.get("capability_requirements"), f"card[{identity}].capability_requirements")
        seen: set[str] = set()
        for index, raw_requirement in enumerate(requirements):
            requirement = _object(raw_requirement, f"card[{identity}].capability_requirements[{index}]")
            capability_id = _string(requirement.get("capability_id"), f"card[{identity}].capability_requirements[{index}].capability_id")
            if capability_id in seen:
                _fail(f"card[{identity}].capability_requirements", f"duplicate capability reference {capability_id}")
            if capability_id not in capability_catalog:
                _fail(f"card[{identity}].capability_requirements", f"unknown capability {capability_id}")
            if requirement.get("status") not in {"existing", "fix", "new"}:
                _fail(f"card[{identity}].capability_requirements[{index}].status", "must be existing, fix, or new")
            seen.add(capability_id)

    professions = _id_map(document.get("professions"), "profession_id", "$.professions")
    leaders = _id_map(document.get("leaders"), "leader_id", "$.leaders")
    if set(professions) != EXPECTED_PROFESSIONS:
        _fail("$.professions", f"must define exactly {sorted(EXPECTED_PROFESSIONS)}")
    for profession_id, profession in professions.items():
        leader_id = _string(profession.get("leader_id"), f"profession[{profession_id}].leader_id")
        if leader_id not in leaders:
            _fail(f"profession[{profession_id}].leader_id", f"unknown leader {leader_id}")
        if leaders[leader_id].get("profession_id") != profession_id:
            _fail(f"profession[{profession_id}].leader_id", "leader belongs to another profession")

    valid_professions = set(professions) | {"neutral"}
    for identity, card in cards.items():
        if card.get("profession_id") not in valid_professions:
            _fail(f"card[{identity}].profession_id", "references an unknown profession")
    for identity, token in tokens.items():
        if token.get("profession_id") not in professions:
            _fail(f"token[{identity}].profession_id", "references an unknown profession")
        source = _string(token.get("source_card_id"), f"token[{identity}].source_card_id")
        if source not in cards:
            _fail(f"token[{identity}].source_card_id", f"references unknown card {source}")

    decks = _id_map(document.get("decks"), "deck_id", "$.decks")
    for deck_id, deck in decks.items():
        profession_id = deck["profession_id"]
        leader_id = _string(deck.get("leader_id"), f"deck[{deck_id}].leader_id")
        if leader_id not in leaders:
            _fail(f"deck[{deck_id}].leader_id", f"unknown leader {leader_id}")
        if leaders[leader_id].get("profession_id") != profession_id:
            _fail(f"deck[{deck_id}].leader_id", "leader belongs to another profession")
        main_ids = {line["design_id"] for line in deck["main"]}
        standby_ids = {line["design_id"] for line in deck["standby"]}
        legal_role_ids = main_ids | standby_ids
        roles = _object(deck.get("gameplay_roles"), f"deck[{deck_id}].gameplay_roles")
        for role, raw_ids in roles.items():
            for identity in _array(raw_ids, f"deck[{deck_id}].gameplay_roles.{role}"):
                if identity not in legal_role_ids:
                    _fail(
                        f"deck[{deck_id}].gameplay_roles.{role}",
                        f"references {identity}, which is not in this deck's main or standby pool",
                    )
        expected_roles = EXPECTED_GAMEPLAY_ROLES.get(deck_id)
        if expected_roles is None:
            _fail(f"deck[{deck_id}].gameplay_roles", "deck has no locked gameplay-role map")
        for role, expected_ids in expected_roles.items():
            actual_ids = roles.get(role)
            if actual_ids != expected_ids:
                _fail(
                    f"deck[{deck_id}].gameplay_roles.{role}",
                    f"must be the locked ordered role list {expected_ids!r}",
                )
        for index, raw_combo in enumerate(_array(deck.get("combo_lines"), f"deck[{deck_id}].combo_lines")):
            combo = _object(raw_combo, f"deck[{deck_id}].combo_lines[{index}]")
            for identity in _array(combo.get("cards"), f"deck[{deck_id}].combo_lines[{index}].cards"):
                if identity not in main_ids:
                    _fail(
                        f"deck[{deck_id}].combo_lines[{index}].cards",
                        f"references {identity}, which is not in this main deck",
                    )

    # The lock file must make these prohibitions machine-readable.  Accept a
    # named object in either format or implementation_scope so the schema can
    # remain documentation-friendly while the policy stays strict.
    containers = [
        value
        for value in (document.get("format"), document.get("implementation_scope"))
        if isinstance(value, dict)
    ]
    safety: Mapping[str, object] | None = None
    for container in containers:
        for key in (
            "design_safety",
            "forbidden_design_patterns",
            "prohibited_mechanics",
            "constraints",
        ):
            candidate = container.get(key)
            if isinstance(candidate, dict):
                safety = candidate
                break
        if safety is not None:
            break
    if safety is None:
        _fail("$.format", "must contain machine-readable design_safety/prohibited_mechanics constraints")
    for key in (
        "zero_cost_cards",
        "infinite_search",
        "self_loop",
        "extra_standby_uses",
        "current_pp_restoration",
    ):
        value = safety.get(key)
        if value is not False and value != "forbidden":
            _fail(f"design_safety.{key}", "must be false or forbidden")


def _number_at(mapping: Mapping[str, object], aliases: Sequence[str], path: str) -> float:
    value = _lookup_semantic(mapping, aliases)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        _fail(path, f"must provide numeric field ({', '.join(aliases)})")
    return float(value)


def _pair(value: object, path: str) -> tuple[float, float]:
    if isinstance(value, dict):
        low = value.get("min")
        high = value.get("max")
        values = [low, high]
    else:
        values = value
    if (
        not isinstance(values, list)
        or len(values) != 2
        or any(isinstance(item, bool) or not isinstance(item, (int, float)) for item in values)
    ):
        _fail(path, "must be a numeric [min, max] pair")
    return float(values[0]), float(values[1])


def _validate_balance(document: Mapping[str, object]) -> None:
    balance = _object(document.get("paper_balance_targets"), "$.paper_balance_targets")
    if balance.get("validation_state") != "target_not_playtested":
        _fail("$.paper_balance_targets.validation_state", "must be target_not_playtested, never a claim of measured balance")

    t2 = _number_at(
        balance,
        (
            "post_mulligan_t2_class_action_probability_min",
            "t2_profession_action_probability_min",
            "t2_in_class_action_probability_min",
        ),
        "$.paper_balance_targets.t2_probability",
    )
    if t2 < 0.90 or t2 > 1.0:
        _fail("$.paper_balance_targets.t2_probability", "must target at least 0.90")

    if "core_line_visibility_t6_range" in balance:
        t6_low, t6_high = _pair(
            balance["core_line_visibility_t6_range"],
            "$.paper_balance_targets.core_line_visibility_t6_range",
        )
    else:
        t6_low = _number_at(balance, ("core_combo_visibility_t6_min", "combo_visibility_t6_min"), "$.paper_balance_targets.t6_min")
        t6_high = _number_at(balance, ("core_combo_visibility_t6_max", "combo_visibility_t6_max"), "$.paper_balance_targets.t6_max")
    if not (0.70 <= t6_low <= t6_high <= 0.80):
        _fail("$.paper_balance_targets", "T6 core-combo visibility target must be 0.70-0.80")
    t10_visibility = _number_at(
        balance,
        (
            "core_line_visibility_t10_approx",
            "core_combo_visibility_t10",
            "combo_visibility_t10",
        ),
        "$.paper_balance_targets.t10_visibility",
    )
    if not (0.88 <= t10_visibility <= 0.92):
        _fail("$.paper_balance_targets", "T10 core-combo visibility must remain approximately 0.90")

    acceptance = balance.get("future_playable_acceptance")
    if isinstance(acceptance, dict):
        win_low, win_high = _pair(
            acceptance.get("win_rate_range"),
            "$.paper_balance_targets.future_playable_acceptance.win_rate_range",
        )
    else:
        win_low = _number_at(balance, ("swapped_seat_win_rate_min", "win_rate_min"), "$.paper_balance_targets.win_rate_min")
        win_high = _number_at(balance, ("swapped_seat_win_rate_max", "win_rate_max"), "$.paper_balance_targets.win_rate_max")
    if (win_low, win_high) != (0.48, 0.52):
        _fail("$.paper_balance_targets", "future swapped-seat win-rate target must be 0.48-0.52")
    if isinstance(acceptance, dict):
        turn_low, turn_high = _pair(
            acceptance.get("winner_turn_median_range"),
            "$.paper_balance_targets.future_playable_acceptance.winner_turn_median_range",
        )
    else:
        turn_low = _number_at(balance, ("winner_turn_median_min", "median_winner_turn_min"), "$.paper_balance_targets.turn_min")
        turn_high = _number_at(balance, ("winner_turn_median_max", "median_winner_turn_max"), "$.paper_balance_targets.turn_max")
    if (turn_low, turn_high) != (10.0, 12.0):
        _fail("$.paper_balance_targets", "winner's own-turn median target must be T10-T12")
    if isinstance(acceptance, dict):
        early = _number_at(
            acceptance,
            ("games_ending_before_t10_max",),
            "$.paper_balance_targets.future_playable_acceptance.games_ending_before_t10_max",
        )
        late = _number_at(
            acceptance,
            ("games_ending_after_t15_max",),
            "$.paper_balance_targets.future_playable_acceptance.games_ending_after_t15_max",
        )
    else:
        early = _number_at(balance, ("finish_before_t10_max", "pre_t10_finish_rate_max"), "$.paper_balance_targets.early_finish")
        late = _number_at(balance, ("finish_after_t15_max", "post_t15_finish_rate_max"), "$.paper_balance_targets.late_finish")
    if early > 0.10 or late > 0.15:
        _fail("$.paper_balance_targets", "finish-tail targets must be <=10% before T10 and <=15% after T15")

    peak_targets = (
        ("oathguard_peak_cracks_per_game", (2.0, 4.0)),
        ("pactmage_peak_cracks_per_game", (5.0, 8.0)),
    )
    for field, expected in peak_targets:
        actual = _pair(balance.get(field), f"$.paper_balance_targets.{field}")
        if actual != expected:
            _fail(
                f"$.paper_balance_targets.{field}",
                f"must remain {int(expected[0])}-{int(expected[1])}",
            )

    # Expected play-pattern ranges can live on each deck; validate both.
    decks = _id_map(document.get("decks"), "deck_id", "$.decks")
    by_profession = {deck["profession_id"]: deck for deck in decks.values()}
    oath = _object(by_profession["oathguard"].get("expected_play_pattern"), "oathguard.expected_play_pattern")
    pact = _object(by_profession["pactmage"].get("expected_play_pattern"), "pactmage.expected_play_pattern")
    ranges = (
        (oath, "future_uses_per_game", 2, 3, "oathguard future uses"),
        (oath, "repairs_per_game", 4, 7, "oathguard repaired cracks"),
        (pact, "future_uses_per_game", 3, 5, "pactmage future uses"),
        (pact, "expected_terminal_cracks", 3, 6, "pactmage ending cracks"),
    )
    for entry, field, expected_low, expected_high, label in ranges:
        if field in entry:
            low, high = _pair(entry[field], label)
        else:
            aliases = {
                "future_uses_per_game": ("future_uses_min", "future_uses_max"),
                "repairs_per_game": ("cracks_repaired_min", "cracks_repaired_max"),
                "expected_terminal_cracks": ("ending_cracks_min", "ending_cracks_max"),
            }[field]
            low = _number_at(entry, (aliases[0],), label)
            high = _number_at(entry, (aliases[1],), label)
        if (low, high) != (float(expected_low), float(expected_high)):
            _fail("$.decks.expected_play_pattern", f"{label} must be {expected_low}-{expected_high}")


def validate(document: object, schema: object | None = None) -> None:
    """Validate the complete Gate 5A lock file, raising :class:`ManifestError`."""

    if schema is not None:
        validate_json_schema(document, schema)
    root = _object(document, "$")
    if root.get("status") != "locked_not_implemented":
        _fail("$.status", "must be locked_not_implemented")
    if root.get("schema_version") not in (1, "1.0.0"):
        _fail("$.schema_version", "must be the v1 design schema")
    design_id_policy = root.get("design_id_policy")
    if design_id_policy not in ("string_design_ids_only", "string_ids_not_runtime_card_ids"):
        value = design_id_policy
        if not isinstance(value, dict) or value.get("runtime_numeric_ids_frozen") is not False:
            _fail("$.design_id_policy", "must preserve string-only design IDs without freezing runtime CardId values")

    cards, tokens = _validate_cards_and_decks(root)
    _validate_rules(root)
    _validate_safety_and_references(root, cards, tokens)
    _validate_balance(root)

    visual_assets = _array(root.get("visual_assets"), "$.visual_assets")
    if len(visual_assets) != 38:
        _fail("$.visual_assets", f"must contain the 38-item future visual inventory, found {len(visual_assets)}")
    asset_ids: set[str] = set()
    asset_names: set[str] = set()
    subjects_by_kind: dict[str, list[str]] = {
        "constructible_card": [],
        "token": [],
        "leader": [],
        "shared_card_back": [],
    }
    leader_ids = {
        entry.get("leader_id")
        for entry in _array(root.get("leaders"), "$.leaders")
        if isinstance(entry, dict)
    }
    for index, raw_asset in enumerate(visual_assets):
        asset = _object(raw_asset, f"$.visual_assets[{index}]")
        asset_id = _string(asset.get("asset_id"), f"$.visual_assets[{index}].asset_id")
        if asset_id in asset_ids:
            _fail("$.visual_assets", f"duplicate asset_id {asset_id}")
        asset_name = _string(asset.get("name"), f"$.visual_assets[{index}].name")
        if asset_name in asset_names:
            _fail("$.visual_assets", f"duplicate visual asset name {asset_name!r}")
        if asset.get("status") != "planned_not_generated":
            _fail(f"$.visual_assets[{index}].status", "must be planned_not_generated in this design-only gate")
        kind = asset.get("kind")
        if kind not in subjects_by_kind:
            _fail(f"$.visual_assets[{index}].kind", "unknown visual asset kind")
        subject_id = _string(asset.get("subject_id"), f"$.visual_assets[{index}].subject_id")
        subjects_by_kind[kind].append(subject_id)
        if kind == "constructible_card" and subject_id not in cards:
            _fail(f"$.visual_assets[{index}].subject_id", f"unknown constructible card {subject_id}")
        if kind == "token" and subject_id not in tokens:
            _fail(f"$.visual_assets[{index}].subject_id", f"unknown token {subject_id}")
        if kind == "leader":
            if subject_id not in leader_ids:
                _fail(f"$.visual_assets[{index}].subject_id", f"unknown leader {subject_id}")
        if kind == "shared_card_back" and subject_id != "shared-card-back-v1":
            _fail(
                f"$.visual_assets[{index}].subject_id",
                "shared card back must use subject_id shared-card-back-v1",
            )
        asset_ids.add(asset_id)
        asset_names.add(asset_name)

    expected_subjects = {
        "constructible_card": set(cards),
        "token": set(tokens),
        "leader": leader_ids,
        "shared_card_back": {"shared-card-back-v1"},
    }
    for kind, expected in expected_subjects.items():
        actual = subjects_by_kind[kind]
        if len(actual) != len(set(actual)) or set(actual) != expected:
            _fail(
                "$.visual_assets",
                f"{kind} subjects must cover each locked subject exactly once; "
                f"missing={sorted(expected - set(actual))}, duplicates={sorted(item for item in set(actual) if actual.count(item) > 1)}",
            )

    # Run the content lock after the descriptive semantic checks so established
    # failures keep their precise rule/deck/reference messages.  Design drift
    # which is otherwise structurally valid is reported by product partition.
    _validate_locked_product_sections(root)


def _read_json(path: Path, label: str) -> object:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ManifestError(f"cannot read {label} {path}: {error}") from error


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--schema", type=Path, default=DEFAULT_SCHEMA)
    args = parser.parse_args(argv)

    try:
        manifest_path = args.manifest.resolve(strict=True)
        schema_path = args.schema.resolve(strict=True)
        document = _read_json(manifest_path, "manifest")
        schema = _read_json(schema_path, "schema")
        validate(document, schema)
    except (OSError, ManifestError) as error:
        print(f"Gate 5A product deck validation failed: {error}", file=sys.stderr)
        return 1

    print(f"validated Gate 5A product deck lock: {manifest_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
