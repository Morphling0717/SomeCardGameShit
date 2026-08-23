# Gate 0+1+2+3A+3B+3C+4A 测试报告

**日期：** 2026-08-23（Asia/Shanghai）

**分支：** `codex/godot-hotseat-gate4a`

**项目基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`

**Gate 3C 基线：** `codex/godot-hotseat-gate3c@a29dd14e75be9ec9bc6340f8f60945b27bc58ce7`

**被测 Gate 4A 实现：** `7a6808ddcd76d2c78fd906a9235f867c11c84e7c`（主体实现 `c2d2e709c789de3c408a1271176a255671e9fb82`，随后仅修正格式）

**范围：** 在 Gate 3C 完整热座闭环上加入默认 3D/2.5D 斜俯视战场、统一 surface intent、3D 射线与拖拽、固定 viewer 透视、actor 池、空间隐私清理和 schema v3 验收；legacy 2D 仅通过 `--legacy-2d-board` 保留为隐藏回归路径。没有修改 C++ 规则、`native_api_v04.h`、ABI 1.0、schema 1、精确 14 个导出、两副固定牌组或 legacy v1 wire 字节。

## 结论

Gate 4A 实现已通过本机 MSVC Release **15/15 CTest**、**2,048 seeds** 压力、**62/62 managed tests**、**62/62 Python tests**、Godot 默认 3D 与 legacy 2D 源码整局 smoke、Windows 默认 3D 导出与 ZIP 解包后真实启动，以及 1600×900 / 1280×720 的 `Resolving` 公共投影视觉检查。

[GitHub Actions run 32617860778](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32617860778) 在 `7a6808d` 上 **4/4 jobs 全绿**：GCC Release、Clang ASan/UBSan、MSVC Release + Windows Godot、AppleClang ARM64 Release + macOS Godot。主体实现提交的前一轮 [run 32617735534](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32617735534) 也为 4/4 全绿。

Windows 与 macOS 均完成 locked restore、C# Debug/Release 零警告构建、62 项托管测试、Godot import、默认 3D 当前工程整局、legacy 2D 当前工程整局、默认 3D 目标平台导出、结构/架构/许可证审计、导出程序整局、ZIP 解包后再次审计和整局。两平台合计运行 **8 次** full-match，每次只输出一次成功标记。

```text
SCGS_GODOT_CI_SMOKE_OK result=Player1Won revision=3 steps=148 covers=71 reveals=71 premature_view_calls=0 disposed=true
```

默认 3D 的严格 schema v3 报告记录：固定 seed `3235823838`、`midrange` 对 `advance`、Player0 先手、第一局 52 个结束回合、两局合计 148 次成功命令提交、`ActionKind` 0～10 全覆盖、71 次遮挡、71 次揭示、揭示前及遮挡期间 0 次 viewer 读取、每条命令至少 2 个完整公共结算帧、两类私密泄露均为 0、1 次 signal 重开、1 次第二局投降终局及 2 次 session 释放。

Gate 4A 新增空间证据为：`surface_intent_e2e=true`、`raycast_e2e=true`、HUD 射线阻断 1 次、拖拽阈值 8 px、相机 70° FOV / 58° 俯角、viewer 透视重建 69 次、actor 池复用 830 次、锁定状态空间输入阻断 148 次、`spatial_private_leaks=0`。legacy 2D 报告仍证明经过共享 surface intent，同时所有 3D 专属证据均为 0。

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

以下命令从仓库根目录执行；`SCGS_GODOT_EXE` 指向已校验的 Godot 4.7.2 .NET 可执行文件：

```powershell
cmake -S . -B build/gate4a-msvc -A x64 `
  -DSCGS_WARNINGS_AS_ERRORS=ON `
  -DSCGS_ENABLE_LEGACY_YGO2_TESTS=ON
cmake --build build/gate4a-msvc --config Release --parallel 4
ctest --test-dir build/gate4a-msvc -C Release --output-on-failure

$env:SCGS_SMOKE_SEEDS = "2048"
ctest --test-dir build/gate4a-msvc -C Release `
  -R '^scgs_unit_tests$' --output-on-failure

cmake --install build/gate4a-msvc --config Release `
  --prefix build/stage-gate4a-msvc
python scripts/audit_native_artifact.py `
  --library build/stage-gate4a-msvc/bin/scgs_v04.dll `
  --architecture x86_64

$env:SCGS_NATIVE_LIBRARY = "$PWD\build\stage-gate4a-msvc\bin\scgs_v04.dll"
$env:SCGS_V04_NATIVE_PATH = $env:SCGS_NATIVE_LIBRARY
python scripts/ci/run_managed_gate3.py
python -m unittest discover -s scripts/tests -p "test_*.py"

