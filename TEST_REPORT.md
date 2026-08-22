# Gate 0+1+2 测试报告

**日期：** 2026-08-22

**分支：** `codex/godot-hotseat-gate2`

**项目基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`

**Gate 2 前置基线：** `f048d11ded5d2bd2579156d8101ff38b2de2b263`

**最终被测实现：** `189239d827dafe34e0caa493a8bb7a55bd31a4d8`

**Gate 2 提交：** `389db85`（C ABI 与测试）、`1f44edf`（规范与交接文档）、`189239d`（sanitizer 链接与 ELF 导出收口）。

**范围：** 在 Gate 0+1 引擎加固之上交付版本化 `scgs_v04` C ABI、schema 1 UTF-8 JSON、C11/动态加载/直接 C++ 对照测试，以及 Windows、Linux、macOS ARM64 安装制品。本轮不包含 Godot 工程、C# P/Invoke、场景或 UI。

## 结论

最终实现已在本机 MSVC Release 与 Debug 下完成构建，两个配置的 CTest 均为 **9/9**。Release 规则压力测试覆盖 **2,048 seeds**；原生 ABI 契约测试完成 **98,793 assertions**，并包含 direct C++/ABI 同 seed 整局逐步对照。安装后的 CMake package 可由独立 C11 consumer 查找、链接和执行，DLL 导出审计确认架构为 x86-64 且仅有规范规定的 14 个 C 符号。

[GitHub Actions run 32565543772](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32565543772) 在最终实现提交上 **4/4 jobs 全绿**：GCC Release、Clang ASan/UBSan、MSVC Release、macOS ARM64 Release；每个 job 均执行构建、完整 CTest、安装、外部 package consumer、动态库架构/导出审计并上传安装制品。

## 执行环境

```text
OS: Windows 11, 10.0.26200.0, AMD64
Generator: Visual Studio 17 2022, x64
MSVC: 19.44.35228.0
CMake: 3.31.6（项目最低要求 3.25）
Clang/clang-cl: 22.1.8
Python: 3.10.11
Git: 2.54.0.windows.1
```

远端矩阵由 GitHub-hosted `ubuntu-latest`、`windows-latest` 与 `macos-15` 执行，Python 固定为 3.12.8。`macos-15` job 显式指定 `CMAKE_OSX_ARCHITECTURES=arm64`。

## 本地实际命令

以下 PowerShell 命令在仓库根目录执行：

```powershell
$cmake = "C:\Users\ASUS\AppData\Local\Programs\cmake-3.31.6-windows-x86_64\bin\cmake.exe"
$ctest = "C:\Users\ASUS\AppData\Local\Programs\cmake-3.31.6-windows-x86_64\bin\ctest.exe"

& $cmake -S . -B build/gate2-final-msvc -A x64 `
  -DSCGS_WARNINGS_AS_ERRORS=ON `
  -DSCGS_ENABLE_LEGACY_YGO2_TESTS=ON

& $cmake --build build/gate2-final-msvc --config Release --parallel 2
$env:SCGS_SMOKE_SEEDS = "2048"
& $ctest --test-dir build/gate2-final-msvc -C Release --output-on-failure

& $cmake --build build/gate2-final-msvc --config Debug --parallel 2
$env:SCGS_SMOKE_SEEDS = "256"
& $ctest --test-dir build/gate2-final-msvc -C Debug --output-on-failure

& .\build\gate2-final-msvc\Release\scgs_tests.exe
& .\build\gate2-final-msvc\Release\scgs_client_api_tests.exe
& .\build\gate2-final-msvc\Release\scgs_wire_tests.exe
& .\build\gate2-final-msvc\Release\scgs_native_api_tests.exe
& .\build\gate2-final-msvc\Release\scgs_demo.exe --verify

& $cmake --install build/gate2-final-msvc --config Release `
  --prefix build/stage-gate2-final-msvc
& $cmake -S scripts/native-package-smoke `
  -B build/package-smoke-gate2-final -A x64 `
  -DCMAKE_PREFIX_PATH="$PWD/build/stage-gate2-final-msvc"
& $cmake --build build/package-smoke-gate2-final --config Release --parallel 2
$env:PATH = "$PWD\build\stage-gate2-final-msvc\bin;$env:PATH"
& .\build\package-smoke-gate2-final\Release\scgs_native_v04_package_smoke.exe

& "C:\Users\ASUS\AppData\Local\Programs\Python\Python310\python.exe" `
  scripts/audit_native_artifact.py `
  --library build/stage-gate2-final-msvc/bin/scgs_v04.dll `
  --architecture x86_64
& "C:\Users\ASUS\AppData\Local\Programs\Python\Python310\python.exe" `
  -m unittest -v tools.tests.test_apply_ygo2_overlay tools.tests.test_protocol_contract

git diff --check
git diff --cached --check
```

Clang 22.1.8 + Ninja 的独立 Release 配置（关闭 legacy Python 测试）另行完成 **7/7 CTest**。Linux sanitizer 的真实 `clang`/`clang++`、ASan/UBSan 和外部 package consumer 由最终 CI job 验证。

