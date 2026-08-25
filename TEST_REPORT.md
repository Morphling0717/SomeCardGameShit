# Gate 5B＋6A：产品运行时底座与动漫视觉样片测试报告

**日期：** 2026-08-26（Asia/Shanghai）

**分支：** `codex/product-runtime-foundation-anime-slice`

**项目基线：** `codex/product-decks-v1-design@cd05a41542c21a4021f53aff3ffb1f9641900429`

**被测实现尖端：** `e8f2a8fc2c3a63eeb7d750bfe898e0c67d84141f`

**实现提交：** `a11e599`、`f538d34`、`cc4e075`、`659e43f`、`e8f912d`、`e8f2a8f`

**范围：** Gate 5B 建立产品规则底座、生成目录、可暂停选择和独立 `scgs_v05`／schema 2；Gate 6A 提供不调用 native 的 AnimeV1 动漫视觉审批样片及其静态自动捕获模式。本报告不把通用能力原语、运输 fixture 或视觉样片写成两副 30 张产品牌组已经可玩。

## 结论

[GitHub Actions run 32894335103](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32894335103) 在实现尖端 `e8f2a8f` 上 **4/4 jobs 全绿**：

| Job | Job ID | 时长 | 结果 |
|---|---:|---:|---|
| Linux GCC Release | `97953311074` | 1 分 34 秒 | 通过 |
| Linux Clang ASan/UBSan | `97953311369` | 2 分 15 秒 | 通过 |
| macOS AppleClang ARM64 Release + Godot | `97953311282` | 11 分 16 秒 | 通过 |
| Windows MSVC Release + Godot | `97953311302` | 1 小时 31 分 17 秒 | 通过 |

Windows 在同一干净 checkout 中通过 Release 原生与压力测试、v04/v05 安装和制品审计、锁定 NuGet restore、101 项托管测试、Godot 构建、AnimeV1 四尺寸截图矩阵、旧默认 3D／legacy 2D／R3 回归、Gate 4B-R2 四尺寸视觉和 600 帧性能套件、正式导出、三条启动路径及 ZIP 往返。最重的四尺寸视觉／性能步骤为 `20:27:02Z`～`21:41:57Z`，成功完成而非被跳过。

macOS 在真实 ARM64 runner 上通过 v04/v05 原生与托管测试、Godot 导入、默认客户端导出、ad-hoc 签名、源码／导出／ZIP 往返启动和 AnimeV1 shader／结构 smoke。GitHub 托管 macOS 虚拟显示实际限制为 1024×684；该结果不能冒充计划中的正式 1280×720 视觉验收。

Linux GCC 验证 Release 与 2,048 seeds；Linux Clang 以 `detect_leaks=1` 验证 ASan/UBSan 与 256 seeds。v04 ABI 1.0／schema 1、legacy v1 wire 和精确 14 个 `scgs_v04_*` 导出保持冻结；v05 以独立 ABI 2.0／schema 2 和精确 14 个 `scgs_v05_*` 导出并行存在。

## Gate 5B 验收结果

- `scgs::v2` 已建立字符串 `design_id`、职业／系列／中立标签、五种卡牌类型、五格混合主战场、三格策略区、双方独立场地格、统一 `MoveReason` 和分层关键词；
- synthetic fixture 验证守护、突进、疾驰、屏障、必杀、主动攻击吸血、护符倒数原格预留、场地非破坏替换和每回合攻击限制；
- `ResolutionQueue` 验证四类私密选择、选择期命令阻断、触发排序、响应中暂停、投降和终局幂等清理；
- 9 项修正＋33 项新增能力形成精确 42／42 通用原语证据；规则代码没有按卡名或 `design_id` 特判；
- Gate 5A 清单确定性生成 34 张可构筑牌＋1 个衍生物的提交态 C++ 目录，普通构建不运行 Python，`--check` 会拒绝生成物漂移；
- 35 个产品定义全部仍为 `LockedNotImplemented` 且 `effects_compiled=false`，不能进入支付或合法行动枚举；
- `scgs_v05` 通过 C11 consumer、ABI/schema 拒绝、严格 UTF-8、生命周期、revision、观看者安全 DTO、私密 option ID、双事件游标和 seed 全树隐藏；
- 托管 v04 与 `Scgs.Client.V05` 消费边界分别通过同提交的真实 v04／v05 DLL 集成测试；Hotseat 四种选择状态另由托管单元测试覆盖；当前 Godot 产品入口仍明确使用 v04。

