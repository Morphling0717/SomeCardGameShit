# Godot 客户端架构

本文描述 Gate 3A 已建立的 Godot 4.7.2 .NET 桌面客户端边界。规则、费用、目标、胜负和隐私裁剪仍完全由 C++ 引擎负责；C# 与 Godot 只负责安全消费 `scgs_v04`。

## 分层

```text
Godot 场景与控件（net8.0，主线程）
              │ 只依赖 IScgsGameSession
              ▼
Scgs.Client（纯托管，net8.0 / net10.0）
              │ LibraryImport + cdecl + schema 1 JSON
              ▼
scgs_v04（C11 ABI 1.0，同提交构建）
              │
观看者安全 C++ API → Game / C++20 规则真值
```

Godot 工程不得引用引擎内部头、`PlayerState`、legacy YGOPro2 类型或自行复制规则判断。快照是状态真值；事件只用于日志和后续表现。

## 工具链与目标

- Godot 4.7.2 .NET，Compatibility renderer；
- `.NET SDK 10.0.400`，由根目录 `global.json` 精确锁定；
- Godot 程序集目标 `net8.0`；纯托管库同时生成 `net8.0` 与 `net10.0`，测试在 `net10.0` 执行；
- 正式桌面目标仅为 Windows x86-64 和 macOS arm64；不支持 Web，也不宣称 Linux 客户端支持。

## 原生库解析与暂存

原生库不提交 Git，必须由当前提交源码构建、审计后暂存：

```text
client/godot/native/windows-x86_64/scgs_v04.dll
client/godot/native/macos-arm64/libscgs_v04.dylib
```

托管 resolver 只加载调用方给出的绝对路径，拒绝相对路径、当前目录和 `PATH` 搜索，也拒绝非 Windows x64 / macOS arm64 进程。首次使用验证 ABI 为 `0x00010000`。

Windows 导出把 DLL 放在 EXE 同目录。Godot 4.7.2 官方 macOS template 仅提供 universal 引擎文件，因此 CI 从固定哈希的官方 archive 派生一个临时 arm64 release template；preset 仍以 arm64 导出，使 C# 只发布一套 osx-arm64 托管数据。派生 template 不提交且不修改官方缓存。导出后把 dylib 放在 `.app/Contents/Frameworks`，再进行 ad-hoc codesign。Windows 产品 DLL 默认使用静态 MSVC runtime；制品审计禁止 `MSVCP140*` 和 `VCRUNTIME140*` 导入。

导出包同时携带项目 GPL、Godot MIT 与完整 `COPYRIGHT.txt`、.NET 8.0.30 MIT 与第三方声明、nlohmann/json MIT、Noto OFL 和总第三方声明；审计会逐项检查固定标记与嵌入的 .NET runtime 版本。macOS 审计还会递归拒绝 bundle 内任何非 arm64-only 的 Mach-O 文件。

## 托管 ABI 边界

全部 14 个 `scgs_v04_*` 导出均以 `LibraryImport`、`cdecl` 和 ABI 固定宽度整数声明。字符串不会自动 marshal；输入先序列化为严格 UTF-8，输出由统一两段式 helper 读取。

边界规则：

- 输入最多 1 MiB；托管输出最多 16 MiB；
- 输出容量变化最多重试三次；
- 成功输出必须以一个 NUL 结尾，NUL 前内容必须是严格 UTF-8；
- 池化缓冲区归还前清零；
- native 失败后在同一线程立即读取 TLS `last_error` 并抛 `ScgsNativeException`；
- `start` 与 `submit` 的规则拒绝作为 `EngineStatus` 返回，不伪装成 native 异常；
- schema、结构和冻结结构枚举不兼容时抛 `ScgsProtocolException`；未知输出字段被忽略，未知 keyword bits 保留。

`ScgsV04SafeHandle` 保存完整 64 位 token，只有零值无效。销毁是幂等且不抛异常；离开比赛场景或重开前必须释放旧 session。

## 会话接口

Godot 只依赖 `IScgsGameSession`，其能力与 C ABI 一一对应：

```text
Start
GetView
ListLegalActions
ListValidTargets / ListValidSlots / ListValidDonors
PreviewPayment
GetReactionContext
SubmitCommand
ReadEvents
```

调用保持 Godot 主线程串行。每个 viewer 拥有独立 `ulong` 事件 cursor；读取一方事件不会推进另一方 cursor。每次成功命令后重新读取快照，不能以事件流推演权威状态。

## Gate 3A 场景

- `Bootstrap`：定位、加载并验证原生库；失败进入受控错误页；
- `MainMenu`：两席分别选择 `midrange` 或 `advance`；
- `Match`：承载可延续的战场区域并渲染第一张真实 viewer 快照；
- `PassDeviceOverlay`：完全不透明的换手遮挡，用户主动揭示后才允许请求新 viewer 数据。

产品启动省略 seed、随机决定先手并洗牌。CI smoke 使用固定 seed、强制 Player0 且关闭洗牌，验证场景 Label 确实来自 DTO，然后输出唯一成功标记并主动退出。

## 当前边界

Gate 3A 只读展示开局后的 Mulligan 快照，不提交调度或其他 `GameCommand`。调度、行动、响应换手、结算、结果和重开完整流程属于 Gate 3B。macOS CI 包仅做 arm64、ad-hoc 签名与 headless smoke；Developer ID、公证和物理 Mac 真人测试尚未完成。
