# Battle Presentation V2 第一阶段：本地回归报告（草稿）

状态：**implementation in progress / visual pending user approval**。

本报告保留本组件实际执行的非 GPU 回归，并补入根代理通过真实 MCP 游戏运行
采集的三代表卡演出、截图与动态性能证据。它不表示用户已经认可新卡体／演出，
也不表示完整战斗表现已经完成。第一阶段代表范围为 LO-11、AP-11、NT-04；
**默认产品路径尚未切换，新表现仅通过独立验收入口启用**。运行时观察事件范围见
[v05 观察事件 v1](v05-event-observations-v1.md)。

## 被测工作区

- 日期：2026-09-06（Asia/Shanghai）；初次完整回归约 00:54～00:57，随后
  根代理追加实机与最终托管回归，以下分开记录，不能混成同一时点的证据。
- 分支：`codex/battle-presentation-v2`。
- 测试时 Git HEAD：`f0602683ea7cd37e2e327a9f389d5f2193c14c02`，但被测代码
  包含该分支尚未提交的第一阶段实现；**不能将结果归属于干净的 f060268**。
- 随后实现已提交并推送为 `70425ef6b7b669aebdaac759df517a7005278ecf`；下文
  后续 CI、本机默认产品 smoke 与新导出单独注明该实现身份。早期工作区测试
  时间不因此被改写为提交后的 CI。
- Windows x64，MSVC 14.44.35207 / Ninja / Release；警告视为错误、静态 CRT；
  `SCGS_ENABLE_LEGACY_YGO2_TESTS=ON`，sanitizer 未启用。
- .NET SDK `10.0.400`，MSTest SDK `4.3.3`，托管目标 `net10.0`。
- 非 GPU 回归组件未启动／停止 Godot；实际编辑器、游戏和 GPU 采集由根代理
  经已连接的 MCP 完成，不把脚本单元测试冒充实际运行。

实际集成库：

| 库 | SHA-256 |
| --- | --- |
| `build/v05-msvc/scgs_v05.dll` | `68d7d37a9f81b35e2ce6bca0989f492225b64b9c1351eb76c64d80f5434b279e` |
| `build/v05-msvc/scgs_v04_fixture.dll`（仅旧协议测试） | `6d96e642f1af9dac0331dc54ecde692970d7f15dcea11e151d8f9a4999bd2e9e` |

v04 成功路径使用合成 fixture，不能把已退役的普通 v04 产品配置当成当前产品。
v05 实际环境变量为 `SCGS_NATIVE_V05_LIBRARY`；错误拼写
`SCGS_V05_NATIVE_LIBRARY` 不会启用这些集成测试。

## 实际结果

| 检查 | 本轮结果 | 原始证据（仓库下 ignored `build/`） |
| --- | --- | --- |
| MSVC 全目标增量构建 | 通过，无编译／链接失败 | `battle-presentation-v2-stage1-tests/msvc-build.log` |
| 完整 CTest | **28/28，通过**；55.51 秒 | `battle-presentation-v2-stage1-tests/ctest-windows-release-final.log` |
| 原生旧规则／压力组 | 30 cases / 8,685 assertions；`SCGS_SMOKE_SEEDS=2048`，每 seed 互换先手，共 4,096 局合成 fixture | `battle-presentation-v2-stage1-tests/ctest-detailed-final.log` |
| v04 安全查询合同 | 463 assertions，通过 | 同上 |
| ProductRuntime | 20 cases / 1,099 assertions，通过 | 同上 |
| ProductGame | 40 cases / 1,469 assertions，通过；包含 35 个锁定定义语义场景及新增观察事件回归 | 同上 |
| legacy v1 wire golden | 31 assertions，通过 | 同上 |
| v04 ABI／显式加载／退役合同 | 100,876 assertions；显式加载及退役合同各校验 14 个导出，通过 | 同上 |
| v05 C consumer／schema／显式加载 | 全部通过；真实 schema adapter 执行 65 个命令、到达选择边界；显式加载校验 14 个导出 | 同上 |
| 初次托管全量、真实 v05 与 v04 fixture 集成 | **204/204，通过；0 skipped / 0 inconclusive**；11.180 秒 | `battle-presentation-v2-stage1-tests/managed-full.log` 与 `managed/Scgs.Client.Tests_net10.0_x64.trx` |
| 加入 8 项动态性能采样合同后的最终托管回归 | **212/212，通过；0 failed / 0 skipped / 0 inconclusive**，01:50:44～01:50:54（+08:00），使用 v04 fixture＋v05 正确路径 | `battle-presentation-v2-stage1-tests/managed-final-fixture/Scgs.Client.Tests_net10.0_x64.trx` |
| 独立验收包装／导出合同 | **25/25，通过**；含新增 8 项包装测试 | `python -m unittest scripts.tests.test_audit_godot_export scripts.tests.test_package_battle_presentation_review` 本轮真实输出 |
| 视觉素材清单 | **69 项通过**：旧 66 项＋第一阶段 3 项；原有 14/23/28 项子清单范围与哈希未改 | `python scripts/audit_visual_assets.py` 的本轮真实输出；随后完整 CTest 也执行素材合同 |

