# SomeCardGameShit

原创 1v1 数字卡牌游戏实验项目。C++20 规则引擎是唯一规则真值；正式客户端路线锁定为 **Godot 4.7.2 .NET** 的桌面单机热座版本。

当前开发分支已完成 **Gate 4B-R3.1 无边界工业竞技场视觉切片** 的自动化与 Windows 候选包交付，等待用户主观批准。默认客户端仍保持已验收自动化的 Gate 4B-R2；只有双击候选包中的 `PLAY_R3_VISUAL_SLICE.cmd` 或显式传入 `--r3-visual-slice` 才进入 R3 候选。候选以连续工业地面、场外机械、浅凹槽格位、中性战术 HUD 和相机相对前景手牌替换 R2 的场地大框、半场色板与原型式构图，但不会改动规则、卡牌或 R2 golden。被测实现尖端 `3d4012f` 的 [CI run 32808917410](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32808917410) 已四项全绿；准确命令、取证、制品字节与 digest 见 [`TEST_REPORT.md`](TEST_REPORT.md)。

R2 的 29 张临时卡图、卡背、菜单背景、未知卡通用正面和 2 张领袖头像仍是冻结的 34 项产品素材；R3.1 只新增 1 张独立候选地坪，并使用可由提交源码确定性重建的原创机械 GLB。R3 报告固定为 `pending_user_approval`；用户明确批准前，不会把候选推广为默认产品路径，也不会用三张视觉切片冒充完整对局表现已经迁移。

纯托管 C# 边界继续消费 `scgs_v04`，`Scgs.Hotseat` 从同 revision 的规范合法行动派生统一 surface intent；Godot 的默认 3D 与 `--legacy-2d-board` 隐藏回归路径只能把点击/拖拽转换为同一 intent，不能复制规则。Gate 4A.1 的策略位法术语义保持不变：玩家必须选择具体空位，法术正面占位到自身链环结算，后排全满时不能施法。双人真人热座与物理目标机仍是发布标签前硬门。

- 规则真值：[`docs/rules-v0.4.md`](docs/rules-v0.4.md)
- Godot 热座开发计划：[`docs/GODOT-HOTSEAT-DEVELOPMENT-PLAN.md`](docs/GODOT-HOTSEAT-DEVELOPMENT-PLAN.md)
- 当前交接：[`docs/DSH-HANDOFF-v0.4-ui.md`](docs/DSH-HANDOFF-v0.4-ui.md)
- 原生 API 契约：[`docs/native-api-v04.md`](docs/native-api-v04.md)
- Godot 客户端架构：[`docs/godot-client-architecture.md`](docs/godot-client-architecture.md)
- UI 状态图与隐私：[`docs/ui-state-map.md`](docs/ui-state-map.md)
- 架构：[`docs/architecture.md`](docs/architecture.md)
- 实测记录：[`TEST_REPORT.md`](TEST_REPORT.md)

## 当前范围

引擎包含两副 30 张固定测试牌组，支持：

- 25 点主战者生命、起手与调度、手牌上限、封存和疲劳；
- 无上限 PP 容量、当前 PP、预支、燃耗、裂痕、修复和增长；
- 5 个单位位、3 个由设施/伏策/待结算法术共用的策略位，以及最多 6 张公开战备牌；
- 单位战斗、持续伤害、同时死亡批次和基础关键词；
- 单一进化形式、解锁后的职业充能、战备部署和组件能力；
- 设施、伏策与最多三层的响应栈；
- 投降、胜负、平局和幂等终局；
- 数据驱动效果；
- legacy v1 wire 金标字节回归。

面向客户端的 C++ API 只暴露观看者可见信息，`scgs_v04` C ABI 将同一契约编码为 UTF-8 JSON；两层都遵循以下循环：

```text
快照 → 合法行动/目标/位置/支付查询 → 带 revision 的命令 → 脱敏事件
```

成功命令使状态 revision 恰好增加一次；失败或过期命令不得改变状态、事件和 revision。对方手牌、背面伏策以及相关事件不会泄露卡名或稳定实例 ID。

C ABI 不暴露 C++ 类、STL、异常或跨 CRT 内存。动态载荷使用调用方所有的两段式缓冲区，native 错误与规则错误分离，事件继续采用 `viewer + after_sequence` 的非破坏读取。完整契约见 [`docs/native-api-v04.md`](docs/native-api-v04.md)。

Alpha 只承诺现有两副固定牌组的闭环。主战技 UI、普通主动能力、人工同时触发排序和固定牌组未使用关键词延后；同一玩家的同时触发暂按确定性场地顺序处理。

Gate 4B-R2 保留 Gate 3C/4A 的确定性 signal 整局、重开、投降、空间拾取与隐私契约，并保留 Gate 4A.1 “法术落到己方空策略位”的规则。默认 3D 使用 authored 战场、相机相对前景手牌架和共享 screen-reading HUD shader；`Covered` 仍是唯一必须完全不透明的交接状态。

