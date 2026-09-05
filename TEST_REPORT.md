# Product Playable v1：新牌组动漫热座验收报告

日期：2026-09-05（Asia/Shanghai）  
分支：`codex/product-playable-v1`  
开发基底：`6e0204e391d0d0b377f7b62d18f1b1fd65d56e81`  
产品实现：`e71bafe9dc6187e1a517c631b83ddc912906d76d`  
跨编译器测试修正：`9dc381f47b299cffd7fb7ab7386ac06c8a6864d3`（仅测试警告修正，产品源码/原生库不变）  
冷导入预算：`66e1fefb656506b28d1bd3da1dc0f4312ffbc243`；性能完成门：`3719e62`；积场与严格导入缓存：`90e361e`（后三项仅验收与构建准备，不改变玩家规则和正常 UI）

## 当前结论

产品入口已切换为 `ProductGame → scgs_v05/schema 2 → Scgs.Client.V05 → ProductHotseatMatchController → ProductMatchScreen`。两副新牌组、14 种动作、私密选择、响应、终局及重开已经在本机真实运行；不再是无 native 的美术样片。

本机 Windows 原生、托管、四尺寸真实 UI、硬件显卡上的隐私/性能、正式导出与 ZIP 解包后启动已经通过。最后实现 `90e361e` 的四项主 CI 全绿，Windows x64 与 macOS ARM64 可玩候选包已生成。**独立重型 CI 未通过：托管 Windows 使用软件渲染，重场帧时失败，两种大尺寸整局超时。不能宣称计划的全部验收已完成。** 该剩余项需要确定合适的图形验收环境；没有降低预算或改判通过。

历史 Gate 5B／6A／卡体 R1 报告完整保留在 [历史测试报告](docs/history/TEST_REPORT-gate5b-6a-card-body-r1.md)。其中旧分支、v04 默认、尚未可玩及旧 CI 时长只描述历史版本。

## 范围与兼容边界

- 34 个可构筑定义＋1 个衍生物使用声明式效果和通用条件；两副精确 30 张主牌、15 种定义、4 张不同公开战备。
- 随从/护符共用五格，法术占己方空策略位，双方独立场地格；可选目标跳过、额外封存代价、选择/响应期间投降均接入产品 UI。
- 旧 `midrange/advance`、旧产品数字定义和科幻美术退役，不提供新牌组别名。正式 v04 保留 ABI/schema 形状、14 导出；成功兼容测试仅由不安装、不打包的 `scgs_v04_fixture` 提供。legacy wire golden 不依赖旧产品牌。
- AnimeV1 是唯一可启动的产品视觉。旧 legacy 2D、R2/R3 与审批切片启动参数受控拒绝；历史报告合同仍使用独立 fixture 验证。
- 不声称胜率 48～52%、赢家 T10～12 或商业美术终审已经完成。双人真人热座、物理 Mac、音频、联机和正式签名仍为后续事项。

## 本机证据

### 原生与托管

逐卡语义、通用能力和修正规则见 [原生证据](docs/product-playable-v1-engine-evidence.md)。Windows 最终原生已通过 28/28 CTest（39.54 秒、Python ON），包括最后一项“每回合限一次”的跨玩家回合重置修复。ProductGame 为 39 cases / 1,397 assertions；v04／fixture／v05 均通过 x64、/MT、精确 14 导出审计。2,048-seed 压力属于独立合成 v04 覆盖，v05 真实固定牌为 32-seed 整局覆盖，二者均不代表平衡验收。

产品实现托管回归为 166/166、0 skipped（6.116 秒），使用同源码真实 v05 与独立 v04 fixture；随后新增四项性能结束条件和三项积场策略回归，最终为 **173/173、0 skipped**，后者还使用下载的真实 CI v05 DLL。Debug/Release C# 构建无警告、无错误。166 与 170 项记录独立保留，未覆盖；173 项记录位于 `build/managed-product-board-accumulation-results`。全部 Python 脚本最终通过 **253/253（36.529 秒）**，含冷导入预算和严格缓存合同。

### Godot 编辑器真实观察

本机通过当前 Codex 的 `godot-scgs` MCP 工具完成，不以终端协议探针或磁盘场景解析代替：

