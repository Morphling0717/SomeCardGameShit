# SomeCardGameShit M1-G 开发计划与执行记录

**计划代号：** M1-G / Godot Hotseat Alpha  
**代码基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`
**Gate 0+1 实现分支：** `codex/godot-hotseat-gate1`
**Gate 2 实现分支：** `codex/godot-hotseat-gate2`
**Gate 3A 已验收尖端：** `codex/godot-hotseat-gate3@5158409`
**Gate 3B 已验收尖端：** `codex/godot-hotseat-gate3b@dd38e93`
**Gate 3C 已验收尖端：** `codex/godot-hotseat-gate3c@a29dd14`
**Gate 4A 工作分支：** `codex/godot-hotseat-gate4a`
**规则基线：** `docs/rules-v0.4.md`  
**目标客户端：** Godot 4.7.2 .NET（Gate 4A 默认 3D/2.5D 占位战场）
**.NET SDK：** 10.0.400
**目标平台：** macOS Apple Silicon、Windows x86-64  
**M1-G 总目标：** 从完整无界面引擎推进到人类可以完整打一局的单机热座版本

> Gate 3C 已建立点击/拖拽战场直操、复杂动作上下文选择与中立公开 `Resolving` 投影。Gate 4A 只把表现后端升级为默认 3D/2.5D，并以隐藏 `--legacy-2d-board` 保留同源 2D 回归；最终自动化、导出和 CI 结果只以 [`../TEST_REPORT.md`](../TEST_REPORT.md) 的同提交实测为准。物理 Apple Silicon、目标分辨率视觉遍历与两名真人热座仍是发布标签前硬门。项目不支持 Web，不修改 legacy v1 wire 字节，也不在本 Gate 创建 PR、合并或标签。

---

## 一、开工基线与当前交付

> 本节的“已经完成/尚未完成”首先记录 `main@cfdf695` 的开工审计，便于解释 Gate 1 的来源。Gate 1 已关闭规则边界与客户端安全 API 缺口，Gate 2 完成稳定原生消费边界，Gate 3A 完成 Godot 桌面骨架与首张安全快照，Gate 3B 接入完整操作流程，Gate 3C 完成直接交互加固；Gate 4A 推进默认 3D 表现，真人/物理设备验收仍未完成。

### 1. 已经完成的部分

当前项目已经从最初的 YGOPro2 原型路线转为：

```text
Godot 4 .NET 客户端
        ↓
版本化 C ABI
        ↓
