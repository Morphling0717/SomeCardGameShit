# 工程交接：Gate 4B-R2 战斗表现与第一次实机验收

> 现行交接文档。旧 [`DSH-HANDOFF.md`](DSH-HANDOFF.md) 与 [`ygopro-integration.md`](ygopro-integration.md) 是历史归档，不是执行指令。

## 0. 基线与当前边界

- 仓库：`Morphling0717/SomeCardGameShit`
- 起始基线：`main@cfdf695d70eeabcc6de9b094c94041364fb1335f`
- Gate 1：`codex/godot-hotseat-gate1@f048d11`
- Gate 2：`codex/godot-hotseat-gate2@8371427`
- Gate 3A 已验收尖端：`codex/godot-hotseat-gate3@5158409`
- Gate 3B 已验收尖端：`codex/godot-hotseat-gate3b@dd38e93`
- Gate 3B 被测实现：`9845a3fc89442e2f2066ae0265e8478e03b52632`
- Gate 3B 自动验收：GitHub Actions run [`32583321294`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32583321294)，4/4 jobs 全绿
- Gate 3C 已验收尖端：`codex/godot-hotseat-gate3c@a29dd14`
- Gate 3C 被测实现：`087d53a5dad3285478e78381914d34acfcaa79f3`；自动验收 run `32592594368`
- Gate 4A 自动化验收尖端：`codex/godot-hotseat-gate4a@7a6808ddcd76d2c78fd906a9235f867c11c84e7c`
- Gate 4A 实现 CI：[run `32617860778`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32617860778)，四项 job 全绿；该轮准确命令、数量和制品摘要见对应被测提交中的历史 `TEST_REPORT.md`
- Gate 4A.1 起始基线：`codex/godot-hotseat-gate4a-layout-fix@0d1d4e5`
- Gate 4A.1 被测实现：`codex/godot-hotseat-gate4a-spell-slots@4be6e09ef9edc363b064b4a7aaba4551359ecb05`
- Gate 4A.1 实现 CI：[run `32696171327`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32696171327)，四项 job 全绿；该轮准确命令、数量和制品摘要见对应被测提交中的历史 `TEST_REPORT.md`
- Gate 4B-R1 被测实现：`codex/godot-hotseat-gate4b-visual-baseline@01a9bb33c7cff148e49067b8bd43ab1e973ea600`
- Gate 4B-R1 实现 CI：[run `32719076472`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32719076472)，四项 job 全绿；R1 最终基线 `1370491` 随后由 [run `32732554577`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32732554577) 再次 4/4 jobs 全绿复验。两者都是 R1 历史证据，不能替代本轮 R2 run
- Gate 4B-R2 起始基线：`codex/godot-hotseat-gate4b-visual-baseline@1370491ade6e779d83fa44334dd4b7e6920f6a9c`
- Gate 4B-R2 主要实现：`codex/godot-hotseat-gate4b-r2-battle-presentation@19159ee0613159e4761bbf2f9acea77efdd82874`
- Gate 4B-R2 被测实现尖端：`cca04b5c9a0e4793c98d8f765527a7a1c51de804`
- Gate 4B-R2 实现 CI：[run `32766050188`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32766050188)，四项 job 全绿；准确测试、视觉和制品摘要见 [`../TEST_REPORT.md`](../TEST_REPORT.md)
- 规则真值：[`rules-v0.4.md`](rules-v0.4.md)，用户最新明确决定优先于旧文档歧义
- 客户端架构：[`godot-client-architecture.md`](godot-client-architecture.md)
- UI 状态：[`ui-state-map.md`](ui-state-map.md)
- 验收清单：[`hotseat-acceptance.md`](hotseat-acceptance.md)
- 真实构建/测试/CI：[`../TEST_REPORT.md`](../TEST_REPORT.md)

Gate 4A 保留 Gate 3C 的完整对局、直接交互、公共投影、原生与导出基线，只把战场表现升级为默认 3D/2.5D，并保留隐藏 legacy 2D 回归。Gate 4A.1 进一步把法术发动改为占用玩家明确选择的己方空策略位，并从两种 presenter 中移除中央施放区。Gate 4B-R1 在这些基线上交付产品菜单、设置、现代玻璃 HUD、34 项临时视觉目录和严格视觉/隐私/性能自动化；Gate 4B-R2 把己方手牌迁移到相机相对前景架，修复真实费用/身材/倒计时徽章，稳定镜头、安全区、战场托座和 HUD 信息架构。11 个 `ActionKind` 与热座隐私状态机保持不变。本文描述源码职责和必须保持的约束；详细命令、数量、制品摘要和未完成边界只以 `TEST_REPORT.md` 为准。

