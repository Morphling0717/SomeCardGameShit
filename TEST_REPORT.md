# Gate 0+1+2+3A+3B+3C+4A+4A.1+4B-R2+4B-R3.1 测试报告

**日期：** 2026-08-25（Asia/Shanghai）

**分支：** `codex/godot-hotseat-gate4b-r3-visual-slice`

**项目基线：** `codex/godot-hotseat-gate4b-r2-battle-presentation@4dfc16db514d4a8d65afd5bea688e070da38df2f`

**主要实现提交：** `22b1b7b2938bdb3984d69d8b49b6293efc904cf7`

**跨平台取证修复：** `b9c829e5c459ba08f112db2b087d2529085a77b9`、`3d4012fe5d9dc8c93e2379d33618a0ee7554d829`

**被测实现尖端：** `3d4012fe5d9dc8c93e2379d33618a0ee7554d829`

**范围：** 在 Gate 4B-R2 默认产品路径完全保留的前提下，新增可回退的 Gate 4B-R3.1 无边界工业竞技场视觉切片、相机相对前景手牌、候选场外机械、中性战术 HUD、真实 session 三态截图、真实恶意私密哨兵迁移取证，以及 Windows 源码/导出/ZIP 启动器三重验收。本轮不修改 C++ 规则、DTO、`IScgsGameSession`、C ABI、schema 1、精确 14 个导出、固定牌组、legacy v1 wire 或 R2 golden；候选仍固定为 `pending_user_approval`，不会由普通启动进入。

## 结论

[GitHub Actions run 32808917410](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32808917410) 在被测实现尖端 `3d4012f` 上 **4/4 jobs 全绿**：

- Linux GCC Release，job `97684381658`：通过，1 分 02 秒；
- Linux Clang ASan/UBSan，job `97684381815`：通过，1 分 53 秒；
- macOS AppleClang ARM64 Release + Godot，job `97684381770`：通过，4 分 33 秒；
- Windows MSVC Release + Godot，job `97684381810`：通过，1 小时 13 分 37 秒。

Windows 在同一干净 checkout 中依次通过默认 R2 3D 整局、legacy 2D 整局、R3 源码候选、R2 四分辨率 visual/performance 套件、正式导出、默认导出整局、R3 导出候选、双 ZIP 打包、包内 `PLAY_R3_VISUAL_SLICE.cmd` 候选启动以及默认 ZIP 解包后整局。macOS 继续验证默认 R2 ARM64 导出、ad-hoc 签名、结构审计和真实启动；R3.1 本轮没有扩大到 macOS 候选产品路径。

两次失败 run 均保留为有效问题发现记录，而不是被忽略：

- run `32807907556` 在 Windows 的 R3 生成器源码哈希处发现 `core.autocrlf` 导致的 LF/CRLF 身份漂移；`b9c829e` 将生成器、候选 shader、候选清单和 launcher 明确锁为 LF；
- run `32808188162` 已越过哈希门，但 Windows hosted runner 把 `--resolution 1600x900` 夹为 `1028×749`；`3d4012f` 让显式 CI 视口使用与 R2 套件相同的无边框窗口尺寸设置，并新增契约测试。

两个修复都收紧证据的一致性，没有删除测试、放宽图片阈值或把实际小尺寸冒充为 1600×900。

## 冻结边界

- 默认产品 profile 仍是 `Gate4BR2`；只有 `--r3-visual-slice` 启用 `R3Candidate`；
- R3 报告为独立 schema 1，游戏 ABI/JSON 仍是 ABI 1.0/schema 1；
- 原生导出仍为精确 14 个 `scgs_v04_*` C 符号；
- R2 visual-suite 仍为 schema 4、四尺寸 16 状态与人工批准的 1600×900 golden；
- R2 冻结产品素材仍为 34 项；R3 仅有 1 项独立候选地坪清单；
- `PLAY_R3_VISUAL_SLICE.cmd` 是 opt-in 候选入口；直接运行 EXE 仍进入 R2；
- legacy v1 wire 字节未改变。

## 本地自动化结果