C++20 v0.4 规则引擎
```

技术决策已经确定：

- 放弃 YGOPro2 / Unity 5.6.7 作为正式客户端；
- 使用 Godot 4 .NET；
- 客户端只提交命令、读取状态和播放表现；
- C++ `Game` 是唯一规则裁判；
- 第一版先做同机热座；
- 异地联机放到下一阶段；
- 视觉采用 YGOPro2 式简洁桌面风格，不做 Master Duel 式重演出；
- 素材全部原创或明确授权。

规则引擎目前已经实现：

- 无上限 PP 容量；
- 当前 PP 与 PP 容量分离；
- 预支、燃耗、裂痕、修复和增长；
- 当前 PP 高于容量的合法状态；
- 超前与按期；
- 单一进化形式与职业充能；
- 公开战备区和部署；
- 组件能力；
- 三层响应结构；
- 5 个单位位和 3 个策略位；
- 单位战斗、持续伤害、同时死亡；
- 手牌溢出、封存、疲劳；
- 数据驱动卡牌效果；
- 两副固定测试牌组。

现有自动化基线包括（准确用例与断言数量以本分支最终 `TEST_REPORT.md` 为准）：

- C++ 规则、客户端契约与 wire 测试；
- 32 个种子、双方轮流先手的确定性烟雾对局；
- 每一步行动后的状态不变量检查；
- Debug、Release、ASan 和 UBSan；
- legacy v1 wire 金标字节测试；
- 预支、燃耗、当前 PP 超过容量和修复的金标场景。

### 2. `main@cfdf695` 尚未完成的部分

开工基线当时没有：

- Godot 工程；
- Godot 场景；
- C++ 动态库接口；
- C# 绑定；
- 客户端安全视图；
- 完整合法行动查询接口；
- 可供人类操作的对局 UI；
- macOS 或 Windows 可玩构建；
- 真人完整对局验收。

开工基线的准确状态是：

```text
规则引擎：基本完成
规则自动测试：已有良好基础
客户端接入接口：未完成
Godot 客户端：不存在
人类可玩版本：不存在
```

Gate 0+1 交付后，客户端安全 C++ 接口已完成，并由只使用“快照 → 查询 → 命令 → 事件”的无界面代理整局测试约束；Gate 2 在其上提供 `scgs_v04` C ABI；Gate 3A 建立 Godot 工程、只读战场和桌面导出；Gate 3B 再加入可提交命令的完整热座 UI。仍未完成的是物理 Apple Silicon 和两名真人完整一局的发布前验收。

---

## 二、UI 前置审计（Gate 1 已关闭的基线问题）

以下条目是对 `main@cfdf695` 的问题记录。Gate 1 已完成这些前置加固并加入回归测试；保留原问题描述是为了让后续开发者理解接口约束，而不是表示缺陷仍然存在。

### P0-1：结束回合没有明确清空未使用的当前 PP

规则文档要求结束阶段将未使用的当前 PP 清零。基线 `end_turn()` 调用的 `clear_end_of_turn_state()` 主要清除临时突进，当时没有明确将离开回合玩家的 `current_pp` 设为 0。

这会让 UI 在对手回合仍显示上一名玩家残余 PP，造成错误信息。

处理方式：

```text
玩家以 5PP 开始回合
→ 消耗 2PP
→ 结束回合
→ 离开回合后 current_pp 必须为 0
→ 下次自己回合开始按新的容量恢复
```

先写失败测试，再修复实现。

### P0-2：反制层选择“不过”可能丢失底层响应

基线响应过程为：

```text
原行动
→ 对手发动伏策
→ 原行动玩家获得一次反制机会
```

当第二层存在可用伏策但玩家选择 `pass_reaction()` 时，必须确认：

- 第一层伏策仍会结算；
- 原始行动仍会按规则继续；
- 响应栈不会被直接清空；
- 结算顺序仍是后进先出。

新增测试：

1. 对方发动伏策，原行动者没有反制牌；
2. 对方发动伏策，原行动者有反制牌但选择不过；
3. 对方发动伏策，原行动者发动反制，严格按 LIFO 结算。

这三条现已由 Gate 1 回归覆盖；后续客户端不得绕过该结算路径。

### P0-3：客户端没有足够的合法行动查询能力

基线公开 API 能执行动作，但纯查询能力不足，客户端当时无法安全判断：

- 哪些手牌可以使用；
- 哪张牌需要预支；
- 使用后会产生多少裂痕；
- 哪些单位可以进化；
- 哪些战备牌可以部署；
- 哪些单位可以作为部署代价；
- 哪些位置和目标合法；
- 当前由谁响应；
- 哪些响应选项合法。

Gate 1 因此增加并统一为：

```text
list_legal_actions()
list_valid_targets()
list_valid_slots()
list_valid_donors()
preview_payment()
get_reaction_context()
submit_command()
read_events()
```

所有高亮、按钮和选择范围都必须来自引擎。

### P0-4：客户端需要按观看者过滤的状态快照

桥接层不能直接把完整 `PlayerState` 暴露给客户端。Gate 1 已从数据层落实观看者隔离，热座模式仍必须遵守这一边界。

现已新增：

```text
make_view(viewer)
```

规则：

- 当前观看者自己的手牌返回完整卡牌数据；
- 对方手牌只返回数量；
- 牌组只返回数量；
- 双方公开区域返回完整数据；
- 对方伏策返回占位和背面状态，不返回定义 ID；
- 战备区对双方完整公开；
- 墓地和封存区完整公开。

### P1-1：法术响应窗口缺少可配置触发

基线存在 `SpellDeclared` 响应窗口，但卡牌触发枚举没有完整对应的 `OnSpellDeclared`；Gate 1 已补齐。

Gate 1 已增加：

```cpp
EffectTrigger::OnSpellDeclared
```

并制作一张测试伏策，验证法术响应和三层反制流程。

### P1-2：卡牌数据缺少表现字段

规则数据不应承担 UI 素材定位。Gate 3B 继续使用纯色几何，并通过 `CardPresentation`、`ActionPresentation`、`GameEventPresentation` 和 `EngineCodeZhCnFormatter` 把 DTO/冻结 code 转为中文通用展示；本 Gate 不新增正式卡图或第二套规则判断。正式表现数据仍延后，届时可独立设计：

```text
CardPresentation
├─ card_id
├─ display_name
├─ rules_text
├─ flavor_text
├─ art_key
├─ frame_key
├─ class_key
├─ trait_keys
└─ keyword_help_keys
```

建议存放于：

```text
game_data/cards.zh-CN.json
game_data/keywords.zh-CN.json
game_data/decks.json
```

规则引擎只认识 `card_id` 和规则字段，Godot 通过 `card_id` 读取表现数据。

### P1-3：主战技暂不进入第一版 UI

v0.4 代码仍保留主战技接口，但当前规则验收重点没有把主战技列为核心系统。

处理方式：

- 代码接口保留；
- 第一版 Godot UI 暂不显示主战技按钮；
- 等规则文档正式确认后再接入；
- 不删除旧接口，也不把旧占位功能当成正式规则。

---

## 三、本阶段完成定义

M1-G 完成时，必须能在一台电脑上由两个人完整打一局。

### 1. 对局流程

```text
启动游戏
→ 选择固定牌组
→ 引擎随机先后手
→ 双方依次进行调度
→ 全屏遮挡并交接设备
→ 玩家揭示自己的回合
→ 出牌、攻击、进化、部署、响应
→ 结束回合
→ 再次遮挡并换人
→ 生命归零、疲劳、投降或平局
→ 显示结果
→ 可以重新开始
```

### 2. 必须支持的系统

- 25 点生命；
- 4 张起手与调度；
- 30 张主牌组；
- 0–6 张公开战备牌；
- 5 个单位位；
- 3 个策略位；
- 当前 PP；
- PP 容量；
- 裂痕；
- 预支；
- 燃耗；
- 修复；
- 增长；
- 超前与按期；
- 单位、法术、设施、伏策；
- 战斗；
- 守护、突进、疾驰、屏障、必杀、吸血；
- 进化；
- 职业进化充能；
- 战备部署；
- 部署代价与组件能力；
- 攻击、法术和登场效果响应窗口；
- 反制与跳过；
- 手牌溢出；
- 疲劳；
- 投降；
- 胜负与平局。

### 3. 不算完成的情况

以下情况不能称为 M1-G 完成：

- 只有静态场景；
- UI 可以点，但状态由 C# 自己修改；
- 只能打白板单位；
- 预支和燃耗只能靠调试按钮；
- 响应窗口不能正常过牌；
- 对手手牌数据被客户端完整读取；
- 只能在 Godot 编辑器中运行，不能导出；
- CI 绿，但用户机器无法实际完成一局；
- 只有 macOS 版本，没有 Windows 构建。

---

## 四、最终技术结构

```text
Godot Control 场景
        │
        ▼
C# BootstrapController / MatchScreen
        │  只渲染状态、传递 intent、安排公共投影后的延迟提交
        ▼
Scgs.Hotseat / HotseatMatchController
        │  只编排 IScgsGameSession，不判断规则
        ▼
IScgsGameSession / Scgs.Client
        │  LibraryImport + cdecl
        ▼
scgs_v04 C ABI
        │
        ▼
C++ Game
        │
        ├─ 校验命令
        ├─ 支付成本
        ├─ 修改状态
        ├─ 处理响应栈
        ├─ 输出事件
        └─ 生成观看者快照
