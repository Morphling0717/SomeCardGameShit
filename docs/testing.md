# 测试说明

## 测试层次

### C++ 规则与状态机

覆盖固定牌组规则、失败无副作用、每步不变量、结束回合、响应栈、目标失效、终局幂等、进化充能和先后手种子。测试名称和准确断言数以本次运行输出及 [`TEST_REPORT.md`](../TEST_REPORT.md) 为准。

### 客户端查询契约

客户端 API 必须验证：

- 每项枚举出的合法行动在同 revision 的游戏副本上可以成功提交；
- 支付预览与实际资源变化一致；
- 非法玩家、过期 revision、非法目标和错误阶段无状态/事件/revision 副作用；
- 敌方手牌、背面伏策和抽牌/调度/设置事件不泄露；
- 两个事件游标互不干扰；
- 无界面代理只经快照、查询、命令和事件完成整局，不读取 `PlayerState`。

### C ABI 契约

`scgs_native_api_contract`、纯 C consumer 与动态加载测试必须覆盖：

- C11/C++20 均可包含公开头，导出表不包含 C++ 符号；
- ABI/schema 不匹配、非法 UTF-8/JSON/枚举、空或过期 handle；
- 每个输出的 `NULL + 0`、容量不足、精确容量、NUL 与无部分写入；
- native 状态和规则状态分离，失败命令保持状态、事件和 revision 不变；
- C ABI 与直接 C++ 在相同 seed、相同命令流下语义一致；
- 敌方手牌、背面伏策和隐藏事件不泄露，两个 viewer 游标互不干扰；
- 仅通过 C ABI 的代理完成固定牌组整局。

动态加载测试必须从实际 `scgs_v04.dll` / `libscgs_v04.so` / `libscgs_v04.dylib` 查找符号，不能只静态链接 import target。安装后 smoke 还要从暂存目录编译独立 consumer，以验证交付包而非构建树偶然可用。

### C# ABI 消费边界

纯托管测试必须覆盖 14 个签名与冻结枚举、optional JSON omission、未知字段兼容、结构性枚举拒绝、未知事件/行动降级、未知 keyword bits 保留、严格 UTF-8、两段缓冲增长/NUL/短写/上限、TLS last-error、native/engine 错误分层、SafeHandle 单次销毁、两个 viewer cursor、Windows/macOS 已知原生库布局，以及揭示前零次 viewer 调用。`Scgs.Hotseat` 还要覆盖调度替换手牌 review、来源/动作/目标/格位/组件/预支步骤、逐步回退、点击与拖拽的规范命令收敛、支付一致性、`Resolving` 公共投影、响应换手、stale revision、渲染后 ACK 和 dispose。Godot 项目同时构建 Debug（编辑器/当前工程 smoke）与 Release（发布编译基线），两者都必须零警告。

同提交动态库集成测试必须完成 ABI 检查、create/start、双 viewer 快照、全部查询 wrapper、事件脱敏、revision 和 dispose；固定牌组/先手矩阵还要自然完成整局并聚合成功提交全部 11 个 `ActionKind`。测试不能读取 `PlayerState`，也不能用模拟 DTO 代替这项集成验证。

### Godot 与桌面导出

Godot headless 验证项目 import、无警告 C# build、场景/节点路径、原生加载和完整热座状态机。CI smoke 固定 seed、强制 Player0、关闭洗牌；它必须通过真实控件 signal 经调度 review、直接出牌/攻击/进化/部署、伏策发动/不过、交接遮挡和事件 ACK 自然完成第一局，再从结果页真实重开并以投降结束第二局，而不是向控制器注入最终 `LegalAction`。Gate 3C 报告使用 schema version 2 严格字段白名单，覆盖 `ActionKind` 0–10，并强制 `signal_e2e=true`、点击/拖拽命令一致、最后必要选择后无通用确认、每次 `Resolving` 公共投影最少两个完整帧、零私密泄露、零提前 viewer 调用、至少一次重开/投降终局和至少两次 session 释放；成功标记必须恰好出现一次。

schema version 2 的字段只能是：`schema_version`、`gate`、`scenario`、`seed`、`player0_deck`、`player1_deck`、`first_player`、`steps`、`turns`、`action_kinds`、`covers`、`reveals`、`premature_view_calls`、`signal_e2e`、`click_drag_canonical_parity`、`selection_commit_without_confirmation`、`resolving_public_frames`、`resolving_private_leaks`、`restarts`、`surrender_terminals`、`result`、`disposed_sessions`。`resolving_public_frames` 是所有命令中观察到的完整公共投影帧数最小值，不是累计帧数；因此 `>=2` 证明没有单条命令绕过绘制屏障。