Gate 4A.1 修改 C++ `CastSpell` 规则与强类型 `Game::cast_spell` 签名；Gate 4B-R1/R2 只修改客户端表现与自动验收。后两轮不修改 legacy v1 wire 字节，不改变 `scgs_v04` ABI 1.0/schema 1/精确 14 导出，不提交原生 DLL/dylib，不创建 PR、不合并、不打标签。

## 1. 不可推翻的架构决定

```text
Godot 4.7.2 .NET 默认 3D / hidden legacy 2D（net8.0，主线程）
        ↓ 只渲染状态并传递 surface intent
Scgs.Hotseat（net8.0 + net10.0，无 Godot 依赖）
        ↓ 只依赖接口
IScgsGameSession / Scgs.Client（net8.0 + net10.0）
        ↓ LibraryImport + cdecl，14 个导出
scgs_v04 C11 + schema 1 JSON
        ↓
客户端安全 C++ API
        ↓
Game / C++20 规则引擎（唯一规则真值）
```

- Godot/C# 不复算费用、合法目标、伤害、触发顺序、响应资格或胜负。
- Godot 不读取 `PlayerState`，不直接链接 C++ 类型，也不消费 legacy YGOPro2 wire。
- UI 的完整命令必须来自同 revision 的 `LegalAction`；渐进选择只能过滤引擎候选。
- 快照是状态真值；事件只用于日志/表现，每位 viewer 的事件 cursor 独立。
- 正式桌面目标仅 Windows x86-64 与 macOS Apple Silicon；不支持 Web 或 Linux 正式客户端。
- 工具链锁定 Godot 4.7.2 .NET、.NET SDK 10.0.400、CMake 3.25+。
- YGOPro2/Unity 已停止投入；overlay、upstream 和远端 M1 分支只作历史参考。

## 2. 已冻结的 Gate 0+1+2 契约

### 规则与观看者安全

- 结束回合顺序是“结束效果 → 清临时状态 → PP 清零并发事件 → `TurnEnded` → 对方回合”。
- 响应栈按“反制 → 响应 → 原行动”LIFO；反制过牌不丢底层；法术必须正面占用己方明确选择的空策略位并支持 `OnSpellDeclared`，在自身链环结算后送墓。
- 支付前完整验证目标；响应中目标失效只跳过依赖该目标的效果。
- 每局至多一个 `MatchEnded`，终局后无抽牌、设施倒计时或其他状态变化。
- 致命响应或响应期间投降时，已声明但未结算的伏策按 LIFO 清理、原法术随后送墓；未声明伏策保留，`MatchEnded` 唯一且最后。
- 进化解锁前不职业充能；先手解锁得 2、后手得 3；解锁后充能封顶 4。
- 产品默认随机先手并洗牌；测试可指定 seed/先手。快照和开局事件记录实际结果。
- 成功命令 revision 恰好 +1；失败命令不改变状态、事件或 revision。
- 自己手牌完整；对方手牌只有数量；对方背面伏策没有 definition/instance ID；公开区域保持公开。
- `read_events(viewer, after_sequence)` 非破坏读取，两位 viewer 游标互不消费。

### ABI

- `engine/include/scgs/native_api_v04.h` 固定 ABI 1.0、schema 1、14 个导出、固定宽度整数和 64 位 token。
- 两段式输出所需长度含尾随 NUL，容量不足不部分写；native failure 与规则 `EngineStatus` 分离，异常不得跨 C 边界。
- `SCGS_ENABLE_LEGACY_YGO2_TESTS` 默认开启；开启时必须找到 Python 3.10+，不得静默少跑。
- legacy v1 wire 的 ID、字段顺序、长度、字节序和金标保持不变。

## 3. Gate 3B API 基线与 Gate 4A.1 CastSpell 增量