## 本地结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 构建与 CTest | 9/9 通过；2,048 seeds |
| MSVC Debug `/W4 /WX` 构建与 CTest | 9/9 通过；256 seeds |
| Clang 22.1.8 + Ninja Release | 7/7 通过；legacy 显式关闭 |
| 规则回归/压力 | 30 cases，8,607 assertions，0 failures |
| 客户端安全 API 契约 | 397 assertions，0 failures |
| legacy v1 wire 金标 | 31 assertions，0 failures |
| native ABI 契约 | 98,793 assertions，0 failures |
| C11 header/link consumer | 通过；公共头重复 include 通过 |
| 运行时动态加载 | 14 个规定导出全部可解析并调用 |
| 安装后 CMake package consumer | `find_package(scgs_native_v04 1.0 CONFIG REQUIRED)`、链接、启动、快照、销毁均通过 |
| DLL 审计 | PE x86-64；精确 14 个 `scgs_v04_*` 导出；无 C++ 符号 |
| legacy Python | 10 tests，全部通过 |
| 记录场景 `scgs_demo --verify` | `verified: true`，不变量成立 |
| `git diff --check` / staged check | 通过 |

CTest 的 9 个目标为：

1. `scgs_unit_tests`
2. `scgs_client_api_contract`
3. `scgs_documented_scenario`
4. `scgs_wire_frozen_golden`
5. `scgs_native_api_c_contract`
6. `scgs_native_api_contract`
7. `scgs_native_api_dynamic_load`
8. `scgs_ygo2_overlay_patcher`
9. `scgs_protocol_contract`

## 远端 CI 与制品

| Job | 配置 | 压力 seeds | 结果与制品 |
|---|---|---:|---|
| `linux-gcc` | GCC Release | 2,048 | 通过；`scgs-native-v04-linux-x86_64-gcc` |
| `linux-clang-sanitizers` | Clang Debug + ASan/UBSan | 256 | 通过；`scgs-native-v04-linux-x86_64-clang-asan-ubsan` |
| `windows-msvc` | MSVC Release x86-64 | 2,048 | 通过；`scgs-native-v04-windows-x86_64-msvc` |
| `macos-arm64` | AppleClang Release ARM64 | 2,048 | 通过；`scgs-native-v04-macos-arm64-appleclang` |

首轮 Gate 2 run `32565185410` 的 Windows/macOS 已通过，但 Linux 暴露出 C consumer sanitizer 链接驱动与 ELF 隐式 C++ 导出问题。提交 `189239d` 改用 C++ linker driver 承载 sanitizer runtime，并用 ELF version script 将导出面收紧为精确 14 个 C 符号；上述最终 run 随后四项全绿。审计规则未因失败而放宽。

## Gate 2 关键覆盖

- C11 头文件不暴露 STL、异常或 C++ 类布局；ABI 版本、schema、固定宽度参数、调用约定与 14 个导出被冻结。
- 所有 JSON 输入执行 1 MiB 上限与 UTF-8 校验；输出采用调用方所有的两段式缓冲区，所需长度包含尾随 NUL，缓冲区不足时不部分写入。
- 64 位 token handle 不复用；未知/已销毁 handle、空指针、非法 enum、错误 schema、错误阶段与过期 revision 均返回稳定 native error，异常不越过 C 边界。
- `start` 使用候选状态提交，失败不改变现有比赛；失败查询/命令不改变状态、事件或 revision，成功命令只增加一次 revision。
- direct `Game` 与 ABI 在同 seed 整局的每一步比较双 viewer 快照、全部合法行动、支付预览、选中动作结果、revision 和脱敏事件，直至终局。
- `ActionKind` 0~10 均至少成功提交一次；定向覆盖预支、燃耗、法术、进化、组件部署、设伏、响应跳过/发动、攻击和投降。
- ABI-only 无界面代理完成固定牌组整局，不读取 `PlayerState`；双观看者事件游标互不消费，敌方手牌、背面伏策及隐藏事件不泄露身份。
- Windows PE、Linux ELF 与 macOS Mach-O 均审计目标架构与精确导出；安装树可由独立 C consumer 使用版本化 CMake package 消费。

## 保持冻结与本轮边界

- legacy v1 wire 的消息 ID、字段顺序、长度、字节序和金标字节未改变；Python legacy 测试仍默认开启且不得静默少跑。
- 本轮只交付 native boundary，不创建 Godot/C# 客户端；Godot 4.7.2 .NET 与 .NET SDK 10.0.400 留给 Gate 3。
- schema 1 仅承诺当前两副固定牌组的 Alpha 闭环；消费者须忽略未知输出字段，但未来 schema 的跨版本兼容仍需独立测试。
- 不承诺 `std::shuffle` 跨不同标准库逐字一致；实际 seed 与先手信息已进入快照/事件。
- Web 明确不支持。
