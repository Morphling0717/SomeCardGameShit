# Gate 0+1+2+3A+3B 测试报告

**日期：** 2026-08-23（Asia/Shanghai）

**分支：** `codex/godot-hotseat-gate3b`

**项目基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`

**Gate 2 基线：** `codex/godot-hotseat-gate2@83714270c07b009ac4c3bf79ddb9d343b777213d`

**Gate 3A 已验收尖端：** `codex/godot-hotseat-gate3@5158409`

**最终被测实现：** `9845a3fc89442e2f2066ae0265e8478e03b52632`

**范围：** 在 Gate 3A 桌面骨架上交付 `Scgs.Hotseat` 安全编排、双方调度、全部现有 `ActionKind`、响应、终局/重开源码路径、确定性自动整局，以及 Windows x86-64 / macOS ARM64 导出包的压缩前与解包后审计和真实启动。没有修改 `scgs_v04` ABI 1.0、schema 1、精确 14 个导出或 legacy v1 wire 字节。

## 结论

被测实现已通过本机 MSVC Release **13/13 CTest**、**2,048 seeds** 压力、**41/41 managed tests**、31 项 Python 契约测试、Godot 源码整局 smoke、Windows 导出 ZIP 往返启动和 1600×900 / 1280×720 首个 Mulligan 交互状态视觉检查。

[GitHub Actions run 32583321294](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32583321294) 在 `9845a3f` 上 **4/4 jobs 全绿**：GCC Release、Clang ASan/UBSan、MSVC Release + Windows Godot、AppleClang ARM64 Release + macOS Godot。Windows 与 macOS 均完成 locked restore、C# Debug/Release 零警告构建、41 项托管测试、Godot import、当前工程整局、目标平台导出、结构/架构/许可证审计、导出程序启动、ZIP 解包后再次审计和启动。

每次 Gate 3B 整局 smoke 都只输出一次成功标记，示例为：

```text
SCGS_GODOT_CI_SMOKE_OK result=Player1Won revision=145 steps=145 covers=213 reveals=68 premature_view_calls=0 disposed=true
```

严格 schema 1 报告记录固定 seed `3235823838`、`midrange` 对 `advance`、Player0 先手、52 回合、145 个成功命令、非投降 `ActionKind` 0～9 全覆盖、213 次遮挡、68 次揭示、遮挡期间 0 次 viewer 读取及 1 次 session 释放。

自动 runner 真实经过 `Bootstrap → MatchScreen → HotseatMatchController → 遮挡/跨帧提交 → 自然终局`，但通过 CI helper 注入引擎枚举出的 `LegalAction`，没有逐个点击 Godot 动态按钮。它也没有点击结果页“重新开始”、返回菜单或错误恢复。因此本报告不把按钮 signal E2E、`terminal-restart` 或真人交互记为已完成。

## 执行环境

```text
OS: Windows 11, 10.0.26200.0, AMD64
Generator: Visual Studio 17 2022, x64
MSVC: 19.44.35228.0
CMake: 3.31.6（项目最低要求 3.25）
Clang/clang-cl: 22.1.8
Python: 3.10.11
.NET SDK: 10.0.400
Godot: 4.7.2.stable.mono
Git: 2.55.0.windows.5
```

远端矩阵使用 `ubuntu-latest`、`windows-latest` 与 `macos-15`，Python 固定为 3.12.8；Windows/macOS 由 `global.json` 精确选择 .NET SDK 10.0.400，并安装校验官方哈希后的 Godot 4.7.2 .NET 编辑器与 `4.7.2.stable.mono` export templates。macOS 导出及 bundle 内所有 Mach-O 均审计为 ARM64-only。

## 本地实际命令

以下为在仓库根目录执行的关键命令；`SCGS_GODOT_EXE` 指向已校验的 Godot 4.7.2 .NET 可执行文件：

```powershell
cmake --build build/gate3b-msvc --config Release --parallel 2
$env:SCGS_SMOKE_SEEDS = "2048"
ctest --test-dir build/gate3b-msvc -C Release --output-on-failure

$env:PATH = "C:\Users\ASUS\.dotnet;$env:PATH"
$env:SCGS_NATIVE_LIBRARY = "$PWD\build\gate3b-msvc\Release\scgs_v04.dll"
$env:SCGS_V04_NATIVE_PATH = $env:SCGS_NATIVE_LIBRARY
python scripts/ci/run_managed_gate3.py

dotnet format client/Scgs.Client/Scgs.Client.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Hotseat/Scgs.Hotseat.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --verify-no-changes --no-restore
dotnet format client/godot/SomeCardGameShit.csproj --verify-no-changes --no-restore