- `PaymentPreview` 与实际支付共享费用投影，只描述 PP、容量、裂痕、进化能量与费用组成；它不结算效果，也不因隐藏伏策存在与否改变结果。
- 部署、进化和普通卡牌支付都走同一投影；提交时仍进行完整规则验证。
- `ReactionContext.origin` 在 pending 时提供公开原行动的 `action`、`player`、`source` 和可选 `target`，非 pending 时省略。
- 未知未来 origin action 可作为通用文本保留；player/target 等结构性未知值继续视为协议错误。
- `GameCommand.slot` 对 `CastSpell` 是必填语义；合法行动按“己方空策略位 × 合法目标 × 预支选择”展开，后排全满时不枚举法术。
- 待结算法术在观看者快照中正面公开，并只允许占据响应栈记录的唯一策略位；自身链环结束或终局清理时送墓。

Gate 3B 的 origin 等增量只扩展 schema 1 既有 JSON 对象；Gate 4A.1 只收紧现有 `slot` 字段和 C++ 规则语义。两者都不增加 C 导出、不提升 schema 版本，也不改变 legacy wire。

## 4. 托管项目

```text
client/
├─ Scgs.Client/                 ABI、DTO、session（net8.0 + net10.0）
├─ Scgs.Hotseat/                热座状态机/编排（net8.0 + net10.0）
├─ Scgs.Client.Tests/           单元与同提交真实 native 集成（net10.0）
└─ godot/                       Godot 桌面层（net8.0）
```

全部项目使用 SDK 10.0.400、warnings-as-errors、确定性构建和 committed lock file。

`Scgs.Client` 继续负责：

- `LibraryImport` + `cdecl` 的 14 个绑定；
- 绝对路径、目标架构和 ABI handshake；
- 64 位 `ScgsV04SafeHandle`；
- 1 MiB 输入、16 MiB 输出、最多三次增长、严格 UTF-8、尾随 NUL 和清零池化缓冲；
- schema 1 强类型 DTO、unknown-field 兼容与结构枚举拒绝；
- native exception 与 `EngineStatus` 分层。

`Scgs.Hotseat` 负责：

- `Covered`、`MulliganSelecting`、`MulliganReview`、`Action`、`Reaction`、`Resolving`、`Finished`、`Faulted`、`Disposed`；
- 按当前 viewer/revision 获取安全快照与合法行动；
- 来源、动作、目标、位置、组件来源、预支的上下文候选过滤与逐步回退；
- 点击/拖拽 intent 收敛到同一唯一规范命令，并取得支付提示；
- 中立公开 `Resolving` 投影、延迟提交与操作者路由；
- 两位 viewer 独立 cursor、`PendingEvents` 与渲染后 ACK；
- stale revision 清选重查、中文 engine code、协议/native 故障状态；
- dispose 旧 session。

## 5. 正常热座流程

```text
菜单选择两席牌组
→ create/start（产品随机 seed、随机先手、洗牌）
→ 完全遮挡“请交给玩家 0”
→ Player0 主动揭示并调度
→ 确认调度后进入中立公开 Resolving，Godot 延迟提交
→ Player0 查看自己的替换手牌并确认交接
→ Player1 主动揭示、调度、查看替换手牌
→ 交给实际先手
→ 点击或拖拽来源，在战场选择动作/目标/位置/组件/预支
→ 最后一个必要选择完成后准备规范命令（无通用确认页）
→ 绘制至少两帧中立公开 Resolving 投影后延迟提交
→ 同一玩家继续，或遮挡交给行动玩家/响应玩家
→ 伏策发动或不过，按 responder 继续安全交接
→ 生命归零、疲劳、投降或平局
→ 终局 overlay
→ 重开（先释放旧 session）或返回菜单
```

调度 review 是隐私流程的一部分：替换手牌只向刚提交调度的 viewer 展示。`CompleteMulliganReview()` 之后才允许切到下一席。

所有命令采用两阶段提交：`PrepareSelectedCommand()` 只冻结规范命令并清空 viewer 私密状态，发布仅含公开信息的 `Resolving`；显示环境经过至少两个 `FramePostDraw`（headless 使用两次 process-frame 栅栏）后才调用 `SubmitPreparedCommand()`。同一操作者继续时刷新原 viewer，操作者变化时转入完全不透明的 `Covered(PassingDevice)`，不得偷读新 viewer。

## 6. Godot 场景与交互

主场景仍为 `Bootstrap`、`MainMenu`、`Match`、`PassDeviceOverlay`。默认由 3D presenter 绘制战场；只有精确参数 `--legacy-2d-board` 启用旧 2D presenter，且主菜单不得提供该切换。legacy 2D 是隐藏功能回归路径，不承担产品视觉等价承诺。

