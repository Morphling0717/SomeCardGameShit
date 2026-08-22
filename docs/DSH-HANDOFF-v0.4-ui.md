# 工程交接：Gate 0+1+2+3A → Godot 热座首帧完成

> 现行交接文档。旧 [`DSH-HANDOFF.md`](DSH-HANDOFF.md) 与 [`ygopro-integration.md`](ygopro-integration.md) 是历史归档，不是执行指令。

## 0. 基线与交付边界

- 仓库：`Morphling0717/SomeCardGameShit`
- 起始基线：`main@cfdf695d70eeabcc6de9b094c94041364fb1335f`
- Gate 1 基线：`codex/godot-hotseat-gate1@f048d11`
- Gate 2 基线：`codex/godot-hotseat-gate2@8371427`
- Gate 3A 实现：`codex/godot-hotseat-gate3@3a2286a484156d88dc767d7b5e3a0050fca01e12`
- 规则真值：[`rules-v0.4.md`](rules-v0.4.md)，用户最新明确决定优先于旧文档歧义
- 客户端架构：[`godot-client-architecture.md`](godot-client-architecture.md)
- UI 状态：[`ui-state-map.md`](ui-state-map.md)
- 构建与实测：[`../TEST_REPORT.md`](../TEST_REPORT.md)

Gate 3A 已交付完整 C# ABI 消费边界、四个 Godot 主场景及一个 `SnapshotSlot` 子场景、首次热座隐私遮挡、第一张真实 viewer 快照，以及 Windows x86-64 / macOS ARM64 可启动 CI 导出。界面只读并停在 Mulligan；没有实现调度提交或完整一局。分支已推送并验证，但没有创建 PR、合并或打标签。legacy v1 wire 字节未改变，原生 DLL/dylib 未提交。

## 1. 不可推翻的架构决定

```text
Godot 4.7.2 .NET Match/观看者 UI（net8.0，主线程）
        ↓ 依赖接口；Bootstrap 是组合根
IScgsGameSession / Scgs.Client（net8.0 + net10.0）
        ↓ LibraryImport + cdecl，14 个导出
scgs_v04 C11 + schema 1 JSON
        ↓
客户端安全 C++ API
        ↓
Game / C++20 规则引擎（唯一规则真值）
```

- Godot/C# 只显示安全快照、引擎查询、命令结果和 viewer 事件，不复算费用、目标、伤害或胜负。
- Godot 不读取 `PlayerState`，不直接链接 C++ 类型，也不消费 legacy YGOPro2 wire。
- 正式桌面目标仅 Windows x86-64 与 macOS Apple Silicon；不支持 Web 或 Linux 正式客户端。
- 工具链锁定 Godot 4.7.2 .NET、.NET SDK 10.0.400、CMake 3.25+；见 [`toolchain.md`](toolchain.md) 与 [`global.json`](../global.json)。
- YGOPro2/Unity 已停止投入。overlay、upstream、工具和远端 M1 分支只作历史参考，保留但不继续扩展。

## 2. 已冻结的 Gate 0+1+2 契约

### 基线与规则

- `SCGS_ENABLE_LEGACY_YGO2_TESTS` 默认开启；开启时必须找到 Python 3.10+，不得静默少跑。
- legacy v1 wire 的 ID、字段顺序、长度、字节序和金标保持不变。
- 结束回合顺序为“结束效果 → 清临时状态 → PP 清零并发事件 → `TurnEnded` → 对方回合”。
- 响应栈按“反制 → 响应 → 原行动”LIFO；反制过牌不丢第一层或原行动；法术声明支持 `OnSpellDeclared`。
- 支付前完整验证目标；响应中目标失效只跳过依赖该目标的效果，其余效果继续。
- `MatchEnded` 每局至多一次，终局后无抽牌、倒计时或其他状态变化。
- 进化解锁前不职业充能；先手解锁得 2、后手得 3；解锁后充能封顶 4。
- `FirstPlayerMode::{Random, Player0, Player1}`；产品默认随机，测试可指定 seed/先手。快照和开局事件记录实际结果，不承诺 `std::shuffle` 跨标准库逐字一致。

### 客户端与 ABI

- 唯一安全循环是“快照 → 查询 → 命令 → viewer 事件 → 新快照”；查询和执行共享 `validate_*`。
- 成功命令 revision 恰好 +1；失败命令不改变状态、事件或 revision。
- 自己手牌完整，对方手牌只有数量；对方背面伏策没有 definition/instance ID；公开区域保持公开。
- `read_events(viewer, after_sequence)` 非破坏读取，两位 viewer 游标互不消费。
- `engine/include/scgs/native_api_v04.h` 固定 ABI 1.0、schema 1、14 个导出、固定宽度整数和 64 位 token。
- 两段式输出所需长度含尾随 NUL，容量不足不部分写；native failure 与规则 `ErrorCode` 分离，异常不得跨 C 边界。

## 3. Gate 3A 托管边界

`client/Scgs.Client` 是无 Godot 依赖的公共边界，同时生成 `net8.0` 与 `net10.0`；`client/Scgs.Client.Tests` 使用 MSTest.Sdk 4.3.3 / `net10.0`，Godot 工程使用官方桌面 `net8.0`。所有 restore 都使用提交的 `packages.lock.json` 与 `--locked-mode`。

