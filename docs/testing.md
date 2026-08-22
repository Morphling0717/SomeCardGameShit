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

Windows/macOS job 还必须实际导出并启动产物；只在编辑器运行不算通过。Windows 审计 DLL 与 EXE 同目录、x86-64 和静态 CRT；macOS 审计 arm64、`Contents/Frameworks`、ad-hoc codesign 与执行权限。两者的 `BUILD_INFO.txt` 都必须精确标识 Gate 3C、锁定工具链和当前 CI checkout commit。压缩后必须解包、重新审计并再次启动。每次 full-match smoke 的外部上限为 180 秒，日志不得含 C# exception 或 Godot error。

### legacy 兼容性

`scgs_wire_frozen_golden` 固定验证 v1 消息长度、字节序、消息 ID 和金标字节。历史命名的 `SCGS_ENABLE_LEGACY_YGO2_TESTS` 当前控制整组 Python CTest：legacy overlay/协议、原生/Godot 制品审计、子进程超时以及 Gate 3B/3C 报告契约。它默认开启；开启时 CMake 必须找到 Python 3.10+，不能静默只注册部分测试。关闭会跳过整组 Python 契约，因此不能用于客户端 Gate 验收。

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

Linux 两个 job 保持纯原生。Windows 与 macOS job 在原生安装审计之后追加 locked managed restore/build/test、等待冷资源扫描完成的 Godot `--import`、目标平台导出、导出包启动与审计；macOS 从已校验的官方 universal template 临时派生 arm64 release template，并要求最终 bundle 只有一套 arm64 托管数据且所有 Mach-O 均为 arm64-only。这不构成 Web 或 Linux 客户端支持声明。

## 报告规则

[`TEST_REPORT.md`](../TEST_REPORT.md) 只记录实际执行过的分支、commit、环境、命令、测试/断言数和结果。不得把以下内容写成已通过：

- 未推送分支的 GitHub CI；
- 当前机器无法运行的编译器或 sanitizer；
- 未在对应提交上实际导入/构建的 Godot 工程；
- 未实际运行的 Godot 当前工程、桌面导出或真人完整对局；
- Web、网络、平衡或正式美术。

测试绿代表已覆盖范围内没有已知失败，不等于 Alpha 全产品验收完成。
