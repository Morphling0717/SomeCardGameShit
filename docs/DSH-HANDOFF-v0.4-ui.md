# DSH 交接文档 — v0.4 引擎完成 → 下一步做 YGOPro2 图形客户端（B）

> 用途：交给下一任 coding agent。接手后先 `git clone` 本仓库并 `git log`，以 **GitHub 当前 main + docs/rules-v0.4.md + 自动测试** 为真值，不要根据聊天脑补。
>
> 仓库：`Morphling0717/SomeCardGameShit`（GPL-3.0-or-later）

---

## 0. 一句话当前状态

`main`（HEAD `c239105`）已经是 **v0.4 完整无界面规则引擎**，三平台 CI 全绿。
**还没有任何可玩 UI**。下一阶段任务是本文档第 6 节描述的 B：把 v0.4 引擎接进 YGOPro2/Unity 做图形客户端。

---

## 1. 规则真值

- **`docs/rules-v0.4.md`** 是唯一规则真值（已冻结入库，含"策略区每玩家 3 格"的最终决定）。
- 优先级：**用户最新要求 > docs/rules-v0.4.md > 已通过的规则测试 > 当前实现**。
- 不要改规则、不要提前做平衡、不要擅自升级 Unity。规则有歧义时问用户，不要替用户拍板。

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
- 协议 wire **legacy v1 冻结**（字段/字节/golden vectors 不变；桥接函数把 v0.4 状态饱和映射进旧 wire）。

**测试**：`engine/tests/test_main.cpp`（22 用例）、`test_wire.cpp`（wire 金标）、`scgs_demo --verify`（金标走查）。本地三种配置 + GitHub 三平台（GCC / Clang-sanitizer / MSVC）全绿。

**卡池**：`engine/src/catalog.cpp` + `engine/include/scgs/card.hpp`，两副测试牌组 `make_midrange_deck()`（中速基准）与 `make_advance_deck()`（预支），各有战备牌 + 组件 + 职业充能条件。卡牌中文名/说明在注释里。

**验收交付物**：
- `docs/v0.4-behavior-coverage.md` —— 13 项稳定 ID（V04-R01~R13）行为覆盖清单，用户已逐条过目确认；
- `docs/protocol-v1-v2-field-delta.md` —— 协议 v2 重设计输入（未来做网络对战时用）；
- `TEST_REPORT.md` —— v0.4 基线报告。

## 3. 环境事实（重要）

- **用户当前在 Apple Silicon Mac 上开发，但两个人都有 Windows 机器**。Unity 5.6.7f1（2017 年）跑不了 Apple Silicon，所以 Unity 实机验收只能在 Windows 上做。
- 本机已 `brew install cmake ninja`（构建用）。
- `scripts/bootstrap-upstream.sh` 用了 bash `mapfile`，在 macOS 自带 bash 3.2 下会报 `mapfile: command not found`（Windows/Linux 的 bash 没问题；在 mac 上跑前先 `brew install bash` 或用 `bash` 新版）。
- `upstream/upstream.lock.json` 锁定了 YGOProUnity_V2（`b90f5bb…`）与 ygopro-core（`8046f3f…`）。

## 4. 用户已确认的 B 路线决定

1. 做 YGOPro2/Unity 图形客户端（不是换现代技术栈）。
2. **第一版 = 单机热座**：两个人共用一台 Windows 电脑轮流操作，换人时用不透明遮罩隐藏手牌（原 M1 候选就是这个方案）。用户会先单人测试，再发给朋友各自单人测试。
3. **局域网对战留作后续阶段**（等热座跑通再做网络层；协议 v2 的字段增量清单已备好）。
4. 表现方向：保留 YGOPro2 的简洁桌面式风格，不要 MDPro3/Master Duel 式重演出；不依赖来源不明的游戏王素材。
5. 首个目标：**打开 Unity 5.6.7f1 就能玩**（规则尽量全：出牌/预支/燃耗/攻击/进化/部署/组件/伏策响应/结束回合/胜负）。

## 5. 现成的参考资产（关键捷径）

上一任（M1 时代）做过一套"原生桥 + C# 热座 + OnGUI"的候选，已分片存档在
`feature/m1-playable-loop` 分支的 `.m1-source.part-00`~`07` + `.m1-source.ready`
（SHA-256 `441ec20f751c5906c36a86d471bf9c7d94163e521503ceb03f8d3fd4038e3aa0`）。
重组方式：

```bash
cat .m1-source.part-* > parts.b64
base64 -d parts.b64 > parts.tar.xz    # macOS 用 base64 -D -i parts.b64 -o parts.tar.xz
tar -xJf parts.tar.xz
```

