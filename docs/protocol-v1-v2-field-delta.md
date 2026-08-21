# Protocol v1 → v2 Field Delta

> **Scope:** This document catalogues every field in the frozen legacy v1 wire
> (PlayerState 211, UnitState 212) against the v0.4 engine state model, and
> enumerates what a future v2 wire would need to add, rename, or remove.
> The v1 wire itself **must not be altered**; this document serves as the design
> input for a future v2 protocol cut.

---

## 1. Frozen v1 Wire — Complete Field Inventory

### 1.1 PlayerState (Message 211)

Wire layout (little-endian, 12 bytes total / 11-byte payload after message ID):

| Offset | Type   | Wire Field Name              | v0.1 Semantic                         | v0.4 Internal Field      |
|--------|--------|------------------------------|---------------------------------------|--------------------------|
| 0      | uint8  | message_id = 0xD3 (211)      | message discriminator                 | —                        |
| 1      | uint8  | protocol_version = 0x01      | wire version guard                    | —                        |
| 2      | uint8  | player                       | PlayerId (0 or 1)                     | PlayerId                 |
| 3–4    | int16  | leader_health                | 主战者当前生命                        | `leader_health`          |
| 5–6    | int16  | maximum_leader_health        | 主战者生命上限                        | `maximum_leader_health`  |
| 7      | uint8  | current_pp                   | 当前PP (0-10 in v0.1)                 | `current_pp`             |
| 8      | uint8  | maximum_pp                   | 最大PP (capped at 10 in v0.1)         | `pp_capacity` (v0.4)     |
| 9      | uint8  | evolution_points             | 进化点数 (0-4)                        | `evolution_points`       |
| 10     | uint8  | own_turn_number              | 本玩家自身回合数                      | `own_turn_number`        |
| 11     | uint8  | flags                        | bit-packed state flags (see §1.1.1)   | (see §1.1.1)             |

#### 1.1.1 PlayerState flags byte

| Bit | v1 Wire Name                  | v0.1 Semantic            | v0.4 Internal Field / Semantic                              |
|-----|-------------------------------|--------------------------|-------------------------------------------------------------|
| 0   | `evolution_used_this_turn`    | 本回合已主动进化          | `evolution_used_this_turn` (semantics unchanged)            |
| 1   | `advanced_summon_used_this_turn` | 本回合已高级召唤       | **SEMANTIC MISMATCH** — v0.4 uses `advance_used_this_turn` (动用未来), which fires on advance OR burn, not advanced summon |
| 2   | `trap_set_this_turn`          | 本回合已设置伏策          | `trap_set_this_turn` (semantics unchanged)                  |
| 3   | `leader_skill_used`           | 本局已使用主战技          | `leader_skill_used` (semantics unchanged)                   |
| 4–7 | (reserved, always 0)          | —                         | Available for new flags in v2                               |

Golden test vector: `D3 01 01 11 00 19 00 03 07 02 06 03`

---

### 1.2 UnitState (Message 212)

Wire layout (little-endian, 24 bytes total / 23-byte payload after message ID):

| Offset | Type   | Wire Field Name     | v0.1 Semantic                            | v0.4 Internal Field         |
|--------|--------|---------------------|------------------------------------------|-----------------------------|
| 0      | uint8  | message_id = 0xD4 (212) | message discriminator                | —                           |
| 1      | uint8  | protocol_version    | wire version guard                       | —                           |
| 2      | uint8  | controller          | PlayerId (0 or 1)                        | `controller`                |
| 3      | uint8  | sequence            | field position order index               | `sequence`                  |
| 4–11   | uint64 | instance_id         | unique card instance ID                  | `id`                        |
| 12–13  | int16  | attack              | 当前攻击力                               | `current_attack`            |
| 14–15  | int16  | health              | 当前生命值                               | `current_health`            |
| 16–17  | int16  | maximum_health      | 生命上限                                 | `maximum_health`            |
| 18–21  | uint32 | keywords            | 关键词位掩码 (KeywordMask)               | `keywords` (from boolean flags) |
| 22     | uint8  | inherited_imprint   | 印记 (from 高级召唤 in v0.1)             | `inherited_imprint` (always None in v0.4) |
| 23     | uint8  | flags               | bit-packed unit state (see §1.2.1)       | (see §1.2.1)                |

#### 1.2.1 UnitState flags byte

