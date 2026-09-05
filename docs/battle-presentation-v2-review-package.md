# Battle Presentation V2：Windows 独立验收包操作单

状态：implementation in progress / visual pending user approval。以下是可复现
流程，不是“已经导出并实际启动通过”的结果。此包保留原 EXE 正常菜单路径，
新增独立 `PLAY_BATTLE_PRESENTATION_REVIEW.cmd`，仅它启用三代表卡验收入口。

## 前置条件

- 使用同一工作区新构建的 v05 DLL，不使用旧 v04 或 fixture DLL。
- Godot 4.7.2 .NET 与 Mono export templates、SDK 10.0.400 已按锁安装。
- 在真正导出前正常保存并关闭当前工程编辑器／游戏，不另开第二个编辑器，
  也不为导出覆盖未保存的人类修改。
- 本机 MCP 安装保留时，让已有 Toolkit export stripping hook 启用；仅排除
  文件并不能删除 `project.binary` 的 autoload/plugin 配置。不要编辑或删除
  `project.godot`、addon 或本机 token 来“绕过”审计。
- 使用全新的导出目录。不要把运行日志、私密 `user://review-evidence`、MCP
  目录或旧验收包混入导出根；包装器会拒绝不认识的根文件。

## 生成新的 Windows export

在仓库根的 x64 MSVC 开发者 PowerShell 中执行；显式 Godot 路径可以替换为
当前已验证的实际安装位置。每一步失败即停止，不继续打包。

```powershell
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROOT = 'C:\Users\ASUS\.dotnet'
$env:PATH = $env:DOTNET_ROOT + [IO.Path]::PathSeparator + $env:PATH
$godot = 'C:\Users\ASUS\AppData\Local\Programs\Godot\4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
$exportDir = Join-Path (Get-Location).Path 'artifacts/review/battle-presentation-v2-stage1-windows/export'
if (Test-Path -LiteralPath $exportDir) { throw 'Use a fresh export directory; do not overwrite a previous candidate.' }
New-Item -ItemType Directory -Path $exportDir | Out-Null
$exportExe = Join-Path $exportDir 'SomeCardGameShit.exe'

cmake --build build/v05-msvc
if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
dotnet restore client/godot/SomeCardGameShit.csproj --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed' }
dotnet build client/godot/SomeCardGameShit.csproj --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
python scripts/stage_godot_native.py --v05-library build/v05-msvc/scgs_v05.dll --destination-root client/godot/native --target windows-x86_64
if ($LASTEXITCODE -ne 0) { throw 'Native staging failed' }
python scripts/dev/check_godot_mcp_export.py --check-presets-only
if ($LASTEXITCODE -ne 0) { throw 'Preset isolation failed' }
python scripts/ci/run_with_timeout.py --timeout 600 --forbid-output 'SCRIPT ERROR:' --forbid-output 'ERROR:' -- $godot --headless --path client/godot --import
if ($LASTEXITCODE -ne 0) { throw 'Import failed' }
python scripts/ci/run_with_timeout.py --timeout 600 --forbid-output 'SCRIPT ERROR:' --forbid-output 'ERROR:' -- $godot --headless --path client/godot --export-release 'Windows x86-64' $exportExe
if ($LASTEXITCODE -ne 0) { throw 'Export failed' }
python scripts/ci/finalize_godot_export.py --platform windows-x86_64 --export $exportExe --product-native-library build/v05-msvc/scgs_v05.dll
if ($LASTEXITCODE -ne 0) { throw 'Finalize failed' }
python scripts/audit_godot_export.py --platform windows-x86_64 --export $exportExe
if ($LASTEXITCODE -ne 0) { throw 'Export audit failed' }
python scripts/dev/check_godot_mcp_export.py --export $exportExe
if ($LASTEXITCODE -ne 0) { throw 'MCP PCK/settings/class-cache isolation failed' }
```

`finalize_godot_export.py` 已包含新三素材的完整生成记录，输出为
`licenses/ANIME_V1_PRESENTATION_V2_GENERATION_RECORD.json`；导出审计要求与
源文件逐字节一致。共享素材清单、素材说明、GPL、Godot、.NET、nlohmann 与
字体许可证继续保留。`BUILD_INFO.txt` 的本机 `commit=local` 不能冒充干净提交。

## 生成独立验收 ZIP

包装器只读取上一步既有 export，不执行 Godot、游戏或 shell launcher。它校验
明确指定的 DLL 与包内 DLL 哈希相同，并三次执行产品/MCP 隔离审计：原目录、
临时组装目录、ZIP 真正解包后的目录。原导出不被修改，已有 ZIP 不会覆盖。

```powershell
python scripts/dev/package_battle_presentation_review.py --export $exportExe --native-library build/v05-msvc/scgs_v05.dll --output artifacts/packages/SomeCardGameShit-battle-presentation-v2-stage1-windows-x86_64-review.zip --allow-worktree
if ($LASTEXITCODE -ne 0) { throw 'Review packaging failed' }
```

本阶段有未提交实现，`--allow-worktree` 明确标记为未提交验收候选；它不会把
HEAD 作为已验证构建提交传给游戏。干净工作区则省略此参数。两种情况下来源
记录均说明：“打包器不重建 export，操作者必须保留独立构建／导出证据”。

输出 ZIP 内新增：

- `PLAY_BATTLE_PRESENTATION_REVIEW.cmd`：带 `-- --battle-presentation-review`。
- `REVIEW_README.txt`：三入口说明、揭示门、未完成与未正式签名状态。
- `REVIEW_PACKAGE.json`：来源状态、实际启动参数、文件 SHA-256、DLL SHA-256；
  明确 `runtime_launched_by_packager=false`，不含私密对局命令。

ZIP 自身 SHA-256 由脚本成功输出。正常游戏仍由 `SomeCardGameShit.exe` 进入。

## 不能省略的后续人工验收

解压整包，在真实显示器上双击新 launcher；确认它确实进入新三按钮验收壳，
分别准备 LO-11、AP-11、NT-04，通过不透明交接页主动揭示，再亲手完成出牌、
目标／格位选择和进化。检查实际GPU、日志、数字姓名、抠像与输入行为。
还要直接运行 EXE 核对正常产品路径未被更改。脚本审计、压缩成功或旧主菜单
截图均不能代替这些证据。

## 当前 CI 静态核对边界

- `codex/**` 的 push 会运行现有 Linux GCC、Clang sanitizer、Windows MSVC、
  macOS ARM64 四项主矩阵；素材和运行时代码不是文档-only 路径。
- 主分类 job 先校验全清单：69 项已登记且原 66 项范围保留；PNG、`.import`、
  `GENERATION_RECORD.json` 及新的 C#/shader/scene 文件必须全部随实现提交。
- importer 缓存使用资产全树／`.import`／工程配置／插件源码精确 key，新资源
  自动使 key 改变，且命中后仍执行真实 import。
- Windows/macOS 的正式 smoke 保留正常 v05 产品路径；本次独立 launcher
  不插入现有默认产品包步骤，不把它伪装成 CI 全动作或 GPU 已通过。
- clean CI 中无需安装本机 MCP addon；本机导出有 addon 时必须用 hook 并通过
  PCK/settings/class-cache 检查。不能把只检查 preset 的通过当作包隔离通过。
- 本文只报告静态流程。新 shader 在各平台的实际导入/加载、运行和最终四项
  CI 状态仍待对应运行，不能提前宣称全绿。
