# SomeCardGameShit

原创 1v1 数字卡牌游戏实验项目。C++20 规则引擎是唯一规则真值；正式客户端路线锁定为 **Godot 4.7.2 .NET** 的桌面单机热座版本。

当前分支是 **Gate 6A-R1：AnimeV1 一体化卡体重写与 CI 分层**，建立在已经完成的 Gate 5B＋6A 运行时底座和动漫样片之上。Gate 5B 已建立与冻结 v0.4 并行的产品规则域、由 Gate 5A 锁定清单生成的 34 张可构筑牌＋1 个衍生物基础目录、混合五格主战场、独立场地、分层关键词、可暂停选择队列、人工触发排序，以及 `scgs_v05` ABI 2.0／JSON schema 2 和纯托管 `Scgs.Client.V05`。能力清单中的 9 项修正＋33 项新增已经形成精确的 42／42 synthetic 可执行原语矩阵。这是可验证的通用底座，**不是**两副 30 张产品牌组已经完整可玩；逐卡效果组合、固定牌整局代理、数值实战和产品入口切换属于 Gate 5C。

Gate 6A 同时提供不调用 native 的独立 **AnimeV1 原创华丽厚涂日式幻想动漫样片**：两名主战者、七张代表卡、两张王牌进化异画、统一卡背、菜单主视觉和开放式幻想竞技场，共 14 项候选素材，并覆盖菜单、牌组设置、普通对局、手牌悬停、混合永久物、响应、交接和结果八种状态。可用 `--anime-style-slice` 启动；它只用于视觉审批，不冒充新牌组可玩版。

Gate 6A-R1 进一步提供 **AnimeV1 一体化卡体候选**。旧样片中由黑色名称条、悬浮徽章和类型按钮拼成的卡框已经判定不合格；新候选使用统一 3:4 连续轮廓、嵌入式费用／身材／倒数座、职业纹章与四级稀有度，并由真实 `CardActor3D` 直接组合，不为每张卡创建 `SubViewport`。七张代表插画与两张进化异画仍只是候选，本轮没有重画或批准它们。该入口同样不调用 native，也不表示誓卫／契术新牌组已经可玩；完整说明与验收边界见 [`docs/anime-v1-card-body-r1.md`](docs/anime-v1-card-body-r1.md)。

AnimeV1 已锁定为整个产品的唯一长期美术方向：菜单、竞技场、主战者、卡牌、卡框、HUD、弹层、VFX、fallback 与 shader 都要统一动漫幻想风。当前 Gate 4B-R2／R3 科幻工业画面只作为迁移期默认客户端和历史回归证据保留；用户批准样片并完成 Gate 6C 后，旧产品 profile、旧卡图与产品入口必须删除，不维护面向玩家的双皮肤模式。

冻结的 `scgs_v04` ABI 1.0／schema 1 继续服务当前旧客户端；新的 `scgs_v05` ABI 2.0／schema 2 独立安装并由 `Scgs.Client.V05` 消费，不能原地扩写 v04。Godot 当前产品入口本轮仍不切换 v05。`Scgs.Hotseat` 已具备 `ChooseMode`、`ChooseCards`、`OrderTriggers`、`ChooseAdditionalCost` 状态底座，后续只从同 revision 的安全查询派生操作，不能在客户端复制规则。双人真人热座与物理目标机仍是发布标签前硬门。

- 规则真值：[`docs/rules-v0.4.md`](docs/rules-v0.4.md)
- Godot 热座开发计划：[`docs/GODOT-HOTSEAT-DEVELOPMENT-PLAN.md`](docs/GODOT-HOTSEAT-DEVELOPMENT-PLAN.md)
- 当前交接：[`docs/DSH-HANDOFF-v0.4-ui.md`](docs/DSH-HANDOFF-v0.4-ui.md)
- 原生 API 契约：[`docs/native-api-v04.md`](docs/native-api-v04.md)
- 产品原生 API 契约：[`docs/native-api-v05.md`](docs/native-api-v05.md)
- Godot 客户端架构：[`docs/godot-client-architecture.md`](docs/godot-client-architecture.md)
- UI 状态图与隐私：[`docs/ui-state-map.md`](docs/ui-state-map.md)
- 架构：[`docs/architecture.md`](docs/architecture.md)
- Gate 5A 牌组设计：[`docs/product-decks-v1-design.md`](docs/product-decks-v1-design.md)
- Gate 5A 能力差距：[`docs/product-decks-v1-capability-gap.md`](docs/product-decks-v1-capability-gap.md)
- Gate 5A 动漫美术圣经：[`docs/product-decks-v1-art-bible.md`](docs/product-decks-v1-art-bible.md)
- Gate 5A 锁定清单：[`design/product-decks-v1/card-pool.lock.json`](design/product-decks-v1/card-pool.lock.json)
- AnimeV1 全产品视觉锁：[`design/product-decks-v1/anime-v1-visual.lock.json`](design/product-decks-v1/anime-v1-visual.lock.json)
- Gate 6A 样片与运行方式：[`docs/anime-v1-visual-slice.md`](docs/anime-v1-visual-slice.md)
- Gate 6A-R1 一体化卡体候选：[`docs/anime-v1-card-body-r1.md`](docs/anime-v1-card-body-r1.md)
- Gate 6A 素材来源与完整 prompt：[`client/godot/assets/visual/anime_v1/slice/PROVENANCE.md`](client/godot/assets/visual/anime_v1/slice/PROVENANCE.md)
- 实测记录：[`TEST_REPORT.md`](TEST_REPORT.md)

