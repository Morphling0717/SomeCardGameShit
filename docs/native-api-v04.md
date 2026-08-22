# `scgs_v04` 原生 API 契约

本文是 v0.4 规则引擎 Gate 2 的规范性 C ABI 与 JSON schema 文档。公开声明以
[`native_api_v04.h`](../engine/include/scgs/native_api_v04.h) 为准；本文冻结载荷语义、
所有权、错误分类和兼容策略。

## 版本与平台

- 动态库名：Windows `scgs_v04.dll`、Linux `libscgs_v04.so`、macOS
  `libscgs_v04.dylib`。
- `scgs_v04` 是规则产品线名称，不等于 CMake `project()` 版本。
- 安装后的 CMake config package 是 `scgs_native_v04` 1.0.0，导入 target 是
  `scgs::native_v04`；该包版本不替代下述 ABI 或 JSON schema version。
- 初始 ABI 为 1.0，`scgs_v04_abi_version()` 返回 `0x00010000`；高 16 位是 major，
  低 16 位是 minor。
- `scgs_v04_create()` 要求相同 major，且客户端请求的 minor 不高于库 minor。
- JSON `schema_version` 初始为 `1`。新增 optional 输出字段不升级 schema；破坏性载荷
  变化升级 schema，破坏性函数/调用约定变化升级 ABI major。
- 第一版构建目标为 Windows x86-64、Linux x86-64 和 macOS arm64；不支持 Web。

## 边界与所有权

- 头文件可由 C11 和 C++20 编译；导出使用 `extern "C"`，Windows 调用约定固定为
  `__cdecl`。
- ABI 只出现 `uint32_t`、`uint64_t`、`char*` 和 64 位 token handle；不出现 C++ 类、
  STL、C `bool`、编译器 enum、`size_t` 或需要跨 CRT 释放的指针。
- handle `0` 无效；销毁 `0` 是幂等成功，销毁未知或已经销毁的非零 handle 返回
  `SCGS_V04_INVALID_HANDLE`。token 在进程生命周期内不复用。
- 动态库不保存调用方传入的字符串/缓冲区指针，也不返回内部内存。所有缓冲区由调用方
  分配和释放。
- 同一 handle 只承诺单线程顺序调用；不同 handle 可独立调用。同一 handle 的两次缓冲区
  读取之间不得并发提交命令。
- 所有导出函数捕获 C++ 异常；异常不得跨过 C ABI。

## Native 状态与规则状态

Native 状态描述 ABI/transport 是否成功；规则状态描述命令是否被游戏规则接受。两者不能
混用。下列 native 数值永久冻结：

| 数值 | 常量 | 含义 |
|---:|---|---|
| 0 | `SCGS_V04_OK` | Native 调用成功 |
| 1 | `SCGS_V04_INVALID_ARGUMENT` | 空指针或不合法的标量参数 |
| 2 | `SCGS_V04_ABI_MISMATCH` | ABI major/minor 不兼容 |
| 3 | `SCGS_V04_INVALID_HANDLE` | 未知、过期或已销毁 handle |
| 4 | `SCGS_V04_INVALID_UTF8` | 输入不是合法 UTF-8 |
| 5 | `SCGS_V04_INVALID_JSON` | JSON 语法错误 |
| 6 | `SCGS_V04_SCHEMA_MISMATCH` | schema、字段类型、范围或枚举错误 |
| 7 | `SCGS_V04_BUFFER_TOO_SMALL` | 输出缓冲区不足 |
| 8 | `SCGS_V04_PAYLOAD_TOO_LARGE` | 输入超过 1 MiB |
| 9 | `SCGS_V04_OUT_OF_MEMORY` | 分配失败 |
| 10 | `SCGS_V04_INTERNAL_ERROR` | 其他已捕获的内部异常 |

