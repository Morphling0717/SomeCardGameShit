// SPDX-License-Identifier: GPL-3.0-or-later
// GENERATED FILE. Sources: design/product-decks-v1/card-pool.lock.json and
// design/product-decks-v1/runtime-foundation.lock.json.
// Regenerate with scripts/design/generate_product_catalog_v2.py.
#include "scgs/product_runtime.hpp"

#include <array>
#include <string_view>

namespace scgs::v2 {
namespace {

struct GeneratedCardRow {
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
};

struct GeneratedSelectorRow {
    std::array<CardKind, 2> allowed_kinds;
    std::size_t allowed_kind_count;
    std::string_view profession_id;
    std::string_view series_id;
    bool include_main_board;
    bool include_field;
};

struct GeneratedModeRow {
    std::string_view design_id;
    std::string_view mode_id;
    std::string_view label;
    TargetSpec target;
    GeneratedSelectorRow target_filter;
};

struct GeneratedConditionRow {
    std::string_view design_id;
    ConditionKind kind;
    std::string_view condition_id;
    int threshold;
    int read_cap;
    std::string_view parameter;
    GeneratedSelectorRow permanent_filter;
};

struct GeneratedAdditionalCostRow {
    std::string_view design_id;
    TargetSpec target;
    std::size_t minimum;
    std::size_t maximum;
    GeneratedSelectorRow filter;
};

inline constexpr std::array<GeneratedCardRow, 35> kGeneratedCards = {{
    {"LO-01", "曜誓传令使·菲娅", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Follower, 1, 1, 1, 0, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "1PP随从1/1。登场：查看牌组顶4张，可展示其中1张非随从“曜誓”牌加入手牌，其余随机置于牌组底。"},
    {"LO-02", "曜誓偿愿祭司·米蕾", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Follower, 2, 2, 2, 0, false, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "2PP随从2/2，不能预支。登场：修复1；若此次修复使裂痕归零，主战者回复2。进化时：修复1。"},
    {"LO-03", "晨钟誓碑", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Amulet, 2, 0, 0, 3, false, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "2PP护符，倒数3，不能预支。登场：修复1。每个自己的回合限一次，裂痕因修复归零时倒数额外-1。倒数结束：在原格召唤3/3守护的“誓光守卫”。"},
    {"LO-04", "曜誓盾侍·格兰", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Follower, 2, 2, 2, 0, true, 0, mask(Keyword::Ward), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "2PP随从2/2，守护。登场—无隙：获得屏障。"},
    {"LO-05", "日轮突骑·希尔妲", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Follower, 3, 3, 2, 0, true, 0, mask(Keyword::Rush), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "3PP随从3/2，突进。登场：若本回合实际修复过裂痕，获得+1/+1。"},
    {"LO-06", "归誓圣仪", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Spell, 3, 0, 0, 0, false, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "3PP法术，不能预支。修复2；若此次修复使裂痕归零，抽1张牌。"},
    {"LO-07", "曜誓·不破阵", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Trap, 2, 0, 0, 0, false, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "2PP伏策，不能预支。敌方宣布攻击时：修复1；随后若无隙，取消该次攻击。"},
    {"LO-08", "曜誓破阵骑·莱昂", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Follower, 5, 4, 4, 0, true, 0, mask(Keyword::Rush), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "5PP随从4/4，突进。每回合限一次，本牌战斗破坏敌方随从且存活后修复1；若裂痕因此归零，获得屏障。"},
    {"LO-09", "曜誓大司祭·伊莲", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Follower, 6, 4, 6, 0, false, 0, mask(Keyword::Ward), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "6PP随从4/6，守护，不能预支。登场：修复2，主战者回复等同于实际修复量；若裂痕归零，获得屏障。"},
    {"LO-10", "日轮圣庭·索拉里斯", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Field, 3, 0, 0, 0, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "3PP场地。每个自己的回合第一次实际修复裂痕后，选择一个己方“曜誓”随从+1/+1；若此时无隙，再使其获得屏障。"},
    {"LO-11", "曜誓大团长·蕾奥妮", "oathguard", "luminous_oath", false, CardAvailability::MainDeck, CardKind::Follower, 10, 8, 8, 0, true, 0, mask(Keyword::Ward), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "10PP随从8/8，守护。登场：若按期打出且无隙，获得疾驰与屏障；否则获得突进。"},
    {"LO-S01", "曜誓援军长·凯因", "oathguard", "luminous_oath", false, CardAvailability::Standby, CardKind::Follower, 0, 2, 3, 0, false, 0, mask(Keyword::Ward), CardImplementationStatus::LockedNotImplemented, false, 2, "本回合实际修复过裂痕。", "", "2/3守护。部署费2；条件为本回合实际修复过裂痕。登场—无隙：另一个己方随从+1/+1。"},
    {"LO-S02", "破晓处刑骑·维奥娜", "oathguard", "luminous_oath", false, CardAvailability::Standby, CardKind::Follower, 0, 4, 3, 0, false, 0, mask(Keyword::Rush), CardImplementationStatus::LockedNotImplemented, false, 3, "本回合己方“曜誓”随从获得过屏障。", "", "4/3突进。部署费3；条件为本回合己方“曜誓”随从获得过屏障。战斗破坏随从且存活后，每回合限一次修复1。"},
    {"LO-S03", "圣碑巨像·阿德拉斯", "oathguard", "luminous_oath", false, CardAvailability::Standby, CardKind::Follower, 0, 5, 7, 0, false, 0, mask(Keyword::Ward) | mask(Keyword::Barrier), CardImplementationStatus::LockedNotImplemented, false, 4, "本局己方“曜誓”护符至少一次因倒数归零离场。", "", "5/7守护、屏障。部署费4；条件为本局己方“曜誓”护符至少一次因倒数归零离场。"},
    {"LO-S04", "曜冠天马骑·塞蕾涅", "oathguard", "luminous_oath", false, CardAvailability::Standby, CardKind::Follower, 0, 6, 6, 0, false, 0, mask(Keyword::Storm), CardImplementationStatus::LockedNotImplemented, false, 9, "当前无裂痕且本局至少两次因修复使裂痕归零。", "", "6/6疾驰。部署费9；条件为当前无裂痕且本局至少两次因修复使裂痕归零。"},
    {"AP-01", "渊契使魔·墨契", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Follower, 1, 1, 2, 0, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "1PP随从1/2。遗言—负契2：抽1张牌。封存不触发遗言。"},
    {"AP-02", "借时优等生·伊蕾娜", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Follower, 2, 2, 2, 0, true, 1, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "2PP随从2/2，燃耗1。登场：若己方场地为“渊契魔导院”，获得屏障。"},
    {"AP-03", "契式·违约穿刺", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Spell, 2, 0, 0, 0, true, 1, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "2PP法术，燃耗1。对敌方随从造成3点伤害；负契4改为5点。"},
    {"AP-04", "未偿禁书《第七码》", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Amulet, 1, 0, 0, 3, true, 1, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "1PP护符，燃耗1，倒数3。已在场时，每个自己的回合第一次动用未来后倒数-1。倒数结束：抽1；负契4改为抽2。本牌不会追溯触发自身入场前的燃耗。"},
    {"AP-05", "渊契魔导院·零时讲堂", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Field, 3, 0, 0, 0, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "3PP场地。打出时抽1，然后将1张手牌置于牌组底；每个自己的回合第一次动用未来后再执行一次。未成功抽牌时不强制置底。"},
    {"AP-06", "债印风纪官·赛菈", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Follower, 3, 3, 2, 0, true, 0, mask(Keyword::Bane), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "3PP随从3/2，必杀。登场—负契2：本回合获得突进。"},
    {"AP-07", "黑蔷薇校医·维奥拉", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Follower, 4, 3, 5, 0, true, 0, mask(Keyword::Lifesteal), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "4PP随从3/5，吸血。超前：本回合获得突进。"},
    {"AP-08", "契式·延期清算", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Spell, 3, 0, 0, 0, false, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "3PP法术，不能预支。选择：修复2；或使一个己方“渊契”随从永久+2/+2并获得屏障。"},
    {"AP-09", "带债讲师·雷维尔", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Follower, 5, 4, 5, 0, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "5PP随从4/5。按期登场：修复1并获得屏障。超前登场：抽2弃1，并在本回合获得突进。"},
    {"AP-10", "债权吞噬兽·格里姆", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Follower, 6, 4, 6, 0, true, 0, mask(Keyword::Ward), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "6PP随从4/6，守护。登场：可以选择敌方随从，对其造成等同于裂痕数、最多5点的伤害。"},
    {"AP-11", "禁忌毕业生·诺克缇娅", "pactmage", "abyssal_pact", false, CardAvailability::MainDeck, CardKind::Follower, 8, 6, 6, 0, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "8PP随从6/6。按期且负契4登场：本回合+2攻击并获得疾驰；否则获得突进。"},
    {"AP-S01", "催债魔犬·奥尔特", "pactmage", "abyssal_pact", false, CardAvailability::Standby, CardKind::Follower, 0, 2, 1, 0, false, 0, mask(Keyword::Rush) | mask(Keyword::Bane), CardImplementationStatus::LockedNotImplemented, false, 1, "本回合曾新增裂痕。", "", "2/1突进、必杀。部署费1；条件为本回合曾新增裂痕。"},
    {"AP-S02", "封债医龙·塞拉菲姆", "pactmage", "abyssal_pact", false, CardAvailability::Standby, CardKind::Follower, 0, 4, 6, 0, false, 0, mask(Keyword::Ward) | mask(Keyword::Barrier) | mask(Keyword::Lifesteal), CardImplementationStatus::LockedNotImplemented, false, 4, "裂痕至少4且主战者生命不高于15。", "", "4/6守护、屏障、吸血。部署费4；条件为裂痕至少4且主战者生命不高于15。"},
    {"AP-S03", "禁书装订机·奥库塔", "pactmage", "abyssal_pact", false, CardAvailability::Standby, CardKind::Follower, 0, 4, 4, 0, false, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 2, "本回合己方“渊契”护符因倒数归零被破坏。", "", "4/4。部署费2；条件为本回合己方“渊契”护符因倒数归零被破坏。登场：修复1。"},
    {"AP-S04", "终期债主·阿巴顿", "pactmage", "abyssal_pact", false, CardAvailability::Standby, CardKind::Follower, 0, 9, 8, 0, false, 0, mask(Keyword::Storm), CardImplementationStatus::LockedNotImplemented, false, 6, "裂痕至少6且控制另一个“渊契”随从或护符。", "封存其中一张己方“渊契”随从或护符。", "9/8疾驰。部署费6；条件为裂痕至少6且控制另一个“渊契”随从或护符；额外封存其中一张作为部署代价。"},
    {"NT-01", "边境游骑·埃尔", "neutral", "neutral", true, CardAvailability::MainDeck, CardKind::Follower, 2, 2, 2, 0, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "2PP随从2/2。登场：若对方五格主战场卡牌数多于己方，本回合获得突进。"},
    {"NT-02", "白垩城门卫", "neutral", "neutral", true, CardAvailability::MainDeck, CardKind::Follower, 3, 2, 4, 0, true, 0, mask(Keyword::Ward), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "3PP随从2/4，守护。"},
    {"NT-03", "旅途篝火", "neutral", "neutral", true, CardAvailability::MainDeck, CardKind::Amulet, 2, 0, 0, 1, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "2PP护符，倒数1。倒数结束：抽1，主战者回复1；被提前破坏时不触发。"},
    {"NT-04", "界域裁定", "neutral", "neutral", true, CardAvailability::MainDeck, CardKind::Spell, 4, 0, 0, 0, true, 0, mask(Keyword::None), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "4PP法术。选择：对敌方随从造成4点伤害；或破坏一个敌方护符／场地。不能伤害主战者。"},
    {"LO-T01", "誓光守卫", "oathguard", "luminous_oath", false, CardAvailability::Token, CardKind::Follower, 0, 3, 3, 0, false, 0, mask(Keyword::Ward), CardImplementationStatus::LockedNotImplemented, false, 0, "", "", "3/3守护。由“晨钟誓碑”的倒数结束能力在原格召唤。"},
}};

inline constexpr std::array<GeneratedModeRow, 4> kGeneratedModes = {{
    {"AP-08", "repair", "修复2", TargetSpec::None, {{{CardKind::Follower, CardKind::Follower}}, 0, "", "", true, true}},
    {"AP-08", "empower", "强化渊契随从", TargetSpec::FriendlyFollower, {{{CardKind::Follower, CardKind::Follower}}, 1, "pactmage", "abyssal_pact", true, false}},
    {"NT-04", "damage_follower", "对敌方随从造成4点伤害", TargetSpec::EnemyFollower, {{{CardKind::Follower, CardKind::Follower}}, 1, "", "", true, false}},
    {"NT-04", "destroy_amulet_or_field", "破坏敌方护符或场地", TargetSpec::EnemyPermanent, {{{CardKind::Amulet, CardKind::Field}}, 2, "", "", true, true}},
}};

inline constexpr std::array<GeneratedConditionRow, 11> kGeneratedConditions = {{
    {"LO-S01", ConditionKind::TurnRepairAtLeast, "turn_actual_repair", 1, 0, "", {{{CardKind::Follower, CardKind::Follower}}, 0, "", "", true, true}},
    {"LO-S02", ConditionKind::TurnBarrierGranted, "turn_luminous_oath_barrier_granted", 1, 0, "", {{{CardKind::Follower, CardKind::Follower}}, 1, "oathguard", "luminous_oath", true, false}},
    {"LO-S03", ConditionKind::MatchCountdownExpiredAtLeast, "match_luminous_oath_amulet_countdown_expired", 1, 0, "", {{{CardKind::Amulet, CardKind::Follower}}, 1, "oathguard", "luminous_oath", true, false}},
    {"LO-S04", ConditionKind::CracksAtMost, "current_cracks_zero", 0, 0, "", {{{CardKind::Follower, CardKind::Follower}}, 0, "", "", true, true}},
    {"LO-S04", ConditionKind::MatchRepairToZeroAtLeast, "match_repair_to_zero_twice", 2, 0, "", {{{CardKind::Follower, CardKind::Follower}}, 0, "", "", true, true}},
    {"AP-S01", ConditionKind::TurnFutureUseAtLeast, "turn_future_cracks_added", 1, 0, "", {{{CardKind::Follower, CardKind::Follower}}, 0, "", "", true, true}},
    {"AP-S02", ConditionKind::CracksAtLeast, "current_cracks_four", 4, 0, "", {{{CardKind::Follower, CardKind::Follower}}, 0, "", "", true, true}},
    {"AP-S02", ConditionKind::LeaderHealthAtMost, "leader_health_fifteen", 15, 0, "", {{{CardKind::Follower, CardKind::Follower}}, 0, "", "", true, true}},
    {"AP-S03", ConditionKind::TurnCountdownExpired, "turn_abyssal_pact_amulet_countdown_expired", 1, 0, "", {{{CardKind::Amulet, CardKind::Follower}}, 1, "pactmage", "abyssal_pact", true, false}},
    {"AP-S04", ConditionKind::CracksAtLeast, "current_cracks_six", 6, 0, "", {{{CardKind::Follower, CardKind::Follower}}, 0, "", "", true, true}},
    {"AP-S04", ConditionKind::ControlsSeriesPermanent, "controls_other_abyssal_pact_permanent", 1, 0, "abyssal_pact", {{{CardKind::Follower, CardKind::Amulet}}, 2, "pactmage", "abyssal_pact", true, false}},
}};

inline constexpr std::array<GeneratedAdditionalCostRow, 1> kGeneratedAdditionalCosts = {{
    {"AP-S04", TargetSpec::FriendlyPermanent, 1, 1, {{{CardKind::Follower, CardKind::Amulet}}, 2, "pactmage", "abyssal_pact", true, false}},
}};

inline constexpr std::array<std::string_view, 42> kRequiredCapabilities = {{
    "advance_prohibition_per_card",
    "printed_barrier",
    "lifesteal_active_attack_only",
    "countdown_permanent",
    "profession_series_neutral_tags",
    "filtered_top_deck_search",
    "randomized_deck_bottom",
    "hand_to_deck_bottom",
    "discard_from_hand",
    "summon_token_original_slot",
    "amulet_main_board",
    "field_zone",
    "field_replacement_without_destroy",
    "destroy_amulet_or_field",
    "permanent_targeting",
    "modal_choice",
    "resolution_condition",
    "repair_to_zero_trigger",
    "actual_repair_amount",
    "dynamic_crack_threshold",
    "crack_scaling_cap_five",
    "future_use_trigger",
    "once_per_owner_turn_trigger",
    "turn_history",
    "match_history",
    "temporary_attack_buff",
    "permanent_stat_buff",
    "permanent_keyword_grant",
    "conditional_draw",
    "conditional_heal",
    "conditional_damage",
    "conditional_countdown_change",
    "combat_kill_survive_trigger",
    "target_friendly_archetype_follower",
    "optional_enemy_follower_target",
    "standby_custom_condition",
    "archive_follower_or_amulet_cost",
    "profession_evolution_charge",
    "no_retroactive_self_trigger",
    "board_card_count_comparison",
    "field_identity_check",
    "draw_then_bottom_if_draw_succeeds",
}};

PermanentSelectorSpec make_selector(const GeneratedSelectorRow& row) {
    PermanentSelectorSpec selector;
    selector.allowed_kinds.assign(
        row.allowed_kinds.begin(),
        row.allowed_kinds.begin() + static_cast<std::ptrdiff_t>(row.allowed_kind_count));
    selector.profession_id = row.profession_id;
    selector.series_id = row.series_id;
    selector.include_main_board = row.include_main_board;
    selector.include_field = row.include_field;
    return selector;
}

} // namespace

CardCatalog make_locked_product_catalog() {
    CardCatalog catalog;
    for (const GeneratedCardRow& row : kGeneratedCards) {
        CardDefinition definition;
        definition.identity = CardIdentity{
            std::string(row.design_id),
            std::string(row.profession_id),
            std::string(row.series_id),
            row.neutral,
        };
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
        for (const GeneratedModeRow& generated_mode : kGeneratedModes) {
            if (generated_mode.design_id != row.design_id) {
                continue;
            }
            ModeSpec mode;
            mode.mode_id = generated_mode.mode_id;
            mode.label = generated_mode.label;
            mode.target = generated_mode.target;
            mode.target_filter = make_selector(generated_mode.target_filter);
            // Gate 5B deliberately generates shape-only modes. Their effect
            // programs remain empty until Gate 5C marks the card executable.
            definition.modes.push_back(std::move(mode));
        }
        if (row.availability == CardAvailability::Standby) {
            StandbySpec standby;
            standby.pp_cost = row.standby_pp_cost;
            for (const GeneratedConditionRow& generated_condition : kGeneratedConditions) {
                if (generated_condition.design_id != row.design_id) {
                    continue;
                }
                ConditionSpec condition;
                condition.kind = generated_condition.kind;
                condition.condition_id = generated_condition.condition_id;
                condition.threshold = generated_condition.threshold;
                condition.read_cap = generated_condition.read_cap;
                condition.parameter = generated_condition.parameter;
                condition.permanent_filter = make_selector(generated_condition.permanent_filter);
                standby.conditions.push_back(std::move(condition));
            }
            for (const GeneratedAdditionalCostRow& generated_cost : kGeneratedAdditionalCosts) {
                if (generated_cost.design_id != row.design_id) {
                    continue;
                }
                standby.requires_additional_cost = true;
                standby.additional_cost_target = generated_cost.target;
                standby.additional_cost_filter = make_selector(generated_cost.filter);
                standby.additional_cost_minimum = generated_cost.minimum;
                standby.additional_cost_maximum = generated_cost.maximum;
            }
            standby.condition_text = row.standby_condition_text;
            standby.additional_cost_text = row.standby_additional_cost_text;
            definition.standby = std::move(standby);
        }
        catalog.add(std::move(definition));
    }
    return catalog;
}

std::span<const std::string_view> required_product_capability_ids() noexcept {
    return kRequiredCapabilities;
}

} // namespace scgs::v2