完整 CTest 中的 Gate 3/4、Anime、导出与工作流组是 Python 合同／合成报告回归，
**不是当前游戏 GPU 截图、实际导出启动或动画质量证明**。2,048-seed 压力属于
旧规则合成 fixture，不能冒充产品 v05 已做 2,048 局压力；v05 实际整局及
表现准备场景由本轮真实动态库托管集成覆盖。

新三素材实际源文件合计 8,302,778 bytes。两张切入均为 RGB 绿幕图，需要
运行时色键抠像；清单校验不代表不存在绿边、溢色或构图问题。

### 首次失败与修复

第一次完整 CTest 为 27/28，50.53 秒；失败日志保留在
`build/battle-presentation-v2-stage1-tests/ctest-windows-release.log`。
唯一失败是历史 `test_shared_manifest_contains_no_retired_industrial_identity`
仍断言共享清单只能有一张 fallback，与此次新增三项素材冲突。

修复仅将该测试的精确白名单改为 fallback＋三项锁定 Presentation V2 素材；
旧工业资产禁入、实际文件清单、哈希与其他素材范围检查保留。没有修改规则、
屏蔽错误或降低行为断言。之后完整 28 项重新运行全绿。

最终托管回归另保留两次配置失败，不能将它们的进程“成功退出”误认成完整覆盖：

- `managed-final/Scgs.Client.Tests_net10.0_x64.trx`：总 212，196 passed、16
  notExecuted，缺少真实 native 环境变量；这是不完整集成运行。
- `managed-final-native/Scgs.Client.Tests_net10.0_x64.trx`：总 212，210 passed、
  2 failed，误把已退役 v04 产品库当作合成 fixture；不是通过修改规则来修复。
- `managed-final-fixture/Scgs.Client.Tests_net10.0_x64.trx`：修正为
  `scgs_v04_fixture.dll` 与当前 `scgs_v05.dll` 后真实 212/212、零跳过。

以上目录均在 `build/battle-presentation-v2-stage1-tests/`，未删除前两次失败证据。
新增 8 项包装单测已并入既有 CTest `scgs_godot_export_audit_contract`（第 17 项），
不增加 CI job 或 CTest 数目；原 28/28 证据仍是更早完整运行，不冒称已包含
后来才注册的 8 项，该补充模块实际单独运行 25/25。

## 真实 MCP 演出与截图（根代理采集）

根代理在当前项目真实运行中操作三个合法准备场面：LO-11 与 AP-11 的出牌和
进化、NT-04 的施放／目标命中均已实际完成；两王牌切入实际播放。通过不透明
交接页主动揭示，不以编辑器视图、旧场面或直接跳过 viewer 门代替对局。
这证明代表流程能实际执行，不代表所有卡牌、所有响应／choice 组合或画面
精致程度已获完整验收。

匿名手牌／伏策在验收模式改用匿名薄圆角卡体，移除旧 BoxMesh 灰色底裙；
仍只使用共享卡背，不绑定卡名、阵营或专属纹理。正式隐私结论仍以相应真实
状态和材质清理证据为范围，不能把单张正常截图当作穷尽性无泄漏证明。

最终截图位于本机：
`C:/Users/ASUS/AppData/Roaming/Godot/app_userdata/SomeCardGameShit/screenshots/`。
已逐张读取 PNG IHDR，以下每组都包含 `lo11`、`ap11`、`nt04` 三张 detail 截图：

| 文件模式 | 实际像素（3/3 文件分别核实） |
| --- | --- |
| `v2-final-<card>-detail-1280.png` | 1280×720 |
| `v2-final-<card>-detail-1600.png` | 1600×900 |
| `v2-final-<card>-detail-2560x1440.png` | 2560×1440 |
| `v2-final-<card>-detail-2560x1600.png` | 2560×1600 |

早期实际只有 2528 宽的误缩截图不计入上述四尺寸验收；名称和数字最终画面
仍需用户直接观察和认可。额外 `v2-final-lo11-*`、Covered 等截图是辅助证据，
不把尚未逐状态完成的四尺寸完整动画矩阵写成通过。

