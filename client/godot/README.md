# Godot 热座客户端与 Gate 6A AnimeV1 样片

此目录是 Godot 4.7.2 .NET、`net8.0` 的桌面热座客户端。产品默认使用 authored 3D/2.5D 战场、相机相对前景手牌架、稳定安全区 HUD、临时卡图/头像目录和完整主菜单外壳；legacy 2D 仅保留为隐藏回归路径。法术没有中央施放区，必须点击或拖到当前玩家自己的具体空策略位；三个位置全满时不可施法。Godot 层引用：

- `../Scgs.Client/Scgs.Client.csproj`：安全 ABI、DTO 与 `IScgsGameSession`；
- `../Scgs.Hotseat/Scgs.Hotseat.csproj`：无 Godot 依赖的热座状态机与命令编排。

两个纯托管项目同时生成 `net8.0` / `net10.0`；Godot 使用 `net8.0`，测试使用 `net10.0`。C++ 引擎仍是费用、目标、响应、状态和胜负的唯一规则真值。

Gate 6A 另有独立 `--anime-style-slice`：它不加载 native、不创建对局，只用于审批 AnimeV1 菜单、主战者、卡牌、开放式竞技场、2.5D 手牌、混合永久物、响应、交接和结果画面。AnimeV1 是整个最终游戏的唯一视觉目标；当前 Gate 4B 科幻客户端只是过渡默认和历史回归路径，不能理解为长期双主题支持。新誓卫／契术牌组尚未接入 Godot，样片不能用于规则试玩。

## 本地运行

必须使用根目录 `global.json` 锁定的 .NET SDK 10.0.400 与 Godot 4.7.2 .NET。将当前提交构建并审计后的动态库放入：

- `native/windows-x86_64/scgs_v04.dll`
- `native/macos-arm64/libscgs_v04.dylib`

也可通过环境变量 `SCGS_NATIVE_LIBRARY`，或 Godot 用户参数 `--native-library=<绝对路径>` 指定库。只接受绝对路径；不会搜索当前目录或任意 `PATH`。随后用 Godot 4.7.2 .NET 打开本目录。

启动页会创建并立即释放一个未开局 session，以验证动态库、ABI 1.0 与 schema 1。预检失败时不会开始比赛或读取任何玩家快照。正式客户端目标仅为 Windows x64 与 macOS arm64；不支持 Web，也不承诺 Linux 正式客户端。

不传表现参数时始终创建默认 3D presenter。只有测试或排障时可以传入精确参数 `--legacy-2d-board`；它不会出现在主菜单，也不能用于替代 3D 产品验收：

```text
godot --path client/godot -- --legacy-2d-board --native-library=<绝对路径>
```

无需原生库查看 AnimeV1 样片：

```text
godot --path client/godot -- --anime-style-slice
```

Windows 导出包可双击 `PLAY_ANIME_STYLE_SLICE.cmd`，macOS 包可双击或执行 `PLAY_ANIME_STYLE_SLICE.command`。自动捕获使用 `--anime-style-slice=<绝对输出目录> --anime-style-slice-exit`；可再用 `--anime-style-state=<state>` 指定 `menu`、`setup`、`action`、`hand-hover`、`mixed-permanents-field`、`reaction`、`covered` 或 `result`。逐项来源、完整 prompt 和哈希见 `assets/visual/anime_v1/slice/PROVENANCE.md` 与 `ASSET_MANIFEST.json`。

## 产品菜单与设置

原生库可用时，“本地热座”进入独立对局设置页，两席可各自选择 `midrange` 或 `advance`，也允许同牌组对局。单人挑战、在线对战、牌组编辑、卡牌图鉴和录像回放是产品化占位入口：它们只显示“开发中”说明，不创建 session，也不访问 native。原生库不可用时菜单仍可打开，仅禁用本地热座并显示受控错误。

设置保存到 `user://settings.cfg`，实际支持窗口/无边框全屏、1280×720 / 1600×900 / 1920×1080 / 2560×1440 窗口尺寸、90% / 100% / 110% / 125% UI 缩放、VSync 和减少动画。未知或非法配置会归一到默认值。

## 对局流程

1. 两席分别选择 `midrange` 或 `advance`（允许相同牌组）；
2. 产品配置省略 seed、随机决定先手并洗牌；
3. create/start 后先显示完全不透明的“请交给玩家 0”；
4. 玩家主动揭示后才请求其观看者快照；
5. 双方依次选择调度牌，提交后先查看自己的替换手牌，再遮挡交接；
6. 点击或拖拽手牌/场上来源，直接选择高亮的目标、具体格位或部署组件；复杂分支与响应使用贴近来源的上下文按钮；
7. 操作者变化时重新完全遮挡，下一位主动揭示前不读取其 view/query/events；
8. 终局可重开或返回菜单；重开会先释放旧 session。

