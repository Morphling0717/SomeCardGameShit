# Gate 0+1+2+3A+3B+3C 测试报告

**日期：** 2026-08-23（Asia/Shanghai）

**分支：** `codex/godot-hotseat-gate3c`

**项目基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`

**Gate 3B 基线：** `codex/godot-hotseat-gate3b@dd38e93eba65652a54ce1861e1e303b344e7fd66`

**被测 Gate 3C 实现：** `087d53a5dad3285478e78381914d34acfcaa79f3`

**范围：** 在 Gate 3B 完整热座闭环上加入来源优先的战场直操、点击/拖拽统一 intent、复杂选择步骤、逐步回退、响应上下文和只含公开信息的 `Resolving` 投影。没有修改 C++ 规则、`native_api_v04.h`、ABI 1.0、schema 1、精确 14 个导出、两副固定牌组或 legacy v1 wire 字节。

## 结论

Gate 3C 实现已通过本机 MSVC Release **14/14 CTest**、**2,048 seeds** 压力、**49/49 managed tests**、**47/47 Python tests**、Godot 当前工程整局 smoke、Windows 导出与 ZIP 解包后真实启动，以及 1600×900 / 1280×720 的 `Resolving` 公共投影视觉检查。

[GitHub Actions run 32592594368](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32592594368) 在 `087d53a` 上 **4/4 jobs 全绿**：GCC Release、Clang ASan/UBSan、MSVC Release + Windows Godot、AppleClang ARM64 Release + macOS Godot。Windows 与 macOS 均完成 locked restore、C# Debug/Release 零警告构建、49 项托管测试、Godot import、当前工程整局、目标平台导出、结构/架构/许可证审计、导出程序启动、ZIP 解包后再次审计和启动。

Gate 3C smoke 的唯一成功标记为：

```text
SCGS_GODOT_CI_SMOKE_OK result=Player1Won revision=3 steps=148 covers=71 reveals=71 premature_view_calls=0 disposed=true
```

严格 schema v2 报告记录固定 seed `3235823838`、`midrange` 对 `advance`、Player0 先手、第一局 52 个结束回合、两局合计 148 次成功命令提交、`ActionKind` 0～10 全覆盖、71 次遮挡、71 次揭示、遮挡期间 0 次 viewer 读取、2 个完整公共结算帧、0 个私密泄露、1 次 signal 重开、1 次第二局投降终局及 2 次 session 释放。`revision=3` 与最终 `Player1Won` 来自重开后的投降终局，不是第一局的最终 revision。

自动 runner 真实经过 `Bootstrap → MatchScreen → HotseatMatchController → Godot 控件 signal → Resolving 两帧 → 提交/换手 → 自然终局 → 结果页重开 → 第二局投降`。它验证点击与拖拽得到逐字段相同的规范命令、最后必要选择后没有通用确认，并用恶意私密哨兵审计结算期间的文本、tooltip、metadata 和回调。

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

以下为仓库根目录执行的关键命令；`SCGS_GODOT_EXE` 指向已校验的 Godot 4.7.2 .NET 可执行文件：

```powershell
cmake --build build/gate3c-msvc --config Release --parallel 2
$env:SCGS_SMOKE_SEEDS = "2048"
ctest --test-dir build/gate3c-msvc -C Release --output-on-failure

$env:SCGS_NATIVE_LIBRARY = "$PWD\build\gate3c-msvc\Release\scgs_v04.dll"
$env:SCGS_V04_NATIVE_PATH = $env:SCGS_NATIVE_LIBRARY
python scripts/ci/run_managed_gate3.py

python -m unittest `
  tools.tests.test_apply_ygo2_overlay `
  tools.tests.test_protocol_contract `
  scripts.tests.test_audit_native_artifact `
  scripts.tests.test_audit_godot_export `
  scripts.tests.test_run_with_timeout `
  scripts.tests.test_validate_gate3b_report `
  scripts.tests.test_validate_gate3c_report -v

python scripts/ci/run_with_timeout.py `
  --timeout 180 `
  --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --expect-output-count 1 `
  --forbid-output "SCRIPT ERROR:" `
  --forbid-output "ERROR:" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke "--native-library=$env:SCGS_NATIVE_LIBRARY" `
  "--ci-report=$PWD\build\gate3c-implementation-current.json"