## 当前范围

冻结 v0.4 引擎目前仍包含旧 `midrange`／`advance` 两副 30 张固定测试牌组；它们只是当前 Godot 回归基线，不是 Gate 5A 锁定的产品内容。新 `scgs::v2` 产品域已经使用独立 synthetic fixture 覆盖混合永久物、场地、离场原因、关键词、选择与触发排序，且普通构建直接编译已提交的生成目录，不依赖 Python。v0.4 的规则、压力与客户端 API 核心覆盖也已改用独立 fixture；仍依赖旧牌组的冻结 v04 native／managed／Godot 整局回归要在产品整局接通后退役。Gate 5C 最终删除旧牌组键、旧定义、旧卡图与菜单选项，不能把旧内容作为隐藏牌组保留。

当前冻结 v0.4 可运行 Alpha 支持：

- 25 点主战者生命、起手与调度、手牌上限、封存和疲劳；
- 无上限 PP 容量、当前 PP、预支、燃耗、裂痕、修复和增长；
- 5 个单位位、3 个由设施/伏策/待结算法术共用的策略位，以及最多 6 张公开战备牌；
- 单位战斗、持续伤害、同时死亡批次和基础关键词；
- 单一进化形式、解锁后的职业充能、战备部署和组件能力；
- 设施、伏策与最多三层的响应栈；
- 投降、胜负、平局和幂等终局；
- 数据驱动效果；
- legacy v1 wire 金标字节回归。

面向客户端的 C++ API 只暴露观看者可见信息，`scgs_v04` 与 `scgs_v05` C ABI 分别将自己的契约编码为 UTF-8 JSON；两层都遵循以下循环：

```text
快照 → 合法行动/目标/位置/支付查询 → 带 revision 的命令 → 脱敏事件
```

成功命令使状态 revision 恰好增加一次；失败或过期命令不得改变状态、事件和 revision。对方手牌、背面伏策以及相关事件不会泄露卡名或稳定实例 ID。

C ABI 不暴露 C++ 类、STL、异常或跨 CRT 内存。动态载荷使用调用方所有的两段式缓冲区，native 错误与规则错误分离，事件继续采用 `viewer + after_sequence` 的非破坏读取。v05 增加混合永久物视图、模式／选择／额外代价字段与脱敏 `PendingChoiceView`，并禁止在实时快照和开局事件输出 seed。完整契约见 [`docs/native-api-v04.md`](docs/native-api-v04.md) 与 [`docs/native-api-v05.md`](docs/native-api-v05.md)。

当前可运行 Alpha 仍只承诺旧两副固定测试牌组的闭环。Gate 5B 的产品域已经验证人工同时触发排序与可暂停选择底座，但还没有把 34 张产品牌效果编译成完整可玩的产品 `Game`；v05 对产品出牌动作会受控拒绝，不能借基础 DTO 冒充完成。Gate 5C 完成逐卡能力、固定牌整局和旧内容删除后，誓卫／契术才会替换当前入口。

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

`SCGS_ENABLE_LEGACY_YGO2_TESTS` 默认 `ON`。为保持既有 CMake/CI 基线，这个历史命名的开关当前注册整组 Python 契约测试，包括 legacy overlay/protocol、原生/Godot 导出、R2 34 项＋R3 候选 1 项＋AnimeV1 14 项＋R1 卡体 23 项分离素材审计、子进程超时、历史整局/视觉报告、R3.1 候选切片、Gate 5A 产品牌组设计契约、提交态产品目录生成检查、Gate 6A/R1 样片结构契约和 CI 分层路由契约；开启时配置阶段必须找到 Python 3.10+，不能静默少注册。设为 `OFF` 会跳过整组 Python 契约，不能用这种构建宣称完成客户端验收。CMake 会按版本与 SHA-256 固定获取 JSON 依赖，不要求系统全局安装。

原生边界可安装到暂存目录以检查真正的消费产物：

```bash
cmake --install build/release --prefix build/stage
```

安装内容包含两版 C 头、ABI/JSON 契约，以及当前平台的 `scgs_v04` 与 `scgs_v05` 动态库；两者各自必须只有精确 14 个导出。

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

无需原生库即可从源码查看一体化卡体候选：

```bash
godot --path client/godot --windowed --resolution 1600x900 -- --anime-card-body-slice
```

Windows／macOS 卡体样片导出包完整解压后，分别运行 `PLAY_ANIME_CARD_BODY_SLICE.cmd` 或 `PLAY_ANIME_CARD_BODY_SLICE.command`；直接启动默认 EXE／`.app` 不会自动进入该候选入口。

日常 Windows CI 已移除约74分钟的旧 R2 全尺寸视觉长测，保留原生、托管、Godot 构建、当前卡体样片、基础整局与正式导出验收。旧 R2/R3/legacy 兼容视觉矩阵及600帧资源稳定检查转移至每日夜间与手动 `windows-visual-heavy` 工作流；它们仍是发布前回归门，只是不再阻塞每次普通提交。

## 目录

```text
engine/          C++20 权威规则引擎、客户端安全 API、C ABI 与测试
client/          Scgs.Client、Scgs.Hotseat、Godot 桌面客户端；YGOPro2 内容仅为历史参考
design/          产品卡池锁定清单、Schema、AnimeV1 全产品视觉锁与跨字段设计契约
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