```

基本原则：

1. C++ 快照是最终状态；
2. C++ 事件只用于日志和动画；
3. 每次命令完成后重新获取快照；
4. 动画不能自行修改规则状态；
5. C# 不计算费用、目标、伤害或合法性；
6. 第一版所有引擎调用都在 Godot 主线程；
7. 第一版不使用 C++ 回调 C#，采用主动轮询；
8. 两位 viewer 的事件 cursor 独立，只有 Godot 完成事件渲染后才 ACK；
9. 准备命令先发布中立公开 `Resolving`，完整绘制至少两帧后延迟提交；只有操作者变化才完全遮挡并等待主动揭示；
10. legacy v1 wire 保留并继续测试，但不作为 Godot 同进程客户端接口。

---

## 五、详细执行阶段

## Gate 0：基线整理与文档纠偏

### 工作内容

本轮开发分支：

```text
codex/godot-hotseat-gate1
```

更新：

- `README.md`
- `docs/architecture.md`
- `docs/roadmap.md`
- `docs/testing.md`
- `TEST_REPORT.md`

Gate 2 在接口真实落地时创建规范性 `docs/native-api-v04.md`；Gate 3A 在客户端真实落地时已经创建以下 UI 专项文档：

```text
docs/godot-client-architecture.md
docs/ui-state-map.md
docs/hotseat-acceptance.md
```

Gate 0+1+2+3A+3B+3C 的已实现架构和验收边界、以及 Gate 4A 的待验收表现契约，统一记录在 `docs/architecture.md`、`docs/testing.md`、`docs/native-api-v04.md`、上述 UI 专项文档、路线图与现行交接中。

清理明确的一次性遗留：

- `.m1-feature.ready`
- `.github/workflows/import-m1-feature.yml`

暂时不批量删除：

- `client/YGOPro2Overlay/`
- `upstream/`
- `tools/apply_ygo2_overlay.py`

这些文件标记为 legacy，不再进入现行构建。

### 工具版本

锁定 Godot 4.7.2 .NET 和 .NET SDK 10.0.400；版本升级必须作为独立任务，不与功能开发混在一起。CMake 最低版本统一为 3.25；legacy Python 测试开启时要求 Python 3.10+。

正式目标仅为：

- macOS Apple Silicon；
- Windows x86-64。

本阶段不做 Web 版本。

### 退出标准

- README 不再称项目为 YGOPro2 项目；
- 新路线文档与交接手册一致；
- Godot、.NET、目标平台被明确锁定；
- C++ 原测试保持全绿；
- 一次性工作流不再存在。

---

## Gate 1：引擎客户端化加固

### ENG-001～003：规则与状态机

- 结束回合按“结束效果 → 清临时状态 → 当前 PP 清零并发事件 → `TurnEnded` → 对方回合”执行。
- 响应实现不持有跨 `vector::push_back` 的元素引用；反制层过牌继续结算底层响应和原行动；真正按反制 → 响应 → 原行动 LIFO 结算。
- 增加 `OnSpellDeclared` 触发与测试伏策。
- 命令支付前完整验证目标；响应中目标失效只跳过依赖目标的效果，其他效果继续，不抛异常、不回滚成本。
- 终局幂等，每局一个 `MatchEnded`；终局后不抽牌、不处理设施倒计时或其他状态变化。
- 部署允许目标位置正是将被封存的组件单位位置。
- 所有公开命令先验证 `PlayerId`，非法枚举返回 `InvalidPlayer`。
- 进化解锁前职业条件不充能；解锁时先手固定获得 2、后手固定获得 3；解锁后才能充能，封顶 4。
- `FirstPlayerMode::{Random, Player0, Player1}`：产品默认随机，测试可强制并提供 seed；快照与开局事件记录实际 seed 和先手。本轮不承诺 `std::shuffle` 跨标准库完全一致。

### ENG-004～008：客户端安全 API

动作全集：

```cpp
enum class ActionKind {
    Mulligan,
    PlayUnit,
    CastSpell,
    PlayTactic,
    Attack,
    Evolve,
    Deploy,
    ActivateTrap,
    PassReaction,
    EndTurn,
    Surrender
};
```

新增 `GameCommand`（含玩家、动作、来源、目标、位置、组件来源、预支选择、调度列表和 `expected_revision`）、`LegalAction`、`ActionQuery`、`PaymentPreview`、`ReactionContext`。

安全视图使用 `CardView`、`PlayerView`、`MatchView`：自己手牌完整，对方手牌只有数量，对方背面伏策不含 definition/instance ID，公开区域完整，并包含单调 revision、实际 seed 和先手。

接口：

```cpp
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

`read_events(viewer, after_sequence)` 非破坏读取并按观看者脱敏；两个游标互不干扰，隐藏事件文本不得含卡名或稳定实例 ID。查询和执行共享同一 `validate_*`；成功命令 revision 恰好 +1，失败命令不改变状态、事件或 revision。旧强类型命令保留为测试/内部兼容，但不能形成第二套验证。

### 测试要求

增加：

- PP 清零与事件顺序；
- 反制过牌、真正三层 LIFO、无悬空引用、法术响应；
- 响应中目标失效与多效果继续结算；
- 致命攻击、疲劳、投降的唯一 `MatchEnded` 及终局后冻结；
- 解锁前不充能、解锁固定能量、解锁后充能封顶；
- 查询结果可提交、支付预览一致、非法输入无副作用；
- 手牌/伏策/事件隐私与独立事件游标；
- 只使用安全 API 的无界面固定牌组完整对局；
- MSVC/GCC Release、Clang ASan+UBSan、2,048/256 seeds 压力、legacy wire/Python tests 和 `git diff --check`。

### 退出标准

无界面测试客户端可以只使用：

```text
查看快照
→ 查询合法行动
→ 提交行动
→ 按游标读取事件
```

完成整局对战，不直接访问 `PlayerState` 内部结构。

---

## Gate 2：版本化 C ABI

### 目标

让 Godot C# 不依赖 C++ 类布局，不直接链接 C++ STL，也不允许异常跨语言边界。

### 文件结构

```text
engine/include/scgs/native_api_v04.h
engine/src/native_api_v04.cpp
engine/tests/test_native_api_v04.cpp
docs/native-api-v04.md
```

### ABI 原则

- 使用进程内不复用的 opaque 64 位 token handle，`0` 无效；
- 所有整数使用明确宽度，不暴露 C `bool`、`size_t` 或编译器 enum；
- `scgs_v04` 产品线名与 ABI 1.0、JSON schema 1、CMake 包版本分别管理；
- 复杂 DTO 使用版本化 UTF-8 JSON，不镜像含 string/vector/optional 的 C++ 布局；
- 所有输出由调用方拥有，统一采用包含末尾 NUL 的两段式缓冲区；
- 不返回 C++ `std::vector`、引用、内部指针或需要跨 CRT 释放的内存；
- 所有导出入口捕获异常，Windows 明确使用 `__cdecl`；
- native/transport 错误码与规则 `ErrorCode` 分离；
- native 适配层只序列化 Gate 1 安全 DTO，不直接读取 `PlayerState` 或原始事件；
- 同一 handle 第一版只承诺单线程顺序调用。

### 已实现接口

