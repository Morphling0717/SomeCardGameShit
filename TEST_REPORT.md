# Gate 0+1+2+3A 测试报告

**日期：** 2026-08-22

**分支：** `codex/godot-hotseat-gate3`

**项目基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`

**Gate 3 前置基线：** `codex/godot-hotseat-gate2@83714270c07b009ac4c3bf79ddb9d343b777213d`

**最终被测实现：** `3a2286a484156d88dc767d7b5e3a0050fca01e12`

**Gate 3A 提交：** `cefe73d`（托管边界、Godot 桌面骨架与首张快照）、`0a7011d`（桌面导出与 macOS ARM64 template 收口）、`3a2286a`（冷导入和 Debug/Release 程序集收口）。

**范围：** 在 Gate 0+1 引擎加固和 Gate 2 `scgs_v04` C ABI 上交付纯托管 `Scgs.Client`、Godot 4.7.2 .NET 桌面工程、热座遮挡后的第一张真实 viewer 快照，以及 Windows x86-64 / macOS ARM64 可启动导出。Gate 3A 的界面只读并停在 Mulligan；不包含调度提交或完整对局。

## 结论

最终实现已通过本机 MSVC Release 的 **11/11 CTest**、**2,048 seeds** 规则压力、**27/27** 托管测试、Godot 冷导入/原生快照 smoke 和 1600×900 / 1280×720 视觉检查。原生回归保持 30 cases / 8,607 assertions、客户端安全 API 397 assertions、legacy wire 31 assertions、native ABI 98,793 assertions；新增 Python 制品审计为 **12/12 tests**。

[GitHub Actions run 32577089388](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32577089388) 在被测提交上 **4/4 jobs 全绿**：GCC Release、Clang ASan/UBSan、MSVC Release + Windows Godot 导出、AppleClang ARM64 Release + macOS Godot 导出。Windows 与 macOS 均实际完成 locked restore、C# Debug/Release 构建、27 项托管测试、Godot 冷导入、当前工程 smoke、目标平台导出、导出结构/架构/许可证审计和导出程序真实启动。

两个当前工程和两个导出程序都输出唯一成功标记：

```text
SCGS_GODOT_CI_SMOKE_OK viewer=Player0 phase=Mulligan revision=0 get_view_calls=1 disposed=true
```

这证明遮挡揭示前没有请求快照、揭示后只读取一次 viewer 0 快照，并在退出前释放 session；不证明 Gate 3B 的完整热座对局已经实现。

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

远端矩阵使用 `ubuntu-latest`、`windows-latest` 与 `macos-15`，Python 固定为 3.12.8；Windows/macOS 通过 `global.json` 精确选择 .NET SDK 10.0.400，并安装校验哈希后的 Godot 4.7.2 .NET 编辑器与 `4.7.2.stable.mono` export templates。最终 macOS bundle 审计为 ARM64-only。

## 本地实际命令

以下 PowerShell 命令在仓库根目录执行；`SCGS_GODOT_EXE` 指向已校验的 Godot 4.7.2 .NET 可执行文件：

```powershell
$cmake = "C:\Users\ASUS\AppData\Local\Programs\cmake-3.31.6-windows-x86_64\bin\cmake.exe"
$ctest = "C:\Users\ASUS\AppData\Local\Programs\cmake-3.31.6-windows-x86_64\bin\ctest.exe"

& $cmake -S . -B build/gate3-final-msvc -A x64 `
  -DSCGS_WARNINGS_AS_ERRORS=ON `
  -DSCGS_ENABLE_LEGACY_YGO2_TESTS=ON
& $cmake --build build/gate3-final-msvc --config Release --parallel 2
$env:SCGS_SMOKE_SEEDS = "2048"
& $ctest --test-dir build/gate3-final-msvc -C Release --output-on-failure

& $cmake --install build/gate3-final-msvc --config Release `
  --prefix build/stage-gate3-final-msvc
python scripts/audit_native_artifact.py `
  --library build/stage-gate3-final-msvc/bin/scgs_v04.dll `
  --architecture x86_64

$env:SCGS_NATIVE_LIBRARY = "$PWD\build\stage-gate3-final-msvc\bin\scgs_v04.dll"
$env:SCGS_V04_NATIVE_PATH = $env:SCGS_NATIVE_LIBRARY
python scripts/ci/run_managed_gate3.py

python scripts/stage_godot_native.py `
  --library $env:SCGS_NATIVE_LIBRARY `
  --destination-root client/godot/native `
  --target windows-x86_64

python scripts/ci/run_with_timeout.py `
  --timeout 180 --forbid-output "SCRIPT ERROR:" --forbid-output "ERROR:" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot --import

python scripts/ci/run_with_timeout.py `
  --timeout 30 --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --forbid-output "SCRIPT ERROR:" --forbid-output "ERROR:" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke "--native-library=$env:SCGS_NATIVE_LIBRARY"

python -m unittest `
  scripts.tests.test_audit_native_artifact `
  scripts.tests.test_audit_godot_export -v