`start` 与 `submit_command_json` 在 native 成功时通过 `out_engine_code` 返回规则状态；native
失败时写入 `SCGS_V04_NO_ENGINE_CODE`（`0xFFFFFFFF`）。规则码 0～35 按当前
`ErrorCode` 顺序冻结：`Ok`、`InvalidPhase`、`NotActivePlayer`、`InvalidPlayer`、
`InvalidCard`、`InvalidZone`、`InvalidTarget`、`InvalidSlot`、`InsufficientPP`、
`HandLimit`、`UnitZoneFull`、`TacticZoneFull`、`SummoningSickness`、`AlreadyAttacked`、
`GuardBlocksTarget`、`EvolutionLocked`、`NoEvolutionPoints`、`EvolutionAlreadyUsed`、
`AlreadyEvolved`、`AdvanceAlreadyUsed`、`AdvanceWouldExceedCap`、`DeployAlreadyUsed`、
`DeployConditionNotMet`、`InvalidDeployment`、`ResponseDepthExceeded`、
`TrapAlreadySetThisTurn`、`NoPendingReaction`、`TrapNotEligible`、`LeaderSkillLocked`、
`LeaderSkillAlreadyUsed`、`MatchAlreadyStarted`、`MatchNotStarted`、
`MulliganAlreadyDone`、`DuplicateSelection`、`GameOver`、`StaleRevision`。以后只能尾增，
适配层必须显式映射，不能依赖 C++ enum 的底层值。

`scgs_v04_get_last_error()` 返回当前线程最近一次 native 失败的脱敏诊断；它不是 UI 文案，
不得包含输入载荷、隐藏卡名、稳定实例 ID 或原始异常内容。除 `get_last_error` 本身外，后续
成功调用会清除旧诊断。

## 两段式输出

所有 JSON/文本输出遵循相同流程：

1. 传 `buffer = NULL`、`capacity = 0`，函数设置 `required_bytes` 并返回
   `SCGS_V04_BUFFER_TOO_SMALL`；
2. 分配至少 `required_bytes` 字节并重试；
3. 成功时写入完整 UTF-8 与末尾 NUL，返回 `SCGS_V04_OK`。

`required_bytes` 包含 NUL。容量不足时不得部分写入；`required_bytes` 仍返回本次所需大小。
除 `get_last_error` 外，带 JSON 输出的函数会先把非空 `required_bytes` 清零；若随后因
handle、viewer、schema 或其他验证失败而返回，该值保持为 0。
输入 JSON 的 `*_bytes` 不包含 NUL。若两次读取之间状态发生变化，第二次按新状态重新计算；
调用方依据输出中的 `revision` 判断是否仍可使用。

## 输入 JSON

所有输入根对象都要求 `"schema_version": 1`。字段名使用 `snake_case`，整数必须是 JSON
整数且在对应无符号范围内，布尔值必须是 `true`/`false`。optional 字段不存在时应省略，
不接受 `null`。未知字段被忽略，以允许 schema 1 追加字段。

### 创建配置

```json
{
  "schema_version": 1,
  "player0_deck": "midrange",
  "player1_deck": "advance",
  "random_seed": 12345,
  "first_player_mode": 1,
  "shuffle_decks": true
}
```

`player0_deck` 与 `player1_deck` 必填，只接受 `midrange`、`advance`。其余字段 optional：
seed 省略表示产品熵源；`first_player_mode` 为 0 Random、1 Player0、2 Player1，默认 0；
`shuffle_decks` 默认 `true`。规则生命、起手、手牌上限和自定义场景不通过产品 ABI 配置。

### 命令

```json
{
  "schema_version": 1,
  "player": 0,
  "action": 4,
  "source": 37,
  "target": {"kind": 0, "player": 1},
  "expected_revision": 8
}
```

`player`、`action`、`expected_revision` 必填。`source`、`target`、`slot`、
`component_donor`、`use_advance`、`mulligan_cards` 按动作选填；缺省 source 为 0、
`use_advance` 为 false、调度列表为空。target kind 0 是主战者，kind 1 是单位且要求
`unit`。规则是否合法只由 Gate 1 `submit_command` 判断。

### 行动查询

查询要求 `schema_version`、`player`、`expected_revision`；`action`、`source`、`target`、
`slot`、`component_donor`、`use_advance`、`mulligan_cards` 均为精确匹配 optional 过滤项，
与 `ActionQuery` 语义相同。

