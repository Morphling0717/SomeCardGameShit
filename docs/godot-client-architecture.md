# Godot 客户端架构

本文描述 Gate 4A 的 Godot 4.7.2 .NET 桌面客户端边界。规则、费用、目标、响应与胜负仍完全由 C++ 引擎裁决；托管层只编排安全查询/规范命令，Godot 的默认 3D 与隐藏 legacy 2D presenter 都只把点击、拖拽和键盘输入转换成同一套 surface intent。

## 分层

```text
Godot Match 场景、presenter 与观看者 HUD（net8.0，主线程）
              │ 只依赖 HotseatUiState / surface intent
              ▼
Scgs.Hotseat（纯托管，net8.0 / net10.0）
              │ 只依赖 IScgsGameSession
              ▼
Scgs.Client（纯托管，net8.0 / net10.0）
              │ LibraryImport + cdecl + schema 1 JSON
              ▼
scgs_v04（C11 ABI 1.0，同提交构建）
              │
观看者安全 C++ API → Game / C++20 规则真值
```

`BootstrapController` 是组合根：它创建 `ScgsGameSession`，启动比赛，再把 `IScgsGameSession` 交给 `HotseatMatchController`。Godot 工程不得引用引擎内部头、`PlayerState`、legacy YGOPro2 类型，也不得复制费用、合法目标、响应资格或胜负判断。

## 工具链与目标

- Godot 4.7.2 .NET，Compatibility renderer；
- `.NET SDK 10.0.400`，由根目录 `global.json` 精确锁定；
- Godot 程序集目标为 `net8.0`；`Scgs.Client` 与 `Scgs.Hotseat` 同时生成 `net8.0` / `net10.0`，测试在 `net10.0` 执行；
- 正式桌面目标仅为 Windows x86-64 和 macOS arm64；不支持 Web，也不宣称 Linux 正式客户端支持。

所有托管项目提交 `packages.lock.json`，构建和 CI 必须使用 locked restore。

## 原生库解析与暂存

原生库不提交 Git，必须由当前提交源码构建、审计后暂存：

```text
client/godot/native/windows-x86_64/scgs_v04.dll
client/godot/native/macos-arm64/libscgs_v04.dylib
```

托管 resolver 只加载调用方给出的绝对路径，拒绝相对路径、当前目录和 `PATH` 搜索，也拒绝非 Windows x64 / macOS arm64 进程。首次使用验证 ABI 为 `0x00010000`。

Windows 导出把 DLL 放在 EXE 同目录。Godot 4.7.2 官方 macOS template 仅提供 universal 引擎文件，因此构建脚本从固定哈希的官方 archive 派生临时 arm64 release template；导出后把 dylib 放入 `.app/Contents/Frameworks`，再进行 ad-hoc codesign。Windows 产品 DLL 默认使用静态 MSVC runtime；制品审计禁止 `MSVCP140*` 和 `VCRUNTIME140*` 导入。

导出包同时携带项目 GPL、Godot MIT 与 `COPYRIGHT.txt`、.NET MIT 与第三方声明、nlohmann/json MIT、Noto OFL 和总第三方声明。macOS 审计递归拒绝 bundle 中任何非 arm64-only 的 Mach-O。

## `Scgs.Client`：ABI 与 DTO 边界

全部 14 个 `scgs_v04_*` 导出均以 `LibraryImport`、`cdecl` 和 ABI 固定宽度整数声明。字符串不自动 marshal；输入先序列化为严格 UTF-8，输出由统一两段式 helper 读取。

边界规则：

- 输入最多 1 MiB，托管输出最多 16 MiB，容量变化最多重试三次；
- 成功输出必须以一个 NUL 结尾，NUL 前内容必须是严格 UTF-8；
- 池化缓冲区归还前清零；
- native 失败后在同一线程立即读取 TLS `last_error` 并抛 `ScgsNativeException`；
- `start` 与 `submit` 的规则拒绝作为 `EngineStatus` 返回，不伪装成 native 异常；
- schema、结构和冻结结构枚举不兼容时抛 `ScgsProtocolException`；未知输出字段被忽略，未知 keyword bits 保留；
- 未知行动/事件值可降级展示，未知 player/phase/zone 等结构值必须拒绝；
- pending 响应必须带公开的 `ReactionOrigin`（行动、玩家、来源和可选目标），非 pending 响应不得带 origin。