python scripts/ci/validate_gate3c_report.py `
  --report build/gate3c-implementation-current.json `
  --scenario full-match

dotnet format client/Scgs.Client/Scgs.Client.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Hotseat/Scgs.Hotseat.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --verify-no-changes --no-restore
dotnet format client/godot/SomeCardGameShit.csproj --verify-no-changes --no-restore

git diff --check
git diff --cached --check
```

Windows 精确提交制品以 `GITHUB_SHA=087d53a5dad3285478e78381914d34acfcaa79f3` 执行：重新构建 native → `stage_godot_native.py` → Godot `--export-release` → `finalize_godot_export.py` → `audit_godot_export.py` → 首次 180 秒有界启动 → `Compress-Archive` → 新目录 `Expand-Archive` → 再次 audit、启动和 schema v2 报告校验。`licenses/BUILD_INFO.txt` 精确记录该 40 位提交。

## 本地结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 构建与 CTest | 14/14 通过；2,048 seeds |
| 规则回归/压力 | 30 cases，8,607 assertions，0 failures |
| 客户端安全 API 契约 | 426 assertions，0 failures |
| legacy v1 wire 金标 | 31 assertions，0 failures |
| native ABI 契约 | 98,806 assertions，0 failures |
| C11 header/link、动态加载与安装后 consumer | 全部通过；精确 14 个导出可解析并调用 |
| Windows DLL 审计 | PE x86-64；无 C++ 导出；不依赖 `MSVCP140*` / `VCRUNTIME140*` |
| C# managed | 49/49，0 skipped；Godot/net8 Debug + Release 与测试/net10 Release 均零警告 |
| Gate 3C 控制器契约 | 每种 `ActionKind` 的来源映射、步骤/自动补全/回退、乱序选择收敛、支付提示、公共投影和双 viewer 隐私均通过 |
| Python legacy | 10/10（overlay 5 + protocol 5） |
| Python CI 工具契约 | 37/37（native audit 5 + Godot export 8 + timeout 3 + Gate 3B report 6 + Gate 3C report 15） |
| Godot 当前工程 signal E2E | 两局合计 148 次成功提交 / 第一局 52 个结束回合；动作 0～10；点击/拖拽一致；重开 1；投降 1；0 premature viewer calls；唯一 marker |
| `Resolving` 恶意 DTO 隐私 | 2 个完整帧；文本/tooltip/metadata/回调泄露 0；期间无 Snapshot/Viewer/LegalActions/events |
| Windows 精确提交 ZIP 往返 | audit + 解包 + 两次真实整局通过；92,984,159 bytes；SHA-256 `e50eac9b1af036a83a95534a1fba639964a3d017863f5288f4aa2ece3b89d581` |
| Windows 精确提交 DLL | 459,264 bytes；SHA-256 `a7498a5c0ed1243f92ed5af71a2e47b85703e01583f7d8303882f1ecbb29775a` |
| 1600×900 / 1280×720 `Resolving` 视觉检查 | 中文无缺字、区域无重叠；双方手牌只显示数量，背面伏策匿名，详情/日志/候选已清空 |
| `dotnet format --verify-no-changes` | 4 个项目通过 |
| `git diff --check` / staged check | 通过 |

CTest 的 14 个目标为：

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
14. `scgs_gate3c_report_contract`

## 远端 CI 与制品

| Job | 配置 | 结果与制品 |
|---|---|---|
| `linux-gcc` | GCC Release；2,048 seeds | 通过；native 安装/consumer/审计；`scgs-native-v04-linux-x86_64-gcc` |
| `linux-clang-sanitizers` | Clang Debug + ASan/UBSan；256 seeds | 通过；sanitized consumer/审计；`scgs-native-v04-linux-x86_64-clang-asan-ubsan` |
| `windows-msvc` | MSVC Release x86-64；2,048 seeds；Godot Windows | 通过；managed 49/49；当前工程、导出、ZIP 往返 Gate 3C signal E2E；native + Gate 3C 客户端 artifact |
| `macos-arm64` | AppleClang Release ARM64；2,048 seeds；Godot macOS | 通过；managed 49/49；当前工程、导出、ZIP 往返 Gate 3C signal E2E；native + Gate 3C 客户端 artifact |

Run `32592594368` 上传的 6 个 CI 验收制品如下。字节数和 SHA-256 是 GitHub Actions artifact archive 元数据，不是 archive 内部单个 DLL/dylib/客户端 ZIP 的摘要：

| 制品 | 字节 | GitHub artifact SHA-256 |
|---|---:|---|
| `scgs-native-v04-linux-x86_64-gcc` | 630,017 | `dac49e30b8986091e734a730e53a9185577a8da1786a1382cb88682f6d3b8d60` |
| `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 5,821,534 | `653f4a2e014aaf3ddd62f5cfe255eeeb2ad664cb76758d7cd056537b19bc924f` |
| `scgs-native-v04-windows-x86_64-msvc` | 251,671 | `280683ab70d9f37dfe53093cf25b0329067e2e8364b14f8bca9bddf0e9ffd745` |
| `SomeCardGameShit-gate3c-windows-x86_64` | 92,715,300 | `a31ac46874a676c181f02226b42ff91d2e557c6ffc3906f9608135b6fdc539ae` |
| `scgs-native-v04-macos-arm64-appleclang` | 481,654 | `ed4be467ec7104835e2f86d75057b3f516f3ade1ddaee8acad0b10f508980062` |
| `SomeCardGameShit-gate3c-macos-arm64` | 77,954,375 | `3bece24ae641875f8e0bc05df5f36e8bb8e50bf07b0470c228242d881b5246a0` |