ActionKind 数值固定为：0 Mulligan、1 PlayUnit、2 CastSpell、3 PlayTactic、4 Attack、
5 Evolve、6 Deploy、7 ActivateTrap、8 PassReaction、9 EndTurn、10 Surrender。

## 输出 JSON

以下表格中的 `u32`、`u64`、`i32` 分别表示对应范围的 JSON 整数；`Card[]` 表示
`CardView` 数组。除明确标为 optional 的字段外，字段都必须存在。optional 输出字段在无值时
直接省略，不输出 `null`；唯一的结构性 `null` 是 `units` / `tactics` 数组中的空格位。

### 根 envelope

每个 JSON 输出根对象都包含：

| 字段 | 类型 | 必需 | 语义 |
|---|---|---|---|
| `schema_version` | u32 | 是 | 固定为 1 |
| `revision` | u64 | 是 | 生成该结果时的当前状态 revision |

当前各函数增加下列对应载荷字段。schema 1 消费者必须忽略未知输出字段，以允许后续追加
optional 信息而不升级 schema：

| 函数 | 载荷字段 | 类型 |
|---|---|---|
| `get_view_json` | `view` | `MatchView` |
| `list_legal_actions_json` | `actions` | `LegalAction[]` |
| `list_valid_targets_json` | `targets` | `Target[]` |
| `list_valid_slots_json` | `slots` | `u64[]` |
| `list_valid_donors_json` | `donors` | `u64[]`（instance ID） |
| `preview_payment_json` | `payment` | `PaymentPreview` |
| `get_reaction_context_json` | `reaction` | `ReactionContext` |
| `read_events_json` | `events`、`last_sequence` | `GameEventView[]`、u64 |

数组保留引擎顺序。`events` 按 `sequence` 递增；有结果时 `last_sequence` 是最后一项的
sequence，无结果时保持调用方传入的 `after_sequence`。API 不保存 viewer cursor。

### `MatchView` 与 `PlayerView`

`MatchView`：

| 字段 | 类型 | 必需 | 语义 |
|---|---|---|---|
| `viewer` | Player | 是 | 本快照观看者 |
| `active_player` | Player | 是 | 当前行动玩家 |
| `first_player` | Player | 是 | 本局实际先手 |
| `random_seed` | u32 | 是 | 本局实际 seed |
| `phase` | Phase | 是 | 当前阶段 |
| `result` | GameResult | 是 | 当前赛果 |
| `revision` | u64 | 是 | 与根 envelope 的 revision 相同 |
| `players` | `PlayerView[2]` | 是 | 固定按 Player0、Player1 排列 |
| `reaction` | `ReactionContext` | 是 | 当前响应上下文 |

`PlayerView`：

| 字段 | 类型 | 必需 | 语义 |
|---|---|---|---|
| `player` | Player | 是 | 玩家身份 |
| `leader_health`、`maximum_leader_health` | i32 | 是 | 当前/最大主战者生命 |
| `current_pp`、`pp_capacity`、`cracks` | i32 | 是 | PP 与裂痕状态 |
| `evolution_energy`、`own_turn_number`、`fatigue_count` | i32 | 是 | 进化能量、自己回合数、疲劳计数 |
| `mulligan_done` | bool | 是 | 是否完成调度 |
| `evolution_used_this_turn`、`advance_used_this_turn` | bool | 是 | 本回合进化/动用未来限制 |
| `deploy_used_this_turn`、`trap_set_this_turn` | bool | 是 | 本回合部署/设伏限制 |
| `leader_skill_used`、`charge_granted_this_cycle` | bool | 是 | 主战技与职业充能状态 |
| `friendly_deaths_this_cycle`、`spells_used_this_turn`、`units_played_this_turn` | i32 | 是 | 条件计数器 |
| `leader_skill` | `LeaderSkillDefinition` | 是 | 主战技定义；当前 alpha 不承诺 UI |
| `deck_count`、`hand_count` | u64 | 是 | 牌组和手牌数量 |
| `hand` | `Card[]` | 是 | 仅当 `player == viewer` 时含完整手牌；否则为空数组 |
| `units` | `(CardView|null)[5]` | 是 | 按 slot 0～4 排列 |
| `tactics` | `(CardView|null)[3]` | 是 | 按 slot 0～2 排列 |
| `graveyard`、`archive`、`standby` | `Card[]` | 是 | 双方公开区域 |