`ScgsV04SafeHandle` 保存完整 64 位 token，只有零值无效。销毁幂等且不抛异常；返回菜单、错误恢复、重开或场景退出前都必须释放旧 session。

## `Scgs.Hotseat`：可替换的热座编排层

`Scgs.Hotseat` 不引用 Godot，公开 `HotseatMatchController`、`HotseatUiState` 和中文 `EngineCode` 格式化器。它把 `IScgsGameSession` 的快照、查询、命令和事件组织成以下稳定 UI 模式：

```text
Covered
MulliganSelecting → MulliganReview
Action
Reaction
Resolving
Finished
Faulted
Disposed
```

编排层遵守这些约束：

1. 根据当前 viewer 和 revision 枚举完整 `LegalAction` 集；来源、动作、目标、位置、组件来源与是否预支的逐步选择，只会缩小引擎返回的候选集。
2. UI 不能自行拼出“看起来合法”的命令；最终提交项必须与同 revision 的规范 `LegalAction` 完全相同。
3. 支付提示来自 `PreviewPayment`。预览只投影命令的费用与资源变化，不模拟卡牌效果或读取隐藏伏策；它不是通用确认页，实际提交仍由引擎再次验证。
4. `StaleRevision` 或查询 revision 变化会刷新快照并清空临时选择、旧高亮和旧支付预览，不自动重提命令。
5. 未知未来行动可以保留在通用展示中，但本客户端不会把不认识的行动当作可交互流程提交。

## 直接交互与规范命令

第一次点击来源只建立选择。若来源只有一种动作，直接进入下一必要步骤；若有多种动作，则在来源旁显示动作按钮。目标、组件、具体格位和预支选择都由同 revision 候选派生；完成最后一个必要选择后立即冻结规范命令，不再显示通用确认页。无目标且无其他选择的动作必须通过一次明确动作按钮提交；调度保留整批确认，投降保留二次确认，结束回合固定按钮直接执行。

点击与拖拽必须汇入同一 intent 和同一规范命令。拖拽不能绕过候选过滤、revision 或支付预览；无效拖放不调用 native。`StepBackSelection` 只撤销最近一个显式选择，完整取消才清空来源。

## 默认 3D 与 legacy 2D presenter

产品默认实例化 3D/2.5D 战场。旧 2D 战场只在启动参数精确包含 `--legacy-2d-board` 时实例化，用于源码级回归和故障定位；主菜单不暴露切换项，发布验收也不能用它替代默认 3D。

两种 presenter 遵守同一边界：

- 只读取 `HotseatUiState` 与已脱敏的 `HotseatPublicBoardView`，不持有 `IScgsGameSession`；
- 将命中结果映射为 `HotseatSurfaceRef`，交给 `HotseatSurfaceInteractionCoordinator` 生成点击/拖拽 intent；
- 不自行决定合法性、支付、目标、格位、组件或命令字段；
- 同 revision 的同一来源与目的地必须得到逐字段相等的 `GameCommandRequest`；
- `Covered`、`Resolving`、`Finished`、`Faulted` 和 `Disposed` 都拒绝空间输入。

默认 3D 使用 Compatibility renderer 下的固定透视相机：FOV 为 70°、俯角约 58°，并按当前 viewer 将己方一侧保持在近端。透视翻转只能在完全不透明遮挡中完成，不能在可见帧泄露下一位玩家的方向或对象。HUD 位于独立 `CanvasLayer`；鼠标先做 HUD hit-test，被 UI 消费时不得继续发射战场射线。空间点击/拖拽通过碰撞层命中 actor，移动达到 8 px 才进入拖拽，否则仍解释为点击。

3D 卡牌 actor 使用对象池以避免同 revision 的整场销毁/重建。actor 归还池时必须清空 Label、材质参数、tooltip、metadata、碰撞层/掩码、signal/callback、拖拽 token、候选状态与 DTO 引用；取出时只从本次安全状态重新赋值。匿名牌背不能携带 definition ID、instance ID 或稳定的可关联 metadata。

## Resolving 公共投影与交接遮挡

任何命令都采用“准备 → 中立投影 → 延迟提交”两阶段：

