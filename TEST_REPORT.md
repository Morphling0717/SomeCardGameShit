# Test report — Milestone 0

**Date:** 2026-08-20
**Branch:** `prototype/headless-core-v0`
**Scope:** deterministic headless rules vertical slice plus the first YGOPro2 protocol overlay.

**Executed environment:** Linux x86_64, GCC 14.2.0, Clang 17.0.0, CMake 3.31.6, Ninja 1.12.1, Python 3.13.5.

## Conclusion

Within the implemented headless scope, no known failing test, invariant violation, AddressSanitizer finding, or UndefinedBehaviorSanitizer finding remains.

This report does **not** certify the complete playable first version. The Unity/YGOPro2 client, Windows native plugin loading, network matches, replay compatibility, real UI input, and human balance targets have not yet been executed in this environment.

## Functional baseline

Default suite:

```text
22 C++ test cases
9,169 C++ assertions
0 failures
4/4 CTest targets passed in Debug
4/4 CTest targets passed in Release
4/4 CTest targets passed with Clang ASan + UBSan
```

The four CTest targets are:

1. `scgs_unit_tests`
2. `scgs_documented_scenario`
3. `scgs_ygo2_overlay_patcher`
4. `scgs_protocol_contract`

The Python layer currently contains 10 tests:

- 5 overlay-patcher tests;
- 5 C++/C# protocol-contract tests.

## Deterministic stress tests

### Release

```bash
SCGS_SMOKE_SEEDS=2048 ./build/release/scgs_tests
```

Result:

```text
22 test cases
523,085 assertions
0 failures
2,048 completed deterministic matches
```

### Clang AddressSanitizer + UndefinedBehaviorSanitizer

```bash
ASAN_OPTIONS=detect_leaks=1:halt_on_error=1 \
UBSAN_OPTIONS=halt_on_error=1:print_stacktrace=1 \
SCGS_SMOKE_SEEDS=256 \
./build/asan/scgs_tests
```

Result:

```text
22 test cases
67,141 assertions
0 failures
256 completed deterministic matches
no sanitizer diagnostics
```

## Golden scenario

Command:

```bash
./build/release/scgs_demo --verify
```

Verified result:

```json
{
  "scenario": "documented_construct_guard_then_combat_evolve",
  "verified": true,
  "player_pp": 3,
  "evolution_points": 0,
  "materials_archived": 2,
  "construct_attack": 7,
  "construct_health": 5,
  "construct_max_health": 8,
  "guard_inherited": true,
  "enemy_unit_destroyed": true
}
```

`construct_health` is 5 after it attacks and receives 3 combat damage; its evolved maximum health is 8.

## What is covered

- start player, opening draw, mulligan, and first-player draw skip;
- PP growth, refresh, limits, and invalid-cost rejection;
- hand limit, public overflow-to-archive event, and escalating fatigue;
- five unit slots and two tactic slots;
- unit play, spells, relics, traps, and tactic replacement;
- persistent unit damage and simultaneous combat;
- guard, rush, storm hooks, barrier, bane, lifesteal, and ambush;
- combat evolution and ability evolution;
- tribute summon and construct summon;
- once-per-turn advanced-summon restriction;
- original-cost material checks;
- printed material imprints and rejection of second inheritance;
- summon-deck units moving to archive when they leave play;
- relic countdowns and the implemented limited trap windows;
- leader-skill generic interface and once-per-game restriction;
- surrender, win, draw, and finished-state locking;
- active-player-first death batches and deterministic last-words order;
- card uniqueness across zones, sequence/controller consistency, PP/HP bounds, reaction-state consistency, and result/phase consistency after every smoke-test action;
- SCGS message IDs 210–219;
- C++ complete-message and YGOPro2 payload-only encoding/decoding;
- fixed C++/C# golden vectors;
- overlay injection idempotency and upstream ID-collision rejection.

## Additional validation

- GCC and Clang compile with warnings treated as errors;
- Python files pass `compileall`;
- shell scripts pass `bash -n`;
- GitHub Actions YAML parses and contains GCC, sanitizer, and Windows jobs;
- generated patch is checked for whitespace errors and replayed into an empty directory before packaging.

## Explicitly not yet verified

- compiling the C# overlay inside Unity 5.6.7;
- building the pinned YGOPro2 client on Windows;
- loading the correct `ocgcore.dll` ABI in the client;
- routing every reserved SCGS message through `Ocgcore.logicalizeMessage`;
- visual PP/leader/unit-health UI;
- real card selection, attack, evolution, material, and imprint input;
- all four final trap windows;
- LAN/network state synchronization;
- replay compatibility and disconnect recovery;
- removal/replacement of every upstream Yu-Gi-Oh asset in a distributable build;
- human playtest targets such as 8–12 minute matches, turn 8–10 endings, and 48%–52% first-player win rate;
- final Royal summon-deck purpose and final leader-skill designs.

The next acceptance gate is **M1: a Windows + Unity 5.6.7 white-card match in YGOPro2**, checked against the same golden scenario and headless state trace.
