# Gate 0+1+2+3A+3B+3C+4A+4A.1+4B-R2 测试报告

**日期：** 2026-08-25（Asia/Shanghai）

**分支：** `codex/godot-hotseat-gate4b-r2-battle-presentation`

**项目基线：** `codex/godot-hotseat-gate4b-visual-baseline@1370491ade6e779d83fa44334dd4b7e6920f6a9c`

**主要实现提交：** `19159ee0613159e4761bbf2f9acea77efdd82874`

**被测实现尖端：** `cca04b5c9a0e4793c98d8f765527a7a1c51de804`

**范围：** 在 Gate 4B-R1 的 34 项原创临时素材、产品菜单、设置、3D 战场与热座隐私状态机上，重写相机相对 2.5D 手牌、费用/攻击/生命/倒计时徽章、稳定镜头、安全区、战场托座和 HUD 信息架构；visual-suite 升级为 Gate 4B-R2 schema 4、16 状态。本轮不修改 C++ 规则、DTO、`IScgsGameSession`、C ABI、schema 1、精确 14 个导出、固定牌组或 legacy v1 wire。

## 结论

[GitHub Actions run 32766050188](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32766050188) 在实现尖端 `cca04b5` 上 **4/4 jobs 全绿**：

- Linux GCC Release：通过，0.85 分钟；
- Linux Clang ASan/UBSan：通过，1.75 分钟；
- Windows MSVC Release + Godot：通过，82.42 分钟；
- macOS AppleClang ARM64 Release + Godot：通过，4.55 分钟。

Windows 和 macOS 均完成 locked managed restore、Godot import、默认 3D 当前工程整局、legacy 2D 当前工程整局、目标平台导出、结构/架构/许可证审计、导出程序整局、ZIP 解包复审及再次整局。Windows 另外完成四种真实窗口尺寸的 16 状态视觉/性能套件与 1600×900 golden。

第一次实现 run `32763313348` 的三项平台 job 已绿，但 Windows 在视觉报告与 golden 的素材清单哈希比较处失败。根因是 Windows 干净 checkout 的 `core.autocrlf=true` 把清单从 LF 转成 CRLF；画面 schema、1280×720/1600×900 结构验证及云端 1600×900 的 16/16 张 golden 比较本身均通过。`cca04b5` 用 `.gitattributes` 把跨平台身份清单锁为 LF，并新增契约测试；模拟 Windows checkout 的 SHA-256 恢复为冻结值，随后完整 run 全绿。没有删除校验或放宽阈值。

## 冻结契约

- 游戏 ABI/JSON：ABI 1.0、schema 1；
- 原生导出：精确 14 个 `scgs_v04_*` C 符号；
- full-match：Gate 4A schema 3，继续继承 Gate 3C 的两局 signal 闭环、11 种 `ActionKind`、交接遮挡、两帧公共结算和隐私证据；
- visual-suite：Gate 4B-R2 schema 4，与游戏 schema 相互独立；
- 1600×900 golden：必须人工检查并通过显式 `--accept` 脚本更新，CI 不自动覆盖；
- legacy v1 wire 字节未改变。

## 本地自动化结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 与 CTest | 16/16 通过；规则压力使用 2,048 seeds |
| C# managed | 75/75，0 failed，0 skipped；Debug/Release 均 0 warning、0 error |
| Python 全集 | 77/77 通过 |
| R2 视觉、golden 元数据与导出审计子集 | 34/34 通过 |
| 视觉素材 | 34/34；路径、唯一 SHA-256、比例、用途与 prompt 摘要通过 |
| 素材清单 SHA-256 | `550cee89ccb1b384149d85aa45725474371b022a646fbef8de28d4c9bbae8eac` |
| `dotnet format --verify-no-changes` | 4 个项目通过 |
| 原生 DLL | x86-64、14 个 C 导出、无 C++ 导出、无动态 `MSVCP140*` / `VCRUNTIME140*` |
| Godot import 与 full-match | 默认 3D、隐藏 legacy 2D 均通过 |
| `git diff --check` | 通过 |

Windows 本机 Clang ASan 与系统 VCRUNTIME/debug CRT 存在运行库不兼容，因此不用于本轮通过结论；同提交的 Linux Clang ASan/UBSan job 是 sanitizer 权威结果并已全绿。

