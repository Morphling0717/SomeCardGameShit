# DSH 交接文档 — v0.4 引擎完成 → 下一步做 Godot 4 图形客户端（B）

> 用途：交给下一任 coding agent。接手后先 `git clone` 本仓库并 `git log`，以 **GitHub 当前 main + docs/rules-v0.4.md + 自动测试** 为真值，不要根据聊天脑补。
>
> 仓库：`Morphling0717/SomeCardGameShit`（GPL-3.0-or-later）
>
> **详细执行计划：** [`docs/GODOT-HOTSEAT-DEVELOPMENT-PLAN.md`](GODOT-HOTSEAT-DEVELOPMENT-PLAN.md)。该文件包含源码审查发现的问题、Gate、C ABI、Godot UI、CI、工单依赖与实机验收标准；实际开发以它作为本文第 6 节的展开版。

---

## 0. 一句话当前状态

`main`（HEAD 含本文档）已经是 **v0.4 完整无界面规则引擎**，三平台 CI 全绿。
**还没有任何可玩 UI**。下一阶段任务是本文档第 6 节描述的 B：把 v0.4 引擎接进 **Godot 4** 做图形客户端（技术选型已由用户拍板，见第 4 节）。

---

## 1. 规则真值

- **`docs/rules-v0.4.md`** 是唯一规则真值（已冻结入库，含"策略区每玩家 3 格"的最终决定）。
- 优先级：**用户最新要求 > docs/rules-v0.4.md > 已通过的规则测试 > 当前实现**。
- 不要改规则、不要提前做平衡。规则有歧义时问用户，不要替用户拍板。

## 2. 已完成的 v0.4 引擎（不要重做）

引擎在 `engine/`，C++20，`Game` 类是唯一状态变更入口。已实现：

- PP 容量无上限、预支/燃耗/裂痕/修复/增长、当前 PP 可高于容量、超前/按期；
- 进化（能量上限 4、主动 2 点、每回合 1 次、先 5 后 4 解锁、进化状态数值、进化时触发、职业充能条件）；
- 战备区（0~6 公开）统一部署（条件/费用/代价数据化、每回合 1 次、不能预支、战备牌离场进封存）；
- 组件能力（部署代价授予、至多 1 个、禁止二次传递、离场清除）；
- 三层响应栈（原行动→响应→反制，LIFO；攻击/法术/登场效果开窗；支付类不开窗）；
- 策略区每玩家 3 格，满格不自动替换；
- 手牌上限 9/溢出封存、递增疲劳、同时战斗伤害、同时死亡批次、守护等布尔关键词；
- 数据驱动效果系统：`EffectRecord{trigger, kind, amount, target_spec}`，无卡级 C++ 钩子；
- 协议 wire **legacy v1 冻结**（字段/字节/golden vectors 不动；桥接函数把 v0.4 状态饱和映射进旧 wire）。

**测试**：`engine/tests/test_main.cpp`（22 用例）、`test_wire.cpp`（wire 金标）、`scgs_demo --verify`（金标走查）。本地三种配置 + GitHub 三平台（GCC / Clang-sanitizer / MSVC）全绿。

**卡池**：`engine/src/catalog.cpp` + `engine/include/scgs/card.hpp`，两副测试牌组 `make_midrange_deck()`（中速基准）与 `make_advance_deck()`（预支），各有战备牌 + 组件 + 职业充能条件。卡牌中文名/说明在注释里。

**验收交付物**：
- `docs/v0.4-behavior-coverage.md` —— 13 项稳定 ID（V04-R01~R13）行为覆盖清单，用户已逐条过目确认；
- `docs/protocol-v1-v2-field-delta.md` —— 协议 v2 设计输入（做联机时用）；
- `TEST_REPORT.md` —— v0.4 基线报告。

## 3. 环境事实（重要）

