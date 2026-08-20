# SomeCardGameShit

一个基于 YGOPro / YGOPro2 技术路线验证的原创数字卡牌游戏项目。

> 当前状态：**Milestone 0 — 无界面规则纵向切片已完成并通过自动测试。**
> 这还不是可下载游玩的 YGOPro2 客户端。当前核心先作为规则真值源与回归测试器，下一步才把同一套状态、消息和操作接进 YGOPro2。

## 当前已经能运行什么

当前 C++20 无界面核心已经实现并测试：

- 25 点主战者生命、先后手、4 张起手与一次调度；
- 0→10 的 PP 增长和每回合补满；
- 9 张手牌上限、溢出封存、递增疲劳伤害；
- 5 个单位位与 2 个战术位；
- 持续保留的单位伤害、单位互相同时造成战斗伤害；
- 守护、突进、疾驰、屏障、必杀、吸血、潜伏的底层规则；
- 战斗进化与能力进化；
- 上位召唤、构成召唤、每回合一次高级召唤；
- 素材原始费用、素材印记和禁止二次继承；
- 召唤牌组单位离场后进入封存区；
- 遗物倒计时、伏策设置与有限反应窗口；
- 主战技通用接口、投降、胜负与平局状态；
- 当前回合方优先的同时死亡批次和遗言结算顺序；
- 为 YGOPro2 预留的二进制消息 ID 210–219 与固定字节序列测试。

内置测试卡池包括两副 30 张固定牌组：王庭与机巧。机巧拥有 6 张召唤牌组卡牌（3 种构成单位，各 2 张）。王庭召唤牌组暂时留空，因为规则草案尚未定义王庭为何需要使用召唤牌组；项目不会把临时猜测伪装成正式规则。两项主战技也明确标记为测试占位。

## 测试结果

当前测试套件包含：

- 22 个功能与场景测试；
- 9,000+ 次断言；
- 32 个不同洗牌种子、双方轮流先手的完整对局烟雾测试；
- 每一步操作后的全局状态不变量检查；
- GCC Debug / Release；
- Clang AddressSanitizer + UndefinedBehaviorSanitizer；
- 规则文档中的“5PP 构成召唤 → 继承守护 → 战斗进化”完整示例。

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
engine/          可独立运行和测试的规则核心
client/          YGOPro2 兼容层与后续客户端修改
upstream/        上游版本锁定和拉取说明
docs/            规则、架构、协议、测试与路线图
scripts/         构建、测试和拉取上游脚本
.github/         CI 配置
```

## 为什么先做无界面核心

直接在 Unity 场景、动画和旧客户端状态里调规则，会让“规则错误”和“显示错误”混在一起。当前核心先把每一个动作变成可重复的状态转移和事件流：

```text
玩家命令 → 合法性检查 → 支付成本 → 完整结算 → 触发批次 → 死亡检查 → 事件输出
```

YGOPro / YGOPro2 接入时，客户端只负责把合法选项交给玩家，再把这些事件演出来。这样以后修改 PP、进化、高级召唤或素材印记，不需要靠人工点几十局来确认有没有破坏旧规则。

## 上游项目

本仓库不会提交游戏王卡图、Logo、音效或其他来源不明的发行包资源。上游客户端与核心通过 `scripts/bootstrap-upstream.sh` 拉取，版本记录在 `upstream/upstream.lock.json`。

- YGOPro2 客户端用于场面、输入与动画；
- YGOPro Core / Lua 体系用于后续规则与卡牌脚本接入研究；
- 当前无界面核心是本项目的测试真值源，不声称已经替代或完成对 `ocgcore` 的改造。

## 许可证

代码以 **GPL-3.0-or-later** 发布。第三方项目仍遵守各自许可证，详见 `THIRD_PARTY_NOTICES.md`。
