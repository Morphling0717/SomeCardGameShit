# ⚠️ 历史归档：SomeCardGameShit 旧 DSH / YGOPro2 工程交接

> **已废弃，不是当前开发指令。** 本文记录 v0.1/M1 的 YGOPro2/Unity 探索，包含过时 commit、分支、规则版本、2 格战术区和一次性 importer 操作。不得照本文恢复 M1、修改远端、推送分支或判断现行能力。当前真值请读 [`DSH-HANDOFF-v0.4-ui.md`](DSH-HANDOFF-v0.4-ui.md)、[`GODOT-HOTSEAT-DEVELOPMENT-PLAN.md`](GODOT-HOTSEAT-DEVELOPMENT-PLAN.md) 与 [`rules-v0.4.md`](rules-v0.4.md)。

---

# SomeCardGameShit — DSH 工程交接文档（原文）

> 用途：把本文件直接交给 DSH / 后续 coding agent。接手者应以 **GitHub 当前内容 + `docs/rules-v0.1.md` + 自动测试** 为真值，不要根据历史聊天自行脑补规则或项目状态。

## 0. 一句话说明项目

`SomeCardGameShit` 是一款原创 1v1 数字卡牌游戏，技术路线是：**保留 YGOPro/YGOPro2 成熟的卡牌对局与客户端思路，以 YGOPro2 的简洁桌面式视觉/操作作为表现方向，在其上实现原创规则。**

目标不是制作游戏王 Mod，也不是把游戏王字段换名字，而是逐渐形成独立规则层、独立卡牌数据和原创素材。

项目开源，代码按 GPL-3.0-or-later 方向维护。

---

## 1. 仓库与当前分支

仓库：`Morphling0717/SomeCardGameShit`

### main

当前已核实的 `main` HEAD：

```text
2df9ba3be27f6e698090986f0074ba5f92264f81
ci: trigger independent M1 verification
```

**重要：根目录 `README.md` 仍然主要描述 M0，所以 README 不是当前 M1 状态的唯一真值。**

`main` 目前可以确认包含：

- 完整 M0 headless rules core；
- YGOPro2 protocol overlay 基础；
- M1 导入/验证相关 workflow 和 marker；
- `docs/m1-ci-verification.md`。

### feature/m1-playable-loop

该分支用于 M1 真人可玩闭环候选。

当前与 `main` **已经 diverged**。最近核实时：

```text
feature/m1-playable-loop ahead of main: 15 commits
feature/m1-playable-loop behind main: 3 commits
merge base: 4bcde21ea6ab2d9d0444bacc8d2f990ecd68540b
```

该分支仍可见：

- `.m1-source.part-*`
- `.m1-source.ready`
- `.github/workflows/bootstrap-m1-managed-loop.yml`
- `.github/workflows/fetch-ygo2-source.yml`

`.m1-source.ready` 当前记录：

```text
archive_sha256=441ec20f751c5906c36a86d471bf9c7d94163e521503ceb03f8d3fd4038e3aa0
parts=8
milestone=M1 local playable loop
```

### 非常重要的交接结论

**不要假设 M1 已经干净地发布到 main。**

之前生成过 M1 playable-loop 候选和自动验证流程，但当前远端结构表明 M1 仍处于“候选源码被分片/一次性 workflow 包装、等待整理为正常源码历史”的状态。

因此接手后的第一项工程任务不是 M2，而是：

> **恢复、核对、清理 M1 候选源码，把它变成普通、可审查、可直接 checkout/build 的 Git 历史，然后重新跑完整测试。**

不要继续依赖一次性 bootstrap workflow 作为长期开发方式。

---

## 2. 游戏设计真值

规则真值文件：

```text
docs/rules-v0.1.md
```

如果代码与该文档冲突，先判断是：

1. 代码未实现；
2. 测试占位；
3. 文档后来明确修改。

在没有用户新指示时，**不要擅自改规则来配合旧 YGOPro 结构。**

### 游戏定位

