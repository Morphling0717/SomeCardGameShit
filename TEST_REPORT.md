# Gate 0+1 测试报告

**日期：** 2026-08-22

**分支：** `codex/godot-hotseat-gate1`

**基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`

**被测实现提交：** `a952c5282eca4adcb6325d2fc027ca21c0568d4e`

**范围：** Godot 客户端化前置 Gate 0+1；不包含 C ABI、Godot 场景/UI 或正式美术。

## 结论

本次工作树在本机 MSVC Release 与 Debug 下均完成构建，两个配置的 CTest 都是 **6/6**。Release 规则压力测试覆盖 **2,048 seeds**，legacy v1 wire 金标和两组 Python legacy 契约测试均通过。失败命令原子性、revision、观看者快照/事件隐私及无界面固定牌组整局代理均由新增客户端 API 契约测试覆盖。

本分支按要求**未推送**，因此本次 commit 的 GitHub Actions **未运行，不能声称 CI 已绿**。本机未安装 GCC、Clang、Godot 4.7.2 .NET 或 .NET SDK 10.0.400；GCC Release 与 Clang ASan/UBSan 只完成了 CI 配置，尚待分支获准推送后验证。`global.json` 与文档中的 Godot/.NET 版本锁定不等于本机运行验证。

## 执行环境

```text
OS: Windows 11, 10.0.26200.0, AMD64
Generator: Visual Studio 17 2022, x64
MSVC: 19.44.35228.0
CMake: 3.31.6-msvc6（项目最低要求 3.25）
Python: 3.10.11
Git: 2.54.0.windows.1
```

## 实际命令

以下 PowerShell 命令在仓库根目录执行；`$cmake` 与 `$ctest` 指向 Visual Studio Build Tools 自带的 CMake 3.31.6：

```powershell
$cmake = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ctest = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"

& $cmake -S . -B build/final-msvc -G "Visual Studio 17 2022" -A x64 `
  -DSCGS_WARNINGS_AS_ERRORS=ON -DSCGS_ENABLE_LEGACY_YGO2_TESTS=ON
& $cmake --build build/final-msvc --config Release --parallel
& $ctest --test-dir build/final-msvc -C Release --output-on-failure

& $cmake --build build/final-msvc --config Debug --parallel
& $ctest --test-dir build/final-msvc -C Debug --output-on-failure

& .\build\final-msvc\Release\scgs_tests.exe
& .\build\final-msvc\Release\scgs_client_api_tests.exe
& .\build\final-msvc\Release\scgs_wire_tests.exe
& .\build\final-msvc\Release\scgs_demo.exe --verify

$env:SCGS_SMOKE_SEEDS = "2048"
& .\build\final-msvc\Release\scgs_tests.exe

& "C:\Users\ASUS\AppData\Local\Programs\Python\Python310\python.exe" `
  -m unittest -v tools.tests.test_apply_ygo2_overlay tools.tests.test_protocol_contract

& $cmake -S . -B build/final-no-legacy -G "Visual Studio 17 2022" -A x64 `
  -DSCGS_WARNINGS_AS_ERRORS=ON -DSCGS_ENABLE_LEGACY_YGO2_TESTS=OFF

git diff --check
git diff --cached --check
```

## 结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 构建 | 通过 |
| MSVC Release CTest | 6/6 通过 |
| MSVC Debug `/W4 /WX` 构建 | 通过 |
| MSVC Debug CTest | 6/6 通过 |
| 规则回归（默认 32 seeds） | 30 cases，543 assertions，0 failures |
| 规则 Release 压力（2,048 seeds） | 30 cases，8,607 assertions，0 failures |
| 客户端 API 契约 | 397 assertions，0 failures |
| legacy v1 wire 金标 | 31 assertions，0 failures |
| legacy Python | 10 tests，全部通过 |
| 记录场景 `scgs_demo --verify` | `verified: true`，不变量成立 |
| legacy Python 测试关闭配置 | 配置成功；证明开关可显式关闭 |
| `git diff --check` / staged check | 通过 |

CTest 的 6 个目标为：

1. `scgs_unit_tests`
2. `scgs_client_api_contract`
3. `scgs_documented_scenario`
4. `scgs_wire_frozen_golden`
5. `scgs_ygo2_overlay_patcher`
6. `scgs_protocol_contract`

## 关键覆盖

- 结束回合清理/PP 清零事件顺序；法术响应、反制不过、真正的反制 → 响应 → 原行动 LIFO。
- 声明前完整目标校验；响应中目标失效只跳过依赖效果，已支付成本不回滚，其余效果继续。
- 致命攻击、疲劳、投降、致死多效果伏策与异常开局都只产生一个 `MatchEnded`，终局后冻结。
- 组件 donor 原位置部署；非法 `PlayerId` 和非法目标枚举无副作用。
- 进化解锁前不充能；先手解锁 2、后手解锁 3；解锁后充能封顶 4。
- 强制/随机先手、实际 seed 与开局事件元数据；同一工具链同 seed 的先手与洗牌顺序一致。
- 观看者快照隐藏敌方手牌身份和背面伏策身份；调度替换抽牌、抽牌和设伏事件按观看者脱敏；设伏牌翻开后晚读历史仍不泄露。
- 两名观看者的事件游标互不消费；成功命令 revision 只加一，错误/过期命令不改变状态、事件或 revision。
- 无界面代理只走“快照 → 查询 → 命令 → 事件”，完成现有固定牌组整局。

## Legacy wire 冻结

协议实现与金标测试文件未修改，金标字节仍通过。当前投影语义是：

- PlayerState flags bit 1：`deploy_used_this_turn`；
- UnitState flags bit 3：`deployed_from_standby && entered_this_turn`；
- 每名玩家当前有 3 个策略位，但 legacy v1 字节布局、消息 ID、字段顺序、长度和字节序不变。

## 尚未验证

- GitHub Actions：分支未推送，所以没有本次远端运行。
- GCC Release：本机无 GCC；CI 已配置 Release 与 2,048 seeds。
- Clang ASan/UBSan：本机无 Clang；CI 已配置 sanitizer 与 256 seeds。
- Godot/.NET 客户端：本轮不创建客户端工程，且本机没有锁定版本的工具链。
- `std::shuffle` 跨不同标准库的逐字重现：本轮不承诺；仅验证同工具链、同 seed 可复现。