**Gate 4A full-match schema version 3** 把 Gate 3C 的全部字段和不变量原样继承：报告仍须覆盖 `action_kinds` 0～10、真实 signal 两局闭环、点击/拖拽一致、无通用确认、至少两次公共投影完整绘制、零提前 viewer 调用、零 `resolving_private_leaks`、重开、投降终局和两次 session 释放。新增字段只能是：`presentation_mode`、`surface_intent_e2e`、`raycast_e2e`、`hud_raycast_blocks`、`drag_threshold_pixels`、`camera_fov_degrees`、`camera_pitch_degrees`、`perspective_rebuilds`、`actor_pool_reuses`、`blocked_spatial_inputs`、`spatial_private_leaks`。Gate 4B-R2 仍以这套报告作为完整对局功能回归。

当前默认 3D full-match 必须报告 `presentation_mode="3d"`、`surface_intent_e2e=true`、`raycast_e2e=true`、`hud_raycast_blocks>=1`、`drag_threshold_pixels=8`、`camera_fov_degrees=58`、`camera_pitch_degrees=58`、`perspective_rebuilds>=1`、`actor_pool_reuses>=1`、`blocked_spatial_inputs>=1` 与 `spatial_private_leaks=0`。validator 仅为已归档 Gate 4A 报告保留旧镜头字段兼容，新的 Gate 4B-R1 产品证据必须使用 58°/58°。用精确参数 `--legacy-2d-board` 启动的源码回归必须报告 `presentation_mode="legacy-2d"`、`surface_intent_e2e=true`、`raycast_e2e=false`、`spatial_private_leaks=0`，其余 3D 专属计数/常量字段为 0。Gate 3B、Gate 3C 与 Gate 4A validator/CTest 必须并存，后续 Gate 不能降低历史报告契约。

Windows/macOS job 还必须实际导出并启动产物；只在编辑器运行不算通过。当前产品在每个平台保留四次 Gate 4A full-match：默认 3D 当前工程、隐藏 legacy 2D 当前工程、默认 3D 导出、默认 3D ZIP 往返。Windows 审计 DLL 与 EXE 同目录、x86-64 和静态 CRT；macOS 审计 arm64、`Contents/Frameworks`、ad-hoc codesign 与执行权限。两者的 `BUILD_INFO.txt` 都必须精确标识 `SomeCardGameShit Gate 4B-R2`、锁定工具链和当前 CI checkout commit。压缩后必须解包、重新审计并再次启动。每次 full-match smoke 的外部上限为 180 秒，日志不得含 C# exception 或 Godot error；唯一成功标记 `SCGS_GODOT_CI_SMOKE_OK` 必须恰好出现一次。

### Gate 4B-R2 视觉、素材与性能契约

**Gate 4B-R2 visual-suite schema version 4** 是 display-backed 截图/性能报告，不是上述 Gate 4A full-match schema version 3；字段白名单、场景证据和 validator 完全独立。视觉报告必须捕获且仅捕获原有 11 种产品状态 `menu`、`match-setup`、`covered`、`mulligan`、`action`、`source-selection`、`slot-or-target-selection`、`reaction`、`resolving`、`result`、`error`，以及 `hand-one`、`hand-five`、`hand-ten`、`hand-hover`、`field-readability`，共 16 种。每张截图必须等待连续两个内容一致的 `FramePostDraw`，记录 state/viewer/revision/viewport/资产清单哈希、桌面/双方主战者/手牌/HUD 像素锚点与区域哈希，并扫描 GPU 最终画面中的恶意洋红私密纹理哨兵。

Windows 在 1280×720、1600×900、2560×1440 和 2560×1600 四种真实窗口尺寸运行套件。四种尺寸都严格检查控件不越界/重叠、战场占物理安全矩形至少 92% 宽/78% 高、调度手牌/托盘分离、无全高不透明黑栏、隐藏调试文本和隐私。费用、攻击、生命和倒计时还必须同时提供真实 `Label3D`、高于底板至少 0.012 世界单位的深度证据和最终 GPU 徽章 ROI。1600×900 只与人工批准、显式更新的 committed golden 比较；macOS 保留资源、结构、ARM64、签名与真实启动检查，不与 Windows 做跨平台像素 golden。