- 1v1 数字卡牌游戏；
- 正常 8–12 分钟；
- 通常第 8–10 回合结束；
- 以场面交换为主；
- 不采用游戏王式长时间连续展开；
- 四个核心系统：PP、进化、高级召唤、主战者；
- 最重要的原创重心之一是 **素材印记**。

### 核心基础规则

- 主战者：25 HP；
- 主牌组：30 张；
- 召唤牌组：6 张；
- 起手 4 张，可调度一次；
- 最大 PP 从 0 开始，每个自己回合 +1，最高 10，并补满当前 PP；
- 手牌上限 9；
- 单位区 5 格；
- 战术区 2 格；
- 没有固定战斗阶段；
- 行动阶段可自由穿插打牌、攻击、进化、高级召唤等动作；
- 单位伤害不会在回合结束后自动恢复；
- 牌组耗尽采用递增疲劳伤害。

### 进化

- 先手 2 点进化点，从自己的第 5 回合开始；
- 后手 3 点进化点，从自己的第 4 回合开始；
- 每回合最多主动进化 1 个单位；
- 战斗进化：永久 +2/+2，本回合获得突进；
- 能力进化：永久 +1/+1，结算卡牌专属能力进化效果，不自动获得突进。

### 高级召唤

高级召唤是本项目的重要规则层，每回合默认最多一次。

现有主要概念：

- 上位召唤；
- 构成召唤；
- 素材进入封存区；
- 高级召唤单位登场回合默认不能攻击主战者；
- 素材费用默认看原始费用；
- 高级召唤单位离场后通常进入封存区。

### 素材印记

高级召唤时，可以从素材的**印刷印记**中选择一个让召唤出的单位继承。

关键边界：

- 可以不继承；
- 印记在单位登场前生效，因此入场效果可以读取；
- 继承印记只在该单位留场期间有效；
- 改变控制者不会清除；
- 后天继承来的印记不能继续二次传递；
- 普通印记应该是短能力，不复制整段卡牌文字。

---

## 3. 第一轮内容范围

第一轮只做两副固定牌组：

```text
王庭
机巧
```

每副目标：

- 30 张主牌组；
- 15 种不同卡，每种 2 张；
- 6 张召唤牌组；
- 1 项主战技。

用途：

### 王庭

验证：

- 普通单位交换；
- 上位召唤；
- 两种进化；
- 中速节奏。

### 机巧

验证：

- 构成召唤；
- 素材印记；
- 零件生成；
- 召唤牌组单位离场处理。

首轮明确不做：

- 仪式召唤；
- 共鸣召唤；
- 中立卡；
- 随机卡包；
- 排位；
- 卡牌养成；
- 复杂任务系统。

不要因为代码里已有通用接口，就提前扩 scope。

---

## 4. 用户明确的产品方向

这是工程决策的重要背景：

- 用户愿意并希望项目保持开源；
- 用户喜欢 YGOPro2 的画面方向：简洁、实用、清楚、操作快；
- 不需要为了“现代感”把界面做成 MDPro3 / Master Duel 那种重演出风格；
- 可以保留 YGOPro2 的桌面式卡牌表现，再逐渐替换为原创 UI、美术、主战者、卡框、场地、特效；
- 不允许最终项目依赖游戏王卡图、Logo、音乐等来源不明或无权使用的素材。

---

## 5. 技术架构

设计原则：**规则真值与客户端表现分离。**

目标结构：

```text
玩家操作
  ↓
合法性检查 / 规则状态
  ↓
成本支付
  ↓
完整结算
  ↓
触发与死亡批次
  ↓
事件/协议消息
  ↓
YGOPro2 客户端表现
```

### engine/

M0 建立的 C++20 headless rules core。

用途：

- 规则真值；
- 回归测试；
- 不依赖 Unity 也能运行场景；
- 防止 UI 修改把规则 bug 隐藏掉。

不要轻易删除这一层。

### client/YGOPro2Overlay/

负责：

- YGOPro2 接入；
- 自定义协议；
- Unity/C# 状态展示；
- 后续 UI 与玩家输入。

### Lua / YGOPro Core 方向

长期原则：