| Bit | v1 Wire Name                    | v0.1 Semantic              | v0.4 Internal Field / Semantic                              |
|-----|---------------------------------|----------------------------|-------------------------------------------------------------|
| 0   | `evolved`                       | 已进化                      | `evolved` (semantics unchanged)                             |
| 1   | `attacked_this_turn`            | 本回合已攻击                | `attacked_this_turn` (semantics unchanged)                  |
| 2   | `entered_this_turn`             | 本回合入场                  | `entered_this_turn` (semantics unchanged)                   |
| 3   | `advanced_summoned_this_turn`   | 本回合通过高级召唤入场       | **DEPRECATED in v0.4** — advanced summon concept removed; bit is always 0 in v0.4 output |
| 4   | `face_down`                     | 背面表示                    | `face_down` (semantics unchanged)                           |
| 5–7 | (reserved, always 0)            | —                           | Available for new flags in v2                               |

Golden test vector: `D4 01 00 03 08 07 06 05 04 03 02 01 07 00 05 00 08 00 03 00 00 00 01 09`

---

### 1.3 Message ID Namespace (210–219)

| Message ID | Enum Name              | Current Status in v1                                  |
|------------|------------------------|-------------------------------------------------------|
| 210        | GameMode               | Defined in `Message` enum; not yet fully implemented  |
| 211        | PlayerState            | Active; frozen wire layout documented above           |
| 212        | UnitState              | Active; frozen wire layout documented above           |
| 213        | EvolutionState         | Defined; maps to v0.1 evolution system                |
| 214        | AdvancedSummonState    | Defined; v0.1 高级召唤 specific — **deprecated concept in v0.4** |
| 215        | RequestEvolutionMode   | Defined; maps to v0.1 EvolutionMode choice            |
| 216        | RequestMaterials       | Defined; v0.1 高级召唤 materials — **deprecated in v0.4** |
| 217        | RequestImprint         | Defined; v0.1 印记 system — **deprecated in v0.4**    |
| 218        | TacticWindow           | Defined; reaction / trap window signalling            |
| 219        | MatchStatistics        | Defined; end-of-match stats                           |

---

## 2. v0.4 State NOT in v1 Wire

The following v0.4 game-state fields exist in the internal engine model but are
**not transmitted** over the v1 wire. In the current implementation they are
expressed only in the `GameEvent` stream (`EventType` entries).

### 2.1 PlayerState — Missing Wire Fields

| v0.4 Internal Field       | Type / Range   | Engine Location         | v0.4 Semantic                                                   | v1 Wire Exposure           |
|---------------------------|----------------|-------------------------|-----------------------------------------------------------------|----------------------------|
| `cracks`                  | int, ≥ 0       | `PlayerState::cracks`   | 裂痕数: PP capacity lost via advance or burn; removed by 修复X | GameEvent `CracksChanged`  |
| `advance_used_this_turn`  | bool           | `PlayerState::advance_used_this_turn` | 本回合动用未来标志 (covers both advance and burn); distinct from v0.1's `advanced_summon_used_this_turn` | Not in wire; v1 flags.bit1 carries the old v0.1 name |
| `standby` zone count      | uint, 0–6      | `PlayerState::standby` (vector) | 战备区: public standby cards; count and card IDs        | Not transmitted            |
| `deploy_used_this_turn`   | (to be added)  | —                       | 本回合战备部署次数 (default max 1)                              | Not in wire                |

> **Note:** `pp_capacity` **is** present in v1 under the name `maximum_pp`
> (wire offset 8, uint8). The field's semantic has changed — v0.1 capped it at
> 10; v0.4 removes that cap — but the wire field itself is reused. A uint8
> wire type supports 0–255, which comfortably covers realistic game values
> without a v2 type change.

### 2.2 UnitState — Missing Wire Fields

| v0.4 Internal Field  | Type  | Engine Location               | v0.4 Semantic                                               | v1 Wire Exposure    |
|----------------------|-------|-------------------------------|-------------------------------------------------------------|---------------------|
| `temporary_rush`     | bool  | `CardInstance::temporary_rush` | 本回合被授予的临时突进能力 (from evolution or advance effect) | Not in wire; v1 flags.bit5 is reserved |
| Granted modifiers    | array | (to be added per v0.4 §31)    | 组件能力: runtime modifiers granted at deploy time; max 1 per deploy | Not in wire |

### 2.3 New State Domains Entirely Absent from v1 Wire