Gate 4B-R2 冻结产品集仍要求 `CardVisualCatalog` 对 29 个 definition 全覆盖、路径和卡图唯一，卡背、菜单背景、fallback 正面与两张头像可加载，共 34 项；`ASSET_MANIFEST.json` 必须保持这组冻结内容及其已批准哈希。同牌组双席、未知牌组 fallback 和所有隐藏牌共享同一卡背也必须验证。未批准的 R3.1 地坪只登记在 `arena/R3_ASSET_MANIFEST.json`。联合审计要求主清单 34 项、候选清单 1 项，跨清单无重复并与实际 35 个 PNG/WebP/SVG 一一对应，所有 SHA-256 逐项匹配。

性能 smoke 固定为 300 帧预热 + 300 帧测量；预热后 actor/material/texture 计数不得增长，无论渲染器类型都不能豁免。报告记录 `adapter_name`、`adapter_type`、`timing_budget_applicable`、p95 与 max。硬件适配器要求 p95 不高于 33.3 ms、max 低于 100 ms；只有 CPU 或名称明确为 Microsoft Basic Render Driver、llvmpipe、SwiftShader/software renderer 的设备可以 `timing_budget_applicable=false`。这仅不应用 GPU 时间阈值，仍要求 16 状态、功能/隐私、600 帧和资源零增长全部通过。

### Gate 5A 产品牌组设计契约

`design/product-decks-v1/card-pool.lock.json` 是尚未落地运行时的产品设计真值，必须通过 `card-pool.schema.json` 约束的结构和 `scripts/ci/validate_product_decks_v1.py` 的跨字段语义校验。清单状态只能是 `locked_not_implemented`，使用字符串设计编号，不能偷渡 C++ `CardId`、数字枚举或“已经可玩”的声明。

设计契约至少拒绝以下漂移：两副主牌不是各 30 张／15 种、同名超过 3 张、职业混入错误、四种共享中立不齐、战备不是各四张唯一牌、曜誓伏策投入不是两张、渊契出现伏策、34 种可构筑定义或一个衍生物数量不符、引用悬空、构筑／战备出现 0 费、恢复当前 PP、额外战备次数、自身检索循环，以及无隙／负契时序、裂痕读取上限、混合五格、独立场地和关键词语义缺失。除此之外，校验器对元数据、规则、职业、主战者、卡牌类型、关键词、能力目录、衍生物、34 张构筑卡、精确牌表、38 项视觉计划、纸面平衡目标、美术方向和旧产品迁移策略分别计算规范化 JSON SHA-256；即使总数仍合法，偷改单卡数值／文本／系列、对调投入张数、能力状态或视觉映射也必须失败并指出漂移分区。

这项测试只证明锁定文件内部一致且没有破坏现有工程。T2 职业内行动概率、T6/T10 连动可见率、曜誓 2～4／渊契 5～8 的裂痕峰值、两职业预支／修复范围、48～52% 胜率和赢家自身 T10～12 中位数都标记为未实测设计目标；必须等下一 Gate 真实实现、互换先后手模拟和真人对局后才能填写实测结果。

可独立运行：

```bash
python scripts/ci/validate_product_decks_v1.py
python -m unittest scripts.tests.test_validate_product_decks_v1
```

### legacy 兼容性

`scgs_wire_frozen_golden` 固定验证 v1 消息长度、字节序、消息 ID 和金标字节。历史命名的 `SCGS_ENABLE_LEGACY_YGO2_TESTS` 当前控制整组 Python CTest：legacy overlay/协议、原生/Godot 制品与“R2 34 项＋R3 候选 1 项”联合视觉素材审计、子进程超时、Gate 3B/3C/4A full-match、Gate 4B-R2 visual-suite/golden、独立 R3.1 候选切片契约，以及不依赖运行时的 Gate 5A 产品牌组设计契约。它默认开启；开启时 CMake 必须找到 Python 3.10+，不能静默只注册部分测试。关闭会跳过整组 Python 契约，因此不能用于客户端 Gate 验收。

### Gate 4B-R3.1 候选切片契约

R3.1 使用单独的 schema 1，不修改 Gate 4B-R2 schema 4 或已批准 golden。Windows display-backed 运行必须固定 1600×900、seed `0xC0DEC0DE`、Player0 先手且不洗牌，通过真实 `IScgsGameSession` 和热座控制器完成双方调度，最终到达 Player0 revision 2 且至少有一个真实 `LegalAction`。