- 所有牌都必须遵守的规则 → 核心；
- 单张卡的具体效果 → Lua/脚本；
- 视觉、动画、点击反馈 → YGOPro2。

不要把所有规则都塞进 Lua，也不要把每张卡的效果硬编码进 C++。

---

## 6. 锁定的上游版本

`upstream/upstream.lock.json` 当前锁定：

### YGOPro2

```text
repo: https://github.com/lllyasviel/YGOProUnity_V2.git
revision: b90f5bbdb0ae60df4060152b94e25a60783040b8
path: vendor/YGOProUnity_V2
role: pinned Unity client baseline
```

该版本项目使用 **Unity 5.6.7f1**。

### ygopro-core

```text
repo: https://github.com/Fluorohydride/ygopro-core.git
revision: 8046f3fb7fceae5aef13a889655f8211c9087174
path: vendor/ygopro-core
role: core and Lua API research baseline; ABI compatibility not assumed
```

注意：当前锁定的现代 `ygopro-core` **不能被默认视为与旧 YGOPro2 的 DLL ABI 完全兼容**。

---

## 7. M0 已完成内容

M0 的定义：**无界面规则纵向切片**。

已经实现/测试过的范围包括：

- 25 HP；
- 先后手；
- 4 张起手和调度；
- 0→10 PP；
- 9 张手牌；
- 溢出封存；
- 疲劳；
- 5 单位位 + 2 战术位；
- 持续单位伤害；
- 同时战斗伤害；
- 守护、突进、疾驰、屏障、必杀、吸血、潜伏等底层关键词；
- 战斗进化 / 能力进化；
- 上位召唤；
- 构成召唤；
- 素材印记与禁止二次继承；
- 召唤牌组单位离场封存；
- 遗物倒计时；
- 伏策基础窗口；
- 主战技通用接口；
- 投降 / 胜负 / 平局；
- 同时死亡批次；
- YGOPro2 私有协议消息 ID 210–219。

M0 的意义不是最终引擎已经完成，而是给后续 UI/规则改动提供可重复测试的真值层。

---

## 8. M1 的目标

M1 的产品目标非常明确：

> **第一次让普通人通过 YGOPro2/Unity 界面完整操作一局最小版对战。**

最低闭环：

1. 打开修改后的 YGOPro2；
2. 双方 25 HP；
3. 显示当前/最大 PP；
4. 双方获得固定测试牌组与手牌；
5. 玩家能点击手牌并选择空位打出单位；
6. 单位显示攻击与当前生命；
7. 新单位默认召唤疲劳；
8. 下一次自己的回合恢复为可攻击；
9. 点击单位后可以攻击敌方单位；
10. 可以攻击敌方主战者；
11. 单位互相同时造成伤害；
12. 单位伤害持续保留；
13. 可以结束回合；
14. 可以投降；
15. 生命归零显示胜负；
16. 本地两名玩家可以通过热座方式完成整局。

**M1 不要求进化、高级召唤、正式卡池、美术或网络匹配全部接入 UI。**

先证明“人能玩”，再扩功能。

---

## 9. 已生成的 M1 候选设计

`feature/m1-playable-loop` 的 bootstrap 内容表明，M1 候选采用了一个独立 C# managed model：

```text
ScgsM1Game.cs
ScgsM1PlayableLoop.cs
```

候选已经设计了：

- 两名玩家；
- 25 HP；
- 30 张固定白板牌组；
- 1–5 费测试单位；
- 4 张起手；
- PP；
- 5 个单位位；
- 出牌；
- 攻击单位；
- 攻击主战者；
- 同时战斗伤害；
- 死亡移除；
- 回合交接；
- 递增疲劳；
- 投降；
- 胜负；
- 本地 hotseat；
- 交接设备时用不透明遮罩隐藏下一名玩家的手牌。

Unity 侧使用简单 `OnGUI` 功能 UI 做第一条人类操作链路，而不是一开始就大改 YGOPro2 prefab/NGUI。

这是合理的临时验证方式，但**不能长期形成第二套规则真值**。

长期必须让：

```text
M0/C++ authoritative rules
        ↓
协议/桥接
        ↓
Unity/YGOPro2 UI
```