| Domain                    | v0.4 Rule Section | Current Engine Status                              | Notes for v2                                         |
|---------------------------|-------------------|----------------------------------------------------|------------------------------------------------------|
| Response stack depth      | §26               | `ReactionWindow` enum + `pending_reaction_` in `Game` | v1 sends `TacticWindow` (218) to signal window open; stack depth and layer counter not exposed |
| Evolution charge condition | §23              | `charge_condition` placeholder in `DeckList` (to be added) | Per-class condition prototype + params; not in wire  |
| Strategy slot type        | §5, §20           | `PlayerState::tactics` (shared array)             | v1 does not distinguish facility vs. trap in slot type; both occupy `UnitState`-style positions without type field |
| Series mechanisms         | §30–32            | Not implemented; rule layer can express            | 成长值/环境值/蜕变/生死状态: not in v1 or current engine; v2 would need extensible per-unit state |

---

## 3. Field-by-Field Delta: v1 → v2 Required Changes

### 3.1 PlayerState (211) — Field Disposition Table

| Wire Field           | v1 Status   | v2 Disposition | Change Description                                                  |
|----------------------|-------------|----------------|---------------------------------------------------------------------|
| `player`             | Active      | KEEP           | No change needed                                                    |
| `leader_health`      | Active      | KEEP           | No change needed                                                    |
| `maximum_leader_health` | Active   | KEEP           | No change needed                                                    |
| `current_pp`         | Active      | KEEP           | No change needed; uint8 sufficient                                  |
| `maximum_pp`         | Active      | RENAME         | Rename to `pp_capacity` in v2 docs and C# overlay to match v0.4 internal name; wire byte at offset 8 stays uint8 at same position |
| `evolution_points`   | Active      | KEEP           | Semantics unchanged (0–4, cap 4)                                    |
| `own_turn_number`    | Active      | KEEP           | No change needed                                                    |
| `flags.bit0`         | Active      | KEEP           | `evolution_used_this_turn` — semantics match v0.4                  |
| `flags.bit1`         | Mismatch    | RENAME         | Wire name `advanced_summon_used_this_turn` → `advance_used_this_turn`; same bit position, broader v0.4 semantic (covers advance AND burn) |
| `flags.bit2`         | Active      | KEEP           | `trap_set_this_turn` — semantics match v0.4                        |
| `flags.bit3`         | Active      | KEEP           | `leader_skill_used` — semantics match v0.4                         |
| `flags.bits4–7`      | Reserved    | EXTEND         | **NEW bit4:** `deploy_used_this_turn` (战备部署已用标志)           |
| **NEW** `cracks`     | Missing     | ADD            | uint8 new field: 裂痕计数 (0–255 sufficient for realistic play)    |
| **NEW** `standby_count` | Missing  | ADD            | uint8 new field: 战备区卡牌数量 (0–6)                              |

### 3.2 UnitState (212) — Field Disposition Table

| Wire Field              | v1 Status    | v2 Disposition | Change Description                                                       |
|-------------------------|--------------|----------------|--------------------------------------------------------------------------|
| `controller`            | Active       | KEEP           | No change needed                                                         |
| `sequence`              | Active       | KEEP           | No change needed                                                         |
| `instance_id`           | Active       | KEEP           | No change needed                                                         |
| `attack`                | Active       | KEEP           | No change needed                                                         |
| `health`                | Active       | KEEP           | No change needed                                                         |
| `maximum_health`        | Active       | KEEP           | No change needed                                                         |
| `keywords`              | Active       | KEEP           | uint32 KeywordMask; v0.4 boolean flags (printed_guard etc.) map to these bits at unit creation; formally named keyword list TBD |
| `inherited_imprint`     | Deprecated   | REPURPOSE      | Always None in v0.4; byte at offset 22 can be repurposed in v2 (e.g., granted_modifier_count or component_kind) |
| `flags.bit0`            | Active       | KEEP           | `evolved` — semantics match v0.4                                        |
| `flags.bit1`            | Active       | KEEP           | `attacked_this_turn` — semantics match v0.4                             |
| `flags.bit2`            | Active       | KEEP           | `entered_this_turn` — semantics match v0.4                              |
| `flags.bit3`            | Deprecated   | REPURPOSE      | Was `advanced_summoned_this_turn` (v0.1 高级召唤); always 0 in v0.4; **NEW in v2:** repurpose as `temporary_rush` (本回合被授予突进) |
| `flags.bit4`            | Active       | KEEP           | `face_down` — semantics match v0.4                                      |
| `flags.bits5–7`         | Reserved     | EXTEND         | Available for future v0.4 state bits (e.g., component modifier active)  |

---

## 4. New Message IDs Required for v2

The current v1 message range 210–219 has headroom for new message types. The
following are candidates identified from v0.4 rule analysis.