## Gate 6A 验收结果

AnimeV1 当前精确包含以下 14 项原创候选素材：

- 透明主战者母图：`aurelia-master.png`、`theraea-master.png`；
- 代表卡基础图：`LO-03`、`LO-07`、`LO-11`、`AP-03`、`AP-05`、`AP-11`、`NT-04`；
- 王牌进化异画：`LO-11-evolved.png`、`AP-11-evolved.png`；
- 公共素材：`card-back.png`、`menu-key-art.png`、`open-fantasy-arena.png`。

源 PNG 合计 `40,145,361` bytes；含 mip 链的保守桌面驻留估算为 `29,378,336` bytes，低于 96 MiB 上限。资产清单 SHA-256 为：

```text
e23d862f577f3ce5e822dff473f49a255fb0e05c4218a39452c2302e9c6be996
```

素材由 OpenAI 内建图片生成能力以新生成模式逐项创建，没有输入参考图。仓库内保存逐项完整 prompt、用途、日期、SHA-256、修改记录和授权说明；原始生成结果保留在本机 `%USERPROFILE%\.codex\generated_images`，导出包附带 AnimeV1 manifest、provenance 和说明文件。

Windows 正式视觉矩阵覆盖 `menu`、`setup`、`action`、`hand-hover`、`mixed-permanents-field`、`reaction`、`covered`、`result` 八态，在 1280×720、1600×900、2560×1440、2560×1600 共生成 32 张截图。每张图等待 process frame 与两个完整 `FramePostDraw`，验证无外围桌框、相机相对扇形手牌、五类卡面、费用／攻防／倒数、混合永久物、双方独立场地、隐藏卡无身份和 Covered 完全不透明。

macOS 源码、导出 app 和 ZIP 内 launcher 在 hosted runner 的实际 1024×684 视口完成相同八态 shader／结构 smoke。自动截图显式关闭时序动效；交互入口保留主战者呼吸、鼠标视差、入场、受击和胜负轻量演出。

样片状态冻结为 `pending_user_approval`。自动化不能代替人物与美术终审；用户明确批准整体方向前不得批量生产剩余美术。进入批量生产前仍需重点确认：

- `AP-11` 前伸装甲爪／手的解剖和轮廓是否清楚；
- `LO-11`、`AP-11` 的普通图与进化异画在手牌缩略尺寸下是否足够容易区分。

## 本地自动化结果

| 验证项 | 结果 | 精确口径 |
|---|---:|---|
| MSVC Release CTest | 24/24 targets | 本次复跑 33.90 秒；0 failed |
| 显式打印的原生断言 | 102,810 | `621 + 463 + 819 + 31 + 100,876`；无计数器的 v05 smoke 不虚构加入 |
| 产品运行时 | 19 cases／819 assertions | synthetic 通用语义与锁定产品目录 |
| Python `scripts/tests` 全集 | 144/144 | 本次复跑 31.357 秒；0 failed |
| Managed/.NET | 101/101 | SDK 10.0.400；真实 v04/v05 DLL；0 failed、0 skipped；7.471 秒 |
| Godot C# build | 通过 | Debug/Release 均 0 warning、0 error |
| MSVC Release 压力 | 30 cases／8,685 assertions | 2,048 seeds、强制双方先手，共 4,096 局 |
| Windows Clang 22.1.8 ASan/UBSan 压力 | 30 cases／1,517 assertions | 256 seeds、512 局；0 failure |
| Windows sanitizer CTest | 11/11 | 26.98 秒；本机 `detect_leaks=0`，泄漏以 Linux CI 为权威 |
| AnimeV1 四尺寸矩阵 | 32/32 captures | 八态 × 四尺寸，结构与可读性验证通过 |
| `git diff --check` | 通过 | 实现尖端工作区干净 |