| 验证项 | 结果 |
|---|---|
| MSVC Release `/W4 /WX` 与 CTest | 17/17 通过；规则压力使用 2,048 seeds |
| C# managed | 75/75，0 failed，0 skipped；Debug/Release 均 0 warning、0 error |
| Python 全集 | 94/94 通过 |
| R3 独立契约 | 14/14 fixture 契约测试通过；另由工作流生成真实 runtime report 并通过严格 validator，负面 fixture 覆盖隐私与伪证据拒绝 |
| 视觉素材 | R2 34/34 + R3 候选 1/1；跨清单路径与哈希唯一 |
| R2 1600×900 回归 | 16/16 状态及 golden 比较通过，归一化误差近零 |
| 原生 DLL | x86-64、14 个 C 导出、无 C++ 导出、无动态 `MSVCP140*` / `VCRUNTIME140*` |
| 默认 full-match | `Player1Won`、revision 3、148 个成功步骤；默认 3D 与 legacy 2D 均通过 |
| R3 源码采集 | 1600×900，3 张产品态 + 2 张隐私态，schema validator 通过 |
| 下载后的 CI R3 包 | 架构/许可证/提交号审计通过；包内 launcher 再次生成并通过 1600×900 报告 |
| `dotnet format --verify-no-changes` | 4 个项目通过 |
| `git diff --check` | 通过 |

主要复现入口：

```text
cmake --preset release
cmake --build --preset release
ctest --preset release --output-on-failure

dotnet test --project client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --configuration Release --no-restore --minimum-expected-tests 1
python -m unittest discover -s scripts/tests -p "test_*.py"
python scripts/audit_visual_assets.py
python scripts/ci/validate_r3_visual_slice.py --report <absolute-report> --width 1600 --height 900
python scripts/audit_godot_export.py --platform windows-x86_64 --export <SomeCardGameShit.exe>
```

Windows 本机 Clang ASan 与系统 VCRUNTIME/debug CRT 存在已知运行库不兼容，因此不用于通过结论；同提交 Linux Clang ASan/UBSan job 是 sanitizer 权威结果并已全绿。

## R3.1 真实 session 与视觉证据

CI 源码候选 artifact `9549230727` 的 `r3-visual-slice.json` 由同一真实 `ScgsGameSession` 产生：固定 seed `3235823838`、Player0 先手、关闭洗牌；两次真实调度后到达 revision 2，最终有 17 个合法行动，选中的是 `PlayUnit` 来源 1。报告固定 `approval_status="pending_user_approval"`，commit source 为 `GITHUB_SHA`，working tree 为 clean。

三张稳定产品态都等待连续两个内容一致的 `FramePostDraw`，frame-pair MAE 为 0：

| 状态 | Viewer | Revision | PNG SHA-256 |
|---|---:|---:|---|
| `action-idle` | 0 | 2 | `04a46c3996aa3370c43ad7ab9411a5ba58db9c765ea910229be364f4e6e241ee` |
| `hand-hover` | 0 | 2 | `787305d891755c76939df0b76d7235ee001be6e1777d8bcdb59c889c1d297724` |
| `source-selected` | 0 | 2 | `9177515398c6485734ce941669c273adbbd5cbfc402d63ddb037fe886466e81b` |

候选画面契约已验证：80×60 连续工业地面延伸到镜头四角之外；没有桌面周界大框、蓝紫半场色板或有限地坪黑边；原创地坪只在中央 46×34 区域采样一次并向程序化钢材渐隐；机械位于 19.8×16.6 对局 footprint 外；空位为浅凹槽/短角标；近端手牌位于相机相对前景层，费用与身材使用真实深度遮挡。

## 隐私取证

取证不是只检查 `GetView`。`CountingSession` 对全部 viewer-scoped snapshot、合法行动、目标、格位、组件、支付、响应上下文、事件读取和事件游标读取统一计数。