Windows 的 Gate 4B-R2 display-backed 视觉套件在 1280×720、1600×900、2560×1440 和 2560×1600 捕获原有 11 种产品状态，并新增单张/五张/十张手牌、手牌悬停与场上可读性，共 16 种状态。visual-suite 使用独立 schema version 4，要求连续两个内容一致的 `FramePostDraw`、真实画面锚点与费用/身材/倒计时 ROI；Gate 4A full-match 仍使用其独立 schema version 3。1600×900 golden 只能人工审阅后显式更新；600 帧稳态测试仍要求预热后 actor/material/texture 零增长。实际测试数量、导出与 CI 状态只以 [`TEST_REPORT.md`](TEST_REPORT.md) 为准。

## 工具链

本地构建需要：

- CMake 3.25 或更高；
- 支持 C++20 的 GCC、Clang 或 MSVC；
- 支持 C11 的 C 编译器（原生 consumer 契约测试）；
- Ninja（使用仓库预设时）；
- Python 3.10 或更高（默认开启的 legacy YGOPro2 Python 回归测试需要）。

客户端精确锁定 Godot **4.7.2 .NET** 和 .NET SDK **10.0.400**；详见 [`docs/toolchain.md`](docs/toolchain.md) 与根目录 `global.json`。正式目标是 Windows x86-64 和 macOS Apple Silicon，**不支持 Web**。

## 构建与测试

使用预设：

```bash
cmake --preset dev
cmake --build --preset dev
ctest --preset dev
./build/dev/scgs_demo --verify
```

Release 与 Clang sanitizer：

```bash
cmake --preset release
cmake --build --preset release
ctest --preset release

cmake --preset asan
cmake --build --preset asan
ctest --preset asan
```

`SCGS_ENABLE_LEGACY_YGO2_TESTS` 默认 `ON`。为保持既有 CMake/CI 基线，这个历史命名的开关当前注册整组 Python 契约测试，包括 legacy overlay/protocol、原生/Godot 导出、R2 34 项＋R3 候选 1 项视觉素材联合审计、子进程超时、Gate 3B/3C/4A 整局报告、Gate 4B-R2 视觉报告/golden 以及独立 R3.1 候选切片契约；开启时配置阶段必须找到 Python 3.10+，不能静默少注册。设为 `OFF` 会跳过整组 Python 契约，不能用这种构建宣称完成客户端验收。CMake 会按版本与 SHA-256 固定获取 JSON 依赖，不要求系统全局安装。

Gate 2 还可安装到暂存目录以检查真正的消费产物：

```bash
cmake --install build/release --prefix build/stage
```

安装内容包含 C 头、ABI/JSON 契约及当前平台的 `scgs_v04` 动态库。

辅助脚本：

```bash
./scripts/test.sh
./scripts/stress.sh
```

Windows 可运行 `scripts/test.ps1`。准确命令、测试数量、断言数量和未验证项以 [`TEST_REPORT.md`](TEST_REPORT.md) 的本次实测为准。

纯托管客户端与 Godot 工程使用锁定 SDK：

```bash
dotnet restore client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --locked-mode
dotnet build client/Scgs.Client.Tests/Scgs.Client.Tests.csproj -c Release --no-restore
dotnet test --project client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --configuration Release --no-restore --minimum-expected-tests 1
dotnet build client/godot/SomeCardGameShit.csproj -c Release
```

Godot 编辑器和导出包必须使用同一提交构建、审计并暂存的原生库；不要把 DLL/dylib 提交到 Git。具体路径、headless smoke 和导出说明见 [`client/godot/README.md`](client/godot/README.md)。

## 目录

```text
engine/          C++20 权威规则引擎、客户端安全 API、C ABI 与测试
client/          Scgs.Client、Scgs.Hotseat、Godot 桌面客户端；YGOPro2 内容仅为历史参考
docs/            规则、架构、路线图、协议和交接文档
scripts/         构建与压力测试脚本
tools/           legacy overlay/协议契约工具
upstream/        已停止投入的上游锁定资料，仅供历史参考
.github/         CI 构建矩阵
```

## Legacy 路线

`client/YGOPro2Overlay/`、`upstream/`、`tools/apply_ygo2_overlay.py` 以及远端 M1 分支属于已停止投入的 YGOPro2/Unity 路线。它们暂时保留用于协议回归和工程考古，不是正式客户端、不会进入 Godot 设计，也不得据此推导当前产品能力。legacy v1 wire 的字节布局仍冻结不变。

## 许可证

代码以 **GPL-3.0-or-later** 发布。第三方项目仍遵守各自许可证，详见 `THIRD_PARTY_NOTICES.md`。