本机锁定工具链为 CMake 3.31.6、Python 3.10.11、.NET SDK 10.0.400；Godot 使用仓库锁定的 4.7.2 .NET。托管测试由 `C:\Users\ASUS\.dotnet\dotnet.exe` 执行，两个真实动态库分别来自同一提交的 `build/release/scgs_v04.dll` 与 `scgs_v05.dll`。

Python 数量不能与 CTest target 相加：独立 144 项为 `scripts/tests` 的 138 项既有／视觉契约加 6 项生成目录单元测试；CTest 内注册 138 项 scripts unittest、10 项 tools legacy unittest，另直接执行一次 generator `--check`。

## Release CTest 明细

| # | CTest target | 输出计数 |
|---:|---|---|
| 1 | `scgs_unit_tests` | 30 cases；621 assertions |
| 2 | `scgs_client_api_contract` | 463 assertions |
| 3 | `scgs_product_runtime_foundation` | 19 cases；819 assertions |
| 4 | `scgs_documented_scenario` | `verified=true`、`invariants_hold=true` |
| 5 | `scgs_wire_frozen_golden` | 31 assertions |
| 6 | `scgs_native_api_c_contract` | v04 C11 consumer smoke |
| 7 | `scgs_native_api_contract` | 100,876 assertions |
| 8 | `scgs_native_api_dynamic_load` | v04 精确 14 exports |
| 9 | `scgs_native_api_v05_c_contract` | v05 C11 consumer smoke |
| 10 | `scgs_native_api_v05_schema_contract` | ABI 2.0／schema 2 contract marker |
| 11 | `scgs_native_api_v05_dynamic_load` | v05 精确 14 exports |
| 12 | `scgs_ygo2_overlay_patcher` | 5 Python tests |
| 13 | `scgs_protocol_contract` | 5 |
| 14 | `scgs_native_artifact_audit_contract` | 6 |
| 15 | `scgs_godot_export_audit_contract` | 13 |
| 16 | `scgs_subprocess_timeout_contract` | 3 |
| 17 | `scgs_gate3b_report_contract` | 6 |
| 18 | `scgs_gate3c_report_contract` | 15 |
| 19 | `scgs_gate4a_report_contract` | 14 |
| 20 | `scgs_gate4b_visual_pipeline_contract` | 29＝25 pipeline＋4 golden |
| 21 | `scgs_r3_visual_slice_contract` | 14 |
| 22 | `scgs_anime_visual_slice_contract` | 15 |
| 23 | `scgs_product_decks_v1_design_contract` | 23 |
| 24 | `scgs_product_catalog_generated_contract` | generator `--check`，非 unittest |

## CI 制品

Actions artifact digest 是 GitHub 上传包的 SHA-256；大小同样是 Actions artifact 大小，不等同于其中原始 ZIP 的字节数。v04/v05 原生命名 artifact 当前都上传各平台的同一份完整安装 stage，stage 内仍分别审计两个动态库；上传容器的 digest 不能拿来比较两支 DLL 是否相同。Windows 两个上传包本次 digest 恰好相同，Linux 与 macOS 因上传封装不同而不同。

真正可解压运行的交付包是 `SomeCardGameShit-product-runtime-foundation-anime-slice-windows-x86_64` 与 `SomeCardGameShit-product-runtime-foundation-anime-slice-macos-arm64`。`SomeCardGameShit-gate6a-anime-slice-*` 是源码截图矩阵，`*-evidence` 是导出／打包启动报告与截图，均不是独立游戏包。

