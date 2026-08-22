# 工程交接：Gate 0+1+2+3A → Gate 3B 完整热座实现

> 现行交接文档。旧 [`DSH-HANDOFF.md`](DSH-HANDOFF.md) 与 [`ygopro-integration.md`](ygopro-integration.md) 是历史归档，不是执行指令。

## 0. 基线与当前边界

- 仓库：`Morphling0717/SomeCardGameShit`
- 起始基线：`main@cfdf695d70eeabcc6de9b094c94041364fb1335f`
- Gate 1：`codex/godot-hotseat-gate1@f048d11`
- Gate 2：`codex/godot-hotseat-gate2@8371427`
- Gate 3A 已验收尖端：`codex/godot-hotseat-gate3@5158409`
- Gate 3B 工作分支：`codex/godot-hotseat-gate3b`
- Gate 3B 被测实现：`9845a3fc89442e2f2066ae0265e8478e03b52632`
- Gate 3B 自动验收：GitHub Actions run [`32583321294`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32583321294)，4/4 jobs 全绿
- 规则真值：[`rules-v0.4.md`](rules-v0.4.md)，用户最新明确决定优先于旧文档歧义
- 客户端架构：[`godot-client-architecture.md`](godot-client-architecture.md)
- UI 状态：[`ui-state-map.md`](ui-state-map.md)
- 验收清单：[`hotseat-acceptance.md`](hotseat-acceptance.md)
- 真实构建/测试/CI：[`../TEST_REPORT.md`](../TEST_REPORT.md)

Gate 3B 在 Gate 3A 首帧之上接入完整调度、行动、响应、终局和重开路径，并增加独立 `Scgs.Hotseat` 编排层。本文描述源码职责和必须保持的约束；Windows/macOS 导出、ZIP 往返启动与四项 CI 已在上述实现提交通过，详细命令、数量、制品摘要和未完成边界只以 `TEST_REPORT.md` 为准。物理设备、双人热座和真实按钮交互硬门仍未完成。

本 Gate 不修改 legacy v1 wire 字节，不改变 `scgs_v04` ABI 1.0/schema 1/精确 14 导出，不提交原生 DLL/dylib，不创建 PR、不合并、不打标签。

## 1. 不可推翻的架构决定