## 真实动态性能：1600×900，15 批／3 轮

证据：`build/battle-presentation-v2-review/dynamic-performance-1600.json`。
本次文件 SHA-256：
`ac392343e169af7ab5749c8774d57e31f1d547c76b7124ecb815b41e29ac635a`。
环境为 NVIDIA GeForce RTX 4080 Laptop GPU、Windows、Compatibility renderer，
VSync 为 Enabled、FPS limit 为 0，减少动画关闭；不是 GitHub 软件渲染器。

采样来自动画活跃期间连续 `FramePostDraw` 的单调时钟；未用静止画面补帧或
300 帧静态预热替代。5 种 workload 首轮记录 `first_use_workload=true`，
后两轮共 10 批重复，总计 2,899 动画帧、2,884 测量间隔；这里的“首次”指本采集会话该 workload 首次，不宣称驱动
或磁盘缓存全部冷启动。15 批均 `completed`、零丢弃采样，全部逐批数值预算满足。

下表每格为该批 **p95 / max（ms）**，不是跨批平均数或伪造的全局 p95：

| workload 哈希前 10 位 | 第一轮（批 1～5） | 第二轮（批 6～10） | 第三轮（批 11～15） |
| --- | --- | --- | --- |
| `a84657df64` | 9.584 / 43.724 | 7.710 / 49.094 | 7.310 / 23.179 |
| `9d2d038fd7` | 7.900 / 49.103 | 7.363 / 52.595 | 7.513 / 47.110 |
| `ebb320ff21` | 7.979 / 23.103 | 8.015 / 34.469 | 7.894 / 45.021 |
| `6211ef4b7c` | 7.772 / 54.079 | 7.733 / 50.117 | 7.585 / 48.065 |
| `0c1137b48c` | 6.890 / 51.350 | 7.008 / 48.703 | 6.937 / 48.001 |

同步回池与两个完整绘制帧后，15/15 均 `motion_clean=true`，动作对象的身份
绑定、可见性、碰撞均为 0，切入纹理解绑且不可见。同一 workload 三轮在两帧后
记录的 card actor / scene-bound material / texture 数分别固定为：
`27/118/33`、`28/120/33`、`27/117/32`、`28/119/32`、`21/103/27`。
这些 texture 数包含共享与 HUD 纹理，不是“身份纹理驻留数”。

**不宣称全局 Godot resources 绝对零增长**：该全局计数会因恢复视图、延迟释放
与其他资源浮动；不同场面的前后总量也不能直接作为泄漏测试。此处只证明所测
同 workload 的可比绑定计数稳定与动作池确实清理。报告原文明确
`no_automatic_overall_pass=true`；数字达标不能代替完整播放、取消、隐私和用户
视觉验收，也不能外推到其他机器、其他分辨率或整局最大场面预算。

## 最终录像候选：保持原始时间轴，不宣称恒定 60 fps

三段最终录像位于 `build/battle-presentation-v2-review/`。根代理重新录制并
抽帧查看，包含新匿名卡背；旧 `*-real-*.mp4` 不作为最新录像交付。
只读 ffprobe 核对三段均为 1600×900，标称帧率 `60/1`，但实际编码为可变
帧率，帧数不足以支持“恒定每秒 60 张真实帧”的说法。录制保持原时间轴和
正常速度，没有按帧数压缩时间；视频捕获吞吐也不是游戏自身帧时的证明。

| 最新文件 | 实际帧数／时长 | SHA-256 |
| --- | --- | --- |
| `LO-11-review-final.mp4` | 822 帧／18.000 s | `e416d049074791e6bab80c5811baa8ec31bfb7cebe528bf9b5c2d328eb88dabe` |
| `AP-11-review-final.mp4` | 801 帧／17.983333 s | `62fcb81fd76ef86e6cb574ffab39bd68b4683d39d71f9f5ccf78cbb7c95dd5e8` |
| `NT-04-review-final.mp4` | 739 帧／15.983333 s | `d5cf40869db24d9df4ba47d662adf95a29e61bc65c4a9e38e4b1ecd2416edab9` |

## 默认产品的硬件全 UI 与静态重场回归（不是新演出测试）

实现 `70425ef6b7b669aebdaac759df517a7005278ecf` 的源码级正常产品路径在本机
RTX 4080 Laptop GPU、1600×900 实际运行完成；未启用新 review 表现替代默认
游戏。证据目录为 `build/battle-presentation-v2-stage1-tests/hardware-static/`。

- `product-smoke.json`：4 局、157 条规范命令，ActionKind 0～13 均实际覆盖；
  2 次自然终局、2 次投降、3 次重开、4 次释放。338 次指针／197 次场面／27 次
  键盘输入，错误归属／区域拖放、选择回退、响应与支付后选择投降均有真实检查。