- **用户当前在 Apple Silicon Mac 上开发，和测试伙伴异地，两人都有 Windows 机器**。
- 已选定的引擎 **Godot 4.x（.NET 版）**：编辑器原生支持 macOS ARM（本机即可开发），可导出 Windows exe、网页版。
- 本机已 `brew install cmake ninja`（构建用）。
- 仓库里 YGOPro2 相关资产（`upstream/`、`client/YGOPro2Overlay/`、`tools/apply_ygo2_overlay.py`）是**已放弃路线**的遗留：不再作为路线使用，也不要继续投入；是否清理由后续单独决定，本轮先不动它们（避免大范围误删）。
- `scripts/bootstrap-upstream.sh` 用了 bash `mapfile`，在 macOS 自带 bash 3.2 下会报错——但既然路线改为 Godot，**不再需要它**。

## 4. 用户已拍板的技术决策（不要推翻）

1. **放弃 YGOPro2/Unity 5.6.7 路线**（原因：老引擎跑不了用户的 Mac、语义与 v0.4 规则严重错位、联机要逆向旧代码、美术/素材有授权债）。
2. **改用 Godot 4**：本机可开发、可导出 Windows、可导出网页。
3. **视觉风格复刻 YGOPro2 的简洁桌面式**（清爽、信息清楚、操作快），不做 Master Duel 式重演出；素材全部原创或明确授权（临时占位可用纯色卡框 + 文字）。
4. **第一版形态：单机热座**（两人共用一台电脑、遮屏换人），用户会先单人测试、再发给朋友各自单人测试；**异地联机是下一阶段**（协议 v2 设计输入已备好，见 `docs/protocol-v1-v2-field-delta.md`）。
5. **规则判断 100% 留在 C++ 引擎**：客户端只提交命令、读状态/事件，绝不把 C#/GDScript 做成第二套规则真值。
6. 首个目标：**打开即玩**（规则尽量全：出牌/预支/燃耗/攻击/进化/部署/组件/伏策响应/结束回合/胜负）。

## 5. 现成的参考资产

### 5.1 引擎 API（直接读，就是真值）
- `engine/include/scgs/game.hpp` —— `Game` 公开方法（play_unit/cast_spell/deploy/attack/evolve/activate_trap/pass_reaction/end_turn/surrender/mulligan/load_scenario + 查询 getter + `drain_events()` + `validate_invariants()`）；
- `engine/include/scgs/{types,card}.hpp` —— 状态与卡牌数据结构；
- `engine/tests/test_main.cpp` —— **每个 API 的正确用法示例都在测试里**，写桥接前先通读它。

### 5.2 M1 时代的"原生桥 + C# 热座"候选（当模式参考，勿直接用）
分片存档在 `feature/m1-playable-loop` 分支的 `.m1-source.part-00`~`07` + `.m1-source.ready`
（SHA-256 `441ec20f751c5906c36a86d471bf9c7d94163e521503ceb03f8d3fd4038e3aa0`）。重组：

```bash
cat .m1-source.part-* > parts.b64
base64 -d parts.b64 > parts.tar.xz    # macOS: base64 -D -i parts.b64 -o parts.tar.xz
tar -xJf parts.tar.xz
```

里面的骨架（**基于旧 v0.1 规则，只抄结构**）：
- `engine/include/scgs/native_api.h` + `engine/src/native_api.cpp` —— C ABI 动态库桥（一字节对齐 struct + ABI 版本 + create/destroy/get_*/play/attack/end_turn/validate），新桥照此模式改 v0.4 字段；
- `client/YGOPro2Overlay/Assets/SomeCardGame/M1/ScgsM1Native.cs` —— DllImport 模式（Godot .NET C# 可复用同样 P/Invoke）；
- `tools/csharp-compile/` —— 无 Unity 也能编译 C# 的 stub 方案，可改造为 Godot API stub 做 CI 编译验证。

## 6. B 的推荐执行顺序（给下一任）

严格分阶段，每阶段提交 + push + CI 绿：