- viewer 请求顺序：`[0, 1, 0]`；主动揭示 3 次；premature view calls 为 0；
- 总 snapshot 请求：5；总 viewer-scoped 读取：14；
- `privacy-resolving.png`：revision 0，snapshot `1→1`、viewer reads `3→3`，SHA-256 `3309a296c9b27ebc3a7fff96f558ff4c74e3318607526e0fba03de898e47ef8b`；
- `privacy-covered.png`：revision 1，snapshot `2→2`、viewer reads `5→5`，SHA-256 `67ae93c27c63c80864886397578134e1af562a1d2daa42b2a786a15883cc56e7`；
- P0 revision-0 调度前真实注入恶意私密字符串和洋红 GPU 材质；两个迁移态均验证节点文字、metadata、身份材质、碰撞、drag token、tween 和 callback 清空；
- detector 自测与真实注入分开记录，两个候选隐私帧和三张产品帧的 sentinel 均为 0；隐藏牌只绑定共享卡背。

## Provenance

报告同时绑定：

| 项目 | SHA-256 / 数量 |
|---|---|
| R2 产品素材清单 | `550cee89ccb1b384149d85aa45725474371b022a646fbef8de28d4c9bbae8eac`，34 项 |
| R3 候选素材清单 | `0762e749dea05ddae3ae6268314ff5071f3a32f8481ca009cd5e68a93e3c17b7`，1 项 |
| 候选地坪 | `9892b03ff0ab3dbe6fb0e733b32461a36e2bc960f7105110f0d6a34b79dd1343` |
| 原创场外机械 GLB | `4ce416e3828dbcdbdf94b407c7f800144497af5afb5f2801bd08b35b267c9108` |
| 候选 shader | `1867570d98c986393704b739d5e618d48002aabc0dffdc8528c5e3f679060d06` |
| Windows launcher | `2ec4ce5175bb0870ba881071af47c60e69d9f25084fc8ebfc52da3b3acad8fe6` |

Blender 5.2.0 从提交的 Python 源确定性重建 GLB 后得到相同 SHA-256；Blender 不是客户端构建、CI 或运行依赖。

## R2 默认路径回归

本轮没有用 R3 截图替换 R2 golden。Windows job `97684381810` 的 display-backed R2 visual/performance 步骤重新运行并通过：

- 1280×720、1600×900、2560×1440、2560×1600 四种窗口；
- 每种尺寸 16 个状态、连续稳定帧、布局/隐私/真实费用与身材 GPU ROI；
- 1600×900 人工批准 golden；
- 300 帧预热 + 300 帧测量，actor/material/texture 测量期零增长；
- Microsoft Basic Render Driver/ANGLE 只豁免 GPU 帧时预算，不豁免功能、图片、资源或隐私门。

普通菜单、直接启动 EXE、默认导出和默认 ZIP 往返均继续进入 R2，并完成 148 步整局。R3 collector 不支持在同一 `MatchScreen` 实例中切回 R2；需要回到菜单/新建 screen，这避免候选还原逻辑破坏 authored R2 HUD。

## 远端 CI 制品

Run `32808917410` 上传 11 个验收制品；字节与 digest 是 GitHub Actions artifact archive 元数据：