| Candidate Message     | Suggested ID | Purpose                                                            |
|-----------------------|--------------|--------------------------------------------------------------------|
| StandbyState          | (TBD)        | Transmit full standby zone contents (0–6 card IDs); currently not sent |
| StrategySlotState     | (TBD)        | Distinguish facility vs. trap in each of the 2 strategy slots      |
| ResponseStackState    | (TBD)        | Current response layer depth (0/1/2/3) and whose turn to respond   |
| EvolutionChargeState  | (TBD)        | Class-specific charge condition progress                           |

> The range 210–219 is partially occupied (210–219 reserved). IDs 220–229 are
> available for new v2 messages without conflicting with YGOPro2 core (≤200)
> or existing SCGS allocations.

---

## 5. Frozen v1 Invariants (Must Not Change)

The following are **hard invariants** that a v2 redesign must not violate for
any message previously covered by v1:

1. `kProtocolVersion = 1` byte at payload offset 0 for all existing messages.
2. `PlayerState` payload is exactly 11 bytes; message is exactly 12 bytes.
3. `UnitState` payload is exactly 23 bytes; message is exactly 24 bytes.
4. All multi-byte integers are little-endian.
5. Golden test vectors must decode to the same values as defined in `docs/protocol.md`.
6. Message IDs 210–219 field names, types, and byte offsets in the C++ encoder/decoder and C# overlay are immutable under the v1 freeze.

---

## 6. v0.4 Rule Changes Driving v2 Protocol Needs

The nine v0.4 rule changes and their protocol implications:

| Rule Change ID | v0.4 Change Summary                          | v1 Gap                                          | v2 Action Required                                 |
|----------------|----------------------------------------------|-------------------------------------------------|----------------------------------------------------|
| R01            | PP容量无上限 (§7.1)                           | `maximum_pp` field name misleading; uint8 type adequate | Rename field in docs/overlay to `pp_capacity`  |
| R02            | 动用未来/预支重做 (§8–§13)                    | flags.bit1 semantics mismatch (v0.1 vs v0.4)   | Rename flags.bit1 to `advance_used_this_turn`      |
| R03            | 裂痕 (§14)                                   | No `cracks` field in wire                       | Add `cracks` uint8 field to PlayerState            |
| R04            | 进化系统重做 (§22–§23)                        | `advanced_summon_used_this_turn` (flags.bit3 of UnitState) always 0 | Repurpose UnitState.flags.bit3 as `temporary_rush` |
| R05            | 战备部署统一 (§24–§25)                        | No `standby_count` or standby card IDs in wire  | Add `standby_count` and `deploy_used_this_turn`    |
| R06            | 组件能力 (§31)                                | `inherited_imprint` always None; byte wasted    | Repurpose `inherited_imprint` byte for component info |
| R07            | 三层响应 (§26)                                | `TacticWindow` (218) only signals window open; no depth | Add ResponseStackState message                |
| R08            | 策略区重命名 (§5)                             | No slot-type field; facility vs. trap not distinguished | Add StrategySlotState message                 |
| R09            | 关键词效果文字化 (§21)                        | `keywords` uint32 remains valid; boolean flags mapped at unit creation | Document mapping table in protocol.md      |

---

## 7. Summary

**v1 → v2 field delta at a glance:**

- **Keep unchanged (7 fields):** player, leader_health, maximum_leader_health,
  current_pp, evolution_points, own_turn_number; UnitState: controller,
  sequence, instance_id, attack, health, maximum_health, keywords; flags.bit0/2/3/4
  on PlayerState; flags.bit0/1/2/4 on UnitState.

- **Rename (2 fields):** `maximum_pp` → `pp_capacity` (PlayerState);
  flags.bit1 `advanced_summon_used_this_turn` → `advance_used_this_turn` (PlayerState).

- **Repurpose (2 fields):** UnitState.flags.bit3 `advanced_summoned_this_turn`
  → `temporary_rush`; UnitState `inherited_imprint` byte → component modifier slot.

- **Add to PlayerState (3 fields):** `cracks` (uint8), `standby_count` (uint8),
  flags.bit4 `deploy_used_this_turn`.

- **Add to UnitState (1 field):** UnitState.flags.bit5 (reserved for component
  modifier active indicator).

- **New messages (4 candidates):** StandbyState, StrategySlotState,
  ResponseStackState, EvolutionChargeState.

- **Deprecated messages (3):** AdvancedSummonState (214), RequestMaterials (216),
  RequestImprint (217) — all tied to the removed v0.1 高级召唤/印记 system.