```c
uint32_t scgs_v04_abi_version(void);

scgs_v04_native_code scgs_v04_create(
    uint32_t requested_abi,
    const char* config_json,
    uint64_t config_bytes,
    scgs_v04_handle* out_handle);

scgs_v04_native_code scgs_v04_destroy(scgs_v04_handle handle);
scgs_v04_native_code scgs_v04_start(
    scgs_v04_handle handle,
    uint32_t* out_engine_code);

/* 所有 JSON 输出均为 char* buffer + uint64_t capacity
   + uint64_t* required_bytes 两段式接口。 */
scgs_v04_native_code scgs_v04_get_view_json(...);
scgs_v04_native_code scgs_v04_list_legal_actions_json(...);
scgs_v04_native_code scgs_v04_list_valid_targets_json(...);
scgs_v04_native_code scgs_v04_list_valid_slots_json(...);
scgs_v04_native_code scgs_v04_list_valid_donors_json(...);
scgs_v04_native_code scgs_v04_preview_payment_json(...);
scgs_v04_native_code scgs_v04_get_reaction_context_json(...);
scgs_v04_native_code scgs_v04_submit_command_json(...);

scgs_v04_native_code scgs_v04_read_events_json(
    scgs_v04_handle handle,
    uint32_t viewer,
    uint64_t after_sequence,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

scgs_v04_native_code scgs_v04_get_last_error(
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);
```

规范性字段、枚举、错误和所有权规则见 [`native-api-v04.md`](native-api-v04.md)。旧草案中的
`drain_events` 与 Gate 1 的观看者脱敏/独立游标冲突，已经由非破坏的
`read_events(viewer, after_sequence)` 取代。

### 构建产物

```text
macOS: libscgs_v04.dylib
Windows: scgs_v04.dll
Linux CI: libscgs_v04.so
```

### 测试要求

`test_native_api_v04.cpp` 必须通过 ABI：

- 创建比赛；
- 完成调度；
- 打出单位；
- 使用预支；
- 使用燃耗；
- 进化；
- 部署；
- 选择组件来源；
- 设置伏策；
- 发动伏策；
- 选择过牌；
- 完成攻击；
- 结束比赛；
- 检查隐藏信息；
- 对照直接调用 `Game` 的状态结果。

此外必须由真正的 C11 consumer 编译公开头，逐项验证 ABI/schema、handle 生命周期、非法
UTF-8/JSON、全部两段式缓冲区、动态符号加载、安装后消费、双 viewer 事件游标与失败命令
原子性。适配层测试代理只能走 ABI 完成固定牌组整局。

### 退出标准

C ABI 与直接 C++ 调用在每一步得到相同语义；Windows DLL、Linux so、macOS ARM64 dylib
均能安装、动态加载并通过导出表审计，三种 OS、四个 job 的 CI 已全绿；本 Gate 未创建 Godot 工程。

---

## Gate 3A：Godot 4 .NET 工程骨架与首张快照 — 已完成

### 实际目录

```text
client/
├─ Scgs.Client/                 纯托管 ABI/DTO/session（net8.0 + net10.0）
├─ Scgs.Client.Tests/           MSTest 与当前提交动态库集成（net10.0）
├─ Scgs.Hotseat/                Gate 3B 纯托管热座编排（net8.0 + net10.0）
└─ godot/
   ├─ project.godot
   ├─ SomeCardGameShit.csproj   Godot 桌面层（net8.0）
   ├─ export_presets.cfg
   ├─ assets/fonts/
   ├─ licenses/
   ├─ native/                   构建后暂存，二进制不提交
   ├─ scenes/{bootstrap,cards,match,menu,overlays,panels}/
   └─ scripts/{Bootstrap,Match,Native,Presentation,UI}/
```

Gate 3A 的 `Scgs.Client` 以 `LibraryImport` + `cdecl` 绑定全部 14 个导出，负责绝对路径解析、ABI handshake、SafeHandle、两段式严格 UTF-8 缓冲、schema 1 DTO 和 native/engine 错误分层。Gate 3B 的 `Scgs.Hotseat` 在它之上负责双 viewer cursor、合法行动选择、遮挡提交和操作者路由；`BootstrapController` 仍是唯一组合根。所有 native 调用在 Godot 主线程顺序执行。

### 已创建场景

```text
Bootstrap.tscn
MainMenu.tscn
Match.tscn
PassDeviceOverlay.tscn
SnapshotSlot.tscn
```

Gate 3B 已新增 `MulliganPanel`、`ActionPromptPanel`、`CardDetailPanel`、`ConfirmationPanel`、`ReactionPanel`、`EventLogPanel`、`ResultOverlay`、`ErrorOverlay` 与 `MatchInteractionDock`，并继续复用 `SnapshotSlot` 作为公开/脱敏卡位。

### 原生库与导出位置

```text
编辑器 Windows: client/godot/native/windows-x86_64/scgs_v04.dll
编辑器 macOS:   client/godot/native/macos-arm64/libscgs_v04.dylib
导出 Windows:   DLL 与 EXE 同目录
导出 macOS:     .app/Contents/Frameworks/libscgs_v04.dylib
```

Windows 产品 DLL 使用静态 MSVC runtime。macOS 导出临时派生 ARM64 template、放置 dylib 后重新 ad-hoc codesign；所有最终 Mach-O 必须 ARM64-only。导出包同时包含 Godot、.NET、nlohmann、Noto 和项目许可证/第三方声明。

### 已达退出标准

- Godot 4.7.2 .NET 冷导入无 script/build error；
- C# Debug/Release 构建零警告，27 项托管测试通过；
- 两个平台从同提交加载并审计对应动态库；
- 可以建立一局 C++ 比赛；
- 完全不透明遮挡揭示后，在界面上显示第一张真实 viewer 0 Mulligan 快照；
- Windows x86-64 与 macOS ARM64 导出程序都能真实启动 smoke。

## Gate 3B：完整热座 Alpha — 源码与自动复验已完成；发布硬门待办

### 热座编排

`HotseatMatchController` 不依赖 Godot，只依赖 `IScgsGameSession`。它公开冻结的 `HotseatUiState`，并覆盖：

- `Covered`、`MulliganSelecting`、`MulliganReview`、`Action`、`Reaction`、`Finished`、`Faulted`、`Disposed`；
- 完整合法行动集合与目标/位置/组件/预支的渐进候选；
- 同 revision 支付预览和规范命令确认；
- 每位 viewer 独立的事件 cursor、`PendingEvents` 和渲染后 `AcknowledgeEvents()`；
- `StaleRevision` 清选重查、engine code 中文化和 native/协议故障状态。

### 两阶段隐私提交（Gate 3B 历史基线）

Gate 3B 的 `ConfirmSelection()` 会发布 `Covered(ResolvingCommand)`。Gate 3C 已将该现行语义替换为 `PrepareSelectedCommand()` → 中立公开 `Resolving` → 至少两个完整绘制帧 → `SubmitPreparedCommand()`；只有初始揭示与实际换手继续使用 `Covered`。