1. 确认连接项目为本仓库 `client/godot`，读取 live MainMenu Scene Tree 与 BackgroundBase 属性。
2. 从编辑器启动 Bootstrap；真实鼠标坐标点击本地热座、开始和主动揭示。
3. 读取运行时调度计数，真实点击手牌使选择从 0/4 变为 1/4；观察抬起手牌与完整卡牌详情。
4. 确认调度和调度结果后观察下一玩家完全遮挡；未绕过下一观看者主动揭示。
5. 读取运行日志并停止游戏。此人工 MCP 回路覆盖菜单/设置/调度/换手，不冒称覆盖全部动作。

原始记录：`build/product-mcp-observation.json`；实际游戏截图位于 Godot `user://screenshots/product-final-*.png`。

最终实现后再次通过 MCP 打开 `ProductMatch.tscn`，live 根节点下实际读到 `Battlefield3D`、`HudLayer`、`ModalLayer`、`Background` 和 `SafeMargin`；启动 Bootstrap 并真实点击菜单、开始、主动揭示，检查实际 1280×720 游戏截图后停止游戏。一次诊断使用了错误 owner scope 查询 `%RevealButton`，产生 NodeNotFound；改用 `ModalLayer/PassDeviceOverlay` 的正确 owner 并全新重启后，日志无游戏错误。两次记录均保留在 `build/product-mcp-final-observation.json`，不把错误探针混称产品错误，也不隐去失败记录；最终实拍为 `user://screenshots/product-delivery-clean-revealed.png`。

### 独立 v05 实际输入套件

`build/product-ui-display-1600-02` 已通过：

- 1600×900、真实 GPU 窗口、4 局、157 条命令、3 次重开、4 次释放。
- 14 种 ActionKind 的提交次数依序为 `[8,29,4,2,38,8,4,1,2,41,2,10,3,5]`。
- 两次自然终局、一次不可取消选择期间投降、一次响应期间投降；唯一且最后的 MatchEnded 均检查。
- 错误 owner 拖放、错误 zone 拖放各一次：原生调用（包含查询）、revision、事件游标均不变；真实 Esc 从选目标退回已明确选择的格位且保留来源。
- 真实 Input.ParseInputEvent 与物理射线命中，不以 EmitSignal、直接控制器提交或合法行动枚举补填动作数。
- 结算至少两个真实 FramePostDraw；零越权 viewer 读取、零私密查询、零未归因命令及引擎失败。
- 成功标记只在运行、视觉及隐私检查全部完成后输出一次：`SCGS_PRODUCT_V05_UI_SMOKE_OK`。

该目录保留为真实中间复验，随后新增规则修复和最终打包将使用新目录，不覆盖失败证据或旧报告。

### GPU 隐私与性能

同一次 display 套件，在**已经主动揭示**的真实 v05 手牌 actor 注入恶意文本、纯洋红纹理及回调/drag token，先在 GPU 正检观察到 19,050 个像素，再走正常调度提交：

- Resolving 连续两帧、Covered 连续两帧：私密文字、身份纹理、回调、碰撞、drag token、隐藏牌身份、私密查询与 viewer 读取全部为 0。
- 四张最终 GPU 截图洋红像素均为 0；不保存注入时的私密正检画面。
- 头less 结果只标记 structural-only，不冒充 GPU 验收。
- 真实 4＋4 主战场、300 帧预热＋300 帧测量：actor 44→44、material 80→80、texture 34→34；全局 resource 130→122（正常回收）。
- 每帧均不增长；p95 **5.0789 ms**、最大帧 **56.7236 ms**，满足 ≤33.3 ms／<100 ms。
- 曾发现“资源回收导致总量减少被误判为增长”的测试错误，已修复为每个采样帧不超过预热基线；不放宽任何帧时预算。失败目录保留。

## 执行命令

使用 Godot 4.7.2 .NET、精确 .NET SDK 10.0.400。Windows 默认 PATH 不保证有锁定 SDK，必须设置 `DOTNET_ROOT=C:\Users\ASUS\.dotnet` 并把该目录置于 PATH 首位。

```powershell
cmake --build build/v05-msvc --parallel 2
$env:SCGS_SMOKE_SEEDS = '2048'
ctest --test-dir build/v05-msvc --output-on-failure
python scripts/stage_godot_native.py --v05-library build/v05-msvc/scgs_v05.dll --destination-root client/godot/native --target windows-x86_64
dotnet build client/godot/SomeCardGameShit.csproj -c Debug --no-restore
python scripts/ci/run_product_smoke.py --executable <absolute-godot-exe> --project client/godot --artifact source --coverage full-ui --output <fresh-directory> --display --capture --performance
python scripts/dev/check_godot_mcp_export.py --export <fresh-export-exe>
python scripts/audit_godot_export.py --platform windows-x86_64 --export <fresh-export-exe>
python scripts/ci/run_product_smoke.py --executable <fresh-export-exe> --artifact export --coverage natural-ui --output <fresh-directory> --display
```