- 公开结算至少两帧；提前 viewer 读取、未授权私密查询、私密状态泄露、
  未归属命令及 engine failure 均为 0。runtime 日志只出现一次
  `SCGS_PRODUCT_V05_UI_SMOKE_OK`。
- `product-privacy.json` 是 display-GPU 实测：真实揭示手牌的正检有 19,050
  个洋红像素，Resolving 与 Covered 各两帧负检为 0，viewer/query 增量均为 0。
  这是默认产品隐私回归，不能据此外推新 Presenting 动画全部路径的 GPU 私密性。
- `visuals/product-performance.json`：双方主战场 4＋4、Action/revision 48；
  300 帧预热＋300 帧测量，p95 **8.5346 ms**、max **57.3337 ms**。actors
  44→44、materials 80→80、textures 34→34，global resources 130→122；
  `zero_growth=true` 表示此静态样本未增长，不代表资源数量绝对相同。

严格硬件复核结果为
`build/battle-presentation-v2-review/hardware-static-verdict.json`，`success=true`，
并明确 `performance_scope=static-heavy-board-not-dynamic-presentation`。
其 implementation SHA 来自操作者输入，报告保存 runtime、smoke、privacy、visual
及 performance 五份证据哈希；不能把静态 600 帧填入前述 15 批动态采样。

## Windows Release 导出与独立验收 ZIP：本机候选

新导出位于
`artifacts/review/battle-presentation-v2-stage1-windows/export/SomeCardGameShit.exe`。
`licenses/BUILD_INFO.txt` 记录实现完整 SHA `70425ef6b7b669aebdaac759df517a7005278ecf`、
Godot 4.7.2 Mono、SDK 10.0.400、运行时 8.0.30、v05/schema 2 与 AnimeV1。

实际完成且本轮只读复核：

- Release 导出成功，原生 DLL 为 x86-64、精确 14 个 scgs 导出、无 C++ 导出及
  动态 MSVC runtime 依赖；导出 DLL 哈希与上方已测试 v05 DLL 相同。
- 新 PCK 为格式 4，**303 个条目、20 项 project settings、1 份脚本 class cache**
  通过 MCP 隔离；未残留 addon、autoload/plugin 引用或探针资料。
- 项目设置块 SHA-256：
  `7251541eee4690d5fc8210ff279fe7f6873169ab0ac9a8ae32301d3e09487329`。
- 实际 EXE 已打开新 review 入口、Covered 与主动 Reveal。用户随后正在实机
  操作，代理暂停 OS 输入；**不声称代理已在导出程序完成三卡进化／全部演出**。
  前述三卡动作和动态采样来自真实源码运行，不能偷换为导出后的同项验证。

独立验收 ZIP：
`artifacts/packages/SomeCardGameShit-battle-presentation-v2-stage1-windows-x86_64-review.zip`，
大小 **206,781,037 bytes**，SHA-256：
`56d66989871ef00dc170aa40527d0f50db2e45f962fc5ad3c4c6b3c29e5b6f1c`。

包装器完成原目录、暂存目录及 ZIP 解包后三次隔离／产品审计，逐文件哈希
round-trip 通过；包内 `REVIEW_PACKAGE.json` 记录 217 个被散列文件、正确 v05
DLL SHA、`-- --battle-presentation-review` 参数，以及
`runtime_launched_by_packager=false`。包本身未正式签名，不是用户认可的最终成品。

来源记录诚实保留 `base_commit=70425ef6b7b669aebdaac759df517a7005278ecf`、
`worktree_dirty=true`（本地 MCP 与未跟踪报告仍在）；`export_provenance` 明确
打包器不重建既有 export。因此不会把本机目录或包自称为绝对干净提交的可复现
构建。完整生成记录、素材清单及许可证随包；截图／录像证据 ZIP 信息见下文。

本机该 review ZIP 真实解包后，正常产品路径另执行 headless `full-ui` 回归：
4 局、157 commands、14 种 ActionKind 计数全部大于 0，唯一成功标记、无运行
错误。证据为 `build/battle-presentation-v2-stage1-tests/local-review-zip-smoke/`
下的 `product-smoke.json` 与 `runtime.log`；该 smoke 本体退出码 0。
外围 shell 随后因独立 Git index 操作返回 1，不能把该后续命令错误写成游戏
smoke 失败，也不能掩盖为整个 shell 成功。**此回归是 headless，不是 GPU
演出或导出 review 三卡真人操作验证**。