成为唯一正式路线。

---

## 10. 当前最大的技术债 / 风险

### A. M1 Git 状态需要先修

最优先。

目标：把 M1 候选还原成普通源码文件，建立干净 feature branch，删除 source chunk 与一次性 bootstrap workflow，再测试。

### B. M1 当前 managed model 与 M0 C++ 核心有重复规则

M1 为快速验证人类 UI，曾在 C# 中再次实现 PP、战斗、疲劳等逻辑。

这是可以接受的临时 spike，但不能继续扩展。

不要直接在 `ScgsM1Game.cs` 上继续实现进化、高级召唤、素材印记、伏策等完整规则。

M1 收口后下一步应开始把 UI 命令映射到 authoritative core。

### C. Unity 5.6.7 很旧

M1 阶段不要顺手升级 Unity。

升级 Unity 是独立项目，会同时引入：

- NGUI；
- Shader；
- 资源序列化；
- DLL；
- API；
- prefab/scene

等迁移问题。

先完成可玩闭环。

### D. 不要复用游戏王字段冒充原创字段

禁止长期使用如下捷径：

```text
Level = Cost
Defense = Health
PendulumScale = EvolutionPoint
Banished = 某个完全不同语义的原创系统（且代码里仍叫 banished）
```

允许底层暂时复用 location 数值以减少协议改动，但新 API / 新数据结构必须使用原创语义。

---

## 11. DSH 接手后的第一批任务

严格按顺序执行。

### Task 1 — 仓库状态审计

```bash
git clone https://github.com/Morphling0717/SomeCardGameShit.git
cd SomeCardGameShit
git fetch --all --prune
git log --oneline --decorate --graph --all -40

git diff main...feature/m1-playable-loop --stat
```

确认当前远端状态与本交接文档一致。

### Task 2 — 恢复 M1 候选源码

检查：

```text
feature/m1-playable-loop
.m1-source.part-*
.m1-source.ready
.github/workflows/bootstrap-m1-managed-loop.yml
```

目标不是重新发明 M1，而是从已有 archive/workflow 中恢复那套已生成候选。

校验 archive：

```text
SHA-256
441ec20f751c5906c36a86d471bf9c7d94163e521503ceb03f8d3fd4038e3aa0
```

恢复后把实际源码放进普通目录并提交。

### Task 3 — 建立干净 M1 分支

建议新建：

```text
feature/m1-playable-loop-clean
```

或在确认历史安全后整理现有分支。

要求：

- 不保留 `.m1-source.part-*`；
- 不把 bootstrap workflow 当源码存储；
- 实际 `.cs/.cpp/.py/.md` 文件正常出现在 tree 中；
- 每次 checkout 后不依赖一次性 workflow 才能得到源码。

### Task 4 — 重跑 M0 + M1 测试

至少：

```bash
cmake --preset dev
cmake --build --preset dev
ctest --preset dev

cmake --preset release
cmake --build --preset release
ctest --preset release

cmake --preset asan
cmake --build --preset asan
ctest --preset asan
```

还要运行 repository 中 M1 managed model / overlay / patcher 对应测试。

任何测试失败都先修，不要跳过。

### Task 5 — 应用到锁定的真实 YGOPro2

```bash
./scripts/bootstrap-upstream.sh
```

或按现有工具把 overlay 应用到：

```text
vendor/YGOProUnity_V2
```

确认：

- 上游 revision 正确；
- patch 可重复运行；
- 不重复注入启动代码；
- 不修改与 M1 无关的大量上游文件。

### Task 6 — Unity 实机验收

环境：

```text
Unity 5.6.7f1
```

必须由真实 Unity Editor / Windows Player 验证：

- 能进入 M1；
- UI 无异常；
- handoff 遮罩不会漏手牌；
- 可以出牌；
- 可以结束回合；
- 单位第二个己方回合能攻击；
- 可以攻击单位；
- 可以攻击主战者；
- 伤害和死亡正确；
- 能产生胜者；
- 能重新开始。

源码编译通过不能冒充这一项已经通过。

