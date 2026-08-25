# Gate 4B-R1 现代玻璃 HUD 热座验收

本清单保留 Gate 3B/3C/4A 与 Gate 4B-R1 的历史视觉契约，并把自动化结果与真人/物理设备验收分开记录。勾选只能依据同一提交的实现、测试日志或人工记录。Gate 4B-R1 的实现 run `32719076472` 与最终基线 `1370491` 的复验 run `32732554577` 均四项 CI 全绿；这些历史证据不替代 Gate 4B-R2 当前状态与第一次主观试玩，后者以现行 [`DSH-HANDOFF-v0.4-ui.md`](DSH-HANDOFF-v0.4-ui.md) 和 [`../TEST_REPORT.md`](../TEST_REPORT.md) 为准。

## 必须保留的 Gate 3B 基线

- [x] Godot 4.7.2 .NET、.NET SDK 10.0.400、locked restore 与 Windows x64/macOS arm64 导出链；
- [x] `Scgs.Client` 的 ABI 1.0/schema 1/14 导出、安全 JSON/UTF-8/handle 和 native/engine 错误分层；
- [x] `Scgs.Hotseat` 只依赖 `IScgsGameSession`，Godot 不复制规则；
- [x] 双方调度、行动、响应、终局/重开源码闭环和两位 viewer 独立事件 cursor/ACK；
- [x] 初始及换手完全遮挡，主动揭示前新 viewer 调用为零；
- [x] 自己手牌完整、对方手牌与背面伏策脱敏；
- [x] native、managed、Godot 整局、桌面导出、ZIP 往返审计与启动回归均继续保留。

Gate 3B 的历史通过不能证明 Gate 3C 的交互或公共投影安全；Gate 3C 必须在最终尖端重新跑完整矩阵。

## Gate 3C 源码契约

### 选择与命令

- [x] `HotseatUiMode.Resolving` 与 `Covered` 分离；选择公开 `HotseatSelectionStep` 和 `HotseatInteractionContext`；
- [x] 来源、动作、目标、格位、组件与预支只过滤同 revision 的引擎 `LegalAction`，不拼第二套命令；
- [x] 点击与拖拽进入同一 intent，最终得到逐字段相等的 `GameCommandRequest`；
- [x] 第一次点击来源不提交；单一动作直接进入下一选择，多动作才显示来源旁上下文按钮；
- [x] 最后一个必要目标/格位/组件选择后立即准备命令，不出现通用确认页；
- [x] 无目标动作要求明确动作按钮；调度整批确认、投降二次确认、结束回合直接执行；
- [x] `StepBackSelection` 逐个撤销显式步骤，自动补全不占历史；完整取消才清空来源；
- [x] 无效拖放不调用 native，不改变 revision、事件或游标；
- [x] 支付提示完全来自 `PreviewPayment`，预支/燃耗/容量/裂痕变化醒目但不追加确认。

### 战场输入与反馈

- [x] 手牌、场上单位/策略、战备、主战者与空位可直接点击或拖拽，高亮不只依赖颜色；
- [x] 攻击显示目标连线，放置显示幽灵牌，部署显示组件及成本；
- [x] 右栏只承载可折叠详情/日志，不再列出主行动流程；
- [x] 悬停显示详情，右键固定详情或在空白处回退，Esc 与键盘确认路径可用；
- [x] 响应以居中上下文层显示公开 origin、合法伏策/目标与“不过”。

### Resolving 与热座隐私

- [x] 命令准备后先绘制至少两个完整帧的 `HotseatPublicBoardView`，期间输入、ACK、重复提交和旧 revision 回调全部锁死；
- [x] 公共投影中双方手牌只有数量，所有背面伏策匿名，且没有详情、日志、tooltip、metadata、候选或私密回调；
- [x] `Resolving` 内 `Snapshot`、`Viewer`、`LegalActions` 与 `PendingEvents` 为空，不保留 viewer DTO/节点引用；
- [x] 同一操作者继续时才刷新原 viewer；操作者变化立即完全遮挡，新 viewer 主动揭示前调用为零；
- [x] engine 拒绝回到刷新后的原 viewer 状态，native/协议故障先清私密状态再进入受控错误页。

## 被测实现自动化

以下项目依据实现提交 `087d53a` 的本地输出与远端 run `32592594368` 勾选；包含本报告的文档尖端仍须由同一工作流再次全绿：