不重复运行已退役产品路径的长时间视觉套件。当前产品四尺寸与性能验收独立执行；CI 快速矩阵覆盖 native、managed、v05 源码整局、正式导出与 ZIP 重新解包启动，重型任务专门保存产品视觉/性能结果。

## 最终本机 UI / 导出复验

以下均使用最终 v05 DLL，SHA-256 为 `D125527D4F093434FAA00FFD37F8043772C1F5E7A04C7E7C9E89672BDB96C37A`，每次 4 局、157 条命令、14 种动作、2 次自然终局与 2 次选择/响应投降：

| 实际 GPU 尺寸 | 证据目录（build 下） | 结果 |
|---|---|---|
| 1280×720 | `product-ui-display-1280-fixed-01` | 15 张实际状态图、隐私通过 |
| 1600×900 | `product-ui-display-1600-final-01` | 15 张状态图、隐私、600 帧通过 |
| 2560×1440 | `product-ui-display-2560x1440-final-01` | 15 张状态图、隐私通过 |
| 2560×1600 | `product-ui-display-2560x1600-final-01` | 15 张状态图、隐私通过 |

1600 最终性能：p95 **4.8427 ms**、最大 **53.533 ms**，每帧资源不增长。人工观察发现初始极短攻击箭头退化为盖在卡图上的红块，限制不足箭头头部长的预览后，已在最终 1600 实拍确认消失；正常长距离箭头保留。其他三尺寸的布局与输入证据不是这项极短箭头的最终截图。

1280 复验曾失败：Godot `ViewportTexture.GetSize()` 返回 1024×576，但实际窗口与 GPU 均为 1280×720。检查器改为以真实 Window.Size 为目标并严格比对 GPU，有限等待同步，不重采样图片、不放宽分辨率。2560×1440 的同一 getter 返回 4096×2304，实际 GPU 验收仍要求 2560×1440。

Windows 正式导出位于 `artifacts/product-playable-v1-local/windows-x86_64`，Release 构建无警告。已通过 EXE/x64、v05 精确14导出及 /MT、许可证/清单、PCK 282 条目、project.binary 20 项设置及 global script class cache 审计：没有 MCP addon/autoload/配置/令牌/探针，也没有嵌入或混入旧原生库。实际 EXE 的 `natural-ui` 经 39 条真实输入命令自然结束，成功标记一次；日志没有 MCP server 启动，记录为 `build/product-export-natural-01`。导出测试子进程会清除继承的原生覆盖环境，不偷偷加载工作区 DLL。

### Windows 本机试玩包

- 文件：`artifacts/packages/SomeCardGameShit-product-playable-v1-windows-x86_64.zip`。
- 字节数：204,070,130；SHA-256：`E7F1E5BF1273C6A249559260FDF588D99EE34B842DAADC96F61A93DA1722A126`。
- 完整解压后运行 `windows-x86_64/SomeCardGameShit.exe`，不得只复制 EXE；包内自带所需托管运行时和 v05 动态库。
- 解包至独立 `artifacts/product-playable-v1-roundtrip` 后重复 PCK、x64、许可证、原生隔离审计，并经 39 条真实输入命令自然终局、GPU 隐私检查与唯一成功标记。记录：`build/product-zip-natural-01`。
- 本地包的 `BUILD_INFO` 明确标记 `commit=local`，不是伪装为远端 CI 包。远端构建另由提交 SHA 标识。

## 远端交付记录

首次实现 [run 33965852283](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33965852283) 在非 MSVC 构建遇到测试代码的 structured-binding 复制与聚合默认初始化警告，因 `-Werror` 失败；`9dc381f` 修正三处测试声明，不修改产品行为。[修正后 run 33966055248](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33966055248) 的 GCC Release、Clang ASan/UBSan 和 Windows 全部成功：

