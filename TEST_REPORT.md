# Gate 0+1+2+3A+3B+3C+4A+4A.1+4B-R1 测试报告

**日期：** 2026-08-24（Asia/Shanghai）

**分支：** `codex/godot-hotseat-gate4b-visual-baseline`

**项目基线：** `main@cfdf695d70eeabcc6de9b094c94041364fb1335f`

**Gate 4B 起点：** `codex/godot-hotseat-gate4a-spell-slots@8b890edd4a741828b9bf58e15b1808d58d3876fc`

**被测 Gate 4B-R1 实现：** `01a9bb33c7cff148e49067b8bd43ab1e973ea600`

**范围：** 在 Gate 4A.1 的完整规则、统一 surface intent、默认 3D 对局与策略位法术基线上，加入 29 张独立原创临时卡图、统一卡背、菜单背景、两张临时主战者头像、通用正面 fallback、产品化主菜单与设置、3D 卡面视觉目录、现代玻璃 HUD、基础动效，以及可复现的四分辨率视觉/性能/隐私验收。本轮不修改 C++ 规则、C ABI、schema 1 游戏 JSON、精确 14 个导出、`ActionKind` 数值、固定牌组或 legacy v1 wire。

## 结论

Gate 4B-R1 实现通过本机 MSVC Release **16/16 CTest**、**2,048-seed** 规则压力、**75/75 managed tests**、**65/65 Python/legacy tests**、34 项视觉素材审计、默认 3D 与 legacy 2D 完整 signal 对局，以及 1280×720、1600×900、2560×1440、2560×1600 四种真实窗口尺寸的 11 状态视觉套件与 600 帧资源/性能 smoke。

[GitHub Actions run 32719076472](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32719076472) 在实现提交 `01a9bb3` 上 **4/4 jobs 全绿**：GCC Release、Clang ASan/UBSan、MSVC Release + Windows Godot、AppleClang ARM64 Release + macOS Godot。

Windows 与 macOS 均完成 locked restore、托管构建/测试、Godot import、默认 3D 当前工程整局、legacy 2D 当前工程整局、默认 3D 目标平台导出、结构/架构/许可证审计、导出程序整局及 ZIP 解包后的再次审计和整局。两平台的基础矩阵合计运行 8 次 full-match，每次只输出一次：

```text
SCGS_GODOT_CI_SMOKE_OK
```

Windows 另外以四种目标尺寸各运行一次 display-backed visual suite。1600×900 的 11 张截图全部通过已提交 golden；另外三种尺寸通过严格结构、布局、素材哈希、隐私和资源零增长验证。

## 报告契约说明

本轮存在三个相互独立的版本号，不能混用：

- 对局 ABI/JSON 继续是 ABI 1.0、schema 1；
- signal full-match 继续使用冻结的 Gate 4A schema v3，以保证后续视觉改造不能降低 Gate 3C/4A 的整局、交互和隐私证据；
- Gate 4B-R1 visual-suite 使用独立 schema v3，记录素材清单哈希、四种 viewport、11 个 UI 状态、布局契约、适配器身份和 600 帧证据。

visual-suite 的硬件时序门为 p95 ≤ 33.3 ms 且单帧 < 100 ms。只有 `adapter_type=cpu`，或适配器名称明确命中 Microsoft Basic Render Driver、llvmpipe、SwiftShader、software renderer 时，才允许将 `timing_budget_applicable` 记为 false。软件渲染器只跳过 GPU 帧时阈值；300 帧预热、300 帧测量、截图/布局/隐私检查，以及 actor/material/texture 零增长仍全部强制执行。正式 CI 不使用通用的 `--skip-performance-budget` 覆盖参数。

## 执行环境

本机视觉与性能证据来自 Windows x86-64 和 NVIDIA GeForce RTX 4080；该适配器被识别为硬件，`timing_budget_applicable=true`。客户端工具链继续精确锁定：

```text
CMake: 3.25 或更高
C++: C++20
Python: 3.10 或更高
.NET SDK: 10.0.400
Godot: 4.7.2.stable.mono
Renderer: Compatibility
```