报告的产品画面集合只能包含 `action-idle`、`hand-hover`、`source-selected` 三态，并固定 `approval_status=pending_user_approval`；隐私取证另写出 `privacy-resolving` 与 `privacy-covered` 两张证据图，不把它们冒充产品状态。报告 provenance 必须绑定 checkout commit/source/dirty 状态、冻结 R2 主清单、独立 R3 候选清单、地坪、GLB、shader 与 launcher 的 SHA-256，不能把两份素材清单拼成会改变 R2 golden 的总清单。每个产品态在安全 FX 和手牌 Transform 收敛后再等待连续两个稳定 `FramePostDraw`；三张产品 PNG 必须互不相同，也不能在画面上缘两角露出有限地坪的黑色外缘。

隐私证据必须在 P0 revision-0 的真实调度提交前向当前 `MatchScreen` 注入恶意私密 sentinel，再实际经过 `Resolving → Covered`。两张证据图都必须没有 sentinel 像素；actor 的文字、metadata、身份材质、碰撞、drag token、tween 和 callback 必须已清除。独立计数器覆盖 `GetView`、合法行动/目标/格位/组件/支付/响应查询以及事件读取/游标读取；两个状态前后的 viewer-scoped read 总数与快照数都必须分别保持不变。detector 自测与这次真实注入要在报告中分开记录。初始完全遮挡、viewer 请求顺序 `[0,1,0]`、零提前 view 调用和对手手牌只用共享卡背仍是强制条件。正式 Windows EXE 与 ZIP 往返包必须由打包后的 `PLAY_R3_VISUAL_SLICE.cmd` 再执行相同切片，证明动态加载的 tscn、GLB、shader、纹理、双素材清单和 launcher 都进入导出。

legacy 测试通过只证明历史兼容层仍可解析，不代表 YGOPro2/Unity 是现行客户端或已经实机可用。

### 压力与 sanitizer

默认压力矩阵为 Release 2,048 seeds 和 Clang ASan/UBSan 256 seeds：

```bash
./scripts/stress.sh
```

可用 `SCGS_RELEASE_STRESS_SEEDS`、`SCGS_ASAN_STRESS_SEEDS` 调整。常规烟雾 seed 数可用 `SCGS_SMOKE_SEEDS` 调整。

## 标准命令

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

git diff --check
```

Windows MSVC 使用 `scripts/test.ps1` 或等价的 Release 配置。CI 在 GCC Release、Clang ASan/UBSan、MSVC Release 和 macOS ARM64 Release 四个 job 中固定 Python 版本，并显式设置 `SCGS_ENABLE_LEGACY_YGO2_TESTS=ON`。每个平台还安装并审计原生库，上传仅供 CI 验收的暂存 artifact。

Linux 两个 job 保持纯原生。Windows 与 macOS job 在原生安装审计之后追加 locked managed restore/build/test、等待冷资源扫描完成的 Godot `--import`、默认 3D 与 legacy 2D 源码 smoke、目标平台默认 3D 导出、导出包启动与 ZIP 往返审计。Windows 另跑四尺寸 display-backed Gate 4B-R2 visual suite、1600×900 golden 和 600 帧性能/资源验证；macOS 从已校验的官方 universal template 临时派生 arm64 release template，并要求最终 bundle 只有一套 arm64 托管数据且所有 Mach-O 均为 arm64-only。这不构成 Web 或 Linux 客户端支持声明。

Gate 4B-R1 实现由 GitHub Actions run `32719076472` 验证，最终 R1 基线 `1370491` 又由 run `32732554577` 复验；这些历史 run 都不能证明 Gate 4B-R2。R2 实现尖端 `cca04b5` 已由 run `32766050188` 的四项完整矩阵验证。R3.1 被测实现尖端 `3d4012f` 又由 run `32808917410` 验证：四项 job 全绿，Windows 源码、正式 EXE 与 ZIP 内 launcher 候选实启，R2 四尺寸 schema 4/golden 回归不变。精确 job、测试数量、截图取证和制品 digest 记录在 [`TEST_REPORT.md`](../TEST_REPORT.md)。包含报告的后续文档尖端仍必须在自身 commit 上复跑，不能沿用实现尖端 run 冒充通过。

## 报告规则

[`TEST_REPORT.md`](../TEST_REPORT.md) 只记录实际执行过的分支、commit、环境、命令、测试/断言数和结果。不得把以下内容写成已通过：

- 未推送分支的 GitHub CI；
- 当前机器无法运行的编译器或 sanitizer；
- 未在对应提交上实际导入/构建的 Godot 工程；
- 未实际运行的 Godot 当前工程、桌面导出或真人完整对局；
- Web、网络、平衡或正式美术。

测试绿代表已覆盖范围内没有已知失败，不等于 Alpha 全产品验收完成。
