# 工程交接：Gate 0+1+2 → Godot 热座客户端前置完成

> 现行交接文档。旧 [`DSH-HANDOFF.md`](DSH-HANDOFF.md) 与 [`ygopro-integration.md`](ygopro-integration.md) 是历史归档，不是执行指令。

## 0. 基线与交付边界

- 仓库：`Morphling0717/SomeCardGameShit`
- 起始基线：`main@cfdf695d70eeabcc6de9b094c94041364fb1335f`
- Gate 1 提交基线：`codex/godot-hotseat-gate1@f048d11`
- Gate 2 实现分支：`codex/godot-hotseat-gate2`
- 规则真值：[`rules-v0.4.md`](rules-v0.4.md)，用户最新明确决定优先于旧文档歧义
- 详细路线：[`GODOT-HOTSEAT-DEVELOPMENT-PLAN.md`](GODOT-HOTSEAT-DEVELOPMENT-PLAN.md)
- 构建与实测：[`../TEST_REPORT.md`](../TEST_REPORT.md)

当前交付在 Gate 0+1 的文档/状态机/安全 C++ API 上完成 Gate 2：`scgs_v04` 纯 C11 ABI、版本化 UTF-8 JSON、Windows/Linux/macOS 动态库、安装包与对照测试。不创建 Godot 场景、不增加正式美术或表现 JSON；不修改 legacy v1 wire 字节。功能分支已获准推送验证，但不合并或打标签。

## 1. 不可推翻的架构决定

```text
Godot 4.7.2 .NET（后续）
        ↓
scgs_v04 C11 + JSON（Gate 2）
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

## 6. Gate 2 原生消费边界

- 规范性头文件是 `engine/include/scgs/native_api_v04.h`，载荷契约见 [`native-api-v04.md`](native-api-v04.md)。
- `scgs_v04` 是 v0.4 产品线名；ABI 初始 1.0，JSON schema 初始 1。CMake 项目版本不能替代 ABI version。
- ABI 只暴露固定宽度整数、UTF-8 缓冲区和 64 位 token handle；没有 C++ 类、STL、异常、`size_t`、C `bool` 或跨 CRT 释放。
- 复杂 DTO 采用调用方所有的两段式 JSON 缓冲区，所需长度包含 NUL，容量不足不部分写。
- Native 状态与规则 `ErrorCode` 分离；所有枚举使用显式冻结映射，不能直接转发 C++ 底层值。
- `read_events_json(viewer, after_sequence)` 是非破坏读取；旧计划中的无 viewer `drain_events` 草案无效。
- 适配层只能调用 Gate 1 的安全 API 并序列化安全 DTO，禁止访问 `PlayerState` 或原始事件后再删字段。
- 同一 handle 第一版只承诺单线程顺序使用；Godot 后续应在主线程消费。

## 7. 验收与限制

必须以 [`../TEST_REPORT.md`](../TEST_REPORT.md) 的实际结果为准，覆盖 MSVC Release/Debug、GCC Release、Clang ASan/UBSan、macOS ARM64、Release 2,048 seeds、sanitizer 256 seeds、C11 consumer、动态加载/导出审计、legacy wire/Python tests 与 `git diff --check`。只有对应 commit 的远端任务完成后才能声称 CI 已绿。

Alpha 只承诺两副现有固定牌组。以下延后：

- 主战技 UI；
- 普通主动能力；
- 同时触发的人工排序；
- 固定牌组未使用关键词；
- 联机、录像、卡组编辑和 Web。

同一玩家同时触发暂按确定性场地顺序，是明确的 alpha 限制。

## 8. 下一步

确认 Gate 2 的最终提交、三平台产物和四平台 CI 后，再单独进入 Gate 3：安装锁定的 .NET SDK 10.0.400 与 Godot 4.7.2 .NET，创建最小工程，并通过 P/Invoke 读取第一张真实快照。C# 不复制规则、不解析内部 `PlayerState`，也不得绕过 `scgs_v04` 直接链接 C++ 类/STL。正式 UI 仍从最小原生加载 smoke 之后开始。