### Task 7 — M1 收口

M1 验收通过后：

- 更新 README，别再显示 M0 为“当前状态”；
- 更新 `docs/roadmap.md`；
- 删除已经无用的一次性 importer/marker；
- 将干净 M1 合入 main；
- GitHub CI 必须绿；
- 再开始下一阶段。

---

## 12. M1 之后的正确下一步

不要立刻做大量美术或四职业。

推荐顺序：

```text
M1A：真人最小闭环稳定
↓
M1B：C# 临时规则逐步接回 authoritative core
↓
M1C：把功能 UI 映射到 YGOPro2 原生 card/field interaction
↓
M2：进化 UI + 规则桥
↓
M3：高级召唤 + 素材选择 + 素材印记
↓
M4：遗物 / 伏策 / 对手回合窗口
↓
两副完整固定牌组
↓
真人平衡测试
```

高级召唤和素材印记是游戏的核心特色，不要在基础输入链路还不稳定时提前把复杂系统一起塞进 UI。

---

## 13. 第一轮平衡验收目标

基础原型完整后记录：

- 先手胜率；
- 平均结束回合；
- 平均时长；
- 每局高级召唤次数；
- 进化点使用量；
- 手牌耗尽回合；
- 无法处理场面的次数；
- 从未被使用的召唤牌组卡；
- 每回合平均操作时间。

目标：

```text
先手胜率 48%–52%
平均第 8–10 回合结束
每名玩家每局 2–3 次高级召唤
大部分进化点能正常用掉
单回合不会长时间展开
落后方仍有 1–2 个值得考虑的行动
高级召唤不能永远只有同一个正确答案
```

---

## 14. Git / 提交规则

用户要求：项目后续更新要持续同步 GitHub，不要只留在 agent 临时容器。

建议工作方式：

```text
1. 先读 main / 当前 feature
2. 修改
3. 本地测试
4. 查看 git diff
5. 提交清晰 commit
6. push GitHub
7. 检查远端 commit/CI
8. 再向用户报告
```

不要：

- 用二进制分片长期当源码仓库；
- 为了绕过工具限制把源码塞进巨大 workflow；
- 报告“已经推送”但不检查远端 tree；
- 报告“Unity 已验证”但实际上只编译了 C#；
- 在测试失败时继续堆功能。

---

## 15. 接手时最重要的判断原则

发生冲突时，优先级如下：

```text
用户最新明确要求
    > docs/rules-v0.1.md
    > 已通过的规则测试
    > 当前实现
    > YGOPro / YGOPro2 原规则
```

原因：YGOPro 是技术底座，不是游戏设计真值。

如果原创规则与 YGOPro 原生结构冲突，应修改/扩展底座，而不是偷偷把原创规则改回游戏王的规则。

---

## 16. 交接给 DSH 的直接执行指令

接手后不要只做分析报告，直接执行：

> 你现在负责 `Morphling0717/SomeCardGameShit`。先完整审计 `main` 与 `feature/m1-playable-loop`，确认本交接文档描述的 M1 分叉/源码分片问题。优先恢复现有 M1 playable-loop candidate，把它整理为正常可审查源码，重跑 M0/M1 全部测试，并在 Unity 5.6.7f1 / Windows 可用环境中完成真实人类操作闭环验收。不要擅自改变 `docs/rules-v0.1.md` 中的游戏规则，不要提前做 M2，不要升级 Unity，不要把 C# 临时 M1 model 扩展成第二套完整规则核心。每个稳定阶段都提交并推送 GitHub，检查远端 commit 与 CI 后再继续。

---

## 17. 目前最重要的成功标准

短期成功不是“代码更多”，而是：

> **从一个干净 clone 开始，不依赖临时聊天文件或一次性隐藏状态，可以构建项目；打开 YGOPro2 M1；两个人能够通过明确 UI 从 25 HP 开始，轮流打出单位、消耗 PP、攻击、结束回合，直到一方生命归零，并且整个过程由可测试的规则层约束。**

达到这一点以后，再进入进化、高级召唤和素材印记的真正游戏化阶段。