python scripts/ci/run_with_timeout.py `
  --timeout 180 --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --expect-output-count 1 --forbid-output "SCRIPT ERROR:" `
  --forbid-output "ERROR:" --forbid-output "Unhandled exception" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke "--native-library=$env:SCGS_NATIVE_LIBRARY" `
  "--ci-report=$PWD\build\gate4a-current-3d.json"
python scripts/ci/validate_gate4a_report.py `
  --report build/gate4a-current-3d.json `
  --scenario full-match --presentation 3d

python scripts/ci/run_with_timeout.py `
  --timeout 180 --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --expect-output-count 1 --forbid-output "SCRIPT ERROR:" `
  --forbid-output "ERROR:" --forbid-output "Unhandled exception" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke --legacy-2d-board `
  "--native-library=$env:SCGS_NATIVE_LIBRARY" `
  "--ci-report=$PWD\build\gate4a-current-legacy-2d.json"
python scripts/ci/validate_gate4a_report.py `
  --report build/gate4a-current-legacy-2d.json `
  --scenario full-match --presentation legacy-2d

dotnet format client/Scgs.Client/Scgs.Client.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Hotseat/Scgs.Hotseat.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --verify-no-changes --no-restore
dotnet format client/godot/SomeCardGameShit.csproj --verify-no-changes --no-restore
git diff --check
git diff --cached --check
```

Windows 精确提交制品以 `GITHUB_SHA=7a6808ddcd76d2c78fd906a9235f867c11c84e7c` 重新执行 native 暂存、Godot `--export-release`、`finalize_godot_export.py`、`audit_godot_export.py`、首次 180 秒有界整局、ZIP 更新、全新目录解包、再次 audit/整局和 schema v3 校验。`licenses/BUILD_INFO.txt` 精确记录该 40 位实现提交。

## 本地结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 构建与 CTest | 15/15 通过；2,048 seeds |
| 规则回归/压力 | 30 cases，8,607 assertions，0 failures |
| 客户端安全 API 契约 | 426 assertions，0 failures |
| legacy v1 wire 金标 | 31 assertions，0 failures |
| native ABI 契约 | 98,806 assertions，0 failures |
| C11 header/link、动态加载与安装后 consumer | 全部通过；精确 14 个导出可解析并调用 |
| Windows DLL 审计 | PE x86-64；无 C++ 导出；不依赖 `MSVCP140*` / `VCRUNTIME140*` |
| C# managed | 62/62，0 skipped；Godot/net8 Debug + Release 与测试/net10 Release 均零警告 |
| surface intent 契约 | 全部 `ActionKind` 来源/目的地映射、点击/拖拽收敛、已选 action 收窄、冲突/过期/歧义无副作用均通过 |
| Python legacy | 10/10（overlay 5 + protocol 5） |
| Python CI 工具契约 | 52/52（native audit 5 + Godot export 9 + timeout 3 + Gate 3B report 6 + Gate 3C report 15 + Gate 4A report 14） |
| 默认 3D Godot signal E2E | 148 次成功提交；动作 0～10；真实 raycast/click/drag/键盘/战备跨层拖放；自然终局、重开、投降；唯一 marker |
| legacy 2D Godot signal E2E | 同一 148 步闭环；共享 surface intent；不伪报 3D 证据 |
| `Resolving` 恶意 DTO 隐私 | 2 个完整帧；Label/材质/metadata/tooltip/碰撞/回调/drag token 泄露 0；期间无 viewer 数据 |
| Windows 精确提交 ZIP 往返 | audit + 解包 + 两次真实整局通过；93,103,109 bytes；SHA-256 `3f703500d118ac511e4c3ce2b587d4ad5f09d3fb477dcb595e6af393af1c3870` |
| Windows 精确提交 DLL | 459,264 bytes；SHA-256 `e8c76c29544ee2b729768a0f37cdf177c1c5c66dd8e21af27be071aab123fc8b` |
| 1600×900 / 1280×720 `Resolving` 视觉检查 | 中文可读、左右栏不遮战场、公开投影牌背匿名；其他状态仍列为人工硬门 |
| `dotnet format --verify-no-changes` | 4 个项目通过 |
| `git diff --check` / staged check | 通过 |

CTest 的 15 个目标为：

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
15. `scgs_gate4a_report_contract`

## 远端 CI 与制品

| Job | 配置 | 结果与制品 |
|---|---|---|
| [`linux-gcc`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32617860778/job/97141375303) | GCC Release；2,048 seeds | 通过；native 安装/consumer/审计；`scgs-native-v04-linux-x86_64-gcc` |
| [`linux-clang-sanitizers`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32617860778/job/97141375199) | Clang Debug + ASan/UBSan；256 seeds | 通过；sanitized consumer/审计；`scgs-native-v04-linux-x86_64-clang-asan-ubsan` |
| [`windows-msvc`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32617860778/job/97141375342) | MSVC Release x86-64；2,048 seeds；Godot Windows | 通过；managed 62/62；默认 3D 当前/导出/ZIP + legacy 2D 源码整局；native + Gate 4A 客户端 artifact |
| [`macos-arm64`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32617860778/job/97141375355) | AppleClang Release ARM64；2,048 seeds；Godot macOS | 通过；managed 62/62；默认 3D 当前/导出/ZIP + legacy 2D 源码整局；native + Gate 4A 客户端 artifact |

