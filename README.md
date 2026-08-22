# SomeCardGameShit

> **现行开发路线（v0.4）：** C++20 规则引擎作为唯一规则真值，下一阶段使用 **Godot 4 .NET** 制作单机热座客户端。YGOPro2 / Unity 路线已经停止继续投入。
>
> - 详细开发计划：[`docs/GODOT-HOTSEAT-DEVELOPMENT-PLAN.md`](docs/GODOT-HOTSEAT-DEVELOPMENT-PLAN.md)
> - 最新交接手册：[`docs/DSH-HANDOFF-v0.4-ui.md`](docs/DSH-HANDOFF-v0.4-ui.md)
> - 唯一规则真值：[`docs/rules-v0.4.md`](docs/rules-v0.4.md)

一个原创 1v1 数字卡牌游戏项目。当前仓库已经完成 v0.4 无界面规则引擎，尚未完成可供人类操作的 Godot 图形客户端。

> 下面的 M0 / YGOPro2 内容保留为历史背景，后续 Gate 0 会按新路线整体重写。

## 当前已经能运行什么

当前 C++20 无界面核心已经实现并测试：

- 25 点主战者生命、先后手、4 张起手与一次调度；
- v0.4 无上限 PP 容量、当前 PP、预支、燃耗、裂痕、修复与增长；
- 9 张手牌上限、溢出封存、递增疲劳伤害；
- 5 个单位位与 3 个策略位；
- 持续保留的单位伤害、单位互相同时造成战斗伤害；
- 守护、突进、疾驰、屏障、必杀、吸血等底层规则；
- 单一进化形式与职业进化充能；
- 公开战备区、部署和组件能力；
- 设施、伏策与三层响应结构；
- 投降、胜负与平局状态；
- 当前回合方优先的同时死亡批次；
- 数据驱动卡牌效果；
- legacy v1 wire 冻结回归测试。

内置测试卡池包括两副 30 张固定牌组：标准中速牌组与预支测试牌组，均含战备牌和职业充能条件。

## 测试结果

当前测试套件包含：

- 22 个 C++ 功能与场景测试；
- 391 次基础断言；
- 32 个不同洗牌种子、双方轮流先手的完整对局烟雾测试；
- 每一步操作后的全局状态不变量检查；
- GCC Debug / Release；
- Clang AddressSanitizer + UndefinedBehaviorSanitizer；
- 预支 → 补满 → 燃耗 → 当前 PP 高于容量 → 修复的 v0.4 金标场景；
- legacy v1 wire 金标字节回归。

精确结果会由 `scripts/test.sh` 重新生成，不要只相信 README 中的数字。

## 本地构建

需要：

- CMake 3.20+
- 支持 C++20 的 GCC、Clang 或 MSVC
- Ninja（使用预设时）

```bash
cmake --preset dev
cmake --build --preset dev
ctest --preset dev
./build/dev/scgs_demo --verify
```

运行 Clang ASan + UBSan：

```bash
cmake --preset asan
cmake --build --preset asan
ctest --preset asan
```

或直接运行完整构建矩阵：

```bash
./scripts/test.sh
```

运行确定性压力测试：

```bash
./scripts/stress.sh
```

实际执行记录见 [`TEST_REPORT.md`](TEST_REPORT.md)。

## 目录

```text
engine/          可独立运行和测试的 v0.4 规则核心
client/          客户端代码；现有 YGOPro2 内容为已放弃路线遗留
docs/            规则、架构、交接、协议与开发计划
scripts/         构建和测试脚本
.github/         CI 配置
```

## 为什么先做无界面核心

直接在图形场景和动画中调规则，会让“规则错误”和“显示错误”混在一起。当前核心先把每一个动作变成可重复的状态转移和事件流：

```text
玩家命令 → 合法性检查 → 支付成本 → 完整结算 → 响应栈 → 死亡检查 → 事件输出
```

Godot 客户端只负责展示引擎提供的合法选项、提交命令，再把事件表现出来。这样修改 PP、进化、部署、组件或响应规则时，不必靠人工点击几十局确认有没有破坏旧规则。

## 旧路线遗留

仓库中的 `client/YGOPro2Overlay/`、`upstream/` 和 `tools/apply_ygo2_overlay.py` 属于已经停止投入的 YGOPro2 / Unity 路线遗留。本阶段暂不批量删除，以免误删参考代码；它们不再作为正式客户端方向。

## 许可证

代码以 **GPL-3.0-or-later** 发布。第三方项目仍遵守各自许可证，详见 `THIRD_PARTY_NOTICES.md`。
