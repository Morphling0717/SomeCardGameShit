# Godot 客户端架构

本文描述 Gate 3B 的 Godot 4.7.2 .NET 桌面客户端边界。规则、费用、目标、响应与胜负仍完全由 C++ 引擎裁决；托管层只编排安全查询/命令，Godot 只渲染 DTO 并收集用户选择。

## 分层

```text
Godot Match 场景与观看者控件（net8.0，主线程）
              │ 只依赖 HotseatUiState / HotseatMatchController
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
Finished
Faulted
Disposed
```

编排层遵守这些约束：

1. 根据当前 viewer 和 revision 枚举完整 `LegalAction` 集；目标、位置、组件来源与是否预支的逐步选择，只会缩小引擎返回的候选集。
2. UI 不能自行拼出“看起来合法”的命令；最终提交项必须与同 revision 的规范 `LegalAction` 完全相同。
3. 支付确认来自 `PreviewPayment`。预览只投影命令的费用与资源变化，不模拟卡牌效果或读取隐藏伏策，实际提交仍由引擎再次验证。
4. `StaleRevision` 或查询 revision 变化会刷新快照并清空临时选择、旧高亮和旧支付预览，不自动重提命令。
5. 未知未来行动可以保留在通用展示中，但本客户端不会把不认识的行动当作可交互流程提交。

## 两阶段遮挡提交

任何命令都采用“准备 → 遮挡 → 延迟提交”两阶段：

```text
可见选择
  → ConfirmSelection（冻结规范命令，不调用 native）
  → Covered(ResolvingCommand)，清除快照/详情/敏感日志并绘制不透明遮挡
  → Godot 下一帧/延迟回调 SubmitPreparedCommand
  → 读取旧 viewer 的结果
  → 同一操作者继续，或 Covered(PassingDevice) 等待下一位主动揭示
```

这样即使结束回合、响应或调度会立刻改变操作者，也不会在遮挡完成前请求新 viewer。规则拒绝会回到刷新后的可见状态并显示中文错误；native/协议错误会销毁敏感展示并进入受控错误页。

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
- `Match`：渲染双方公开资源、战备、单位、策略、墓地/封存、己方手牌和对方牌背，并把点击转交给热座控制器；
- `PassDeviceOverlay`：初次揭示、换手和命令结算时的完全不透明遮挡；
- `SnapshotSlot`：公开或脱敏卡位；隐藏卡绝不携带 definition/instance ID、tooltip 或可反推身份的 metadata；
- `MulliganPanel`、`ActionPromptPanel`、`CardDetailPanel`、`ConfirmationPanel`、`ReactionPanel`、`EventLogPanel`：分别承载调度、合法行动/候选、卡牌详情、支付确认、伏策响应和观看者日志；
- `ResultOverlay`、`ErrorOverlay`：终局的重开/返回菜单，以及可恢复或不可恢复错误。

产品启动省略 seed、随机决定先手并洗牌。测试路径可固定 seed、强制 Player0 且关闭洗牌。界面使用纯色几何和 DTO 派生的中文文本，不包含第二套正式卡牌表现 JSON。

## Gate 3B 边界

Gate 3B 的代码范围覆盖双方调度、普通行动、目标/位置/组件/预支选择、支付确认、攻击、进化、部署、设施与伏策、反制/过牌、结束回合、投降、终局、返回菜单和重开。自动化与导出验收状态以 [`../TEST_REPORT.md`](../TEST_REPORT.md) 为准，不以本文替代测试报告。

以下仍是发布标签前的硬门，不能因 headless 或 CI smoke 通过而省略：

- 在物理 Apple Silicon Mac 上启动、完成整局、退出并重开；
- 两名真人在目标桌面构建上完成热座整局并检查遮挡/交接。

Developer ID 签名、公证、正式美术/音效、主战技、普通主动能力、同时触发人工排序、Web 与 Linux 正式客户端不属于本 Gate。