Run `32617860778` 上传的 6 个 CI 验收制品如下。字节数和 SHA-256 是 GitHub Actions artifact archive 元数据，不是 archive 内部单个 DLL/dylib/客户端 ZIP 的摘要：

| 制品 | 字节 | GitHub artifact SHA-256 |
|---|---:|---|
| `scgs-native-v04-linux-x86_64-gcc` | 630,017 | `b0aa4f7498b8bd8b1962713623b9f8094c9ef62ad66f05982354f05884a6e411` |
| `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 5,821,534 | `e9b3101dc3730720660ab7ba00435f97b62c00e8645a612f1e7f2a6d81a8f397` |
| `scgs-native-v04-windows-x86_64-msvc` | 251,672 | `13fb018d30436141e83eed31c7642ee0937c7c8e1565d92d66831b6fb2936246` |
| `SomeCardGameShit-gate4a-windows-x86_64` | 92,832,624 | `dd274ec703c04d471fa393bdde1ff4df1431c8cbd7f2c71fbf483831a376468b` |
| `scgs-native-v04-macos-arm64-appleclang` | 481,654 | `44dd290aaf13c75bec1dafc9772fa8e5894ca5c393f5618817095a788ebaa40a` |
| `SomeCardGameShit-gate4a-macos-arm64` | 78,069,294 | `e38f13cb0a575b0499544e9387a5519245fc996efb24f01c54d6f0404eeba50f` |

Windows 客户端内 DLL 与 EXE 同目录。macOS artifact 保留 `.app` 执行权限，dylib 位于 `.app/Contents/Frameworks`，finalize 后重新 ad-hoc codesign；所有 Mach-O 为 ARM64-only。两个包均通过 GPL、Godot MIT/COPYRIGHT、.NET、nlohmann MIT、Noto OFL 和第三方声明审计。它们是未正式签名/未公证的 CI 验收制品，不是发布版本。

## 本轮发现与收口

- 3D 的真实拾取必须尊重 HUD：输入先执行 GUI hit-test，再做物理射线；8 px 以下仍按点击，revision 或模式变化会清除 drag token。
- 多动作来源拖拽最初可能忽略已选择的 action。`PlanDrag` 现在继承同源已选 action，显式冲突在访问 controller/native 前无副作用拒绝。
- 3D 战备使用空间牌堆打开 2D 托盘；托盘来源可跨 2D/3D 边界落到具体 3D 格位，仍只生成统一 surface intent。
- 响应初始居中层显式锁死空间输入；选择伏策且仍需目标后才隐藏响应层并返回 3D 战场。
- actor 池在 `Covered`/`Resolving`/重开/销毁时清除 DTO、ID、Label、材质名、metadata、tooltip、碰撞、回调、描边、箭头、幽灵牌和拖拽 revision；恶意私密哨兵审计为 0。
- 相机保持 70° FOV / 58° 俯角，只允许安全范围缩放；左右栏真实宽度变化会重新计算水平偏移，viewer 透视只在完全不透明遮挡内瞬时重建。

## Gate 4A 边界与发布前硬门

- legacy v1 wire、`native_api_v04.h`、ABI 1.0、schema 1 和 14 个导出保持冻结；原生 DLL/dylib 未提交 Git。
- C# 与 Godot 不读取 `PlayerState`，不复算费用、目标、响应或胜负，只消费安全 DTO、引擎查询、规范命令和观看者事件游标。
- 本轮人工截图只完成两个目标分辨率下的 `Resolving` 公共投影。普通行动、目标选择、响应目标和 `Covered` 的逐状态截图矩阵仍是发布前人工视觉硬门，不能由 headless smoke 代替。
- 默认 3D presenter 已独立承担空间渲染、射线、相机和 actor 池；隐藏 legacy 2D 的旧节点渲染仍保留在 `MatchScreen` 兼容路径中，但其点击/拖拽只能经过同一个 `HotseatSurfaceInteractionCoordinator`，没有第二套规则或命令验证。
- 物理 Apple Silicon 上的整局/退出/重开、两名真人热座隐私观察、未安装 Visual Studio 的 Windows x86-64 机器整局仍未执行。
- Developer ID 签名、公证、正式卡图/音效/复杂动画、触摸/手柄、主战技、普通主动能力、同时触发人工排序、Web/Linux 正式客户端和联机均不在本 Gate。
- 在上述物理设备与真人硬门完成前，不创建 `v0.4-hotseat-alpha.1` 标签。本轮也未创建 PR、未合并、未打标签。

本文件记录实现提交 `7a6808d` 的可复现证据；包含测试报告的文档提交不改变产品代码，并由同一工作流再次验证分支尖端。