### `CardView` 与卡牌定义

`CardView`：

| 字段 | 类型 | 必需 | 语义 |
|---|---|---|---|
| `instance_id` | u64 | optional | 稳定实例 ID；对方背面伏策省略 |
| `definition_id` | u32 | optional | 定义 ID；对方背面伏策省略 |
| `definition` | `CardDefinition` | optional | 完整定义；对方背面伏策省略 |
| `kind` | CardKind | optional | 卡牌类型；对方背面伏策省略 |
| `name` | string | 是 | UTF-8 名称；对方背面伏策固定为空字符串 |
| `owner`、`controller` | Player | 是 | 所有者与当前控制者 |
| `zone` | Zone | 是 | 当前区域 |
| `sequence` | u64 | 是 | 当前区域中的位置 |
| `cost` | i32 | 是 | 当前展示费用 |
| `current_attack`、`current_health`、`maximum_health` | i32 | 是 | 当前战斗数值 |
| `keywords` | u32 | 是 | `Keyword` 位掩码，见下方冻结表 |
| `evolved`、`attacked_this_turn`、`entered_this_turn` | bool | 是 | 单位状态 |
| `temporary_rush`、`deployed_from_standby`、`face_down` | bool | 是 | 临时突进、部署来源与背面状态 |
| `countdown` | i32 | 是 | 设施倒计时；不适用时为 0 |
| `granted_component` | `ComponentSpec` | 是 | 运行时组件能力 |

`CardDefinition`：

| 字段 | 类型 | 必需 | 语义 |
|---|---|---|---|
| `id` | u32 | 是 | 卡牌定义 ID |
| `name` | string | 是 | UTF-8 规则名称 |
| `kind` | CardKind | 是 | 卡牌类型 |
| `cost`、`attack`、`health`、`countdown` | i32 | 是 | 印刷费用/数值/倒计时 |
| `printed_guard`、`printed_rush`、`printed_storm` | bool | 是 | 印刷关键词 |
| `printed_barrier`、`printed_lifesteal`、`printed_bane` | bool | 是 | 印刷关键词 |
| `evolved_attack`、`evolved_health` | i32 | 是 | 进化后印刷数值；两者均为 0 时使用规则默认值 |
| `additional_cost` | object | 是 | 固定含 i32 `burn_pp_capacity` |
| `deployment` | `DeploymentSpec` | optional | 仅有部署规格的卡牌输出 |
| `component` | `ComponentSpec` | 是 | 卡牌可授予的组件能力 |
| `effects` | `EffectRecord[]` | 是 | 数据驱动效果，保留定义顺序 |

其余嵌套定义：

| 对象 | 必需字段 |
|---|---|
| `LeaderSkillDefinition` | string `name`、i32 `cost`、`EffectRecord[] effects` |
| `EffectRecord` | EffectTrigger `trigger`、EffectKind `kind`、i32 `amount`、TargetSpec `target_spec` |
| `ComponentSpec` | bool `has_component`、EffectKind `granted_kind`、i32 `granted_amount` |
| `DeploymentSpec` | DeploymentCondition `condition`、i32 `condition_amount`、i32 `pp_cost`、bool `archive_one_friendly_unit` |

### 命令、查询结果与支付

`LegalAction` 固定含 `command`（`GameCommand`）与 `payment`（`PaymentPreview`）。输出的
`GameCommand` 不含 `schema_version`，其余字段如下；补回 `schema_version: 1` 后可原样提交：

| 字段 | 类型 | 必需 |
|---|---|---|
| `player` | Player | 是 |
| `action` | ActionKind | 是 |
| `source` | u64 | 是 |
| `target` | Target | optional |
| `slot` | u64 | optional |
| `component_donor` | u64 | optional |
| `use_advance` | bool | 是 |
| `mulligan_cards` | `u64[]` | 是 |
| `expected_revision` | u64 | 是 |