里面的骨架（**基于旧 v0.1 规则，只能当模式参考，不能直接用**）：
- `engine/include/scgs/native_api.h` + `engine/src/native_api.cpp` —— C ABI 动态库桥（`scgs_m1_create/get_player_state/play_unit/attack/…`），ABI 版本 + 一字节对齐 struct；
- `client/YGOPro2Overlay/Assets/SomeCardGame/M1/` —— `ScgsM1Models.cs`（模型）/ `ScgsM1Native.cs`（DllImport）/ `ScgsM1LocalMatch.cs`（热座对局）/ `ScgsM1Bootstrap.cs`（OnGUI + 遮屏换人）；
- `tools/csharp-compile/` —— Unity 5.6 的 API stub（`UnityEngineStubs.cs` + `ProgramStubs.cs` + `ScgsM1Compile.csproj`），用 dotnet 编译 C# 验证（Unity 5.6 = C# 6）；
- `tools/apply_m1_ygo2.py`（可能改名为 apply_v04_ygo2.py）—— 把 overlay 打进锁定版 YGOPro2，幂等、检查 210–219 冲突、注入启动入口。

**下一步的 B 就是把这套模式照着 v0.4 的 `Game` API 重写一遍**，而不是从零发明。

## 6. B 的推荐执行顺序（给下一任）

严格分阶段，每阶段提交 + push + CI 绿：

1. **B0 上游**：修好/跑通 `scripts/bootstrap-upstream.sh` 拉取 `vendor/YGOProUnity_V2`（锁 `b90f5bb`）。本地/CI 都要能 checkout。
2. **B1 原生桥**：在 `engine/` 新增 C ABI `scgs_v04`（照抄 M1 的 `native_api.h` 模式，改成 v0.4 字段：pp_capacity/cracks/evolution_energy/standby/deploy/响应栈等）。用 `engine/tests/test_native_api.cpp` 测，CMake 加 `scgs_native` 共享库 + MSVC 导出 `scgs_native.dll`。
3. **B2 C# 层**：`client/YGOPro2Overlay/Assets/SomeCardGame/V04/` 下写 `ScgsV04Models.cs / ScgsV04Native.cs / ScgsV04LocalMatch.cs / ScgsV04Bootstrap.cs`（OnGUI 功能 UI：显示双方生命/PP容量/裂痕/进化能量/手牌/单位/战备区/策略区；命令：出牌(含预支勾选)、攻击单位/主战者、进化、部署、设置伏策/设施、发动伏策/过、结束回合、投降；换人遮屏）。
4. **B3 编译验证**：`tools/csharp-compile/` 更新为 v0.4 的 C# 6 + Unity 5.6 stub，dotnet 编译；`tools/apply_v04_ygo2.py` 覆盖注入 + 幂等；CMake/CI 都加这些 gate。
5. **B4 CI**：三平台 CI 增加 `scgs_native.dll`（MSVC）构建与 C# 编译、patcher 测试。
6. **B5 Windows 实机验收**：用户在自己 Windows + Unity 5.6.7f1 打开 `vendor/YGOProUnity_V2`，玩一局热座。**只有用户实测通过才算 B 完成**——源码编译绿不能冒充。

## 7. 硬约束 / 红线

- 协议 wire legacy v1 冻结（`kProtocolVersion=1`、字段/字节/golden vectors 不动）；桥接只做饱和映射。**v0.4 新状态（裂痕/动用未来/充能/部署）走引擎自身，不进旧 wire**；网络对战时才升协议 v2（用 `docs/protocol-v1-v2-field-delta.md`）。
- 不把 C# 做成第二套规则真值：Unity 只提交命令、读状态/事件，规则判断全在 C++ `Game`。
- 不升级 Unity、不改 NGUI/渲染管线（第一版）。
- 不用游戏王卡图/Logo/音乐等无授权素材。
- 每个稳定阶段提交并 push GitHub，检查远端 commit + CI 再继续。

## 8. 关键文件索引

| 文件 | 作用 |
|---|---|
| `docs/rules-v0.4.md` | 规则真值（唯一） |
| `engine/include/scgs/{types,card,game,protocol}.hpp` | 引擎公开 API |
| `engine/src/{catalog,game,protocol}.cpp` | 引擎实现 |
| `engine/tests/{test_main,test_wire}.cpp` | 规则/wire 测试 |
| `docs/v0.4-behavior-coverage.md` | 行为覆盖清单（用户已确认） |
| `docs/protocol-v1-v2-field-delta.md` | 协议 v2 设计输入 |
| `upstream/upstream.lock.json` | 上游锁定版本 |
| `client/YGOPro2Overlay/` | C# 兼容层（现只解码 211/212） |
| `tools/apply_ygo2_overlay.py` | 现有 overlay 注入器（M0 时代） |

## 9. 给下一任的第一句话

> 你现在负责 `Morphling0717/SomeCardGameShit` 的 B 阶段：把已完成的 v0.4 C++ 规则引擎接进 YGOPro2/Unity，做出第一版单机热座可玩客户端。先照本文档第 5/6 节，从 M1 候选分片里取回"原生桥 + C# OnGUI 热座"骨架并改成 v0.4；本机能做的（C ABI 桥、C# 编译、patcher、三平台 CI）全部 CI 验证，Unity 实机验收留给用户在 Windows 上做。不要改规则、不要升级 Unity、不要做网络对战（那是下一阶段）。每个稳定阶段提交并推送 GitHub。