调度成功后先进入 `MulliganReview`，让原 viewer 查看自己的替换手牌；确认看完后才交给下一席或进入实际先手。响应也按 responder 重复同一遮挡交接，不允许跨 viewer 预读。

### UI 闭环

Godot 通过统一候选支持调度、单位/法术/策略、攻击、进化、部署、伏策发动/不过、结束回合和投降；确认页显示引擎返回的费用投影。终局显示结果并允许重开或返回菜单，重开先释放旧 session。

事件日志只消费当前 viewer 的脱敏事件；全部事件完成渲染后才推进该 viewer cursor。对方手牌和背面伏策节点不携带身份、tooltip 或稳定 metadata。

### 本 Gate 不可伪装完成的验收

Gate 3A 首帧 smoke、Gate 3B headless 自动整局和 CI 导出都不能代替物理 Apple Silicon 与两名真人热座整局。这两项仍是创建 `v0.4-hotseat-alpha.1` 前的硬门；自动化/CI 已由 run `32583321294` 验证，完整状态和限制见 `TEST_REPORT.md`。

---

## 六、Godot 界面布局规范

### 1. 总体视觉

- 桌面视角；
- 卡牌永远是视觉中心；
- 信息密度高但结构清晰；
- 操作反馈直接；
- 动画短；
- 不用大型演出阻断节奏；
- 第一版使用原创纯色卡框、几何图形和文字占位。

### 2. 对局布局

```text
┌────────────────────────────────────────────┐
│ 对手信息：生命 / 当前PP / 容量 / 裂痕 / 进化能量 │
│ 对手手牌：只显示数量与背面                         │
│ 对手战备区：公开 0~6 张                           │
├────────────────────────────────────────────┤
│ 对手策略区：3 格                                   │
│ 对手单位区：5 格                                   │
│                                                    │
│                  战斗中央区域                       │
│                                                    │
│ 己方单位区：5 格                                   │
│ 己方策略区：3 格                                   │
├────────────────────────────────────────────┤
│ 己方战备区 / 墓地 / 封存区 / 牌组数量              │
│ 己方手牌                                            │
│ 己方生命 / 当前PP / 容量 / 裂痕 / 进化能量         │
│ [结束回合]                                          │
└────────────────────────────────────────────┘
```

### 3. PP 显示

明确分开显示：

```text
当前 PP：5
PP 容量：8
裂痕：3
```

当前 PP 高于容量时必须正常显示，不能截断或视为错误。

### 4. 预支与燃耗确认

涉及未来资源时显示：

```text
卡牌费用：8
当前 PP：5
预支差额：3
燃耗：0

使用后：
当前 PP：0
PP 容量：2
裂痕：3
下回合自然容量：3
```

按钮：

```text
[按期支付]
[动用未来]
[取消]
```

### 5. 部署界面

```text
点击公开战备牌
→ 显示部署条件
→ 引擎返回是否满足
→ 显示部署费用
→ 选择合法位置
→ 必要时选择作为代价的己方单位
→ 显示该单位可授予的组件
→ 确认
```

### 6. 响应窗口

显示：

```text
对方正在：攻击 / 使用法术 / 结算登场效果
当前响应层：1 或 2
当前需要操作：玩家 X
可发动伏策：N 张
```

操作：

```text
[发动选择的伏策]
[不过]
```

### 7. 热座隐私遮挡

每次换人时：

- 完全覆盖整个画面；
- 不保留半透明背景；
- 隐藏手牌、卡牌详情和敏感日志；
- 显示“请交给玩家 X”；
- 由下一名玩家主动揭示；
- 揭示后才获取对应观看者快照。

---

## 七、可玩闭环开发顺序

以下保留原始 Vertical Slice 顺序以便追溯。Gate 3B 源码已通过统一 `LegalAction`/DTO 流程接入四个 slice，自动化矩阵也已通过；是否达到可发布验收仍取决于物理 Apple Silicon、双人热座及验收清单中的其余人工硬门。

### Vertical Slice 1：最基础完整比赛

先实现：

- 固定牌组；
- 开局；
- 调度；
- 换人遮挡；
- 生命；
- PP；
- 手牌；
- 打出普通单位；
- 5 个单位位；
- 攻击单位和主战者；
- 持续伤害；
- 单位死亡；
- 结束回合；
- 疲劳；
- 投降；
- 胜负；
- 重新开始。

完成后必须能打一局。

### Vertical Slice 2：v0.4 资源机制

加入：

- PP 容量；
- 裂痕；
- 预支；
- 燃耗；
- 修复；
- 增长；
- 当前 PP 高于容量；
- 超前；
- 按期；
- 支付预览。

### Vertical Slice 3：进化与战备

加入：

- 进化解锁；
- 进化能量；
- 每回合一次；
- 进化目标和进化后状态；
- 进化时效果；
- 职业充能；
- 公开战备区；
- 部署条件、费用和代价；
- 组件继承；
- 战备牌离场封存。

### Vertical Slice 4：策略区与响应

加入：

- 3 个策略位；
- 设施；
- 倒计时；
- 伏策设置；
- 每回合一次设置；
- 背面信息隐藏；
- 攻击响应；
- 法术响应；
- 登场效果响应；
- 反制；
- 过牌；
- 后进先出；
- 响应日志。

---

## 八、自动化测试计划

### 1. C++ 规则测试

保留所有现有测试，并增加 Gate 1 的回归测试。每次命令后继续执行：

```cpp
validate_invariants()
```

### 2. C ABI 测试

覆盖：

- ABI 版本不匹配；
- 空 handle；
- 固定宽度 ABI 签名、调用约定与 schema 不匹配；
- 所有 JSON/文本输出的 `NULL + 0`、短缓冲区、精确容量、NUL 与无部分写入；
- UTF-8 文本；
- 错误码；
- 动态库创建与销毁；
- 完整比赛；
- 隐藏信息；
- 事件顺序；
- 直接 C++ 与 C ABI 结果一致。

### 3. C# 绑定测试

通过 `dotnet test` 检查：