`Target` 固定含 Target kind `kind` 与 Player `player`；单位目标还必须含 u64 `unit`，主战者
目标省略 `unit`。

`PaymentPreview`：

`PaymentPreview` 严格表示命令提交时支付的成本投影，而不是完整命令结算后的状态。
`*_after` 只包含印刷 PP、预支、燃耗、部署 PP 与主动进化能量成本；不包含卡牌效果、
战斗、回合切换或响应结算造成的资源变化。没有成本的行动保持 after 与 before 相同。
这样预览不会因为对方背面伏策是否匹配当前行动而产生可观察差异。

| 字段 | 类型 | 必需 |
|---|---|---|
| `status` | `Status` | 是 |
| `current_pp_before`、`current_pp_after` | i32 | 是 |
| `pp_capacity_before`、`pp_capacity_after` | i32 | 是 |
| `cracks_before`、`cracks_after` | i32 | 是 |
| `evolution_energy_before`、`evolution_energy_after` | i32 | 是 |
| `base_cost`、`burn_cost`、`advance_cost` | i32 | 是 |
| `used_advance` | bool | 是 |

`Status` 固定含 u32 `engine_code` 与 string `message`。`engine_code` 使用上文冻结的规则码，
不是 native 状态码。

### `ReactionContext` 与 `GameEventView`

`ReactionContext`：

| 字段 | 类型 | 必需 | 语义 |
|---|---|---|---|
| `pending` | bool | 是 | 是否存在响应窗口；为 false 时其余上下文字段只作占位 |
| `window` | ReactionWindow | 是 | 响应窗口类型 |
| `responder` | Player | 是 | 当前响应者 |
| `subject` | u64 | 是 | 公开的法术/单位/攻击主体；无主体时为 0 |
| `depth` | u64 | 是 | 当前响应栈深度 |
| `eligible_count` | u64 | 是 | 合法伏策数量；两名 viewer 都可见 |
| `eligible_traps` | `Card[]` | 是 | 仅 responder viewer 可见完整身份；其他 viewer 为空数组 |
| `revision` | u64 | 是 | 生成上下文时的状态 revision |
| `origin` | `ReactionOrigin` | optional | pending 时必有；公开描述被挂起的原行动，非 pending 时省略 |

`ReactionOrigin` 固定含 ActionKind `action`、Player `player` 和 u64 `source`；原行动有目标时
还含与命令相同结构的 optional `target`。`subject` 为兼容既有消费者保留的窗口摘要；新客户端
应使用 `origin` 渲染发动者、来源和完整目标。

`GameEventView`：

| 字段 | 类型 | 必需 | 语义 |
|---|---|---|---|
| `sequence` | u64 | 是 | 全局单调事件序列 |
| `type` | EventType | 是 | 事件类型 |
| `player` | Player | 是 | 事件主体玩家 |
| `card` | u64 | optional | 安全可见时的实例 ID |
| `definition_id` | u32 | optional | 安全可见时的定义 ID |
| `value`、`secondary_value` | i32 | 是 | 事件类型定义的数值载荷 |
| `hidden_card` | bool | 是 | 卡牌身份是否已按 viewer 隐藏 |
| `text` | string | 是 | UTF-8 表现提示；不是状态真值 |
| `random_seed` | u32 | optional | 仅 `MatchStarted` |
| `first_player` | Player | optional | 仅 `MatchStarted` |

### 冻结枚举与位掩码

这些数值由 native 适配层显式映射，不依赖 C++ enum 的底层值。已有值不得改号或复用；
新增值只能尾增，新增关键词只能使用尚未分配的位。