```text
可见选择
  → PrepareSelectedCommand（冻结规范命令，不调用 native）
  → Resolving（清除 viewer 私有对象并绘制中立公开战场）
  → 显示环境至少两次 FramePostDraw；headless 两次 process-frame 栅栏
  → SubmitPreparedCommand
  → 读取旧 viewer 的结果
  → 同一操作者继续，或 Covered(PassingDevice) 等待下一位主动揭示
```

`Resolving` 不是旧 viewer 快照的删字段副本。其公共 DTO 只保存公开资源、公开区域和手牌数量；双方手牌身份均不存在，所有背面伏策都没有 definition/instance ID、tooltip 或稳定 metadata。详情、日志、候选、高亮、输入回调和拖拽数据也必须在进入该状态时清空。规则拒绝会回到刷新后的旧 viewer 可见状态；native/协议错误会销毁敏感展示并进入受控错误页。

初始揭示和真实操作者变化使用完全不透明的 `Covered`。下一位主动揭示前，控制器不得为其调用 view、query 或 events；`Resolving` 不能替代这层物理隐私屏障。

调度提交后先向原 viewer 展示替换后的己方手牌（`MulliganReview`）。该 viewer 确认看完后，控制器才决定交给另一席或进入实际先手的行动阶段。

## 事件游标与 ACK

两名 viewer 各自持有独立 `ulong` cursor。`ReadEvents(viewer, after_sequence)` 是非破坏读取；控制器先把新事件放入 `PendingEvents`，Godot 完成日志渲染后才调用 `AcknowledgeEvents()` 推进该 viewer 的 cursor。

因此：

- 状态刷新不能提前吞掉尚未显示的事件；
- 换手不会推进另一方 cursor；
- 重读未 ACK 的批次是允许的；日志层替换当前批次，不能把同一 sequence 重复追加；
- 快照始终是状态真值，事件只用于日志和表现；
- 伏策设置、抽牌、调度等事件仍由 native 按 viewer 脱敏，隐藏文本不得包含卡名或稳定实例 ID。

## Godot 场景与控件

- `Bootstrap`：定位、加载并验证原生库，创建/释放 session，处理重开与受控错误；
- `MainMenu`：两席分别选择 `midrange` 或 `advance`，允许相同牌组；
- `Match`：渲染双方公开资源、战备、单位、策略、墓地/封存、己方手牌和对方牌背，并把点击/拖拽转交给热座控制器；
- `PassDeviceOverlay`：仅用于初次揭示和真实换手的完全不透明遮挡；
- `SnapshotSlot`：公开或脱敏卡位；隐藏卡绝不携带 definition/instance ID、tooltip 或可反推身份的 metadata；
- 调度、卡牌详情、上下文动作、居中响应、事件日志与投降确认控件：承载复杂选择与信息；右侧栏只作可折叠详情/日志，不再是主要操作入口；
- `ResultOverlay`、`ErrorOverlay`：终局的重开/返回菜单，以及可恢复或不可恢复错误。

产品启动省略 seed、随机决定先手并洗牌。测试路径可固定 seed、强制 Player0 且关闭洗牌。界面使用纯色几何和 DTO 派生的中文文本，不包含第二套正式卡牌表现 JSON。

## Gate 4A 边界

Gate 4A 在 Gate 3C 完整交互/隐私闭环上交付默认 3D/2.5D 占位战场、HUD/射线输入闸门、viewer 透视切换和安全 actor 池；legacy 2D 仅作隐藏回归。它不增加规则、卡牌、正式卡图、音效或复杂动画，也不改变 `scgs_v04` ABI/schema、14 个导出或 legacy v1 wire。自动化与导出验收状态以 [`../TEST_REPORT.md`](../TEST_REPORT.md) 为准，不以本文替代测试报告。

以下仍是发布标签前的硬门，不能因 headless 或 CI smoke 通过而省略：

- 在物理 Apple Silicon Mac 上启动、完成整局、退出并重开；
- 两名真人在目标桌面构建上完成热座整局并检查遮挡/交接。

Developer ID 签名、公证、正式美术/音效/复杂动画、触摸/手柄、主战技、普通主动能力、同时触发人工排序、Web 与 Linux 正式客户端不属于本 Gate。Gate 4A 的源码完成也不能替代 1600×900/1280×720 人工视觉遍历、两名真人热座或物理目标机器验收。