- P/Invoke 调用约定、固定宽度参数和 64 位 handle；
- JSON schema 与 enum 数值映射；
- UTF-8 转换；
- 动态库搜索路径；
- 快照映射；
- 对方隐藏手牌；
- `ReactionOrigin` 的 pending/idle shape 与未来 action 降级；
- 中立公开 `Resolving` 投影、两帧提交屏障和新 viewer 揭示前零调用；
- 调度 selection/review、渐进候选和规范命令匹配；
- 支付预览与合法行动费用一致，stale revision 清选不自动重提；
- 两位 viewer cursor 只在 ACK 后独立推进；
- engine/native/协议错误分层、未知 code 中文降级与幂等 dispose；
- 同提交真实动态库经安全接口完成整局和终局复验。

### 4. Godot headless 测试

至少包括：

- 项目可导入；
- C# 构建通过；
- Bootstrap 场景可实例化；
- Match 场景节点路径完整；
- 原生库能够加载；
- 揭示前没有 viewer 读取，揭示后能创建比赛并取得安全快照；
- 命令不会在中立公开 `Resolving` 完整绘制两帧前执行；
- 调度、普通行动、响应、终局和重开路径能由安全 DTO/查询驱动；
- 每位 viewer 的事件只在日志渲染后 ACK；
- 成功标记只出现一次，结构化报告通过严格字段白名单校验；
- 导出预设有效。

### 5. Gate 3B 真人/实机测试 — 待办

Gate 3A 已完成 CI runner 上的 ARM64/x86-64 首帧导出、审计与 headless 启动 smoke；Gate 3B 增加的自动整局和导出复验仍不能替代下列物理设备、人工交互和完整一局。未实际执行前不得勾选。

#### macOS Apple Silicon

- 编辑器启动；
- Debug 运行；
- 导出 `.app`；
- 动态库加载；
- 完成一局热座；
- 退出和重开。

#### Windows x86-64

- 导出 `.exe`；
- DLL 路径正确；
- 用户机器启动；
- 朋友机器启动；
- 完成一局热座；
- 不依赖已安装 Visual Studio；
- 错误时生成可读取日志。

#### 两名真人热座

- 每次调度、结束回合和响应换手都先完全遮挡；
- 下一位玩家必须主动揭示，遮挡前不出现其快照/事件；
- 对方手牌、背面伏策、详情和日志不泄露身份；
- 两人从调度完成到唯一终局，并各验证一次重开或返回菜单。

---

## 九、CI 计划

### Gate 2+3A 已建立矩阵

四个 job 都必须配置、构建并运行完整 CTest，随后安装 `scgs_v04`、从安装目录编译独立
C11 consumer、审计目标架构与精确 14 个 C 导出，并上传暂存 artifact：

### Linux GCC Release

```text
GCC Release + `-Werror`
2,048-seed 规则压力
libscgs_v04.so / x86-64
```

### Linux Clang ASan/UBSan

```text
Clang Debug + ASan + UBSan
256-seed sanitizer 压力
libscgs_v04.so / x86-64
```

### macOS 15 ARM64 Release

```text
AppleClang Release
2,048-seed 规则压力
libscgs_v04.dylib / arm64
```

### Windows MSVC Release

```text
MSVC Release /W4 /WX
2,048-seed 规则压力
scgs_v04.dll / x86-64
```

### Gate 3A 已增加

Linux 两个 job 保持纯原生。Windows 与 macOS job 已在原生安装/审计后追加：

- `actions/setup-dotnet` 按 `global.json` 选择 10.0.400；
- 校验和缓存 Godot 4.7.2 .NET 编辑器与 Mono export templates；
- locked restore、Godot Debug/Release build 和 27 项 managed tests；
- 完成 Godot cold import、当前工程真实 native snapshot smoke；
- Windows x86-64 / macOS ARM64 导出、finalize、架构/布局/许可证审计；
- 30 秒超时内真实启动导出程序并检查唯一成功标记；
- 分别上传 native 与客户端包。

被测实现的四个 job 已在 CI run `32577089388` 全绿。本路线不设 Web 或 Linux 正式客户端构建任务。

### Gate 3B 追加要求

Linux 两个 job 仍保持纯原生。Windows 与 macOS job 在 Gate 3A 基线上还必须：

- locked restore/build `Scgs.Client`、`Scgs.Hotseat`、Godot 和 managed tests；
- 使用同提交真实 native 完成安全接口整局、双 viewer 隐私、响应 origin 和终局/dispose 集成测试；
- current-project 与导出程序各自只输出一次 `SCGS_GODOT_CI_SMOKE_OK`；
- 生成只包含固定字段的 Gate 3B 报告，验证 `premature_view_calls == 0`；
- Windows/macOS 导出 zip 解包到新目录后再次进行结构/架构/许可证审计并真实启动；
- artifact 使用 Gate 3B 名称并记录 SHA-256。

Gate 3B 被测实现 `9845a3f` 的 run [`32583321294`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32583321294) 已 4/4 jobs 全绿；准确测试数量、整局报告和 artifact 摘要记录在 `TEST_REPORT.md`，未沿用 Gate 3A run 作为通过结论。

---

## 十、开发工单与依赖关系

