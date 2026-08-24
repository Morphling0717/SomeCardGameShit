# Gate 0+1+2+3A+3B+3C+4A+4A.1 测试报告

**日期：** 2026-08-24（Asia/Shanghai）

**分支：** `codex/godot-hotseat-gate4a-spell-slots`

**项目基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`

**Gate 4A 自动化基线：** `codex/godot-hotseat-gate4a@7a6808ddcd76d2c78fd906a9235f867c11c84e7c`

**Gate 4A.1 起始基线：** `codex/godot-hotseat-gate4a-layout-fix@0d1d4e5`

**被测 Gate 4A.1 实现：** `4be6e09ef9edc363b064b4a7aaba4551359ecb05`

**范围：** 法术必须从手牌正面进入己方明确选择的空策略位，响应期间保持公开占位，并在自身链环结束或终局清理时送墓；三格全满时不能施法。默认 3D 与隐藏 legacy 2D 均删除中央施放区，但保留冻结的 `CastZone` 枚举值作为兼容占位。本轮修改 C++ 法术规则与强类型 `cast_spell` 签名，不改变 `native_api_v04.h`、ABI 1.0、schema 1、精确 14 个导出、`ActionKind` 数值、两副固定牌组或 legacy v1 wire。

## 结论

Gate 4A.1 实现通过本机 MSVC Release **15/15 CTest**、**2,048-seed** 压力、**67/67 managed tests**、**62 项 Python/legacy 测试**、Godot 默认 3D 与 legacy 2D 源码整局 smoke，以及 1280×720、1600×900、2560×1440 的 `Action` / `Resolving` 截图验收。

[GitHub Actions run 32696171327](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32696171327) 在实现提交 `4be6e09` 上 **4/4 jobs 全绿**：GCC Release、Clang ASan/UBSan、MSVC Release + Windows Godot、AppleClang ARM64 Release + macOS Godot。

Windows 与 macOS 均完成 locked restore、C# Debug/Release 零警告构建、67 项托管测试、Godot import、默认 3D 当前工程整局、legacy 2D 当前工程整局、默认 3D 目标平台导出、结构/架构/许可证审计、导出程序整局、ZIP 解包后再次审计和整局。两平台合计运行 **8 次** full-match，每次只输出一次成功标记。

```text
SCGS_GODOT_CI_SMOKE_OK result=Player1Won revision=3 steps=148 covers=71 reveals=71 premature_view_calls=0 disposed=true
```

当前 schema v3 报告仍以 `gate: 4A` 标识冻结的 Gate 4A smoke 契约；Gate 4A.1 沿用该 schema。默认 3D 与 legacy 2D 均记录固定 seed `3235823838`、`midrange` 对 `advance`、Player0 先手、第一局 52 个结束回合、两局合计 148 次成功命令、`ActionKind` 0～10 全覆盖、71 次遮挡、71 次揭示、揭示前 0 次 viewer 读取、每条命令至少 2 个完整公共结算帧、1 次 signal 重开、1 次投降终局及 2 次 session 释放。

默认 3D 的空间证据为：真实 surface intent 与 raycast、HUD 射线阻断 1 次、拖拽阈值 8 px、相机 70° FOV / 58° 俯角、viewer 透视重建 69 次、actor 池复用 839 次、锁定状态空间输入阻断 148 次、私密泄露 0。legacy 2D 证明经过同一 surface intent，同时不伪报 3D 专属证据。

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

远端矩阵使用 `ubuntu-latest`、`windows-latest` 与 `macos-15`，Python 固定为 3.12.8；Windows/macOS 由 `global.json` 精确选择 .NET SDK 10.0.400，并使用校验官方哈希后的 Godot 4.7.2 .NET 编辑器与 Mono export templates。macOS 导出及 bundle 内所有 Mach-O 均审计为 ARM64-only。

## 本地实际命令

以下命令从仓库根目录执行；`SCGS_GODOT_EXE` 指向已校验的 Godot 4.7.2 .NET 可执行文件：

```powershell
cmake -S . -B build/gate4a1-msvc -A x64 `
  -DSCGS_WARNINGS_AS_ERRORS=ON `
  -DSCGS_ENABLE_LEGACY_YGO2_TESTS=ON
cmake --build build/gate4a1-msvc --config Release --parallel
ctest --test-dir build/gate4a1-msvc -C Release --output-on-failure

$env:SCGS_SMOKE_SEEDS = "2048"
& .\build\gate4a1-msvc\Release\scgs_tests.exe