1. **B0 工程骨架**：仓库加 `client/godot/`（Godot 4 .NET 项目：主场景 + 卡牌/区域基础布局 + 热座遮屏场景切换），CI 加 Godot headless 导出（下载 Godot CLI 即可在 Actions 里跑 `--headless --export`）——**至少保证 CI 里项目能导出**。
2. **B1 原生桥**：在 `engine/` 新增 C ABI `scgs_v04`（照 5.2 的 M1 模式，字段换成 v0.4：pp_capacity/cracks/evolution_energy/standby/deploy/响应栈/伏策窗口等）。新增 `engine/tests/test_native_api.cpp` 用引擎真值逐动作验证桥；CMake 加 `scgs_native` 共享库（MSVC 导出 dll，macOS 导出 dylib）。**桥就是客户端唯一入口。**
3. **B2 Godot C# 客户端**：C# 脚本 `ScgsV04Native.cs`（P/Invoke）、`ScgsV04Match.cs`（本地热座对局包装）、`ScgsV04UI.cs`（Godot 控件布局：双方生命/PP容量/裂痕/进化能量/手牌/单位/战备区/策略区 + 按钮：出牌(含预支勾选)、攻击、进化、部署、设置、发动伏策/过、结束回合、投降；换人时全屏遮罩）。视觉按第 4.3 条做简洁桌面风。
4. **B3 编译验证**：CI 用 dotnet 编译 C#（Godot 4 .NET 支持 SDK 式项目），加 Godot API stub（照 5.2 的 csharp-compile 思路）保证无编辑器也能编译；本地 Godot 编辑器打开无红字。
5. **B4 三平台 CI**：linux-gcc/clang-sanitizer/windows-msvc 三 job 增加 `scgs_native` 构建 + C# 编译 + Godot headless 导出 + 全部测试。
6. **B5 Windows/Mac 实机验收**：用户在自己机器上打开 Godot 项目玩一局热座。**只有用户实测通过才算 B 完成**——编译绿不能冒充。
7. **B6（下一阶段，本轮不做）**：异地联机。届时把 `docs/protocol-v1-v2-field-delta.md` 落地为真正的联机协议，引擎做权威端（本机进程或独立服务器），双客户端连。

## 7. 硬约束 / 红线

- 协议 wire legacy v1 冻结（`kProtocolVersion=1`、字段/字节/golden vectors 不动）；v0.4 新状态（裂痕/动用未来/充能/部署）走引擎自身，不进旧 wire；联机时再升协议 v2。
- 不把 C#/GDScript 做成第二套规则真值：客户端只提交命令、读状态/事件。
- 不用游戏王卡图/Logo/音乐等无授权素材（临时占位用原创纯色/文字）。
- 每个稳定阶段提交并 push GitHub，检查远端 commit + CI 再继续。
- 本轮不做：联机网络、录像、账号、卡牌平衡调整。

## 8. 关键文件索引

| 文件 | 作用 |
|---|---|
| `docs/rules-v0.4.md` | 规则真值（唯一） |
| `engine/include/scgs/{types,card,game,protocol}.hpp` | 引擎公开 API |
| `engine/src/{catalog,game,protocol}.cpp` | 引擎实现 |
| `engine/tests/{test_main,test_wire}.cpp` | 规则/wire 测试（API 用法示例） |
| `docs/v0.4-behavior-coverage.md` | 行为覆盖清单（用户已确认） |
| `docs/protocol-v1-v2-field-delta.md` | 联机协议 v2 设计输入 |
| `client/YGOPro2Overlay/`、`upstream/`、`tools/apply_ygo2_overlay.py` | 已放弃路线的遗留，暂不动 |
| `feature/m1-playable-loop` 分支 | M1 候选分片（桥/C# 模式参考） |

## 9. 给下一任的第一句话

> 你现在负责 `Morphling0717/SomeCardGameShit` 的 B 阶段：把已完成的 v0.4 C++ 规则引擎接进 Godot 4，做出第一版单机热座可玩客户端（视觉复刻 YGOPro2 简洁桌面风）。先通读 `engine/tests/test_main.cpp` 和 `game.hpp` 掌握引擎 API，再从 `feature/m1-playable-loop` 分片取回"原生桥 + C# P/Invoke"骨架改成 v0.4；本机能做的（C ABI 桥、C# 编译、Godot headless 导出、三平台 CI）全部 CI 验证，实机验收由用户在自己机器上做。不要改规则、不要做联机（下一阶段）、不要继续投入 YGOPro2 路线。每个稳定阶段提交并推送 GitHub。