git diff --check
git diff --cached --check
```

视觉检查另以同一真实 DLL 运行 `--ci-smoke --ci-screenshot=<绝对路径>`，分别生成 1600×900 与 1280×720 截图；检查了中文字体、主要区域、对手牌背、己方手牌身份和完全不透明遮挡。

## 本地结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 构建与 CTest | 11/11 通过；2,048 seeds |
| 规则回归/压力 | 30 cases，8,607 assertions，0 failures |
| 客户端安全 API 契约 | 397 assertions，0 failures |
| legacy v1 wire 金标 | 31 assertions，0 failures |
| native ABI 契约 | 98,793 assertions，0 failures |
| C11 header/link、动态加载与安装后 consumer | 全部通过；14 个导出可解析并调用 |
| Windows DLL 审计 | PE x86-64；精确 14 个 `scgs_v04_*`；不依赖 `MSVCP140*` / `VCRUNTIME140*` |
| C# managed | 27/27；Godot/net8 Debug + Release 与测试/net10 Release 均零警告 |
| C# + 当前 DLL 集成 | ABI/create/start、双 viewer、全部查询、一次调度、事件脱敏、revision、dispose 均通过 |
| legacy Python overlay/protocol | 10/10 tests |
| Python native/export 审计 | 12/12 tests（5 native + 7 Godot export） |
| Godot 冷导入与当前工程 smoke | 通过；真实 viewer 0 Mulligan 快照；`GetView` 恰好 1 次 |
| 1600×900 / 1280×720 视觉检查 | 中文无缺字；主要区域无重叠；对手手牌只显示牌背；遮挡完全不透明 |
| `git diff --check` / staged check | 通过 |

CTest 的 11 个目标为：

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

## 远端 CI 与制品

| Job | 配置 | 结果与制品 |
|---|---|---|
| `linux-gcc` | GCC Release；2,048 seeds | 通过；`scgs-native-v04-linux-x86_64-gcc` |
| `linux-clang-sanitizers` | Clang Debug + ASan/UBSan；256 seeds | 通过；`scgs-native-v04-linux-x86_64-clang-asan-ubsan` |
| `windows-msvc` | MSVC Release x86-64；2,048 seeds；Godot Windows export | 通过；native + `SomeCardGameShit-gate3a-windows-x86_64` |
| `macos-arm64` | AppleClang Release ARM64；2,048 seeds；Godot macOS export | 通过；native + `SomeCardGameShit-gate3a-macos-arm64` |

Run `32577089388` 上传的 6 个未正式签名 CI 验收制品：

| 制品 | 字节 | SHA-256 |
|---|---:|---|
| `scgs-native-v04-linux-x86_64-gcc` | 624,302 | `bde9b733b34944b8748d7b5f1c46b06002b8740ff825433e00a82d1ae8ad96b0` |
| `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 5,776,138 | `96b9b4bdf40199fd6b81d9e4154f8875a123681b3883ea2c280f8f7ff5014a3d` |
| `scgs-native-v04-windows-x86_64-msvc` | 249,648 | `5972b5b134d4870c3f4d9a41fabbd4250a9e702325ebadb81f0158da56757b7c` |
| `SomeCardGameShit-gate3a-windows-x86_64` | 92,514,331 | `554a16a0747ed5943366231e5b38268983dc3481afa314562457c3e68b73df2d` |
| `scgs-native-v04-macos-arm64-appleclang` | 477,100 | `b6fba1e708fa6ffe89787c64954d476c4e6a0168f047b4b8be47ee8cca74ea91` |
| `SomeCardGameShit-gate3a-macos-arm64` | 77,758,702 | `61521138ea1532f12811ba395e2e5909cf05a0343e49e961915c1a21ec2bc5ba` |

Windows 客户端 zip 内 DLL 与 EXE 同目录；macOS 客户端 artifact 保留 `.app` 执行权限，所有 Mach-O 均为 ARM64-only，dylib 位于 `Contents/Frameworks`，导出后已重新 ad-hoc codesign。两个导出包都通过 GPL、Godot MIT/COPYRIGHT、.NET、nlohmann MIT、Noto OFL 和第三方声明审计。

## 两次失败如何收口

- Run `32571678452` 暴露 `--editor --quit-after 2` 可能在首次资源导入完成前退出；未放宽错误检查，改为显式等待 `--import`。
- Run `32576491298` 进一步暴露项目全局 Theme 在冷导入前引用字体，以及当前工程 smoke 需要 Debug Godot 程序集。最终实现把 Theme 绑定迁到 `Bootstrap` 场景根节点，并在 managed contract 中同时构建 Debug 与 Release。
- macOS 官方模板为 universal；CI 只从固定哈希的官方 archive 临时派生 ARM64 release template，校验输入架构、`lipo` 输出、zip 路径/执行权限和原子替换。派生模板及所有 DLL/dylib 均不提交 Git。

Run `32577089388` 随后在同一实现提交上四项全绿；审计规则和 smoke 断言未因失败而放宽。

## 保持冻结与 Gate 3A 边界

- legacy v1 wire 的消息 ID、字段顺序、长度、字节序和金标字节未改变；Python legacy 测试仍默认开启且不得静默少跑。
- 原生 `native_api_v04.h`、schema 1 与精确 14 个 ABI 导出未改变；Windows 产品构建改用静态 MSVC runtime，不要求目标机预装 VC runtime。
- C# 与 Godot 不读取 `PlayerState`、不复算规则，只消费 `IScgsGameSession` 的安全 DTO、查询、命令与观看者事件游标。
- Gate 3A 只显示 Mulligan 的第一张只读安全快照。调度 UI、出牌、攻击、进化、部署、伏策响应、结果、重开与真人完整一局属于 Gate 3B。
- macOS 本轮仅验证 CI ARM64、ad-hoc 签名和 headless smoke；Developer ID、公证与物理 Mac 真人测试尚未完成。
- Alpha 仍只承诺现有 `midrange` / `advance` 两副固定牌组；Web 与 Linux 正式客户端不支持。