- `LibraryImport` + `cdecl` 绑定全部 14 个 `scgs_v04_*`；参数只用 `uint`、`ulong`、`nint` 和调用方缓冲。
- `NativeLibraryResolver` 只接受显式绝对路径，只允许 Windows x64 / macOS ARM64；不搜索 cwd 或任意 `PATH`。首次调用验证 ABI 1.0。
- `ScgsV04SafeHandle` 保留完整 64 位 token，只有 0 无效；destroy 幂等且不抛。
- DTO 使用 `System.Text.Json`、snake_case、数字枚举和 schema 1 envelope。未知输出字段忽略；未知结构性 player/phase/zone 拒绝；未知事件/行动降级；未知 keyword bits 原样保留。
- 统一输出 helper 约束 1 MiB 输入、16 MiB 托管输出、最多三次增长、严格 UTF-8、尾随 NUL 和清空后的池化缓冲归还。
- native failure 同线程立刻读取 TLS `last_error` 并抛 `ScgsNativeException`；`start/submit` 规则失败返回 `EngineStatus`。
- `IScgsGameSession` 暴露快照、全部查询、支付预览、响应上下文、命令和事件；每个 viewer 独立维护 `ulong` cursor。

27 项托管测试覆盖签名/枚举、JSON/shape、buffer/UTF-8、错误分层、SafeHandle、事件游标、已知库布局、揭示门和当前提交真实动态库集成。

## 4. Gate 3A Godot 流程

已创建：

```text
Bootstrap.tscn
MainMenu.tscn
Match.tscn
PassDeviceOverlay.tscn
SnapshotSlot.tscn（可复用卡位组件）
```

运行顺序：

1. `Bootstrap` 从显式绝对路径加载原生库并验证 ABI/schema；
2. 主菜单让两席独立选择 `midrange` / `advance`，不限制相同牌组；
3. 产品开始配置省略 seed、随机先手并洗牌；CI 固定 seed、Player0 先手且不洗牌；
4. create/start 后先显示完全不透明的“请交给玩家 0”遮挡；
5. 用户主动揭示后才第一次调用 `GetView(Player0)`；
6. `Match` 结构化渲染双方生命、当前 PP、容量、裂痕、进化能量、牌组/手牌数量、公开区、5 个单位位、3 个策略位、己方真实手牌和对方无身份牌背；
7. 返回菜单或退出路径都调用统一的 session 释放逻辑；实际点击后重开属于 Gate 3B 动态复验。

界面使用 Compatibility renderer、1600×900 参考画布并支持 1280×720、zh-CN、鼠标与 Esc。唯一二进制素材是 Noto Sans CJK SC 2.004 Regular，SHA-256 为 `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b`；其余视觉为纯色几何和文字。

## 5. 暂存、导出与许可证

原生库由同提交源码构建和审计后暂存，禁止提交 Git：

```text
client/godot/native/windows-x86_64/scgs_v04.dll
client/godot/native/macos-arm64/libscgs_v04.dylib
```

- Windows 使用 `SCGS_MSVC_STATIC_RUNTIME=ON` 的 `/MT` 产品 DLL；DLL 与导出 EXE 同目录，审计禁止 `MSVCP140*` / `VCRUNTIME140*`。
- macOS CI 从固定哈希的官方 universal archive 临时派生 ARM64 release template；最终 dylib 位于 `.app/Contents/Frameworks`，随后对 bundle ad-hoc codesign。所有 Mach-O 必须 ARM64-only。
- 两个平台都实际启动导出程序，30 秒内必须输出唯一 smoke 标记并以 0 退出；缺库、错架构、ABI/schema 错误走受控错误页。
- 导出包携带 GPL、Godot MIT/COPYRIGHT、.NET、nlohmann MIT、Noto OFL 与第三方声明。

## 6. 验收事实

被测实现 `3a2286a` 的 [GitHub Actions run 32577089388](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32577089388) 为 **4/4 jobs 全绿**：

- Linux GCC Release / 2,048 seeds；
- Linux Clang Debug + ASan/UBSan / 256 seeds；
- Windows MSVC Release + 27/27 managed + Godot 冷导入、当前工程/导出 EXE smoke；
- macOS AppleClang ARM64 + 27/27 managed + Godot 冷导入、当前工程/导出 app smoke。

本机结果为 11/11 CTest、27/27 managed、12/12 Python 制品审计；1600×900 与 1280×720 真实截图检查通过。准确命令、断言数、失败 run 的收口原因、artifact 大小与 SHA-256 见 [`TEST_REPORT.md`](../TEST_REPORT.md)。验收清单见 [`hotseat-acceptance.md`](hotseat-acceptance.md)。

## 7. 限制与下一步

Gate 3B 从调度 UI 和持续换手流程开始，依次接入：

1. 双方调度选择、提交、revision 刷新和 viewer 事件；
2. 普通单位/法术/策略、攻击、结束回合与投降；
3. 支付预览、预支/燃耗、进化、部署和组件；
4. 设施、伏策、反制/响应、结果和重开；
5. Windows / 物理 Apple Silicon Mac 真人完成一局。

不要从 Gate 3A smoke 推断完整对局已可玩。主战技、普通主动能力、同时触发人工排序、固定牌组未使用关键词、正式素材、联机、录像、卡组编辑、Developer ID、公证、Web 与 Linux 正式客户端仍延后。同一玩家同时触发暂按确定性场地顺序，是明确的 Alpha 限制。