cmake --install build/gate4a1-msvc --config Release `
  --prefix build/stage-gate4a1-msvc
python scripts/audit_native_artifact.py `
  --library build/stage-gate4a1-msvc/bin/scgs_v04.dll `
  --architecture x86_64

$env:SCGS_NATIVE_LIBRARY = "$PWD\build\stage-gate4a1-msvc\bin\scgs_v04.dll"
$env:SCGS_V04_NATIVE_PATH = $env:SCGS_NATIVE_LIBRARY
python scripts/ci/run_managed_gate3.py
python -m unittest discover -s scripts/tests -p "test_*.py"

python scripts/ci/run_with_timeout.py `
  --timeout 180 --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --expect-output-count 1 --forbid-output "SCRIPT ERROR:" `
  --forbid-output "ERROR:" --forbid-output "Unhandled exception" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke "--native-library=$env:SCGS_NATIVE_LIBRARY" `
  "--ci-report=$PWD\build\gate4a1-current-3d.json"
python scripts/ci/validate_gate4a_report.py `
  --report build/gate4a1-current-3d.json `
  --scenario full-match --presentation 3d

python scripts/ci/run_with_timeout.py `
  --timeout 180 --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --expect-output-count 1 --forbid-output "SCRIPT ERROR:" `
  --forbid-output "ERROR:" --forbid-output "Unhandled exception" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke --legacy-2d-board `
  "--native-library=$env:SCGS_NATIVE_LIBRARY" `
  "--ci-report=$PWD\build\gate4a1-current-legacy-2d.json"
python scripts/ci/validate_gate4a_report.py `
  --report build/gate4a1-current-legacy-2d.json `
  --scenario full-match --presentation legacy-2d

dotnet format client/Scgs.Client/Scgs.Client.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Hotseat/Scgs.Hotseat.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --verify-no-changes --no-restore
dotnet format client/godot/SomeCardGameShit.csproj --verify-no-changes --no-restore
git diff --check
```

## 本地结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 构建与 CTest | 15/15 通过 |
| 规则回归/压力 | 30 cases，8,685 assertions，0 failures；2,048 seeds |
| 客户端安全 API 契约 | 463 assertions，0 failures |
| legacy v1 wire 金标 | 31 assertions，0 failures |
| native ABI 契约 | 100,876 assertions，0 failures |
| C11 header/link、动态加载与安装后 consumer | 全部通过；精确 14 个导出可解析并调用 |
| Windows DLL 审计 | PE x86-64；无 C++ 导出；不依赖 `MSVCP140*` / `VCRUNTIME140*` |
| C# managed | 67/67，0 skipped；Godot/net8 Debug + Release 与测试/net10 Release 均零警告 |
| Python legacy | 10/10（overlay 5 + protocol 5） |
| Python CI 工具契约 | 52/52（native audit 5 + Godot export 9 + timeout 3 + Gate 3B report 6 + Gate 3C report 15 + Gate 4A report 14） |
| 默认 3D Godot signal E2E | 148 次成功命令；动作 0～10；真实 raycast/click/drag/键盘；自然终局、重开、投降；唯一 marker |
| legacy 2D Godot signal E2E | 同一 148 步闭环；共享 surface intent；不伪报 3D 证据 |
| `Resolving` 恶意 DTO 隐私 | 2 个完整帧；Label/材质/metadata/tooltip/碰撞/回调/drag token 泄露 0；期间无 viewer 数据 |
| 当前实现 DLL | 461,824 bytes；SHA-256 `bc248192a1896a0ba4fed92f4d08141a1f87c584d52425aeb3620cef0236d8e9` |
| 三分辨率 `Action` / `Resolving` 截图 | 中央施放区不存在、双方后排归属明确、`Resolving` 匿名；其余状态仍为人工硬门 |
| `dotnet format --verify-no-changes` | 4 个项目通过 |
| `git diff --check` | 通过 |

六张本地截图为：

- `build/gate4a1-action-1280x720.png`、`build/gate4a1-resolving-1280x720.png`
- `build/gate4a1-action-1600x900.png`、`build/gate4a1-resolving-1600x900.png`
- `build/gate4a1-action-2560x1440.png`、`build/gate4a1-resolving-2560x1440.png`

这些截图是本地验收证据，不提交 Git。

## 远端 CI 与制品

| Job | 配置 | 结果与制品 |
|---|---|---|
| [`linux-gcc`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32696171327/job/97338532107) | GCC Release；2,048 seeds | 通过；native 安装/consumer/审计；`scgs-native-v04-linux-x86_64-gcc` |
| [`linux-clang-sanitizers`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32696171327/job/97338532242) | Clang Debug + ASan/UBSan；256 seeds | 通过；sanitized consumer/审计；`scgs-native-v04-linux-x86_64-clang-asan-ubsan` |
| [`windows-msvc`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32696171327/job/97338532251) | MSVC Release x86-64；2,048 seeds；Godot Windows | 通过；managed 67/67；默认 3D 当前/导出/ZIP + legacy 2D 源码整局 |
| [`macos-arm64`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32696171327/job/97338532244) | AppleClang Release ARM64；2,048 seeds；Godot macOS | 通过；managed 67/67；默认 3D 当前/导出/ZIP + legacy 2D 源码整局 |

Run `32696171327` 上传的 6 个 CI 验收制品如下。Gate 4A.1 沿用 Gate 4A 的 schema 与 artifact 名，实际源码身份由 checkout SHA `4be6e09ef9edc363b064b4a7aaba4551359ecb05` 区分。字节数和 SHA-256 是 GitHub Actions artifact archive 元数据，不是 archive 内部单个文件的摘要。

| 制品 | 字节 | GitHub artifact SHA-256 |
|---|---:|---|
| `scgs-native-v04-linux-x86_64-gcc` | 635,306 | `22704bc42e1195372e9953ad44dd0be5507a0bd0ff600f49f360b76d93200026` |
| `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 5,875,921 | `6afce85137c4abbbd118187ad5f2d135073ea9e154df9f15c143d8ba227e8956` |
| `scgs-native-v04-windows-x86_64-msvc` | 253,055 | `9927a2e0a22b47ecf033ba98049d1b893ac9a6eb353ec566e2de477da100ccb7` |
| `SomeCardGameShit-gate4a-windows-x86_64` | 92,841,708 | `1368c7b02c0b1ce2c9b8398fef6c3675bdae276da20de9147a343180b2cf12a3` |
| `scgs-native-v04-macos-arm64-appleclang` | 489,499 | `0b682801e68224ce72d77a3649360cc5964496ddf3cd210472346e3d0e021673` |
| `SomeCardGameShit-gate4a-macos-arm64` | 78,078,934 | `6bb3b717724104e34f87734caa0b7d7be3f2e794b4fcf465afefdda912087e62` |