| ID | 状态 | 优先级 | 工单 | 依赖 |
|---|---|---:|---|---|
| DOC-001 | 完成 | P0 | 更新 README 为 v0.4 + Godot 路线 | 无 |
| DOC-002 | 完成 | P0 | 重写 architecture、roadmap、testing | DOC-001 |
| CLEAN-001 | 完成 | P1 | 移除一次性 M1 marker/import workflow | 无 |
| ENG-001 | 完成 | P0 | 结束回合清空当前 PP | 无 |
| ENG-002 | 完成 | P0 | 修复反制层选择过牌 | 无 |
| ENG-003 | 完成 | P1 | 增加法术响应触发 | ENG-002 |
| ENG-004 | 完成 | P0 | 增加观看者快照 | ENG-001 |
| ENG-005 | 完成 | P0 | 增加合法行动查询 | ENG-001 |
| ENG-006 | 完成 | P0 | 增加目标、位置、代价查询 | ENG-005 |
| ENG-007 | 完成 | P0 | 增加支付预览 | ENG-005 |
| ENG-008 | 完成 | P0 | 暴露完整响应上下文 | ENG-002 |
| ENG-009 | 完成 | P1 | 引擎随机先后手 | ENG-004 |
| DATA-001 | 待办 | P1 | 建立卡牌表现数据 | DOC-002 |
| ABI-001 | 完成 | P0 | 定义 `native_api_v04.h` | ENG-004～008 |
| ABI-002 | 完成 | P0 | 实现动态库 | ABI-001 |
| ABI-003 | 完成 | P0 | C ABI 对照测试 | ABI-002 |
| GODOT-001 | 完成 | P0 | 创建 Godot 4 .NET 工程 | ABI-003、CI-001 |
| GODOT-002 | 完成 | P0 | C# P/Invoke 与库解析 | ABI-002 |
| UI-001 | 完成 | P0 | 主战场静态布局 | GODOT-001 |
| HOTSEAT-001 | 完成 | P0 | 双 TFM 热座状态机、公共结算投影、换手遮挡与独立 cursor/ACK | GODOT-002、ENG-004～008 |
| UI-002 | 完成 | P0 | 初始/持续换手遮挡与中立公开结算投影 | HOTSEAT-001 |
| UI-003 | 完成 | P0 | 基础出牌/攻击/结束回合/投降 | GODOT-002、ENG-005 |
| UI-004 | 完成 | P0 | 双方调度与替换手牌 review | UI-002、ABI-003 |
| UI-005 | 完成 | P0 | 预支/燃耗支付预览 | ENG-007、UI-003 |
| UI-006 | 完成 | P0 | 进化与职业充能显示 | UI-003 |
| UI-007 | 完成 | P0 | 战备部署、位置与组件 | ENG-006、UI-003 |
| UI-008 | 完成 | P0 | 设施、伏策、响应 origin 与不过 | ENG-008、UI-003 |
| UI-009 | 完成 | P1 | 脱敏事件日志、终局与错误 overlay | UI-003～008 |
| CI-001 | 完成 | P0 | 动态库三平台构建 | ABI-003 |
| CI-002 | 完成 | P0 | dotnet 与 Godot headless | GODOT-002 |
| CI-003 | 完成 | P0 | macOS/Windows 导出产物 | CI-002 |
| CI-004 | 完成 | P0 | Gate 3C schema v2 整局报告、唯一 marker、解包复审/启动 | UI-002～009 |
| UI-010 | Gate 4A 开发中 | P0 | 默认 3D presenter、HUD/射线闸门、透视与 actor 池 | HOTSEAT-001 |
| CI-005 | Gate 4A 开发中 | P0 | schema v3、3D 导出/往返与 legacy 2D 源码 smoke | UI-010、CI-004 |
| QA-001 | 待办 | P0 | Mac 实机完整一局 | CI-003 |
| QA-002 | 待办 | P0 | Windows 两台机器完整一局 | CI-003 |
| REL-001 | 待办 | P0 | 标记 `v0.4-hotseat-alpha.1` | QA-001、QA-002 |

---

## 十一、提交策略

长期每个 Gate 应独立提交。Gate 0+1 曾按要求只创建本地提交；Gate 2、Gate 3A、Gate 3B 与 Gate 3C 均已获得明确推送授权，分别在对应 `codex/godot-hotseat-*` 分支检查 CI，但仍不创建 PR、不合并或打标签。Gate 4A 继续使用独立分支，并在真实 CI 完成前不填写结果。

已产生的 Gate 0～3C 主题提交包括：

```text
docs: align repository with v0.4 Godot route
test: cover end-turn PP and counter-pass response
fix(engine): harden v0.4 turn and response flow
feat(engine): add viewer-scoped match views
feat(engine): add legal action and payment queries
feat(native): expose versioned v0.4 C ABI
test(native): verify ABI parity and hidden information
feat(godot): deliver Gate 3A desktop snapshot shell
fix(ci): stabilize Gate 3A Godot exports
fix(godot): make cold imports deterministic

# Gate 3B/3C 已拆分主题
fix(engine): harden Gate 3B client previews
test(ci): enforce Gate 3B package smoke contracts
feat(client): add safe hot-seat match controller
feat(godot): add Gate 3B interaction presentation
feat(godot): connect the complete hot-seat match loop
test(godot): verify complete hot-seat flows and exports
docs: record Gate 3B implementation and measured validation
feat(godot): add Gate 3C direct interactions
docs: record Gate 3C validation
```

获得推送授权后：

1. 核对远端提交；
2. 核对 CI；
3. 不在红色 CI 上继续叠大型功能；
4. 主线始终保持可构建；
5. 只有 Gate 完成后才合并到 `main`。

---

## 十二、风险控制

### 1. C# 逐渐变成第二规则引擎

控制：

- 所有高亮来源于合法行动查询；
- 所有支付提示来源于支付预览；
- C# 只显示；
- 引擎拒绝后立即刷新快照。

### 2. 快照和事件不同步

控制：

- 快照是最终真值；
- 事件是表现建议；
- 每个命令后重新读取新快照；
- 每位 viewer 的事件先进入 pending，完成渲染后才 ACK 对应 sequence；
- 日志/动画可以延迟显示，但不能延迟或推演规则状态。

### 3. 原生动态库打包失败

控制：

- CI 从干净环境导出；
- 启动时输出实际搜索路径；
- 缺库时显示明确错误；
- 导出产物自动运行 smoke check。

### 4. 热座泄露隐藏信息

控制：

- viewer-scoped snapshot；
- 准备命令先进入 `Resolving`，清空 viewer 私有引用并生成独立的中立公开投影；
- 公共投影完整绘制至少两帧才提交，期间锁死输入、事件 ACK 和旧 revision 回调；
- `Resolving` 中双方手牌只保留数量，背面伏策统一匿名，销毁详情/日志/tooltip/metadata/候选；
- 卡牌详情面板关闭；
- 新操作者主动揭示前不调用其 view/query/events；
- 不在日志记录隐藏卡牌名称；
- 对方伏策只显示背面。

### 5. Godot 版本漂移

控制：

- 固定明确版本；
- README 写明版本；
- CI 使用相同版本；
- 升级 Godot 单独开工单。

### 6. 过早美术化

控制：

- 前四个 Vertical Slice 使用纯色占位；
- 完整规则可玩后再统一视觉；
- 第一版只要求清楚、快速和一致。

---

## 十三、本阶段明确不做

- 异地联机；
- 服务器；
- 账号；
- 排位；
- 录像；
- 观战；
- 断线重连；
- 卡组编辑器；
- 卡牌收藏；
- 抽卡；
- 商店；
- 平衡调整；
- 新职业；
- 大量新卡；
- Web 版本；
- 手机版本；
- 大型召唤动画；
- 清空全部旧 YGOPro2 遗留；
- 重做 legacy v1 wire；
- 正式协议 v2。

---

## 十四、执行顺序与当前状态

Gate 0+1 已完成 1～10，Gate 2 已完成 11～12，Gate 3A 已完成 13～15 和首帧版本的 22；Gate 3B 已接入 16～21 的源码闭环并完成同提交自动化/导出复验。第 23 项真人/物理设备硬门仍未完成。
以下保留 M1-G 的完整依赖顺序；状态以每行标记为准：

