# 工程交接：Gate 0+1 → Godot 热座客户端前置完成

> 现行交接文档。旧 [`DSH-HANDOFF.md`](DSH-HANDOFF.md) 与 [`ygopro-integration.md`](ygopro-integration.md) 是历史归档，不是执行指令。

## 0. 基线与交付边界

- 仓库：`Morphling0717/SomeCardGameShit`
- 起始基线：`main@cfdf695d70eeabcc6de9b094c94041364fb1335f`
- 实现分支：`codex/godot-hotseat-gate1`
- 规则真值：[`rules-v0.4.md`](rules-v0.4.md)，用户最新明确决定优先于旧文档歧义
- 详细路线：[`GODOT-HOTSEAT-DEVELOPMENT-PLAN.md`](GODOT-HOTSEAT-DEVELOPMENT-PLAN.md)
- 构建与实测：[`../TEST_REPORT.md`](../TEST_REPORT.md)

本轮只做 Gate 0+1：文档/构建基线、规则状态机加固和客户端安全 C++ API。不创建 Godot 场景、不实现 C ABI、不增加正式美术或表现 JSON；不修改 legacy v1 wire 字节；不推送、合并或打标签。

## 1. 不可推翻的架构决定

```text
Godot 4.7.2 .NET（后续）
        ↓
版本化 C ABI（后续）
        ↓
客户端安全 C++ API（Gate 1）
        ↓
Game / C++20 规则引擎（唯一规则真值）
```

- Godot/C# 只显示快照、列出引擎给出的选项、提交命令和播放事件，不复算费用、目标、伤害或胜负。
- 正式目标是 Windows x86-64 与 macOS Apple Silicon，明确不支持 Web。
- 工具链锁定 Godot 4.7.2 .NET、.NET SDK 10.0.400、CMake 3.25+；详见 [`toolchain.md`](toolchain.md) 和根目录 [`global.json`](../global.json)。
- YGOPro2/Unity 已停止投入。overlay、upstream、工具和远端 M1 分支只作历史参考；不要删除，也不要继续扩展。

## 2. Gate 0 基线

- README、架构、路线图、测试说明、开发计划与本交接采用同一现状；旧交接保留原文并已醒目标为历史归档。
- 一次性 `.m1-feature.ready` 与 `import-m1-feature.yml` 已移除。
- `SCGS_ENABLE_LEGACY_YGO2_TESTS` 默认开启；开启时配置要求 Python 3.10+，不能静默少跑两个 Python CTest。CI 固定 Python 并显式开启该选项。
- legacy v1 wire 保持完全相同。历史字段名不等于当前投影语义：PlayerState flags bit1 当前投影 `deploy_used_this_turn`；UnitState flags bit3 当前投影“战备部署入场且仍为入场回合”。

## 3. Gate 1 规则决定

以下行为有回归测试约束：

- 结束回合严格按“结束效果 → 清临时状态 → 当前 PP 清零并发事件 → `TurnEnded` → 对方回合”执行。
- 响应栈按“反制 → 响应 → 原行动”LIFO 结算；反制过牌不得丢失第一层伏策或原行动；实现不得保留跨 vector 增长的元素引用；法术声明支持 `OnSpellDeclared`。
- 命令在支付成本前完整验证目标。响应期间目标失效时，仅跳过依赖目标的效果，继续同一记录中的其他效果，不抛异常且不回滚成本。
- `MatchEnded` 每局至多一次；终局后不抽牌、不处理设施倒计时，不再改变比赛状态。
- 部署目标格可以正是即将作为组件代价被封存的单位格。
- 所有公开命令先检查玩家枚举，非法值返回 `InvalidPlayer`。
- 进化解锁前，职业条件不产生能量；先手第五个自己回合解锁并获得 2，后手第四个自己回合解锁并获得 3；解锁后职业条件才可充能，封顶 4。
- `FirstPlayerMode::{Random, Player0, Player1}`：产品默认随机，测试可强制并指定 seed；快照和开局事件记录实际 seed/先手。本阶段不保证 `std::shuffle` 跨标准库完全一致。

## 4. 客户端唯一入口

### 数据类型

- `ActionKind`：`Mulligan`、`PlayUnit`、`CastSpell`、`PlayTactic`、`Attack`、`Evolve`、`Deploy`、`ActivateTrap`、`PassReaction`、`EndTurn`、`Surrender`。
- `GameCommand`：玩家、动作、来源牌/单位、目标、位置、组件来源、预支选择、调度列表与 `expected_revision`。
- 查询类型：`LegalAction`、`ActionQuery`、`PaymentPreview`、`ReactionContext`。
- 安全视图：`CardView`、`PlayerView`、`MatchView`。
- 脱敏事件：`GameEventView` + sequence 游标。

### `Game` API

```text
make_view
list_legal_actions
list_valid_targets
list_valid_slots
list_valid_donors
preview_payment
get_reaction_context
submit_command
read_events
```

典型客户端循环：

```text
make_view(viewer)
→ list_legal_actions / 细分查询
→ submit_command(expected_revision)
→ read_events(viewer, after_sequence)
→ 重新 make_view
```

查询和执行共享 `validate_*`；旧强类型命令可供测试/内部兼容，但不能形成第二套验证。成功命令只增加一次 revision；失败命令不改变状态、事件或 revision。

## 5. 隐私契约

- 自己手牌返回完整数据；对方手牌只返回数量。
- 对方背面伏策不返回 definition ID 或 instance ID。
- 战备、单位、墓地和封存区公开。
- `read_events(viewer, after_sequence)` 是非破坏读取；两位观看者游标互不干扰。
- 抽牌、调度、设置伏策等事件按 viewer 脱敏；隐藏文本不得含卡名或稳定实例 ID。
- UI 遮屏只是额外保护，不能替代引擎层脱敏。

## 6. 验收与限制

必须以 [`../TEST_REPORT.md`](../TEST_REPORT.md) 的实际结果为准，覆盖 MSVC Release、可用环境下的 GCC/Clang sanitizer、Release 2,048 seeds、sanitizer 256 seeds、legacy wire/Python tests 与 `git diff --check`。未推送分支不能声称新 commit 的 GitHub CI 已绿。

Alpha 只承诺两副现有固定牌组。以下延后：

- 主战技 UI；
- 普通主动能力；
- 同时触发的人工排序；
- 固定牌组未使用关键词；
- 联机、录像、卡组编辑和 Web。

同一玩家同时触发暂按确定性场地顺序，是明确的 alpha 限制。

## 7. 下一步

确认 Gate 0+1 的本地提交和完整测试后，再单独进入 Gate 2：设计版本化 C ABI。不要直接让 Godot 链接 C++ 类/STL，不要让异常跨语言边界，也不要在 C# 中复制规则。Gate 2 稳定后才创建 Godot 4.7.2 .NET 工程。