目标、格位或组件是命令选择的一部分：最后一个必要选择完成后直接提交，不再出现通用确认页。无目标动作必须再按一次明确的动作按钮；调度保留一次整批确认，投降保留二次确认，结束回合直接执行。

命令准备后进入不可交互的 `Resolving`，显示环境先经过至少两次 `FramePostDraw`（headless 使用两次 process-frame 栅栏）的中立公开战场，再延迟提交。此投影不引用 viewer 快照：双方手牌仅保留数量，所有背面伏策统一匿名，详情、日志、tooltip、metadata、候选和回调全部清除。只有初始揭示与实际操作者变化才进入完全不透明的 `Covered`；下一位主动揭示前不得读取其 view/query/events。

鼠标操作同时支持“点击来源 → 点击目的地”和拖拽。拖拽只是同一 intent 的快捷方式，不能产生不同命令；无效拖放原位回弹且不调用 native。Esc 或右键空白回退一个显式选择步骤，再次取消才清空来源；悬停显示详情，右键可固定详情。

3D presenter 使用 58° FOV、约 58° 俯角和 8 px 拖拽阈值。`BattlefieldViewportLayout` 固定左右 HUD 安全区，详情或日志显隐不再使镜头缩放“呼吸”；桌面滚轮缩放也不改变相机相对 `BattlefieldHandRig` 的屏幕卡高。HUD 位于独立 `CanvasLayer`：左侧是可收窄的卡牌详情抽屉，右上是两个独立玩家状态舱，阶段胶囊、结束回合、暂停和日志都是紧凑悬浮控件，不保留全高黑色侧栏。单一 `BackBufferCopy` 与共享 screen-reading CanvasItem shader 为顶层面板提供统一皮肤；`Covered` 仍保持完全不透明。

命中 HUD 时不得继续发射空间射线；viewer 透视只在完全遮挡内切换。卡牌 actor 归还池时清除文字、卡图/材质参数、tooltip、metadata、碰撞、signal/callback、tween 和拖拽 token，防止跨 viewer 复用泄露；隐藏 actor 只能绑定共享卡背。

## 事件与隐私

- Player0 与 Player1 各有独立事件 cursor；
- `ReadEvents` 非破坏读取，事件完成日志渲染后才调用 ACK；
- 对手手牌只显示数量与无身份牌背；
- 对手背面伏策不保存 definition/instance ID、卡名 tooltip 或稳定 metadata；
- `Resolving` 公共投影不包含任何 viewer 私有对象；完全遮挡会进一步清除全部可见战场并等待主动揭示；
- 快照是状态真值，事件只用于日志/表现。

## CI smoke

基础入口使用真实原生库、固定 seed、强制 Player0 且关闭洗牌：

```text
godot --headless --path client/godot -- --ci-smoke --native-library=<绝对路径>
```

成功时只允许输出一次 `SCGS_GODOT_CI_SMOKE_OK` 并以 0 退出；缺库、错架构、ABI/schema 不符、C# exception 或 Godot error 必须失败。严格 **Gate 4A full-match schema v3** 报告继承 Gate 3C 的全部真实 signal 两局闭环、11 种 `ActionKind`、选择、隐私、重开、投降和释放约束；默认 3D 还验证真实 surface/raycast、HUD 拦截、58°/58° 镜头、8 px 阈值、透视重建、actor 池复用、锁定输入和零空间私密泄露。

两平台 CI 都运行默认 3D 当前工程、默认 3D 导出与默认 3D ZIP 往返；另各运行一次隐藏 legacy 2D 源码回归：

```text
godot --headless --path client/godot -- --ci-smoke --legacy-2d-board \
  --native-library=<绝对路径> --ci-report=<绝对路径>
```

legacy 报告必须证明仍经过共享 surface intent，同时不伪报 raycast、镜头、透视或 actor 池证据。真实结果以根目录 `TEST_REPORT.md` 为准。

本地视觉验收使用带显示后端的默认 3D 产品路径：

```text
godot --path client/godot --windowed --audio-driver Dummy -- \
  --ci-smoke --native-library=<绝对路径> \
  --ci-visual-suite=<绝对输出目录> \
  --ci-visual-viewport=1600x900
```