1. [完成] 基于 `main@cfdf695` 创建 `codex/godot-hotseat-gate1`；
2. [完成] 更新 README、architecture、roadmap 和 testing；
3. [完成] 移除一次性 M1 导入标记和工作流；
4. [完成] 为结束回合 PP 清零写回归测试；
5. [完成] 为响应反制层过牌写回归测试；
6. [完成] 修复两项规则边界；
7. [完成] 增加法术响应测试；
8. [完成] 建立观看者快照；
9. [完成] 建立合法行动、目标和支付预览查询；
10. [完成] 完成无界面“查询 → 命令 → 快照”完整对局测试；
11. [完成] 定义并实现 `scgs_v04` C ABI；
12. [完成] 运行 ABI 与直接 C++ 结果对照；
13. [完成] 创建 Godot 4 .NET 工程；
14. [完成] 加入 P/Invoke；
15. [完成] 显示第一张真实引擎快照；
16. [Gate 3B 历史完成] 调度 review、每次换手和命令结算的两阶段完全遮挡；Gate 3C 将命令结算替换为公共投影，只保留真实换手完全遮挡；
17. [完成] 接入普通单位、法术/策略、攻击、结束回合和投降；
18. [完成源码] 接入从双方调度到结果/重开的完整基础比赛；
19. [完成源码] 接入预支、燃耗、裂痕和引擎支付预览；
20. [完成源码] 接入进化、部署、位置和组件选择；
21. [完成源码] 接入策略区、响应 origin、伏策发动/不过和换手；
22. [Gate 3A 与 Gate 3B 自动复验完成] macOS ARM64 和 Windows x86-64 CI 导出/启动 smoke；
23. [待办] 在两类机器上完成真人整局测试；
24. [待办] 标记 `v0.4-hotseat-alpha.1`。

## 十五、Gate 3C：直接交互加固

Gate 3C 基于 `codex/godot-hotseat-gate3b@dd38e93`，不修改 C++ 规则、C ABI、schema 1、14 个导出、固定牌组或 legacy v1 wire。

### 固定操作语义

- 高频动作采用“点击来源 → 点击目的地”或等价拖拽；两条路径进入相同 intent 并得到逐字段相同的规范命令。
- 第一次点击来源不提交。唯一动作自动进入下一必要步骤，多动作才在来源旁显示上下文按钮。
- 目标、组件、具体格位和预支是选择步骤；最后一个必要选择完成后立即准备命令，不显示通用确认页。
- 无目标动作必须再按明确动作按钮；调度整批确认与投降二次确认保留，结束回合直接执行。
- Esc/右键空白逐步回退；无效拖放原位回弹，不调用 native。
- 右侧行动列表退出主流程，只保留可折叠详情与事件日志；复杂响应采用居中提示层。

### 新状态与隐私

- `HotseatSelectionStep`、`HotseatInteractionContext` 与选择历史由 `Scgs.Hotseat` 提供；Godot 不解析动作规则。
- `Resolving` 与 `Covered` 分离。前者只持有不含 viewer 私有引用的 `HotseatPublicBoardView`，后者只用于初始揭示和实际换手。
- 每次命令准备后必须先完整绘制公共投影至少两帧；期间禁止输入、ACK、重复提交与旧 revision 回调。
- 同一操作者继续时刷新原 viewer；操作者变化时先完全遮挡，新 viewer 主动揭示前不得调用 view/query/events。

### 自动验收

- 保留 Gate 3B 的 native/managed/整局/导出/ZIP 往返矩阵并新增选择步骤、逐步回退和公共投影隐私测试。
- Godot full-match 必须通过真实控件 signal 覆盖 `ActionKind` 0～10：第一局自然终局后真实触发结果页重开，第二局再以真实投降 signal 终局；不得直接注入最终 `LegalAction`。
- Gate 3C smoke 报告使用独立 schema version 2；唯一 marker 仍为 `SCGS_GODOT_CI_SMOKE_OK`。
- Windows/macOS artifact 改用 `SomeCardGameShit-gate3c-*`；最终真实 run、数量和摘要只写入 `TEST_REPORT.md`。

## 十六、Gate 4A：默认 3D/2.5D 占位战场

Gate 4A 基于 `codex/godot-hotseat-gate3c@a29dd14`。本轮不修改 C++ 规则、C ABI、schema 1、14 个导出、固定牌组或 legacy v1 wire，只替换 Godot 表现与空间输入边界。

### Presenter 与输入

- 无启动参数时创建固定透视 3D presenter；只有精确 `--legacy-2d-board` 启用旧 2D 回归，主菜单不暴露切换。
- 两个 presenter 都只产生 `HotseatSurfaceRef` / `HotseatSurfaceIntent`，并共享 `HotseatSurfaceInteractionCoordinator`；不能各自拼装命令。
- 3D 相机固定 70° FOV、约 58° 俯角，当前 viewer 在近端；透视只在完全遮挡内重建。
- HUD hit-test 优先于空间射线；拖拽阈值固定 8 px，无效落点和锁定状态不调用 native。
- actor 池回收必须清除文字、材质、tooltip、metadata、碰撞、回调、拖拽 token 和 DTO 引用。

### 隐私与自动验收

- `Covered` 完全不透明，`Resolving` 只含中立公开投影；显示环境至少两个 `FramePostDraw`，headless 使用两次 process-frame 栅栏后才提交。
- Gate 4A schema v3 完整继承 Gate 3C 的 22 个字段/整局约束，并新增 presentation、surface、raycast、HUD、镜头、透视、actor 池、锁定输入和空间泄露证据。
- Windows/macOS 各跑默认 3D 当前工程、默认 3D 导出、默认 3D ZIP 往返与一次 legacy 2D 源码整局；唯一 marker 保持 `SCGS_GODOT_CI_SMOKE_OK`。
- 导出 `BUILD_INFO.txt` 标识 Gate 4A，artifact 使用 `SomeCardGameShit-gate4a-*`。在真实 run 完成前不得在 `TEST_REPORT.md` 填写 Gate 4A 数量、摘要或全绿结论。
- 1600×900、1280×720 人工视觉遍历、两名真人热座、物理 Apple Silicon 与无 Visual Studio Windows 仍是发布标签前硬门。

**历史决策与持续约束：Gate 0+1 先完成文档纠偏、规则回归和客户端查询接口，再进入 UI；后续也必须保持 Godot 只是表现层，不让 C# 逐步长成第二套规则引擎。**
