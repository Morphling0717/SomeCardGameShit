# SomeCardGameShit 下一阶段开发计划

**计划代号：** M1-G / Godot Hotseat Alpha  
**代码基线：** `main@e55a49918715e839d3929387fdea18c9bb280c3b`  
**规则基线：** `docs/rules-v0.4.md`  
**目标客户端：** Godot 4 .NET  
**目标平台：** macOS Apple Silicon、Windows x86-64  
**本阶段目标：** 从完整无界面引擎推进到人类可以完整打一局的单机热座版本

---

## 一、项目当前状态

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

现有自动化基线包括：

- 22 个 C++ 测试用例；
- 391 次基础断言；
- 32 个种子、双方轮流先手的确定性烟雾对局；
- 每一步行动后的状态不变量检查；
- Debug、Release、ASan 和 UBSan；
- legacy v1 wire 金标字节测试；
- 预支、燃耗、当前 PP 超过容量和修复的金标场景。

### 2. 尚未完成的部分

当前没有：

- Godot 工程；
- Godot 场景；
- C++ 动态库接口；
- C# 绑定；
- 客户端安全视图；
- 完整合法行动查询接口；
- 可供人类操作的对局 UI；
- macOS 或 Windows 可玩构建；
- 真人完整对局验收。

准确状态是：

```text
规则引擎：基本完成
规则自动测试：已有良好基础
客户端接入接口：未完成
Godot 客户端：不存在
人类可玩版本：不存在
```

---

## 二、在做 UI 前必须先处理的问题

正式开发 Godot 客户端之前，必须插入一个“引擎客户端化加固”阶段。否则 C# 会被迫复制规则逻辑，最后形成第二套规则引擎。

### P0-1：结束回合没有明确清空未使用的当前 PP

规则文档要求结束阶段将未使用的当前 PP 清零。当前 `end_turn()` 调用的 `clear_end_of_turn_state()` 主要清除临时突进，没有明确将离开回合玩家的 `current_pp` 设为 0。

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

当前响应过程为：

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

客户端开发必须等这三条全部通过。

### P0-3：客户端没有足够的合法行动查询能力

当前公开 API 能执行动作，但纯查询能力不足。客户端不能自行判断：

- 哪些手牌可以使用；
- 哪张牌需要预支；
- 使用后会产生多少裂痕；
- 哪些单位可以进化；
- 哪些战备牌可以部署；
- 哪些单位可以作为部署代价；
- 哪些位置和目标合法；
- 当前由谁响应；
- 哪些响应选项合法。

引擎必须增加：

```text
list_legal_actions()
list_playable_cards()
list_valid_slots()
list_valid_targets()
list_evolvable_units()
list_deployable_cards()
list_valid_component_donors()
get_payment_preview()
get_reaction_context()
```

所有高亮、按钮和选择范围都必须来自引擎。

### P0-4：客户端需要按观看者过滤的状态快照

桥接层不能直接把完整 `PlayerState` 暴露给客户端。热座模式也应从数据层避免误泄露。

新增：