## 完整对局与隐私

默认 3D、legacy 2D、原始 Windows 导出、ZIP 往返以及下载后的 CI 包复验均继续满足：

- 两局合计 148 次成功命令，第一局自然终局为 52 次回合结束；
- `ActionKind` 0～10 全覆盖；
- 71 次完全遮挡与 71 次主动揭示，揭示前 viewer 读取为 0；
- 每条命令的中立 `Resolving` 公共投影至少完整绘制 2 帧；
- 重开、投降终局和 2 次 session 释放均完成；
- 私密 DTO、定义/实例 ID、卡图/材质、tooltip、metadata、回调、tween、碰撞与 drag token 泄露均为 0；
- 点击、拖拽、键盘与同 revision 规范命令路径保持一致。

下载后的 CI Windows 包在本机再次完成 x64/14 导出/静态运行库/许可证/提交号审计，并只输出一次：

```text
SCGS_GODOT_CI_SMOKE_OK result=Player1Won revision=3 steps=148 covers=71 reveals=71 premature_view_calls=0 disposed=true
```

## Gate 4B-R2 视觉结果

每种尺寸均捕获且仅捕获 16 个状态：`menu`、`match-setup`、`error`、`mulligan`、`covered`、`action`、`hand-one`、`hand-five`、`hand-ten`、`hand-hover`、`field-readability`、`source-selection`、`slot-or-target-selection`、`resolving`、`reaction`、`result`。

每次捕获要求连续两个内容一致的 `FramePostDraw`，同时验证全帧、桌面、双方主战者、手牌、HUD 和费用/身材/倒计时 ROI。手牌证据要求 1/5/10 张精确计数、悬停计数、最低屏幕卡高与最大 ±8° roll；`field-readability` 验证真实 `Label3D`、底板深度间距和最终 GPU 徽章像素，而不是旁路字符串。

本机 NVIDIA GeForce RTX 4080 Laptop GPU 的 600 帧报告如下：

| Viewport | 状态 | p95 | 最大帧 | actor | material | texture | 时序门 |
|---|---:|---:|---:|---:|---:|---:|---|
| 1280×720 | 16 | 1.8209 ms | 3.1964 ms | 21→21 | 37→37 | 11→11 | 适用，通过 |
| 1600×900 | 16 | 1.7518 ms | 2.8853 ms | 21→21 | 37→37 | 11→11 | 适用，通过 |
| 2560×1440 | 16 | 1.8112 ms | 2.8872 ms | 21→21 | 37→37 | 11→11 | 适用，通过 |
| 2560×1600 | 16 | 1.8042 ms | 3.1487 ms | 21→21 | 37→37 | 11→11 | 适用，通过 |

GitHub Windows runner 使用 Microsoft Basic Render Driver/ANGLE 软件渲染；四份报告的 `timing_budget_applicable=false`，因此不冒充 GPU 帧时性能。但 300 帧预热、300 帧测量、16 状态截图、布局/隐私/哈希检查以及 actor 21→21、material 37→37、texture 11→11 的零增长仍全部强制通过。

第一次 run 的云端 1600×900 的 16 张实际截图与提交 golden 的归一化 MAE 为 0.000294～0.001255、边缘差为 0.000279～0.001224，远低于 0.025/0.08 门槛；最终绿色 run 再次执行相同比较并通过。

## 手牌、徽章、镜头与 HUD 验收

- 己方 1～10 张手牌位于相机相对的屏幕下方前景架，不再平铺在桌面坐标；悬停抬起约 12%，相邻卡让位，选中卡保持最前；
- 1280×720 静止手牌卡高不低于 142 px，1600×900 及以上不低于 170 px；
- 对方手牌只使用屏幕上方匿名共享卡背，不绑定 definition、instance ID 或私密材质；
- 费用、攻击、生命和倒计时使用分离的真实徽章；法术不伪造身材，单位 0/0、多位数和受伤值均有自动证据；
- 镜头不因详情、日志或 HUD 显隐产生“呼吸”，滚轮桌面缩放不改变前景手牌尺寸；
- 左右安全区按 1280/1600/2560 宽固定为 240+196、288+240、320+264 px；
- 单位位、策略位、主战者、牌组、墓地、封存与战备均有明确托座和双方归属；
- `Covered`/`Resolving` 前清除手牌 DTO、文字、材质身份、metadata、碰撞、回调、tween 与拖拽数据。