`MainMenu` 已是产品壳：本地热座可用，单人挑战、在线对战、牌组编辑、卡牌图鉴和录像回放只显示“开发中”且不得创建 session。独立对局设置允许两席分别选择 `midrange` 或 `advance`，包括相同牌组；设置实际持久化窗口/无边框全屏、四档窗口尺寸、四档 UI 缩放、VSync 与减少动画，非法配置回退默认值。原生库不可用时完整菜单仍可显示，但本地热座必须禁用并给出受控错误。

Gate 3B 的 `ActionPromptPanel` / `ConfirmationPanel` 场景可以暂留作源码兼容，但 Gate 3C 的常规行动不得再经它们完成；专用调度与投降确认不受此限制。

`Match` 结构化渲染双方生命、PP、容量、裂痕、进化能量、牌组/手牌数量、战备、墓地/封存、5 个单位位、3 个策略位、己方真实手牌和对方无身份牌背。3D 与 legacy 2D 都把命中映射为 `HotseatSurfaceRef`，再由共同协调器产生选择 intent 并过滤引擎候选，不直接修改战场。法术必须先选择己方空策略位；中央施放区已退役。单一动作自动进入下一必要步骤，多动作才在来源旁弹出按钮；无效拖放原位回弹且不调用 native。

Gate 4B-R2 的 3D 相机继续使用 58° 基准 FOV、约 58° 俯角，但 `BattlefieldViewportLayout` 按 1280/1600/2560 宽固定 240+196、288+240、320+264 px 左右安全区，详情、日志或 HUD 显隐不得再引起镜头缩放“呼吸”。`BattlefieldHandRig` 把己方 1～10 张手牌置于屏幕下方相机相对弧线，对方手牌使用上方匿名共享卡背架；滚轮只调整桌面，不改变前景手牌屏幕尺寸。当前 viewer 在近端且透视只在完全遮挡内重建。HUD hit-test 优先于空间 raycast；移动达到 8 px 才成为拖拽。`Covered`、`Resolving`、调度、终局、错误和销毁状态均锁死空间输入。actor 归还池时必须清空文字、材质、tooltip、metadata、碰撞、signal/callback、tween、拖拽 token 和 DTO 引用。

现代玻璃 HUD 不再使用左右全高黑栏：左侧为 248～320 px 自适应卡牌详情抽屉；右上是双方悬浮状态舱；阶段位于战场上沿玻璃胶囊；结束回合按钮靠近己方区域；暂停和日志使用紧凑图标入口。两张临时头像只按对局设置中的公开牌组映射，相同牌组允许相同头像，未知牌组使用中立 fallback，不读取 viewer 私密 DTO。

调度使用底部居中的玻璃卡牌托盘；响应使用紧凑玻璃浮窗，需要目标时关闭浮窗并返回 3D 战场；战备、暂停、日志、结果和错误页使用同一圆角玻璃系统。`Covered` 仍完全不透明；`Resolving` 只消费安全公共投影，清除 viewer 私密 HUD/actor 数据并在完整绘制两帧后提交。

界面继续使用 Compatibility renderer、1600×900 参考画布、最低 1280×720、16:9～16:10 适配与 zh-CN；正式输入范围为鼠标及 Tab/方向键、Enter/Space、Esc。费用、攻击、生命与倒计时必须来自独立真实 `Label3D`/徽章，文字平面高于底板至少 0.012 世界单位并接受正常深度测试；场上卡不显示长名称。视觉清单严格包含 34 项：29 张独立临时卡图、通用正面、统一卡背、菜单背景和两张临时头像；Noto Sans CJK SC 2.004 Regular 仍按其 OFL 单独分发。当前插画、卡背、头像与卡框都是可替换临时素材，不是最终发布美术，本轮也没有音效或音乐。

## 7. 暂存、导出与自动验收

原生库必须来自同提交源码，二进制不提交：

```text
编辑器 Windows: client/godot/native/windows-x86_64/scgs_v04.dll
编辑器 macOS:   client/godot/native/macos-arm64/libscgs_v04.dylib
导出 Windows:   DLL 与 EXE 同目录
导出 macOS:     .app/Contents/Frameworks/libscgs_v04.dylib
```