```text
Godot 4.7.2 .NET Match/观看者 UI（net8.0，主线程）
        ↓ 只渲染状态并传递点击
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
- 响应栈按“反制 → 响应 → 原行动”LIFO；反制过牌不丢底层；法术声明支持 `OnSpellDeclared`。
- 支付前完整验证目标；响应中目标失效只跳过依赖该目标的效果。
- 每局至多一个 `MatchEnded`，终局后无抽牌、设施倒计时或其他状态变化。
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

## 3. Gate 3B 引擎/API 补强

- `PaymentPreview` 与实际支付共享费用投影，只描述 PP、容量、裂痕、进化能量与费用组成；它不结算效果，也不因隐藏伏策存在与否改变结果。
- 部署、进化和普通卡牌支付都走同一投影；提交时仍进行完整规则验证。
- `ReactionContext.origin` 在 pending 时提供公开原行动的 `action`、`player`、`source` 和可选 `target`，非 pending 时省略。
- 未知未来 origin action 可作为通用文本保留；player/target 等结构性未知值继续视为协议错误。

这些变化只扩展 schema 1 中原有 JSON 对象的可选/公开字段，不增加 C 导出，也不改变 legacy wire。

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

- `Covered`、`MulliganSelecting`、`MulliganReview`、`Action`、`Reaction`、`Finished`、`Faulted`、`Disposed`；
- 按当前 viewer/revision 获取安全快照与合法行动；
- 目标、位置、组件来源、预支的渐进候选过滤；
- 将唯一规范命令送入支付预览和确认；
- 两阶段遮挡提交与操作者路由；
- 两位 viewer 独立 cursor、`PendingEvents` 与渲染后 ACK；
- stale revision 清选重查、中文 engine code、协议/native 故障状态；
- dispose 旧 session。

## 5. 正常热座流程

```text
菜单选择两席牌组
→ create/start（产品随机 seed、随机先手、洗牌）
→ 完全遮挡“请交给玩家 0”
→ Player0 主动揭示并调度
→ 确认后先进入 ResolvingCommand 遮挡，Godot 延迟提交
→ Player0 查看自己的替换手牌并确认交接
→ Player1 主动揭示、调度、查看替换手牌
→ 交给实际先手
→ 行动/目标/位置/组件/预支选择
→ 显示引擎支付预览并确认
→ 遮挡后延迟提交
→ 同一玩家继续，或遮挡交给行动玩家/响应玩家
→ 伏策发动或不过，按 responder 继续安全交接
→ 生命归零、疲劳、投降或平局
→ 终局 overlay
→ 重开（先释放旧 session）或返回菜单
```

调度 review 是隐私流程的一部分：替换手牌只向刚提交调度的 viewer 展示。`CompleteMulliganReview()` 之后才允许切到下一席。

所有命令采用两阶段提交：`ConfirmSelection()` 只冻结规范命令并立即清空敏感状态，发布 `Covered(ResolvingCommand)`；Godot 完成遮挡绘制后才延迟调用 `SubmitPreparedCommand()`。操作者变化时不得在遮挡内偷读新 viewer。

## 6. Godot 场景与交互

主场景仍为 `Bootstrap`、`MainMenu`、`Match`、`PassDeviceOverlay`，并新增/接入：

```text
SnapshotSlot
MulliganPanel
ActionPromptPanel
CardDetailPanel
ConfirmationPanel
ReactionPanel
EventLogPanel
ResultOverlay
ErrorOverlay
MatchInteractionDock
```

`Match` 结构化渲染双方生命、PP、容量、裂痕、进化能量、牌组/手牌数量、战备、墓地/封存、5 个单位位、3 个策略位、己方真实手牌和对方无身份牌背。点击手牌、单位、策略、战备、主战者或空位只选择引擎候选；不直接修改战场。

响应页显示公开 origin、响应深度、responder、可发动伏策和“不过”。事件日志由 DTO 转为中文，并在完成渲染后 ACK；隐藏事件文本仍由 native 保证无卡名/稳定 ID。

界面继续使用 Compatibility renderer、1600×900 参考画布、1280×720 缩放、zh-CN、鼠标与 Esc。唯一二进制素材是 Noto Sans CJK SC 2.004 Regular（SHA-256 `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b`）；其余视觉为原创纯色几何和文字。

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
- 两个平台导出包携带 GPL、Godot/.NET/nlohmann/Noto 和第三方声明。
- smoke 必须有超时、只出现一次 `SCGS_GODOT_CI_SMOKE_OK`，并拒绝 Godot error/C# exception。
- Gate 3B 结构化报告使用固定字段白名单；`premature_view_calls` 必须为 0。
- zip 必须解包到新目录后重新审计并真实启动，不能只验证压缩前目录。

Gate 3B run `32583321294` 已验证唯一 marker、严格整局报告、Windows/macOS 导出及 ZIP 解包后再次审计/启动，并上传 Gate 3B 命名制品；真实 job、测试数量、artifact 大小/哈希和问题收口记录在 [`../TEST_REPORT.md`](../TEST_REPORT.md)。自动整局由 CI helper 注入合法行动，不等于 Godot Button signal E2E，也没有执行 `terminal-restart`。

## 8. 接手者必须完成的发布前硬门

源码、单元测试、headless smoke 和 CI 导出完成后，仍必须：

1. 在物理 Apple Silicon Mac 上启动 arm64 `.app`，完成整局、退出和重开；
2. 让两名真人在目标桌面构建完成一局，逐次观察全遮挡、设备交接和主动揭示；
3. 在未安装 Visual Studio 的 Windows x86-64 机器验证包可启动并完成整局；
4. 把人工发现的问题变成回归测试并重跑完整矩阵；
5. 以上完成后才允许标记 `v0.4-hotseat-alpha.1`。

不要从 CI runner 的 headless 成功推断物理 Mac 或双人热座已验收。

## 9. 延后项

主战技、普通主动能力、同时触发人工排序、固定牌组未使用关键词、正式卡图/音效/动画、独立正式表现 JSON、联机、录像、卡组编辑、Developer ID、公证、Web 与 Linux 正式客户端均延后。同一玩家同时触发暂按确定性场地顺序，是明确的 Alpha 限制。