**Gate 4B-R2 visual-suite schema v4** 与 Gate 4A full-match schema v3 是完全独立的报告和 validator。视觉套件保留原有 11 种产品状态，并新增 `hand-one`、`hand-five`、`hand-ten`、`hand-hover` 和 `field-readability`，共 16 种状态；每张截图等待连续两个内容一致的 `FramePostDraw`，并记录桌面、主战者、手牌、HUD 及费用/攻击/生命/倒计时的真实像素证据。四种验收尺寸为 1280×720、1600×900、2560×1440、2560×1600；1600×900 golden 只能通过显式更新脚本替换并经人工审阅。

同一套件运行 300 帧预热 + 300 帧测量；预热后 actor/material/texture 数必须零增长。报告必须写入 `adapter_name`、`adapter_type` 和 `timing_budget_applicable`。普通硬件适配器要求 p95 不高于 33.3 ms 且单帧低于 100 ms；CPU、Microsoft Basic Render Driver、llvmpipe、SwiftShader 或明确 software renderer 只豁免 GPU 时间阈值，仍必须完成全部功能、隐私、600 帧和资源零增长检查。

`--ci-screenshot=<绝对 PNG 路径>` 仍可与 `--ci-smoke` 一起截取恶意私密哨兵被清除后的首个 `Resolving` 完整绘制帧。默认 headless smoke 等待两个 process-frame 栅栏，不生成截图。

## 素材与许可证

视觉目录的 Gate 4B-R2 产品集为 29 个冻结 definition 各一张唯一原创临时卡图，加统一卡背、16:9 菜单背景、未知卡通用正面和 `midrange` / `advance` 两张头像，共 34 项；它们继续由冻结的 `assets/visual/ASSET_MANIFEST.json` 独立记录和供 R2 golden 哈希引用。R3.1 尚未批准的工业竞技场地坪单独登记在 `assets/visual/arena/R3_ASSET_MANIFEST.json`。联合审计要求两个清单合计 35 项，并要求每个新增 PNG/WebP/SVG 有且只有一条记录。未知 definition 使用无身份通用正面，所有隐藏牌共享同一卡背，不绑定 definition-specific 纹理。

Noto Sans CJK SC 2.004 Regular 的许可证、NOTICE 和 SHA-256 在 `assets/fonts/` 中。桌面导出附带项目 GPL、Godot、.NET runtime、nlohmann/json、Noto、`ASSET_NOTICES.md` 与第三方声明；finalize 与制品审计会强制检查。

Gate 4B-R2 聚焦手牌、数值徽章、镜头与战场/HUD 构图的第一轮实机验收，不包含最终商业卡图、音效/音乐、大型演出、独立正式表现 JSON、触摸/手柄、联机、Developer ID 签名/公证、Web 或 Linux 正式客户端。精细模型、机械场地材质、完整响应链/动作演出与菜单统一延后到后续视觉轮次；主战技、普通主动能力和同时触发人工排序也仍延后。

## Gate 4B-R3.1 候选实机切片

R3.1 不会在普通启动时替换 R2。开发树可用显示后端运行真实 1600×900 session：

```text
godot --path client/godot --windowed --resolution 1600x900 -- \
  --r3-visual-slice=<绝对输出目录> \
  --native-library=<scgs_v04.dll 的绝对路径> \
  --ci-visual-viewport=1600x900
```

不带输出值的 `--r3-visual-slice` 会写入 `user://r3-visual-slice` 并保持窗口打开；自动化只有显式增加 `--r3-visual-slice-exit` 才会在三张产品实拍、`privacy-resolving` / `privacy-covered` 两张取证图和报告写完后退出。隐私取证会在真实 revision-0 调度前注入恶意 sentinel，验证两态节点清理、GPU 零泄露及 viewer read 计数不增长。

Windows 制品名为 `SomeCardGameShit-gate4b-r3-visual-slice-windows-x86_64.zip`。必须先完整解压，再双击 ZIP 根目录的 `PLAY_R3_VISUAL_SLICE.cmd`；直接双击 `SomeCardGameShit.exe` 仍会进入默认 R2，不会启用候选。采集 READY 标记出现前会阻止 Esc/返回菜单释放真实 session，完成或失败时都恢复用户原有 VSync 模式；READY 后窗口保持可操作。CI 也必须实际经由打包后的脚本启动，而不是绕过它直启 EXE。导出包同时包含冻结 R2 主清单和独立 R3 候选清单，报告还绑定 commit、地坪、GLB、shader 与 launcher 的 SHA-256，并固定为 `pending_user_approval`；在用户批准前，不能用它覆盖 Gate 4B-R2 golden 或改成默认产品画面。