Windows 客户端内 DLL 与 EXE 同目录。macOS artifact 保留 `.app` 执行权限，dylib 位于 `.app/Contents/Frameworks`，finalize 后重新 ad-hoc codesign；所有 Mach-O 为 ARM64-only。两个包均通过 GPL、Godot MIT/COPYRIGHT、.NET、nlohmann MIT、Noto OFL 和第三方声明审计。它们是未正式签名/未公证的 CI 验收制品，不是发布版本。

## Gate 4A.1 规则、查询与界面验收

- `CastSpell` 在支付前验证玩家、阶段、手牌、目标、具体策略位和费用；缺失/越界格位返回 `InvalidSlot`，已占用格位返回 `TacticZoneFull`，失败不支付、不发事件、不增加 revision。
- 合法行动按“空策略位 × 合法目标 × 预支选择”展开；三格全满时不枚举 `CastSpell`，支付预览与同 revision 命令共享验证。
- 法术正面进入精确格位并保持到自身链环结算；无响应、单层/三层 LIFO、目标失效、法术自身致命、致命响应和响应期间投降均有回归覆盖。
- 终局清理只把已声明未结算的伏策按 LIFO 送墓，不伪造 `TrapActivated`；未声明伏策保留，原法术随后送墓，`MatchEnded` 唯一且为最后事件。
- 不变量只允许响应栈中唯一的待结算法术占据其记录格位；专门负测拒绝非响应状态下长期停留的法术。
- Hotseat 固定按“格位 → 目标 → 预支”推进；即使只剩一个空位也必须明确点击。点击/拖拽收敛到相同带 `slot` 命令，直接拖向目标、对方/已占后排或 `CastZone` 均在访问 native 前拒绝。
- 默认 3D 与 legacy 2D 都没有中央施放区节点、文字、碰撞或信号；响应快照在所选己方后排公开显示法术，结算后移入墓地。

## 冻结契约与发布前硬门

- `native_api_v04.h`、ABI 1.0、schema 1、14 个 C 导出、JSON DTO、`IScgsGameSession`、`ActionKind` 数值和 legacy v1 wire 保持不变。
- `GameCommand.slot` 对 `CastSpell` 从可选语义变为必填；C++ 强类型 `Game::cast_spell` 不保留自动选择首个空位的重载。
- 本轮没有增加卡牌、正式素材、动画、音效、C ABI 导出或 JSON 字段。
- 已验收三种分辨率的 `Action` / `Resolving`；目标选择、响应目标、`Covered` 等完整逐状态人工矩阵仍需发布前复核。
- 物理 Apple Silicon、未安装 Visual Studio 的 Windows 机器及两名真人热座整局仍是发布标签前硬门。

本文件记录实现提交 `4be6e09` 及其 GitHub Actions run `32696171327` 的可复现证据。包含本报告的后续文档提交不改变产品代码，但仍必须由同一四项工作流重新验证分支尖端。