python scripts/ci/run_with_timeout.py `
  --timeout 180 `
  --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --expect-output-count 1 `
  --forbid-output "SCRIPT ERROR:" `
  --forbid-output "ERROR:" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke "--native-library=$env:SCGS_NATIVE_LIBRARY" `
  "--ci-report=$PWD\build\gate3b-local-reports\source-head-9845a3f.json"

python scripts/ci/validate_gate3b_report.py `
  --report build/gate3b-local-reports/source-head-9845a3f.json `
  --scenario full-match

python -m unittest `
  tools.tests.test_apply_ygo2_overlay `
  tools.tests.test_protocol_contract `
  scripts.tests.test_audit_native_artifact `
  scripts.tests.test_audit_godot_export `
  scripts.tests.test_run_with_timeout `
  scripts.tests.test_validate_gate3b_report -v

git diff --check
git diff --cached --check
```

Windows 当前提交制品另从 `git archive 9845a3f` 的干净源码快照导出：`stage_godot_native.py` → Godot `--export-release` → `finalize_godot_export.py` → `audit_godot_export.py` → `Compress-Archive` → 新目录 `Expand-Archive` → 再次 audit → 180 秒有界整局启动 → 严格报告校验。`licenses/BUILD_INFO.txt` 精确记录完整提交 `9845a3fc89442e2f2066ae0265e8478e03b52632`。

视觉检查以同一真实 DLL 非 headless 运行 `--ci-smoke --ci-screenshot=<绝对路径>`，在 1600×900 与 1280×720 各完成一局。检查了初始 Mulligan 交互状态、中文字体、主战场布局、己方手牌和对方匿名牌背；没有逐页人工覆盖所有后续行动/响应/结果面板。

## 本地结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 构建与 CTest | 13/13 通过；2,048 seeds |
| 规则回归/压力 | 30 cases，8,607 assertions，0 failures |
| 客户端安全 API 契约 | 426 assertions，0 failures |
| legacy v1 wire 金标 | 31 assertions，0 failures |
| native ABI 契约 | 98,806 assertions，0 failures |
| C11 header/link、动态加载与安装后 consumer | 全部通过；精确 14 个导出可解析并调用 |
| Windows DLL 审计 | PE x86-64；无 C++ 导出；不依赖 `MSVCP140*` / `VCRUNTIME140*` |
| C# managed | 41/41；Godot/net8 Debug + Release 与测试/net10 Release 均零警告 |
| 真实 native managed 整局 | 4 种固定牌组组合 × 双方先手共 8 局自然终局，另有投降终局；覆盖全部 `ActionKind` 0～10、双 viewer 隐私、独立 cursor、预览/执行与 dispose |
| Python legacy | 10/10（overlay 5 + protocol 5） |
| Python CI 工具契约 | 21/21（native audit 5 + Godot export 7 + timeout/marker 3 + Gate 3B report 6） |
| Godot 当前工程整局 | 145 steps / 52 turns；0 premature viewer calls；唯一 marker；严格报告通过 |
| Windows 当前提交 ZIP 往返 | audit + 解包 + 真实整局通过；92,978,651 bytes；SHA-256 `b805e5678c006585473c37d24504753752dd46502a4e42f1babca94eff833c0d` |
| Windows 当前提交 DLL | SHA-256 `e4d90aad73ea916c8444e5f4ad4ca377f3d2ae420eb3b3d5537a19ef046758d4` |
| 1600×900 / 1280×720 初始状态视觉检查 | 中文无缺字；卡槽横排；主区域无重叠/异常裁切；对手手牌只显示匿名牌背 |
| `dotnet format --verify-no-changes` | 4 个项目通过 |
| `git diff --check` / staged check | 通过 |

CTest 的 13 个目标为：

1. `scgs_unit_tests`
2. `scgs_client_api_contract`
3. `scgs_documented_scenario`
4. `scgs_wire_frozen_golden`
5. `scgs_native_api_c_contract`
6. `scgs_native_api_contract`
7. `scgs_native_api_dynamic_load`
8. `scgs_ygo2_overlay_patcher`
9. `scgs_protocol_contract`
10. `scgs_native_artifact_audit_contract`
11. `scgs_godot_export_audit_contract`
12. `scgs_subprocess_timeout_contract`
13. `scgs_gate3b_report_contract`

## 远端 CI 与制品

