# scgs_v05 原生边界（ABI 2.0／JSON schema 2）

`scgs_v05` 是产品牌组运行时的新边界，与冻结的 `scgs_v04` 并行存在。它不修改
v04 的 ABI、14 个导出、schema 1 或 legacy v1 wire。

> Gate 5B 状态：当前动态库连接的是确定性 foundation adapter，用于冻结并验证
> schema 2、生命周期、隐私、revision 和托管绑定。快照来自真实 `ProductBoard`，
> 并用按牌组身份构造的显式验收 fixture 展示混合主战场、独立场地、背面伏策和一次
> 可恢复的私密选择；它支持开局、该选择、调度、结束回合与投降，但尚不执行产品
> 卡牌效果。其他出牌命令会返回 `InvalidCard`。fixture 不是开局规则，也不是两副
> 产品牌已可玩的声明。

## 1. 固定版本

- 动态库名：Windows `scgs_v05.dll`，macOS `libscgs_v05.dylib`
- ABI：`0x00020000`（2.0）
- JSON schema：`2`
- 调用约定：C ABI，Windows `cdecl`
- 输入上限：1 MiB；所有字符串为严格 UTF-8 且长度不包含 NUL
- 输出：调用方两段式缓冲区，`required_bytes` 包含结尾 NUL

## 2. 精确 14 个导出

`scgs_v05_abi_version`、`create`、`destroy`、`start`、`get_view_json`、
`list_legal_actions_json`、`list_valid_targets_json`、`list_valid_slots_json`、
`list_valid_donors_json`、`preview_payment_json`、`get_reaction_context_json`、
`submit_command_json`、`read_events_json`、`get_last_error`，除首项外均以
`scgs_v05_` 为前缀。签名以 `engine/include/scgs/native_api_v05.h` 为唯一权威。

`get_last_error` 是线程局部诊断；调用失败后必须立即在同一线程读取。规则失败通过
`out_engine_code` 返回，不属于 native exception。

## 3. 冻结枚举

- `CardKind`：Follower 0、Spell 1、Amulet 2、Trap 3、Field 4
- `Zone`：None 0、Deck 1、Hand 2、MainBoard 3、Tactic 4、Graveyard 5、
  Archive 6、Standby 7、Field 8
- `ActionKind`：保留 0～10；PlayAmulet 11、PlayField 12、ResolveChoice 13
- `TargetKind`：Leader 0、Permanent 1

关键词 bit 保持 Guard/Ward、Rush、Storm、Barrier、Bane、Lifesteal 的既有位值；
schema 2 分开输出 `printed_keywords`、`permanent_keywords`、`turn_keywords` 和最终
`keywords`，未知未来 bit 必须原样保留。

## 4. 请求与快照

配置只接受 `oathguard_luminous_oath_v1` 与 `pactmage_abyssal_pact_v1`。双方允许选择
同一牌组；未知配置字段或错误字段类型会以 schema mismatch 拒绝。测试可以传
`random_seed`，但 seed 不得出现在观看者快照、开局事件或普通事件中。

`GameCommand`／`ActionQuery` 在原字段之外支持：

- `mode_id`
- `choice_id`
- 有序 `selected_option_ids`
- `additional_cost_cards`

命令按 `ActionKind` 使用严格字段矩阵：无关字段即使是 `false` 或空数组也必须省略，
提交时作为无副作用的规则错误返回。调度只携带 `mulligan_cards`；结束回合、不过与
投降不携带动作参数；`ResolveChoice` 只携带 `choice_id` 与有序 option ID。当前
foundation 不执行产品出牌，因此目标、格位和组件查询不会枚举任何无法提交的候选。
所有查询仍严格校验动作字段、比赛生命周期和 `expected_revision`；非法查询通过 native
错误与同线程 `last_error` 明确失败，不能伪装成成功的空候选列表。

玩家状态包含 `main_board`（固定 5 格）、`tactics`（固定 3 格）和仅在有牌时出现的
`field`。公开卡包含 `design_id`、`profession_id`、`series_id`、`neutral` 与 card kind。
对方手牌只给数量；对方背面伏策不能包含实例 ID、设计 ID、构筑标签或身份材质信息，
并且 `sequence`、费用、身材、倒数与所有关键词层都必须为零。

`pending_choice` 使用短生命周期 opaque ID。选择者可看到 `choice_id` 和 options；
另一观看者只能知道选择正在等待以及选择者，不得获得 choice/option ID 或候选卡。
这些 ID 绑定当前 session 与当前 pending choice；完成选择、投降或切换 session 后均不可
重放，且不编码 native handle、卡牌实例或 definition 身份。
待选择时最终规则运行时只允许选择者 `ResolveChoice`，但双方始终可以投降；合法行动
查询按此枚举，并保证每一项都能在同 revision 的等价新会话上成功提交。
foundation 的选择完成事件使用可前向兼容的通用事件类型；另一观看者只收到固定脱敏
文本，不包含 choice/option ID 或卡牌身份。两个观看者仍用各自事件游标读取。

## 5. 托管边界

`Scgs.Client.V05` 提供独立的 `LibraryImport`、`ScgsV05SafeHandle`、严格 schema 2 DTO、
`IScgsV05GameSession` 和 `ScgsV05GameSession`。它与 `Scgs.Client` 的 v04 类型并行，
因此 Godot 在迁移完成前不会意外把两种 schema 混用。两个观看者分别维护事件游标。