独立截图／录像证据包：
`artifacts/packages/SomeCardGameShit-battle-presentation-v2-stage1-evidence.zip`，
大小 **51,057,348 bytes**，SHA-256：
`ba85790bd9f94e0b8305eacf4d2e4df2048cfcfa8be60469fab7c37a25d0eaf5`。
包含 12 张三卡×四尺寸 detail、4 张辅助 PNG、3 段正常时间轴 VFR 视频、
4 份测量 JSON 与说明。它是待用户审阅的实拍证据，不是视觉批准文件。

## 实现提交四项主 CI 全绿（不等于视觉批准）

实现 `70425ef6b7b669aebdaac759df517a7005278ecf` 的
[CI run 33982319008](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33982319008)
由主代理及专门监控代理核对原始日志，四项均已完成：

| Job | CTest | 托管 | 真实产品 UI／导出／ZIP |
| --- | --- | --- | --- |
| Linux GCC Release | 28/28，41.17 s，`SCGS_SMOKE_SEEDS=2048` | 不适用 | 纯原生，不作 Linux 产品客户端声明 |
| Linux Clang ASan/UBSan | 28/28，95.80 s，`SCGS_SMOKE_SEEDS=256` | 不适用 | 纯原生 sanitizer |
| Windows MSVC | 28/28，51.97 s | 212/212，零跳过 | source 1600、source 1280、export、ZIP 四个独立成功标记 |
| macOS ARM64 | 28/28，36.21 s | 212/212，零跳过 | source、export、ZIP 三个独立成功标记 |

Windows 制品 ID `9974416519`、macOS 制品 ID `9974401710`。它们是当前 CI 的
正常产品路径制品；不要把正常 `--ci-product-smoke` 的成功标记当作三代表新
review 演出全部通过。独立 review launcher、本机动态采样和未正式签名候选
ZIP 的实际范围见前文；仍不支持 Linux 正式客户端。

这里记录的是 **70425ef 实现提交** 的全绿，不是尚未提交／验证的最终报告
文档尖端；后续文档提交若有 CI，必须使用它自己的 run 结果，不沿用此 run。

## 复现命令

在 x64 MSVC 开发者环境、仓库根目录执行：

```powershell
cmake --build build/v05-msvc
$env:SCGS_SMOKE_SEEDS = '2048'
ctest --test-dir build/v05-msvc --output-on-failure --output-log build/battle-presentation-v2-stage1-tests/ctest-windows-release-final.log

$env:SCGS_NATIVE_V05_LIBRARY = (Resolve-Path build/v05-msvc/scgs_v05.dll).Path
$env:SCGS_NATIVE_LIBRARY = (Resolve-Path build/v05-msvc/scgs_v04_fixture.dll).Path
& C:\Users\ASUS\.dotnet\dotnet.exe test --project client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --configuration Release --no-restore --report-trx --results-directory build/battle-presentation-v2-stage1-tests/managed
python scripts/audit_visual_assets.py
```

本轮是已有锁定依赖的 `--no-restore` 回归；没有重新声称完成干净环境 locked
restore。最终可复现交付仍须以提交后的 CI 和正确打包的原生库为准。
`git diff --check` 本轮通过；详细 CTest 输出另复制到阶段证据目录，避免随后
运行覆盖 CMake 的 `LastTest.log`。

## 本报告未执行／仍待验收

- 最终报告文档尖端自己的 CI；实现提交四项已绿，不替代后续提交验证。
- macOS 独立三代表 review 候选的真人操作与新演出；CI 正常产品导出／解包
  已通过，但并非 review 演出专门验收。Windows 本机 review ZIP 解包后的
  完整三代表演出仍待真人验收；默认产品 headless 回归已单独记录。
- 新表现路径中三代表卡之外的完整对局、所有攻击／响应／choice 连续组合、
  每种取消与输入路径、四尺寸完整状态矩阵及系统性的隐私 GPU 哨兵验收；上述代表流程和
  12 张精确尺寸截图不扩张成全量完成。
- 1600×900 三代表 workload 之外的硬件 GPU 动效性能、最大场面与身份纹理
  驻留预算、跨硬件／平台对比；不得把本次可比绑定计数稳定扩张为全局资源
  零增长，也不得用软件渲染器或静态原型结果替代未测硬件表现预算。
- 用户对第一阶段卡体和演出的视觉批准；未批准前不批量扩展全卡表现、不把
  该草稿作为最终版本发布或完整可玩成品视觉验收报告。

本地 MCP 安装与编辑器配置等无关工作区修改继续保留，不属于产品提交内容。
本草稿已报告实现提交四项 CI 全绿，但未报告最终文档尖端或用户视觉验收通过。