| Job | 配置 | 结果与制品 |
|---|---|---|
| `linux-gcc` | GCC Release；2,048 seeds | 通过；native 安装/consumer/审计；`scgs-native-v04-linux-x86_64-gcc` |
| `linux-clang-sanitizers` | Clang Debug + ASan/UBSan；256 seeds | 通过；sanitized consumer/审计；`scgs-native-v04-linux-x86_64-clang-asan-ubsan` |
| `windows-msvc` | MSVC Release x86-64；2,048 seeds；Godot Windows | 通过；managed 41/41；当前工程、导出、ZIP 往返整局；native + Gate 3B 客户端 artifact |
| `macos-arm64` | AppleClang Release ARM64；2,048 seeds；Godot macOS | 通过；managed 41/41；当前工程、导出、ZIP 往返整局；native + Gate 3B 客户端 artifact |

Run `32583321294` 上传的 6 个 CI 验收制品如下。字节数和 SHA-256 是 GitHub Actions artifact archive 元数据，不是 archive 内部单个 DLL/dylib/客户端 ZIP 的摘要：

| 制品 | 字节 | GitHub artifact SHA-256 |
|---|---:|---|
| `scgs-native-v04-linux-x86_64-gcc` | 630,017 | `69dc9bf14889314729a82a3dafe558df83b79421be07bf50d61ad3ac6adcc80a` |
| `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 5,821,534 | `696a67d0da4b00cecd2f877a3871756d69d78ffeda07b441e92e15155bbc0e4f` |
| `scgs-native-v04-windows-x86_64-msvc` | 251,671 | `6c9e367150f5cb56f23100ce1ff92cc3941a2902dde635be36079514b292c5ba` |
| `SomeCardGameShit-gate3b-windows-x86_64` | 92,644,860 | `5d3a11b79977ffd39e3b118fd5e4433befaa318485e53ad110ec329e74badb73` |
| `scgs-native-v04-macos-arm64-appleclang` | 481,654 | `fd39ee0a08362bc0d07efbc0d25aceb741fe8ae187c5d9c6796ae7b9918d005a` |
| `SomeCardGameShit-gate3b-macos-arm64` | 77,885,330 | `66d8c810b6466126e4d340a9d582e54ff40009d8172c3c8ff8e458c4d62188fe` |

Windows 客户端内 DLL 与 EXE 同目录。macOS artifact 保留 `.app` 执行权限，dylib 位于 `.app/Contents/Frameworks`，finalize 后重新 ad-hoc codesign；所有 Mach-O 为 ARM64-only。两个包均通过 GPL、Godot MIT/COPYRIGHT、.NET、nlohmann MIT、Noto OFL 和第三方声明审计。它们是未正式签名/未公证的 CI 验收制品，不是发布版本。

## 本轮发现与收口

- 首次 Gate 3B 视觉截图暴露 `SnapshotSlot` 子 Label 未铺满父 Button，中文被挤成单字竖排并使战场重叠。提交 `9845a3f` 改为 full-rect anchors；随后 1600×900 与 1280×720 均重新截图并完整终局通过。
- Windows 本机 clang-cl ASan 构建能完成，但所有测试程序在进入项目代码前因 LLVM ASan 与 UCRT Debug CRT 组合发生启动期 bad-free；这不是本报告的 sanitizer 通过证据。权威 Linux Clang ASan/UBSan job 在同一提交以 256 seeds 和安装后 consumer 全绿。
- Gate 3B 首个远端 run `32583321294` 即 4/4 全绿；没有降低 marker、报告、架构、许可证、sanitizer 或解包后启动断言来换取通过。

## Gate 3B 边界与发布前硬门

- legacy v1 wire、`native_api_v04.h`、ABI 1.0、schema 1 和 14 个导出保持冻结；原生 DLL/dylib 未提交 Git。
- C# 与 Godot 不读取 `PlayerState`，不复算费用、目标、响应或胜负，只消费安全 DTO、查询、命令和观看者事件游标。
- 自动整局覆盖真实引擎、真实 session、热座状态机、遮挡/揭示、全部动作种类和自然终局，但不等于逐个 Godot Button signal E2E；结果页重开、返回菜单和受控错误恢复也未做真实交互 E2E。
- Gate 3B 新增面板尚未逐状态完成人工分辨率/可读性遍历；本轮人工视觉证据只覆盖初始 Mulligan 状态和主战场。
- 物理 Apple Silicon 上的整局/退出/重开、两名真人热座隐私观察、未安装 Visual Studio 的 Windows x86-64 机器整局仍未执行。
- Developer ID 签名、公证、主战技、普通主动能力、同时触发人工排序、正式卡图/音效/动画、独立表现 JSON、Web/Linux 正式客户端均不在本 Gate。
- 在上述物理设备与真人硬门完成前，不创建 `v0.4-hotseat-alpha.1` 标签。本轮也未创建 PR、未合并、未打标签。

本文件记录实现提交 `9845a3f` 的可复现证据；包含测试报告的文档提交不改变产品代码，并由同一工作流再次验证分支尖端。