| Artifact ID | 名称 | Bytes | SHA-256 |
|---:|---|---:|---|
| `9580668485` | `scgs-native-v04-linux-x86_64-gcc` | 1,300,285 | `62bc96d28dfa90f2c3ee2dd9381dd6f8fa28b9b51e9e6e8a88f2b1bd8005ace9` |
| `9580669629` | `scgs-native-v05-linux-x86_64-gcc` | 1,300,285 | `f295994256df965cd012e72e8d8d7747c384e949cb419e6bcfd01e1cb3c2f0a2` |
| `9580691825` | `scgs-native-v04-linux-x86_64-clang-asan-ubsan` | 12,239,566 | `9660d7ad97c718aa6bd83efc9220657b7e9b27ae8c92de3aa393a88b9cbba2d2` |
| `9580693713` | `scgs-native-v05-linux-x86_64-clang-asan-ubsan` | 12,239,566 | `83123707d83ba5bacfa3ff3d82a50806554731177a50ab65653c585912d72dac` |
| `9581007808` | `scgs-native-v04-macos-arm64-appleclang` | 1,041,705 | `516960d1506e407834bdf0458977b7358e315f86e268d1c04fa94f03a7bca6a7` |
| `9581008812` | `scgs-native-v05-macos-arm64-appleclang` | 1,041,705 | `68e48347df6d3b8cdc222bab7d4828780e9318025107e5ad9be45d408d3c9bf0` |
| `9583717150` | `scgs-native-v04-windows-x86_64-msvc` | 478,418 | `27500e8c3220fc399bc80c89add2d7d33eb7ae1f50adf0ea352c93a9235697e7` |
| `9583718084` | `scgs-native-v05-windows-x86_64-msvc` | 478,418 | `27500e8c3220fc399bc80c89add2d7d33eb7ae1f50adf0ea352c93a9235697e7` |
| `9581005323` | `SomeCardGameShit-product-runtime-foundation-anime-slice-macos-arm64` | 169,319,838 | `c3a9a1251a755da71d6ab130f25588a9975ba72df2b1e18db96a4689d130b76a` |
| `9581006791` | `SomeCardGameShit-product-runtime-foundation-anime-slice-macos-evidence` | 12,593,718 | `b7c310031a7e57317eb4ec6930c77329d18692bced8b06fae05a542526089c6f` |
| `9583711872` | `SomeCardGameShit-product-runtime-foundation-anime-slice-windows-x86_64` | 183,898,404 | `3f9647b205cabb75e2419db54c235dd8e6de7405038f25087012c26a1c54bb03` |
| `9583713644` | `SomeCardGameShit-product-runtime-foundation-anime-slice-windows-evidence` | 23,655,086 | `65d58e6ed09c2a652bf943a0e06eaa63578fd2033d91bbca41051f04aeeeaade` |
| `9580921374` | `SomeCardGameShit-gate6a-anime-slice-macos-arm64` | 6,499,700 | `305094b4d649dd2f3dddda493a392c16f795337feb0404317cc2396f96fe08a2` |
| `9580933378` | `SomeCardGameShit-gate6a-anime-slice-windows` | 72,903,037 | `9889ec684f478d05f49c10367e069ab7655dbd245cd22b453e7c2de67593c8a0` |
| `9583555925` | `SomeCardGameShit-gate4b-r2-windows-visual-suite` | 82,014,832 | `a95f47c63aa1411f1a0ff68d0d8f14567c49d35faa0355a2d11b9f3868fdf1cd` |
| `9583702367` | `SomeCardGameShit-gate4b-r2-windows-x86_64` | 183,898,346 | `8eec86fea6762dada645b754247ecdd1188222a3d4dfd68faae245f078dd88fa` |
| `9583706685` | `SomeCardGameShit-gate4b-r3-visual-slice-windows-x86_64` | 183,898,372 | `79d368de65116a5dd2464479bac25094bcaddfe38fded832cd1efa0b74875e00` |
| `9581001769` | `SomeCardGameShit-gate4b-r2-macos-arm64` | 169,320,439 | `f13c80a051a60c592a8dd79a1156a046f7edfb8e93d5c6b6f4e6baf6ba19b294` |
| `9581005657` | `SomeCardGameShit-r3-candidate-windows-visual-slice` | 7,809,222 | `801936e329ca1bf7976e45ed270d305fbb507749a61ebc1968d0712bcf89eb48` |
| `9583716220` | `SomeCardGameShit-r3-candidate-windows-exported-visual-slice` | 7,809,222 | `8c5097e40b83f6f1c14400de3a801b9f7f06f9a535e0fa6dca44f789eb722440` |
| `9583714924` | `SomeCardGameShit-r3-candidate-windows-packaged-visual-slice` | 7,809,222 | `1a461a1cae1db958fe632fcb2ec834fd422e10d5b0d8fb83419d91805d14011a` |