- [x] GCC Release、Clang ASan/UBSan、MSVC Release 与 AppleClang ARM64 四项原生矩阵全绿；
- [x] 2,048-seed Release、256-seed sanitizer、legacy wire/Python、精确 14 导出和 `git diff --check` 通过；
- [x] managed 测试覆盖每种动作的选择步骤、自动补全、逐步回退、规范命令收敛、支付与公共投影隐私；
- [x] Godot 通过真实控件 signal 覆盖调度、出牌、攻击、进化、部署、伏策发动/不过、结束回合、投降、终局和重开；
- [x] 点击/拖拽路径产生相同规范命令；最后必要选择后无通用确认；
- [x] 恶意私密 DTO 不能泄露到 `Resolving` 的截图、节点 metadata、tooltip 或回调；
- [x] Gate 3C schema v2 报告严格通过：`action_kinds` 为 0～10、`gate="3C"`、三项直接交互布尔为 true、`resolving_public_frames>=2`、两类泄露计数为 0、`restarts>=1`、`surrender_terminals>=1`、`disposed_sessions>=2`；
- [x] 当前工程、Windows/macOS 导出及 ZIP 解包后启动各只输出一次 `SCGS_GODOT_CI_SMOKE_OK`；
- [x] artifact 使用 `SomeCardGameShit-gate3c-windows-x86_64` 与 `SomeCardGameShit-gate3c-macos-arm64`，架构、native 布局、许可证和摘要已记录。

以上是 Gate 3C 的历史被测证据，不自动证明 Gate 4A 的 3D presenter、空间输入或 actor 池安全。

## Gate 4A 源码与报告契约

### 默认 3D 与共享操作

- [x] 无参数产品入口默认创建 3D/2.5D presenter；主菜单不提供 2D 切换；
- [x] 只有精确参数 `--legacy-2d-board` 创建 legacy 2D presenter，且该路径仍完成同一 signal full-match；
- [x] 3D 与 2D 只把 surface 映射为同一 `HotseatSurfaceIntent`，不各自维护合法性或命令拼装；
- [x] 手牌、主战者、双方单位/策略位与战备都有明确空间/HUD surface，点击与拖拽得到相同规范命令；法术必须落到己方具体空策略位，中央施放区不存在；
- [x] HUD 命中阻止 raycast，未达 8 px 仍按点击，非法落点不调用 native；
- [x] 固定 70° FOV、约 58° 俯角，当前 viewer 始终位于近端且透视切换发生在完全遮挡内；
- [x] `Covered`、`Resolving`、调度、终局、错误和销毁状态均拒绝空间输入。

### 空间隐私与对象池

- [x] 3D actor 归还池时清空 Label、材质参数、metadata、tooltip、碰撞层/掩码、signal/callback、拖拽 token 与 DTO 引用；
- [x] 匿名手牌/伏策 actor 不含 definition ID、instance ID、卡名或稳定可关联 metadata；
- [x] 恶意私密哨兵无法进入 `Resolving` 的 3D 场景树、材质、碰撞、tooltip、metadata 或回调；
- [x] 显示环境的公共投影至少经过两次 `FramePostDraw` 才提交，headless 专用回退经过两次 process-frame 栅栏；
- [x] 透视重建和 actor 池复用不会短暂显示前一 viewer 的私密数据。

### Gate 4A 自动化（已执行）

- [x] schema v3 报告严格继承 Gate 3C 全部字段及整局约束，`gate="4A"`、`action_kinds` 为 0～10、两类私密泄露计数均为 0；
- [x] 默认 3D 报告验证 surface/raycast、HUD 拦截、8 px、70°/58°、透视重建、actor 池复用与锁定状态空间输入；
- [x] legacy 2D 报告验证共享 surface intent，且不伪报任何 3D raycast/镜头/对象池证据；
- [x] Windows/macOS 各运行默认 3D 当前工程、导出、ZIP 往返和一次 legacy 2D 源码整局，共八次唯一成功标记；
- [x] `BUILD_INFO.txt` 精确标识 Gate 4A；artifact 使用 `SomeCardGameShit-gate4a-windows-x86_64` 与 `SomeCardGameShit-gate4a-macos-arm64`；
- [x] 最终实现提交与包含实测报告的分支尖端均完成四项 CI；该轮 run、job、字节与 digest 记录在对应被测提交中的历史 `TEST_REPORT.md`。

## Gate 4B-R1 产品视觉与自动化（已完成）