- GCC 13.3 Release：2,048-seed 配置，28/28 CTest，30.99 秒。
- Clang 18.1 ASan/UBSan：256-seed 配置，28/28 CTest，96.31 秒；两 Linux 安装消费者和 v04/v05 精确 14 导出审计通过。
- Windows：源码 full-ui／1280 natural-ui／正式导出／ZIP 往返均通过。远端四局为 **232 条命令**，动作计数 `[8,44,10,2,56,8,6,1,2,67,2,13,3,10]`；不能把本机 157 条替换填入。固定 seed 不承诺跨标准库 shuffle 序列一致。
- Windows 干净 checkout 的 PCK 有 19 项设置，本机安装 MCP 工作区导出为 20；两者均已逐项检查没有开发插件引用。CI 内层试玩 ZIP SHA-256 为 `709b0a9da5100e053d13bb0858871dac7f7edd43fc719087ee0c283f2b2c753a`；GitHub 外层 artifact 摘要另为 `113aca4613fde60f9bd6fa9580bbc3a97e4c51ee000dd12921feb5d59e9ca2fb`，不可混淆。
- macOS 首次压缩新素材到 AP-07（约 70%）时触发 600 秒 import 准备超时。`66e1fef` 仅将该准备步骤增至 1800 秒，不改变游戏 600 秒边界、真实输入、隐私或帧时预算。[复验 run 33966806826](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33966806826) 的 macOS ARM64 job 已成功：冷导入实际 693 秒（11 分 33 秒），源码 full-ui 40 秒、导出签名 18 秒、导出实启 11 秒、ZIP 实启 12 秒。这是 CI Apple Silicon 的真实 v05 进程/输入证据，不是物理 Mac 真人或 macOS GPU 视觉验收。制品 `SomeCardGameShit-product-playable-v1-macos-arm64` 的外层 artifact ID 为 `9969910317`，摘要 `f24d64c948000de3bb468e444fe4c35c9e5e5b9ecd944ca1474a7d94cbab7235`；仅 ad-hoc 签名、未公证。

独立 Windows 四尺寸视觉/性能 [run 33965852104](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33965852104) 使用产品实现 `e71bafe`：1280 全通过，1600 的 15 状态均捕获，但四局结束前未遇到真实重场，正确以 `heavy_board_not_observed` 失败；没有运行后的两种大尺寸。下载并人工检查了实际 CI action 截图，证据保存在 `build/remote-product-heavy-failure-01`，没有把这次 run 写成性能通过。

`3719e62` 修复测试调度：完成动作和两类投降后，仍须实际取得 ≥8 格、双方各 ≥3 格的 300＋300 帧成功性能证据；否则停止额外投降、继续原固定种子自然局，最多 12 局及原 8 分钟限制。不注入战场、不降低预算、不用字符串替代 GPU。本机复验 `build/product-ui-display-1600-performance-stop-01` 全通过：170 项托管回归、15 状态／157 命令、GPU 隐私、4＋4 重场、p95 4.4657 ms／最大 50.957 ms，actor 44/material 80/texture 34 无增长。重型工作流还改为收齐四尺寸证据后汇总失败，任何失败仍使整个 job 失败。

`90e361e` 四项主 CI 全绿后才整理本报告；包含本报告的文档提交仍由同一四平台主矩阵重新验证，交付时核对其实际 checks，不以父提交成功替代。独立重型任务的失败不因文档或主矩阵全绿而消失。

### 最后一次验收提速（不降低门槛）

- 使用下载的 CI `9dc381f` 真实 v05 DLL 在本机复现远端 shuffle；仅修结束门时，`build/product-ui-ci-native-heavy-01` 到第 10 局／660 命令才遇 5＋3 重场，完成 600 帧、p95 4.4332 ms／最大 44.309 ms。证明不是只在本机原生库上碰巧通过。
- `90e361e` 只在全动作与两种投降已有真实证据、且性能仍待测时，切换测试代理的积场策略：从当前 viewer 合法命令中选随从、护符、无封存额外代价的部署、结束回合及响应不过；不选敌方目标/攻击/清场，不注入状态、不读取隐藏信息。测量成功立即恢复通常代理，并自然完成该局。
- `build/product-ui-ci-native-board-accumulation-01`：同 CI DLL、前四局计数与远端完全一致；第 5 局 revision 19 达到真实 4＋4，最终 5 局／288 命令、3 次自然终局＋2 次投降、5 次释放。15 张状态图与 GPU 隐私通过；actor 42/material 78/texture 33 无增长，resource 145→123；600 帧 p95 4.268 ms／最大 49.9144 ms。8 分钟、12 局、重场与帧时门槛均未改变。
- 只缓存干净 CI 成功生成的 `.godot/imported`，键包括平台/架构、确切发行安装脚本哈希、全部素材/导入设置、project/export 配置与 addon 导入器源码；没有模糊恢复键、不缓存 editor/Mono/会话/令牌、不从本机旧导入目录播种。命中仍执行 Godot `--import`、资源审计、真实运行与导出隔离；不以 cache hit 代替验收。
- 最后实现 [CI run 33967734033](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33967734033) 绑定 `90e361eaafec8a6a57ddba3892cc37702719d90f`，GCC、Clang sanitizer、Windows/MSVC 和 macOS/ARM64 **4/4 成功**。Windows/macOS 均完成同提交 native、173 项托管、源码真实全动作、导出与 ZIP 往返、架构/许可证/MCP 隔离审计。