全部 21 项 artifact 的过期时间为 `2026-11-23T20:15:54Z`。

本地可交付的原始包另行核对：

| 包 | 原始 ZIP bytes | SHA-256 |
|---|---:|---|
| Windows x86-64 | 184,145,554 | `9b0ef068f865fa51b4bdbbdd204a75490a7c884005624c13d80145ff07b33f7e` |
| macOS ARM64 | 169,588,919 | `8f5901669603cbc7d4485657b3f8f1166bfd5f463db7dee9cc323dd3947d45dc` |

macOS 包内 app 主程序与 `PLAY_ANIME_STYLE_SLICE.command` 均保持 `0755`；应用只做 ad-hoc 签名，没有 Developer ID 签名或公证。

完整解压后，Windows 双击 `PLAY_ANIME_STYLE_SLICE.cmd`；macOS 运行 `PLAY_ANIME_STYLE_SLICE.command`，且不能只把 launcher 单独抽出。源码或程序也可使用 `--anime-style-slice`，并用 `--anime-style-state=<state>` 指定八态之一。默认 EXE／`.app` 仍进入旧 R2 过渡产品路径；AnimeV1 launcher 才进入不访问 native 的审批样片。

## 尚未完成与不得误报的边界

- 34 张可构筑牌和 1 个衍生物的逐卡 effect graph 仍为空，全部产品定义仍锁定不可执行；
- v05 当前是 foundation session：目标／格位／组件查询为空，支付预览是零值 fixture，响应上下文固定为无响应；产品出牌动作受控拒绝；
- 尚无誓卫／契术固定牌整局代理、压力对局、真人对局或平衡数据；48%～52% 胜率和赢家 T10～12 仍只是 Gate 5D 目标；
- Godot 默认产品入口仍运行 v04 旧测试牌组；AnimeV1 样片完全不访问 native；
- AnimeV1 只有 14 项审批素材，剩余 27 张构筑卡图＋1 个衍生物图尚未批量生产；
- 旧 `midrange`／`advance`、旧卡图和科幻产品 profile 尚未删除，Gate 5C／6C 切换后才退役；
- 音乐、音效、语音、正式商业 Logo、Developer ID 签名、公证、物理 Apple Silicon 真人测试和双人完整热座发布验收尚未完成；
- 本 Gate 没有创建 PR、合并或标签。

## 主要复现命令

```text
cmake --preset release
cmake --build --preset release
ctest --preset release --output-on-failure

python -m unittest discover -s scripts/tests -p "test_*.py"
python scripts/design/generate_product_catalog_v2.py --check
python scripts/ci/validate_product_decks_v1.py
python scripts/ci/validate_anime_visual_slice.py <report> --expected-viewport <width>x<height>

$env:PATH = "C:\Users\ASUS\.dotnet;" + $env:PATH
$env:SCGS_NATIVE_LIBRARY = "<absolute scgs_v04.dll>"
$env:SCGS_V04_NATIVE_PATH = $env:SCGS_NATIVE_LIBRARY
$env:SCGS_NATIVE_V05_LIBRARY = "<absolute scgs_v05.dll>"
python scripts/ci/run_managed_gate3.py

git diff --check
```

## 报告提交与最终尖端

本报告记录的是实现尖端 `e8f2a8f` 和 run `32894335103`。包含本报告的后续文档提交不会改变产品或测试代码，但分支最终尖端仍必须重新完成同一四项工作流；最终交付不得用实现尖端的绿色 run 冒充尚未运行的文档尖端。