- Windows 产品 DLL 使用 `/MT`，审计禁止动态 MSVC runtime。
- macOS CI 使用派生 arm64 template，放置 dylib 后重新 ad-hoc codesign，所有 Mach-O 必须 arm64-only。
- 两个平台导出包携带 GPL、Godot/.NET/nlohmann/Noto、临时生成素材声明和第三方声明；`BUILD_INFO.txt` 必须精确记录锁定工具链和当前 CI checkout commit。
- smoke 必须有超时、只出现一次 `SCGS_GODOT_CI_SMOKE_OK`，并拒绝 Godot error/C# exception。
- Gate 4A/4A.1 结构化整局报告沿用 schema version 3 固定字段白名单；它完整继承 Gate 3C 的 `ActionKind` 0～10、真实 signal 两局、选择、公共投影、viewer 和释放约束，并新增 presentation、surface/raycast、HUD、8 px、透视、actor 池、锁定输入与空间隐私证据。
- zip 必须解包到新目录后重新审计并真实启动，不能只验证压缩前目录。

Gate 3C run `32592594368` 与 Gate 4A run `32617860778` 只是历史回归基线。Gate 4A.1 实现已由 [run `32696171327`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32696171327) 在四项 job 中验证严格 v3 报告：Windows/macOS 各运行默认 3D 当前工程、默认 3D 导出、默认 3D ZIP 往返和一次 legacy 2D 源码整局，并上传沿用名称的 `SomeCardGameShit-gate4a-*` 制品。其 checkout SHA 必须是 `4be6e09ef9edc363b064b4a7aaba4551359ecb05`；后续任何代码尖端都必须重新满足同一矩阵，不能沿用本次 run 冒充新提交证据。

Gate 4B-R2 使用独立 schema 4 visual suite：保留历史 11 状态并增加 `hand-one`、`hand-five`、`hand-ten`、`hand-hover` 与 `field-readability`，共 16 类状态；每张截图必须等待连续两个内容一致的 `FramePostDraw`，并验证全帧、桌面、主战者、手牌、HUD 与费用/攻击/生命/倒计时的最终 GPU ROI。四种尺寸仍为 1280×720、1600×900、2560×1440 与 2560×1600。1600×900 感知式 golden 只能显式更新并经人工批准；CI 不自动覆盖。素材审计要求 34 项唯一记录，身份清单用 `.gitattributes` 锁定 LF，避免 Windows checkout 改变冻结 SHA-256。最大场面性能 smoke 预热 300 帧、测量 300 帧，测量期 actor/material/texture 数零增长，硬件渲染预算为 p95 ≤ 33.3 ms、单帧 < 100 ms；已识别纯软件 renderer 只豁免 GPU 时间阈值，16 状态功能、截图、布局、隐私、600 帧和资源零增长仍全部强制。

Gate 4B-R2 导出制品使用 `SomeCardGameShit-gate4b-r2-windows-x86_64` 与 `SomeCardGameShit-gate4b-r2-macos-arm64`，Windows 另上传四尺寸 visual-suite。实现尖端 `cca04b5` 的 run `32766050188` 已 4/4 jobs 全绿并上传 7 个制品；包含本轮文档修改的最终分支尖端仍须重新满足完整矩阵，不能沿用该 run 冒充文档尖端通过。

## 8. 接手者必须完成的发布前硬门

Gate 4B-R2 的视觉、隐私、资源、性能与第一次实机包自动交付已经完成，但以下四项硬门仍必须由真实证据关闭：

1. 用户在当前 Windows 实机上完成第一次主观试玩并反馈画面与操作；
2. 在物理 Apple Silicon Mac 上启动 arm64 `.app`，完成整局、退出和重开；
3. 在未安装 Visual Studio 的 Windows x86-64 机器验证导出包可启动并完成整局；
4. 让两名真人在目标桌面构建完成一局，逐次观察完全遮挡、设备交接、主动揭示、交互可理解性和公共结算隐私。

人工发现的问题必须转为回归并重跑完整矩阵；四项全部完成后才允许标记 `v0.4-hotseat-alpha.1`。不要从 CI runner 的 headless 或 display-backed 成功推断主观体验、物理 Mac、干净 Windows 或双人热座已验收。

## 9. 延后项

主战技、普通主动能力、同时触发人工排序、固定牌组未使用关键词、最终发布卡图/卡框/Logo、音效/音乐、复杂动画、独立正式表现 JSON、联机、录像、卡组编辑、Developer ID、公证、Web 与 Linux 正式客户端均延后。同一玩家同时触发暂按确定性场地顺序，是明确的 Alpha 限制。