- [x] 主菜单使用产品化导航壳；本地热座可进入独立对局设置，未接入功能只显示“开发中”且不创建 session、不调用 native；
- [x] 设置可持久化窗口/无边框全屏、四档窗口尺寸、四档 UI 缩放、VSync 与减少动画，非法配置回退默认值；
- [x] 普通对局采用现代玻璃 HUD：左侧自适应卡牌详情抽屉、右上双方悬浮状态舱、战场上沿阶段胶囊，以及靠近己方区域的结束回合按钮；正常产品状态不存在全高不透明黑栏；
- [x] 调度、响应、战备、暂停、日志、结果和错误界面统一使用圆角玻璃弹层；`Covered` 仍是唯一完全不透明的换手遮挡；
- [x] `Resolving` 只消费 `HotseatPublicBoardView`，清除 viewer 私密状态与输入，并在公共投影完整绘制两帧后提交；11 个 `ActionKind` 与既有热座隐私状态机未改变；
- [x] 两张临时头像只按对局设置中的公开牌组映射；相同牌组允许相同头像，未知牌组使用中立 fallback，不读取私密 DTO；
- [x] 视觉清单严格覆盖 34 项素材：29 张独立临时卡图、通用正面、统一卡背、菜单背景和两张临时头像；路径、用途与 SHA-256 均有唯一记录；
- [x] display-backed visual suite 以固定对局捕获菜单、设置、`Covered`、调度、行动、来源、格位/目标、响应、`Resolving`、结果和错误共 11 类状态；1280×720、1600×900、2560×1440 与 2560×1600 的布局、文字、归属和隐私结构契约均通过；
- [x] 1600×900 感知式 golden 只能由显式工具和人工批准更新；CI 不会自动覆盖 PNG 或批准视觉变化；
- [x] 最大场面性能 smoke 先预热 300 帧再测量 300 帧，测量期 actor/material/texture 数不得增长；硬件渲染执行 p95 ≤ 33.3 ms、单帧 < 100 ms，已识别的纯软件 renderer 只豁免 GPU 时间阈值，11 状态功能、截图、布局、隐私、600 帧和资源零增长仍全部强制；
- [x] 默认产品入口保持 3D；`--legacy-2d-board` 仅是隐藏功能回归后门，不承诺视觉等价，也不出现在玩家菜单。

Gate 4B-R1 的实现提交四项 CI 已绿；包含本文修改的最终文档尖端仍须按既有矩阵复验，准确 run、断言数量和制品摘要只写入 `TEST_REPORT.md`。

## Gate 4B-R3.1 未批准视觉切片

- [x] 普通启动和 Gate 4B-R2 schema 4/golden 仍使用 `Gate4BR2`，候选只能由 `--r3-visual-slice` 显式启动；
- [x] 80×60 工业地面在四角延伸到镜头外，没有周界框、半场色板、有限地坪黑边或平铺的候选图；
- [x] 空位只保留浅凹槽/短角标，机械位于逻辑对局 footprint 外；场内头像终端只读取公开牌组身份；
- [x] 近端手牌为相机相对弧线，费用/身材与卡面使用真实深度遮挡，悬停/选择不存在跨卡串字；
- [x] 1600×900 三态实拍来自同一真实 native session、两次真实调度和 revision 2 的 17 个合法行动；
- [x] 报告固定 `pending_user_approval`，记录 `[0,1,0]` viewer 顺序、零提前读取、共享隐藏牌背、稳定手牌 Transform/FramePostDraw，并绑定 commit、双素材清单、地坪、GLB、shader 与 launcher 的 SHA-256；
- [x] P0 revision-0 调度前向真实 `MatchScreen` 注入恶意私密 sentinel；独立 `privacy-resolving` / `privacy-covered` 证据图、节点清理检查和覆盖快照/查询/事件的 viewer read 总计数证明实际迁移零泄露，detector 自测不冒充真实取证；
- [ ] 实现与最终文档尖端四项 CI 全绿，正式 Windows EXE/ZIP 往返候选实启和制品摘要已写入 `TEST_REPORT.md`；
- [ ] 用户双击导出包中的 `PLAY_R3_VISUAL_SLICE.cmd` 并明确批准这套构图、配色、手牌、主战者和 HUD；批准前不得推广到默认产品与完整状态。

## 发布标签前仍未完成的三项硬门

- [ ] 物理 Apple Silicon Mac 完成整局、退出和重开；
- [ ] 未安装 Visual Studio 的 Windows x86-64 机器启动导出包并完成整局；
- [ ] 两名真人完成一局并确认空间拾取/拖拽可理解、HUD 不误穿透、公共结算不泄露、每次实际换手完全遮挡。

三项全部完成并把发现的问题纳入回归后，才允许创建 `v0.4-hotseat-alpha.1` 标签。

## Gate 4B-R1 之外

当前 29 张卡图、统一卡背、菜单背景、两张头像和卡框均为可替换的原创临时素材，不等同于最终发布美术。正式卡图/卡框/Logo、音效/音乐、大型特效与复杂动画、触摸/手柄、主战技、普通主动能力、同时触发人工排序、固定牌组未使用关键词、独立正式表现 JSON、Developer ID 签名/公证、Web/Linux 正式客户端、联机、录像和卡组编辑均延后。同一玩家同时触发继续使用确定性场地顺序，并作为 Alpha 限制公开记录。