| Artifact ID | 制品 | 字节 | GitHub artifact SHA-256 |
|---:|---|---:|---|
| `9549118258` | `scgs-native-v04-linux-x86_64-gcc` | 635,306 | `0805c67ae6e0b0b2d4b9aa15f031327fb2d3df4cb86d415e64fe979f514f28e3` |
| `9549137413` | `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 5,875,921 | `9afd4677ca99b08eb38ba3dc8701b72ba0ee694ee57a2894a31185f30901fb60` |
| `9549193942` | `scgs-native-v04-macos-arm64-appleclang` | 489,499 | `72232cbc9a91023ce9442b73e8f4e040430f52826ed62650e61c95481488d7d2` |
| `9549193559` | `SomeCardGameShit-gate4b-r2-macos-arm64` | 141,659,851 | `c36f8d55add487ec543f9a6a7213e7484e4fd547893cda26bff7aec9c09fc8ae` |
| `9549230727` | `SomeCardGameShit-r3-candidate-windows-visual-slice` | 7,809,223 | `9652d673079c996feb648cd37aa8ddf273c4cef0225c17c8787a2e45a48572d5` |
| `9550587196` | `SomeCardGameShit-gate4b-r2-windows-visual-suite` | 82,015,419 | `8a7115aef75fdd4e20c38d91cf8362bb0e4c936893318258673eee65e657a8d7` |
| `9550651936` | `SomeCardGameShit-gate4b-r2-windows-x86_64` | 156,415,076 | `b6f10a012f33b9e5459f72c39f10518ad4f71e3b76c4dd47c1526335022755ce` |
| `9550653742` | `SomeCardGameShit-gate4b-r3-visual-slice-windows-x86_64` | 156,415,102 | `97505296df93ada836780d2b62b163f9e6da62596ad4976153190c0876a6fe6c` |
| `9550654520` | `SomeCardGameShit-r3-candidate-windows-packaged-visual-slice` | 7,809,223 | `afedc7f2f1c243e4c94a6aaa9ef4d29835c7ef04133ab273c7aaae0adc0c20e7` |
| `9550655337` | `SomeCardGameShit-r3-candidate-windows-exported-visual-slice` | 7,809,223 | `4057410d77c616b156293ef10f70982081daa507f549abc9e3de9f79720b1617` |
| `9550655861` | `scgs-native-v04-windows-x86_64-msvc` | 253,056 | `e8abe1b28058141471f74da6b96beb0234e3795944088bc8ed33493caa91f4ec` |

下载并解开 artifact `9550653742` 后，用户可直接交付的内层 ZIP 为：

```text
SomeCardGameShit-gate4b-r3-visual-slice-windows-x86_64.zip
bytes=156674807
sha256=df46901bb699daf0453cad1c4e35af69907ab0191af47c76875780ba3c40437f
```

包内 `licenses/BUILD_INFO.txt` 记录 `commit=3d4012fe5d9dc8c93e2379d33618a0ee7554d829`、Godot `4.7.2.stable.mono`、.NET SDK `10.0.400` 与 runtime `8.0.30`。标题仍写 Gate 4B-R2 是有意的：EXE 本身的默认产品路径仍是 R2；只有同目录的固定哈希 launcher opt-in 到尚未批准的 R3 候选。Windows DLL 与 EXE 同目录并使用静态 MSVC runtime；macOS 仍为 ARM64、Frameworks dylib 与 ad-hoc 签名。制品均为未正式签名/未公证的测试包。

## 本地交付路径

- CI R3 Windows 内层试玩 ZIP：`build/ci-run-32808917410/r3-package-artifact/outer/SomeCardGameShit-gate4b-r3-visual-slice-windows-x86_64.zip`
- 完整解包目录：`build/ci-run-32808917410/r3-package-artifact/unpacked/`
- 下载后的 CI 源码实拍与报告：`build/ci-run-32808917410/r3-source-visual-artifact/unpacked/windows-1600x900/`
- 下载包在本机通过 launcher 再次生成的报告：`build/ci-run-32808917410/r3-package-local-run/windows-1600x900/`
- 本机实现提交实拍：`build/r3-visual-slice-window-forced-fix/`

## 尚未完成的硬门

本轮完成的是候选构图、自动化与可试玩 Windows 包，不等于用户已经批准视觉：

- 用户完整解压当前 R3 Windows ZIP，双击 `PLAY_R3_VISUAL_SLICE.cmd`，并明确批准或否决构图、配色、手牌、主战者与 HUD；
- 批准前 R3 不得成为默认路径，不得覆盖 R2 golden，也不得把三张切片截图冒充完整对局视觉迁移；
- 调度/响应/结算/结果弹层迁移、动作演出、菜单统一、音效与最终商业美术属于批准后的后续 R3；
- 未安装 Visual Studio 的 Windows x86-64 整局、物理 Apple Silicon Mac 整局/退出/重开、两名真人完整热座仍是发布标签前硬门。

这些硬门完成前不得创建 `v0.4-hotseat-alpha.1` 标签。本轮没有创建 PR、合并或标签。

本文件记录被测实现尖端 `3d4012f` 与 run `32808917410` 的可复现证据。包含本报告的后续文档提交不改变产品代码，但分支最终尖端仍必须由同一四项工作流重新验证；不得用实现尖端的绿色 run 冒充尚未运行的文档尖端。