## 远端 CI 制品

Run `32766050188` 上传 7 个 CI 验收制品。下列大小和 digest 是 GitHub Actions artifact archive 元数据：

| 制品 | 字节 | GitHub artifact SHA-256 |
|---|---:|---|
| `scgs-native-v04-linux-x86_64-gcc` | 635,306 | `fbaf3d277b11e201c2255b9a2baf519c95d21389b2e293cc37cf24455c24c630` |
| `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 5,875,921 | `810e41fe870670379bc857076796636bd60e190e9c4f31496d883242a5c9e242` |
| `scgs-native-v04-windows-x86_64-msvc` | 253,056 | `6618942127f9aa3d2c64903bc6ed7a125085acd2b67e99a3668a41262a844a4f` |
| `scgs-native-v04-macos-arm64-appleclang` | 489,499 | `f87ce66bdb4856f924723ece3abb25355e8bc3ffb1197e9164ee3e65a77eabdd` |
| `SomeCardGameShit-gate4b-r2-windows-visual-suite` | 82,014,832 | `71276fd557e4b86dbb48179549118a571ba807a6a9bf9b4a4452d31bd71214a4` |
| `SomeCardGameShit-gate4b-r2-windows-x86_64` | 153,543,965 | `74fef89f2f57bc7bfd70b98bc60eebb0441c62b40c4572f5a0a99adbb9704c16` |
| `SomeCardGameShit-gate4b-r2-macos-arm64` | 138,789,398 | `7a9448a06e6e9848b616d7cce492e8e758a6079d8a4bfb5e968a16b5543c1f20` |

下载并解包 GitHub Windows artifact 后，内部可直接交付的客户端 ZIP 为 153,802,001 bytes，SHA-256：

```text
f7a4408a7aa5f7b7cc3e5a6e8b74cecf20b8592c57b21ee3f7854effe225b6a7
```

包内 `licenses/BUILD_INFO.txt` 精确记录 `commit=cca04b5c9a0e4793c98d8f765527a7a1c51de804`、Godot `4.7.2.stable.mono`、.NET SDK `10.0.400` 与 runtime `8.0.30`。Windows DLL 与 EXE 同目录并使用静态 MSVC runtime；macOS 包为 ARM64、Frameworks dylib 与 ad-hoc 签名。两者都是未正式签名/未公证的测试制品，不是商业发布包。

## 本地交付路径

- CI Windows 客户端 ZIP：`build/ci-run-32766050188/windows-package-artifact/SomeCardGameShit-gate4b-r2-windows-x86_64.zip`
- CI 四尺寸截图与报告：`build/ci-run-32766050188/windows-visual-artifact/`
- 下载后再次解包与实启目录：`build/ci-run-32766050188/windows-product-roundtrip/`
- 同提交本机独立构建 ZIP：`build/gate4b-r2-cca04b5-package/SomeCardGameShit-gate4b-r2-windows-x86_64.zip`

## 尚未完成的发布硬门

本轮完成的是第一次实机验收包的开发与自动化交付，不等于用户已经接受视觉与操作体验。仍未完成：

- 用户在当前 Windows 实机上完成第一次主观试玩并反馈；
- 未安装 Visual Studio 的 Windows x86-64 机器完成整局；
- 物理 Apple Silicon Mac 完成整局、退出和重开；
- 两名真人完成热座整局并逐次观察交接隐私与交互理解。

这些硬门完成前不得创建 `v0.4-hotseat-alpha.1` 标签。R3 的精细卡体、机械场地模型、材质灯光、响应链/动作演出、完整弹层与菜单统一也尚未开始。

本文件记录实现尖端 `cca04b5` 及 run `32766050188` 的可复现证据。包含本报告的后续文档提交不改变产品代码，但分支最终尖端仍必须由同一四项工作流重新验证；不得用实现尖端的绿色 run 冒充尚未运行的文档尖端。