### 当前 CI 可玩候选包

两包均已从上述成功 run 下载到本机，并核对包内 `licenses/BUILD_INFO.txt` 的 `commit=90e361eaafec8a6a57ddba3892cc37702719d90f`。下列是**内层玩家包**摘要，不是 GitHub artifact 外层摘要：

| 平台 | 本机文件 | 字节数 | SHA-256 |
|---|---|---:|---|
| Windows x64 | `artifacts/ci-90e361e-windows/unpacked/SomeCardGameShit-product-playable-v1-windows-x86_64.zip` | 204,065,868 | `16DB9B1F64713FC917AC313209770E4BD5339AFF9847C52A4063FBDE72E01C98` |
| macOS ARM64 | `artifacts/ci-90e361e-macos/unpacked/SomeCardGameShit-product-playable-v1-macos-arm64.app.zip` | 189,962,347 | `C794FC153E39690D39EC7C1E861C94438BC2EACDEA9D08823F68C69A96C2582E` |

GitHub 可下载对应 [Windows artifact 9970226962](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33967734033/artifacts/9970226962) 与 [macOS artifact 9970200074](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33967734033/artifacts/9970200074)。macOS 内层 ZIP 保留可执行权限；不要在 Windows 解开 `.app` 再重新压缩。仅 ad-hoc 签名、没有 Developer ID/公证、没有物理 Mac 真人验收。

### 未关闭：远端硬件图形验收

[重型 run 33967734145](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/33967734145) 保留失败，完整下载证据在 `build/remote-product-heavy-90e361e`：

- 1280×720 的真实输入、15 状态和保护帧检查通过。
- 1600×900 确实在第 5 局 revision 19 获得 4＋4 重场，并完成 300 帧预热＋300 帧测量；不是未遇场面。actor 42/material 78/texture 33 无增长，resource 119→97，`zero_growth=true`；**p95=350.7066 ms、max=377.5467 ms**，超过既定 33.3/100 ms 门槛，因此失败。
- 2560×1440 与 2560×1600 在原 8 分钟整局时限内没有完成全动作覆盖。虽然已生成部分真实截图，不能当作这两个尺寸完整 CI 通过；渲染、截图与测试开销都计入总时间，未把超时笼统当作所有产品逻辑正常的证明。
- 日志实际适配器是 `ANGLE / Microsoft Basic Render Driver`。[微软文档](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/directx-warp) 明确 WARP 属于 CPU 软件光栅化；[GitHub 标准 runner 规格](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) 不提供该 Windows label 的硬件 GPU 承诺，GPU 属于另行配置的 larger runner。此处是验收环境假设缺口，不能说“远端显卡性能已验收”。
- 现有报告的 `display-gpu` 标记表示真实渲染最终画面而非 headless，并没有检测硬件适配器；CPU 软件渲染同样可能生成该标记。应与原始适配器日志合读，后续报告需增加适配器身份，不能把该字段作为硬件加速证明。
- 本机 NVIDIA RTX 4080 Laptop 的同 CI DLL 实测 p95 4.268 ms/max 49.9144 ms 和四尺寸实拍仍有效，但不是 GitHub 重型全绿的替代品。没有偷偷提高时限、放宽性能、关闭失败检查或拿旧 golden 覆盖。
- 后续需明确选择本机硬件图形验收与托管 CI 功能/打包分层，或另行授权具备 GPU 的执行环境。未开通付费 runner，未将个人电脑注册为公开仓库的自动执行器。