远端矩阵使用 `ubuntu-latest`、`windows-latest` 与 `macos-15`，Python 固定为 3.12.8；Windows/macOS 通过 `global.json` 精确选择 .NET SDK 10.0.400，并使用校验官方哈希后的 Godot 4.7.2 .NET 编辑器与 Mono export templates。macOS 导出及 bundle 内所有 Mach-O 均审计为 ARM64-only。

## 本地复现命令

以下命令从仓库根目录执行；`SCGS_GODOT_EXE` 指向已校验的 Godot 4.7.2 .NET 可执行文件。构建目录名不属于产品契约。

```powershell
cmake -S . -B build/gate4b-r1-msvc -A x64 `
  -DSCGS_WARNINGS_AS_ERRORS=ON `
  -DSCGS_ENABLE_LEGACY_YGO2_TESTS=ON
cmake --build build/gate4b-r1-msvc --config Release --parallel
ctest --test-dir build/gate4b-r1-msvc -C Release --output-on-failure

$env:SCGS_SMOKE_SEEDS = "2048"
& .\build\gate4b-r1-msvc\Release\scgs_tests.exe

cmake --install build/gate4b-r1-msvc --config Release `
  --prefix build/stage-gate4b-r1-msvc
python scripts/audit_native_artifact.py `
  --library build/stage-gate4b-r1-msvc/bin/scgs_v04.dll `
  --architecture x86_64

$env:SCGS_NATIVE_LIBRARY = "$PWD\build\stage-gate4b-r1-msvc\bin\scgs_v04.dll"
$env:SCGS_V04_NATIVE_PATH = $env:SCGS_NATIVE_LIBRARY
python scripts/ci/run_managed_gate3.py
python -m unittest discover -s scripts/tests -p "test_*.py"
python -m unittest scripts.tests.test_gate4b_visual_pipeline
python scripts/audit_visual_assets.py

python scripts/ci/run_with_timeout.py `
  --timeout 180 --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --expect-output-count 1 --forbid-output "SCRIPT ERROR:" `
  --forbid-output "ERROR:" --forbid-output "Unhandled exception" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke "--native-library=$env:SCGS_NATIVE_LIBRARY" `
  "--ci-report=$PWD\build\gate4b-r1-current-3d.json"
python scripts/ci/validate_gate4a_report.py `
  --report build/gate4b-r1-current-3d.json `
  --scenario full-match --presentation 3d

python scripts/ci/run_with_timeout.py `
  --timeout 180 --expect-output SCGS_GODOT_CI_SMOKE_OK `
  --expect-output-count 1 --forbid-output "SCRIPT ERROR:" `
  --forbid-output "ERROR:" --forbid-output "Unhandled exception" `
  -- "$env:SCGS_GODOT_EXE" --headless --path client/godot -- `
  --ci-smoke --legacy-2d-board `
  "--native-library=$env:SCGS_NATIVE_LIBRARY" `
  "--ci-report=$PWD\build\gate4b-r1-current-legacy-2d.json"
python scripts/ci/validate_gate4a_report.py `
  --report build/gate4b-r1-current-legacy-2d.json `
  --scenario full-match --presentation legacy-2d