```text
get_match_snapshot(viewer)
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

当前存在 `SpellDeclared` 响应窗口，但卡牌触发枚举没有完整对应的 `OnSpellDeclared`。

本阶段增加：

```cpp
EffectTrigger::OnSpellDeclared
```

并制作一张测试伏策，验证法术响应和三层反制流程。

### P1-2：卡牌数据缺少表现字段

规则数据不应承担 UI 文本和素材定位。新增独立表现数据：

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
C# MatchController
        │  只提交命令、请求合法选项
        ▼
ScgsV04Native.cs
        │  P/Invoke
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
8. legacy v1 wire 保留并继续测试，但不作为 Godot 同进程客户端接口。

---

## 五、详细执行阶段

## Gate 0：基线整理与文档纠偏

### 工作内容

建立开发分支：

```text
feature/godot-hotseat
```

更新：

- `README.md`
- `docs/architecture.md`
- `docs/roadmap.md`
- `docs/testing.md`
- `TEST_REPORT.md`

新增：

```text
docs/godot-client-architecture.md
docs/native-api-v0.4.md
docs/ui-state-map.md
docs/hotseat-acceptance.md
```

清理明确的一次性遗留：

- `.m1-feature.ready`
- `.github/workflows/import-m1-feature.yml`

暂时不批量删除：

- `client/YGOPro2Overlay/`
- `upstream/`
- `tools/apply_ygo2_overlay.py`

这些文件标记为 legacy，不再进入现行构建。

### 工具版本

锁定一个确定的 Godot 4 .NET 稳定版和对应 .NET SDK；版本升级必须作为独立任务，不与功能开发混在一起。

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

### ENG-001：结束回合 PP 清零

增加回归测试并修复实现。

### ENG-002：响应反制过牌

增加响应栈回归测试，修复反制层选择不过时丢失底层行动的问题。

### ENG-003：法术响应触发

加入 `OnSpellDeclared` 和一张测试伏策。

### ENG-004：观看者快照

增加：

```cpp
PublicCardView
HiddenCardView
PlayerView
MatchView
```

接口：

```cpp
MatchView make_view(PlayerId viewer) const;
```

### ENG-005：合法行动枚举

建议引入：

```cpp
enum class ActionKind {
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

以及：

```cpp
std::vector<LegalAction> list_legal_actions(PlayerId viewer) const;
std::vector<Target> list_valid_targets(const ActionRequest&) const;
std::vector<std::size_t> list_valid_slots(const ActionRequest&) const;
std::vector<InstanceId> list_valid_donors(const ActionRequest&) const;
PaymentPreview preview_payment(PlayerId, InstanceId, bool use_advance) const;
```

### ENG-006：响应上下文

客户端需要获得：

```text
当前响应玩家
响应窗口类型
当前层数
可发动伏策
原行动摘要
是否允许过牌
```

### ENG-007：引擎随机先后手

增加：

```cpp
FirstPlayerMode::Random
FirstPlayerMode::Player0
FirstPlayerMode::Player1
```

随机结果由引擎产生并写入事件。

### 测试要求

增加：

- `test_end_turn_clears_current_pp`
- `test_counter_layer_pass_resolves_lower_layers`
- `test_counter_trap_resolves_lifo`
- `test_spell_declared_reaction`
- `test_view_hides_enemy_hand`
- `test_view_hides_facedown_trap_definition`
- `test_legal_action_query_matches_commands`
- `test_payment_preview_matches_real_payment`
- `test_reaction_context_identifies_responder`

### 退出标准

无界面测试客户端可以只使用：

```text
查看快照
→ 查询合法行动
→ 提交行动
→ 读取事件
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
```

### ABI 原则

- 使用 opaque handle；
- 所有整数使用明确宽度；
- 所有结构包含 `struct_size`；
- 提供 ABI 版本；
- 字符串统一 UTF-8；
- 不返回 C++ `std::vector` 或引用；
- 不让异常跨过 ABI；
- 数组采用“先问数量，再填缓冲区”；
- 错误码与错误文本分离；
- 动态库自行拥有内部内存；
- C# 只复制自己需要的数据。

### 建议接口

```c
uint32_t scgs_v04_abi_version(void);

scgs_handle* scgs_v04_create(
    const scgs_match_config* config
);

void scgs_v04_destroy(scgs_handle* handle);

scgs_status scgs_v04_start(scgs_handle* handle);

scgs_status scgs_v04_get_view(
    scgs_handle* handle,
    uint8_t viewer,
    scgs_match_view* out_view
);

size_t scgs_v04_list_actions(
    scgs_handle* handle,
    uint8_t viewer,
    scgs_legal_action* buffer,
    size_t capacity
);

size_t scgs_v04_list_targets(
    scgs_handle* handle,
    const scgs_action_query* query,
    scgs_target* buffer,
    size_t capacity
);

scgs_status scgs_v04_submit_command(
    scgs_handle* handle,
    const scgs_command* command
);

size_t scgs_v04_drain_events(
    scgs_handle* handle,
    scgs_event* buffer,
    size_t capacity
);

size_t scgs_v04_get_last_error(
    scgs_handle* handle,
    char* buffer,
    size_t capacity
);
```

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

### 退出标准

C ABI 与直接 C++ 调用在每一步都得到相同结果。

---

## Gate 3：Godot 4 .NET 工程骨架

### 目录

```text
client/godot/
├─ project.godot
├─ SomeCardGameShit.csproj
├─ export_presets.cfg
├─ assets/
├─ data/
├─ native/
├─ scenes/
│  ├─ bootstrap/
│  ├─ match/
│  ├─ cards/
│  ├─ dialogs/
│  └─ overlays/
└─ scripts/
   ├─ Native/
   ├─ Match/
   ├─ UI/
   └─ Presentation/
```

### C# 结构

```text
NativeLibraryResolver.cs
ScgsV04Native.cs
ScgsV04Models.cs
MatchController.cs
SnapshotMapper.cs
EventPresenter.cs
SelectionController.cs
HotseatPrivacyController.cs
CardPresentationDatabase.cs
```

### 原生库目录

```text
client/godot/native/macos-arm64/libscgs_v04.dylib
client/godot/native/windows-x86_64/scgs_v04.dll
```

### Godot 场景

```text
Bootstrap.tscn
MainMenu.tscn
Match.tscn
PassDeviceOverlay.tscn
MulliganDialog.tscn
CardDetailPanel.tscn
ActionMenu.tscn
TargetSelectionOverlay.tscn
ReactionDialog.tscn
ResultOverlay.tscn
```

### 退出标准

- Godot 编辑器打开无错误；
- C# 编译通过；
- 能载入对应平台动态库；
- 可以建立一局 C++ 比赛；
- 可以在界面上显示第一张真实引擎快照。

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
- 结构尺寸错误；
- 缓冲区容量不足；
- UTF-8 文本；
- 数组两段式读取；
- 错误码；
- 动态库创建与销毁；
- 完整比赛；
- 隐藏信息；
- 事件顺序；
- 直接 C++ 与 C ABI 结果一致。

### 3. C# 绑定测试

通过 `dotnet test` 检查：

- P/Invoke 结构尺寸；
- enum 数值；
- UTF-8 转换；
- 动态库搜索路径；
- 快照映射；
- 对方隐藏手牌；
- 事件映射；
- 原生错误文本。

### 4. Godot headless 测试

至少包括：

- 项目可导入；
- C# 构建通过；
- Bootstrap 场景可实例化；
- Match 场景节点路径完整；
- 原生库能够加载；
- 能创建比赛并取得首张快照；
- 导出预设有效。

### 5. 实机测试

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

---

## 九、CI 计划

### Linux GCC

```text
构建规则引擎
构建 libscgs_v04.so
运行规则测试
运行 native API 测试
运行 wire freeze 测试
```

### Linux Clang Sanitizer

```text
ASan
UBSan
规则测试
native API 测试
确定性烟雾对局
```

### macOS

```text
构建 arm64 dylib
运行 native API 测试
dotnet build
Godot headless import
导出 macOS 测试包
上传构建产物
```

### Windows MSVC

```text
构建 scgs_v04.dll
运行 native API 测试
dotnet build
Godot headless import
导出 Windows x86-64
运行基础启动检查
上传 ZIP
```

本阶段不设 Web 构建任务。

---

## 十、开发工单与依赖关系

| ID | 优先级 | 工单 | 依赖 |
|---|---:|---|---|
| DOC-001 | P0 | 更新 README 为 v0.4 + Godot 路线 | 无 |
| DOC-002 | P0 | 重写 architecture、roadmap、testing | DOC-001 |
| CLEAN-001 | P1 | 移除一次性 M1 marker/import workflow | 无 |
| ENG-001 | P0 | 结束回合清空当前 PP | 无 |
| ENG-002 | P0 | 修复反制层选择过牌 | 无 |
| ENG-003 | P1 | 增加法术响应触发 | ENG-002 |
| ENG-004 | P0 | 增加观看者快照 | ENG-001 |
| ENG-005 | P0 | 增加合法行动查询 | ENG-001 |
| ENG-006 | P0 | 增加目标、位置、代价查询 | ENG-005 |
| ENG-007 | P0 | 增加支付预览 | ENG-005 |
| ENG-008 | P0 | 暴露完整响应上下文 | ENG-002 |
| ENG-009 | P1 | 引擎随机先后手 | ENG-004 |
| DATA-001 | P1 | 建立卡牌表现数据 | DOC-002 |
| ABI-001 | P0 | 定义 `native_api_v04.h` | ENG-004～008 |
| ABI-002 | P0 | 实现动态库 | ABI-001 |
| ABI-003 | P0 | C ABI 对照测试 | ABI-002 |
| GODOT-001 | P0 | 创建 Godot 4 .NET 工程 | DOC-001 |
| GODOT-002 | P0 | C# P/Invoke 与库解析 | ABI-002 |
| UI-001 | P0 | 主战场静态布局 | GODOT-001 |
| UI-002 | P0 | 热座隐私遮挡 | GODOT-001、ENG-004 |
| UI-003 | P0 | 基础出牌/攻击/结束回合 | GODOT-002、ENG-005 |
| UI-004 | P0 | 调度流程 | UI-002、ABI-003 |
| UI-005 | P0 | 预支/燃耗支付预览 | ENG-007、UI-003 |
| UI-006 | P0 | 进化与职业充能 | UI-003 |
| UI-007 | P0 | 战备部署与组件 | ENG-006、UI-003 |
| UI-008 | P0 | 设施、伏策与响应窗口 | ENG-008、UI-003 |
| UI-009 | P1 | 事件动画和对局日志 | UI-003～008 |
| CI-001 | P0 | 动态库三平台构建 | ABI-003 |
| CI-002 | P0 | dotnet 与 Godot headless | GODOT-002 |
| CI-003 | P0 | macOS/Windows 导出产物 | CI-002 |
| QA-001 | P0 | Mac 实机完整一局 | CI-003 |
| QA-002 | P0 | Windows 两台机器完整一局 | CI-003 |
| REL-001 | P0 | 标记 `v0.4-hotseat-alpha.1` | QA-001、QA-002 |

---

## 十一、提交策略

每个 Gate 单独提交并推送：

```text
docs: align repository with v0.4 Godot route
test: cover end-turn PP and counter-pass response
fix(engine): harden v0.4 turn and response flow
feat(engine): add viewer-scoped match views
feat(engine): add legal action and payment queries
feat(native): expose versioned v0.4 C ABI
test(native): verify ABI parity and hidden information
feat(godot): bootstrap Godot 4 .NET client
feat(godot): implement basic hotseat match loop
feat(godot): expose v0.4 resource and deployment systems
feat(godot): implement tactic response flow
ci: build and export Godot desktop clients
```

每次推送后：

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
- 所有确认窗口来源于支付预览；
- C# 只显示；
- 引擎拒绝后立即刷新快照。

### 2. 快照和事件不同步

控制：

- 快照是最终真值；
- 事件是表现建议；
- 每个命令后先保存新快照；
- 动画队列可以延迟显示，但不能延迟规则状态。

### 3. 原生动态库打包失败

控制：

- CI 从干净环境导出；
- 启动时输出实际搜索路径；
- 缺库时显示明确错误；
- 导出产物自动运行 smoke check。

### 4. 热座泄露隐藏信息

控制：

- viewer-scoped snapshot；
- 遮挡期间销毁当前手牌 UI；
- 卡牌详情面板关闭；
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

## 十四、正式开工顺序

1. 创建 `feature/godot-hotseat`；
2. 更新 README、architecture、roadmap 和 testing；
3. 移除一次性 M1 导入标记和工作流；
4. 为结束回合 PP 清零写回归测试；
5. 为响应反制层过牌写回归测试；
6. 修复两项规则边界；
7. 增加法术响应测试；
8. 建立观看者快照；
9. 建立合法行动、目标和支付预览查询；
10. 完成无界面“查询 → 命令 → 快照”完整对局测试；
11. 定义并实现 `scgs_v04` C ABI；
12. 运行 ABI 与直接 C++ 结果对照；
13. 创建 Godot 4 .NET 空工程；
14. 加入 P/Invoke；
15. 显示第一张真实引擎快照；
16. 完成调度和热座遮挡；
17. 完成普通单位、攻击和结束回合；
18. 完成完整基础比赛；
19. 接入预支、燃耗和裂痕；
20. 接入进化、部署和组件；
21. 接入策略区与三层响应；
22. 完成 macOS 和 Windows 导出；
23. 在两类机器上完成真人整局测试；
24. 标记 `v0.4-hotseat-alpha.1`。

**第一份代码改动不应该是画 UI，而应该是文档纠偏、两个回归测试，以及客户端查询接口。只有这层稳定，Godot 才会真正只是表现层，而不会逐步长成第二套规则引擎。**