| JSON 类型 | 冻结数值 |
|---|---|
| Player | 0 Player0，1 Player1 |
| ActionKind | 0 Mulligan，1 PlayUnit，2 CastSpell，3 PlayTactic，4 Attack，5 Evolve，6 Deploy，7 ActivateTrap，8 PassReaction，9 EndTurn，10 Surrender |
| Target kind | 0 Leader，1 Unit |
| CardKind | 0 Unit，1 Spell，2 Relic，3 Trap |
| Zone | 0 None，1 Deck，2 Hand，3 Unit，4 Tactic，5 Graveyard，6 Archive，7 Standby |
| Phase | 0 NotStarted，1 Mulligan，2 Action，3 Reaction，4 Finished |
| ReactionWindow | 0 None，1 SpellDeclared，2 EntryEffectPending，3 AttackDeclared |
| GameResult | 0 Ongoing，1 Player0Won，2 Player1Won，3 Draw |

| EffectTrigger | 数值 |
|---|---:|
| OnPlay | 0 |
| OnPlayIfAdvanced | 1 |
| OnPlayIfNotAdvanced | 2 |
| OnEntry | 3 |
| OnEvolution | 4 |
| OnLastWords | 5 |
| OnCountdownExpire | 6 |
| OnSpellDeclared | 7 |
| OnAttackDeclared | 8 |
| OnEntryEffectPending | 9 |

| EffectKind | 数值 |
|---|---:|
| DrawCards | 0 |
| DealDamageToEnemyUnit | 1 |
| DealDamageToLeader | 2 |
| HealLeader | 3 |
| RepairCracks | 4 |
| GainPPCapacity | 5 |
| BuffFriendlyUnit | 6 |
| GrantRush | 7 |
| CancelAttack | 8 |
| DamageEnteredUnit | 9 |

| TargetSpec | 数值 |
|---|---:|
| None | 0 |
| EnemyUnit | 1 |
| FriendlyUnit | 2 |

| DeploymentCondition | 数值 |
|---|---:|
| None | 0 |
| FriendlyUnitsMin | 1 |
| SpellsThisTurnMin | 2 |

| EventType | 数值 |
|---|---:|
| MatchStarted | 0 |
| TurnStarted | 1 |
| TurnEnded | 2 |
| CardDrawn | 3 |
| FatigueDamage | 4 |
| HandOverflowArchived | 5 |
| PPChanged | 6 |
| CracksChanged | 7 |
| CardMoved | 8 |
| UnitEntered | 9 |
| UnitDamaged | 10 |
| LeaderDamaged | 11 |
| LeaderHealed | 12 |
| UnitDestroyed | 13 |
| AttackDeclared | 14 |
| AttackCancelled | 15 |
| UnitEvolved | 16 |
| EvolutionEnergyChanged | 17 |
| UnitDeployed | 18 |
| TrapWindowOpened | 19 |
| TrapActivated | 20 |
| LeaderSkillUsed | 21 |
| PlayerSurrendered | 22 |
| MatchEnded | 23 |
| MulliganCompleted | 24 |

`keywords` 是 u32 位掩码：

| 位/数值 | Keyword |
|---:|---|
| 无位 / `0x00000000` | None |
| bit 0 / `0x00000001` | Guard |
| bit 1 / `0x00000002` | Rush |
| bit 2 / `0x00000004` | Storm |
| bit 3 / `0x00000008` | Barrier |
| bit 4 / `0x00000010` | Bane |
| bit 5 / `0x00000020` | Lifesteal |
| bit 6 / `0x00000040` | Ambush |

## 隐私与一致性

- Native 层只能序列化 `MatchView`、`LegalAction`、`PaymentPreview`、`ReactionContext` 和
  `GameEventView`，不得直接读取 `PlayerState` 或原始 `GameEvent` 后再删除字段。
- 对方手牌只通过 `hand_count` 暴露数量；`hand` 为空。
- 对方背面伏策不包含 `instance_id`、`definition_id`、`definition` 或真实 `name`。
- 抽牌、调度和设置伏策事件按 viewer 脱敏；历史事件即使牌以后公开也不得补漏身份。
- `read_events_json(handle, viewer, after_sequence, ...)` 是非破坏读取；两个 viewer 的游标
  完全由调用方持有，互不消费。
- 成功命令使 revision 恰好增加一次；规则失败或 native 失败不改变状态、事件或 revision。
- 快照是状态真值；事件只用于日志和表现。