$sizes = @(
  @{ Width = 1280; Height = 720 },
  @{ Width = 1600; Height = 900 },
  @{ Width = 2560; Height = 1440 },
  @{ Width = 2560; Height = 1600 }
)
foreach ($size in $sizes) {
  $width = $size.Width
  $height = $size.Height
  $timeout = if ($width -eq 2560) { 2400 } else { 1200 }
  $suite = "$PWD\build\gate4b-r1-visual-$width`x$height"
  python scripts/ci/run_with_timeout.py `
    --timeout $timeout --expect-output SCGS_GODOT_CI_SMOKE_OK `
    --expect-output-count 1 --forbid-output "SCRIPT ERROR:" `
    --forbid-output "ERROR:" --forbid-output "Unhandled exception" `
    -- "$env:SCGS_GODOT_EXE" --path client/godot --windowed `
    --audio-driver Dummy --resolution "$width`x$height" -- `
    --ci-smoke "--native-library=$env:SCGS_NATIVE_LIBRARY" `
    "--ci-visual-suite=$suite" "--ci-visual-viewport=$width`x$height"
  python scripts/ci/validate_gate4b_visual_suite.py `
    --report "$suite\visual-suite.json" `
    --width $width --height $height `
    --asset-manifest client/godot/assets/visual/ASSET_MANIFEST.json

  if ($width -eq 1600 -and $height -eq 900) {
    $report = Get-Content -Raw "$suite\visual-suite.json" | ConvertFrom-Json
    $goldenRoot = "$PWD\client\godot\tests\visual_goldens\gate4b\windows-1600x900"
    foreach ($capture in $report.captures) {
      python scripts/ci/compare_visual_golden.py `
        --actual "$suite\$($capture.file)" `
        --expected "$goldenRoot\$($capture.state).png" `
        --heatmap "$suite\heatmaps\$($capture.state).png" `
        --mae-limit 0.025 --edge-limit 0.08
    }
  }
}

dotnet format client/Scgs.Client/Scgs.Client.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Hotseat/Scgs.Hotseat.csproj --verify-no-changes --no-restore
dotnet format client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --verify-no-changes --no-restore
dotnet format client/godot/SomeCardGameShit.csproj --verify-no-changes --no-restore
git diff --check
```

1600×900 golden 只能在人工逐张查看后通过以下显式命令更新；CI 从不自动覆盖 golden：

```powershell
python scripts/ci/update_gate4b_goldens.py `
  --report build/gate4b-r1-visual-1600x900/visual-suite.json `
  --destination client/godot/tests/visual_goldens/gate4b/windows-1600x900 `
  --accept
```

## 本地自动化结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 构建与 CTest | 16/16 通过 |
| 规则回归/压力 | 30 cases，8,685 assertions，0 failures；2,048 seeds |
| C11 header/link、动态加载与安装后 consumer | 全部通过；精确 14 个导出可解析并调用 |
| Windows DLL 审计 | PE x86-64；无 C++ 导出；不依赖 `MSVCP140*` / `VCRUNTIME140*` |
| C# managed | 75/75，0 failed，0 skipped |
| Python 全集 | 65/65：legacy 10 + CI/tooling 55 |
| Gate 4B visual pipeline 子集 | 13/13；已包含在上述 65 项中 |
| 视觉素材 | 34/34；路径、唯一 SHA-256、比例、用途和 prompt 摘要全部通过 |
| 当前实现 DLL | 461,824 bytes；SHA-256 `bc248192a1896a0ba4fed92f4d08141a1f87c584d52425aeb3620cef0236d8e9` |
| `dotnet format --verify-no-changes` | 4 个项目通过 |
| `git diff --check` | 通过 |

34 项素材包括 29 张当前卡牌定义的独立临时插画、1 张统一卡背、1 张 16:9 菜单背景、2 张阵营头像和 1 个未知卡牌通用正面。素材 manifest 源文件 SHA-256 为：

```text
550cee89ccb1b384149d85aa45725474371b022a646fbef8de28d4c9bbae8eac
```

## 完整对局、空间输入与隐私

默认 3D 与隐藏 legacy 2D 的确定性 full-match 均继续通过同一热座控制器和规范命令路径：

- 第一局自然终局为 52 个回合结束，两局合计 148 次成功命令；
- `ActionKind` 0～10 全覆盖；
- 71 次完全遮挡、71 次主动揭示，揭示前 viewer 读取 0；
- 每条命令的中立 `Resolving` 公共投影至少完整绘制 2 帧；
- 1 次结果页 signal 重开、1 次投降终局、2 次 session 释放；
- 点击、拖拽、键盘、HUD 拦截、空间 raycast 与同 revision 规范命令收敛保持有效；
- 默认 3D 相机为 58° FOV / 58° 俯角，viewer 透视重建 69 次；
- actor 池复用 838 次，锁定状态空间输入阻断 148 次；
- viewer DTO、Label、definition-specific 纹理/材质、metadata、tooltip、碰撞、回调、tween、drag token 与 GPU `#ff00ff` 私密哨兵泄露均为 0。

legacy 2D 证明仍经过同一 surface intent 和热座隐私状态机，同时不伪报 3D 专属 raycast、镜头、透视或 actor 池证据。

## 四分辨率视觉与性能结果

每种尺寸均捕获以下 11 个真实 UI 状态：`menu`、`match-setup`、`covered`、`mulligan`、`action`、`source-selection`、`slot-or-target-selection`、`reaction`、`resolving`、`result`、`error`。

本机 RTX 4080 的报告如下。actor/material/texture 数量均为 300 帧预热后、随后 300 帧测量前后相等：

| Viewport | 战场可见宽×高占比 | p95 | 最大帧 | actor | material | texture | 时序门 |
|---|---:|---:|---:|---:|---:|---:|---|
| 1280×720 | 0.797219 × 0.782304 | 1.3288 ms | 2.1921 ms | 17→17 | 30→30 | 11→11 | 适用，通过 |
| 1600×900 | 0.797219 × 0.782304 | 1.1988 ms | 1.6916 ms | 17→17 | 30→30 | 11→11 | 适用，通过 |
| 2560×1440 | 0.797219 × 0.782304 | 1.5407 ms | 2.2439 ms | 17→17 | 30→30 | 11→11 | 适用，通过 |
| 2560×1600 | 0.824040 × 0.736624 | 1.0846 ms | 1.6552 ms | 17→17 | 30→30 | 11→11 | 适用，通过 |

四种尺寸还共同满足：控件全部位于 viewport 内、受约束 HUD 区域不重叠、普通状态不存在全高不透明黑栏、至少存在一个共享玻璃表面、非调试启动不显示 Revision/seed 等开发标签，战场状态的可见宽度占比 ≥ 0.68、高度占比 ≥ 0.72。战场占比使用投影与物理 viewport 的真实交集计算，超大但离屏的矩形不能让报告假绿。

1600×900 的 11 项 Windows golden 比较全部通过：缩小至 320×180 后归一化 MAE ≤ 0.025、边缘差 ≤ 0.08。其他三种尺寸执行相同截图、哈希与结构契约，但不冒充跨尺寸像素 golden。

## Gate 4B-R1 产品路径验收

- 主菜单保留 `SomeCardGameShit` 标题；本地热座、设置和退出可用，单人、在线、牌组编辑、图鉴和录像明确显示开发中且不创建 native session。
- 本地热座设置页允许两席独立选择 `midrange` 或 `advance`，包括相同牌组；阵营视觉身份只来自公开的牌组设置。
- 设置实际覆盖窗口/无边框全屏、四种窗口分辨率、90%/100%/110%/125% UI 缩放、VSync 和减少动画；非法配置回退默认值。
- 29 个冻结 definition 均有唯一视觉目录映射；未知未来定义使用无身份通用正面。两种牌组头像、同牌组双席与未知牌组 fallback 均有运行时契约。
- 对局产品路径采用现代玻璃 HUD：自适应卡牌详情抽屉、悬浮玩家状态舱、阶段胶囊、靠近己方区域的结束回合按钮、折叠日志与暂停入口，不再使用左右全高黑栏。
- 空格位使用低对比度角标/细线/类型符号；手牌悬停、重排、落位、回弹、送墓、攻击、阶段和响应使用可关闭或缩短的基础动效。
- `Covered` 保持完全不透明；`Resolving` 只显示中立公开战场。隐藏牌只绑定共享卡背，不能进入 definition-specific 图片、材质参数、tooltip 或稳定 metadata。
- `CardActor3D` 的 CI 私密纹理只用于瞬时 GPU 哨兵验证，释放时显式销毁，不能进入共享 artwork cache；首个注入后的 `Resolving` 截图未出现洋红私密像素。

这些结果证明 Gate 4B-R1 已达到可持续测试的产品视觉基线，不代表临时素材已成为最终商业美术。

## 远端 CI 与制品

| Job | 配置 | 结果与制品 |
|---|---|---|
| [`linux-gcc`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32719076472/job/97406443458) | GCC Release；2,048 seeds | 通过；native 安装/consumer/审计；`scgs-native-v04-linux-x86_64-gcc` |
| [`linux-clang-sanitizers`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32719076472/job/97406443474) | Clang Debug + ASan/UBSan；256 seeds | 通过；ASan/UBSan consumer/审计；`scgs-native-v04-linux-x86_64-clang-asan-ubsan` |
| [`windows-msvc`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32719076472/job/97406443331) | MSVC Release x86-64；2,048 seeds；Godot Windows | 通过；managed 75/75、四尺寸 visual suite、默认 3D 当前/导出/ZIP、legacy 2D 源码整局 |
| [`macos-arm64`](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32719076472/job/97406443434) | AppleClang Release ARM64；2,048 seeds；Godot macOS | 通过；managed 75/75、默认 3D 当前/导出/ZIP、legacy 2D 源码整局 |

Windows GitHub runner 的 Compatibility 路径使用软件渲染。它在四种尺寸中仍完成全部 signal 对局、11 状态截图、1600×900 golden、600 帧、资源零增长和隐私验证；报告明确将时序门记为不适用，因此不把软件渲染帧时冒充 RTX 4080 或正式硬件性能证据。

Run `32719076472` 上传 7 个 CI 验收制品。下列字节数和 SHA-256 是 GitHub Actions artifact archive 元数据，不是 archive 内部单个文件的摘要：

| 制品 | 字节 | GitHub artifact SHA-256 |
|---|---:|---|
| `scgs-native-v04-linux-x86_64-gcc` | 635,306 | `5fd81a2929539d19ee7f2c19cbd52b4eb7fe5e654946408b51860e1ddf2a207f` |
| `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 5,875,921 | `56f7861bfba6a9eba698c1ebf8d070f162733e1a117e3cfe0e9f9e9433c4165e` |
| `scgs-native-v04-windows-x86_64-msvc` | 253,055 | `23a3e1df9cd095bade1070913ff28ce32ed2578aa5cecf8485145241dc263968` |
| `SomeCardGameShit-gate4b-windows-visual-suite` | 50,107,375 | `71eb0edba0acd69fee3df4ecf6bac0b969f589ed1f0b10e709c275cbbe18ea64` |
| `SomeCardGameShit-gate4b-windows-x86_64` | 153,499,030 | `55f19b755f071d895834e76f04c400d8f4b38c651b011334113d754739a6d170` |
| `scgs-native-v04-macos-arm64-appleclang` | 489,499 | `34775f7d867fbb6ac651cfddb8430060246d087c708bc92820a14c5f13f72e5c` |
| `SomeCardGameShit-gate4b-macos-arm64` | 138,742,441 | `7cf468abd190a6095b173e04a9d217a3509506d8e35f9b0448b6d1199689024e` |

Windows 客户端内 DLL 与 EXE 同目录并使用静态 MSVC runtime。macOS artifact 保留 `.app` 执行权限，dylib 位于 `.app/Contents/Frameworks`，finalize 后重新 ad-hoc codesign，所有 Mach-O 为 ARM64-only。两个客户端包均包含项目 GPL、Godot MIT/COPYRIGHT、.NET、nlohmann MIT、Noto OFL、`ASSET_NOTICES.md` 和第三方声明；它们是未正式签名/未公证的 CI 验收制品，不是商业发布包。

## 冻结契约与发布前硬门

- `native_api_v04.h`、ABI 1.0、游戏 schema 1、14 个 C 导出、JSON DTO、`IScgsGameSession`、`ActionKind` 数值、两副固定牌组与 legacy v1 wire 保持不变。
- Gate 4A.1 的策略位法术语义继续回归：法术必须选择己方具体空策略位，响应期间正面占位，自身链环完成后送墓；三格全满时不能施法。
- 29 张卡图、卡背、菜单背景和两张头像均为原创、可替换的临时素材；本轮没有音频、音乐、正式 Logo、最终商业卡框或大型召唤/粒子演出。
- legacy 2D 只承担功能回归，不承诺与默认 3D 产品路径视觉等价。
- 主战技、普通主动能力、同时触发人工排序、固定牌组未使用关键词、触摸、手柄、联机、录像、卡组编辑、Developer ID 签名、公证、Web 与 Linux 正式客户端仍未交付。
- 物理 Apple Silicon Mac、未安装 Visual Studio 的 Windows x86-64 机器，以及两名真人热座完整一局仍是发布标签前硬门；CI 通过不能替代这些实机/人工证据。

本文件记录实现提交 `01a9bb3` 及其 GitHub Actions run `32719076472` 的可复现证据。包含本报告的后续文档提交不改变产品代码，但分支尖端仍必须由同一四项工作流重新验证；不得用实现提交的绿色 run 冒充尚未运行的文档尖端。