Windows 客户端内 DLL 与 EXE 同目录。macOS artifact 保留 `.app` 执行权限，dylib 位于 `.app/Contents/Frameworks`，finalize 后重新 ad-hoc codesign；所有 Mach-O 为 ARM64-only。两个包均通过 GPL、Godot MIT/COPYRIGHT、.NET、nlohmann MIT、Noto OFL 和第三方声明审计。它们是未正式签名/未公证的 CI 验收制品，不是发布版本。

## 本轮发现与收口

- 公共结算最初只等待两个 `ProcessFrame` 信号；显示后端可能只实际绘制一个完整帧。实现改为每一帧都等待 `RenderingServer.FramePostDraw`，headless 才使用 process-frame 栅栏，并对两个阶段分别执行隐私审计。
- 动态选择回调最初可能在相同 key 被新 revision 复用。现在 key 同时携带 revision 和单调 generation，回调执行前再次校验当前 generation、revision 和可见模式。
- 多步骤单位拖拽最初可能把敌方目标位误作己方放置位。现在拖拽目的地具有 `Target` / `Donor` / `Slot` 明确语义，歧义目的地直接回弹且不调用 native。
- 选择需要目标的伏策后，居中响应层原本会挡住战场。现在响应牌选定后隐藏居中层，目标选择直接回到战场；“不过”仍为直接提交。
- 本机 Windows clang-cl ASan/UBSan 不是正式 sanitizer 证据：该组合在预期异常路径进入 Windows 运行库时失败。权威 sanitizer 结果来自同提交的 Linux Clang ASan/UBSan job，并以 256 seeds 全绿。

## Gate 3C 边界与发布前硬门

- legacy v1 wire、`native_api_v04.h`、ABI 1.0、schema 1 和 14 个导出保持冻结；原生 DLL/dylib 未提交 Git。
- C# 与 Godot 不读取 `PlayerState`，不复算费用、目标、响应或胜负，只消费安全 DTO、引擎查询、规范命令和观看者事件游标。
- 自动 signal E2E 覆盖动作、响应、结算、换手、自然终局、重开和第二局投降，但不等于两名真人对操作直觉、文字表达和所有卡牌组合的体验验收。
- 本轮人工截图只检查两个分辨率下的 `Resolving` 公共投影；主要选择、响应与换手状态尚未完成逐页人工视觉遍历。
- 物理 Apple Silicon 上的整局/退出/重开、两名真人热座隐私观察、未安装 Visual Studio 的 Windows x86-64 机器整局仍未执行。
- Developer ID 签名、公证、主战技、普通主动能力、同时触发人工排序、正式卡图/音效/动画、独立表现 JSON、Web/Linux 正式客户端、触摸/手柄和联机均不在本 Gate。
- 在上述物理设备与真人硬门完成前，不创建 `v0.4-hotseat-alpha.1` 标签。本轮也未创建 PR、未合并、未打标签。

本文件记录实现提交 `087d53a` 的可复现证据；包含测试报告的文档提交不改变产品代码，并由同一工作流再次验证分支尖端。
