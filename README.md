# SomeCardGameShit

原创 1v1 数字卡牌游戏。C++20 是唯一规则裁判，Godot 4.7.2 .NET 提供固定斜视 2.5D、动漫幻想风的桌面热座客户端。

## 当前状态：Product Playable v1

当前开发分支为 `codex/product-playable-v1`，产品入口已经接入 **`scgs_v05` ABI 2.0 / JSON schema 2**，不再是仅展示卡牌的无 native 样片。源码已接通两副新牌组、选择/响应、终局与重开；完整实机、导出和 CI 验收仍以 [TEST_REPORT.md](TEST_REPORT.md) 为准，不能把实现存在当成最终验收完成。

| 职业与系列 | 产品牌组键 | 玩法 |
| --- | --- | --- |
| 誓卫 · 曜誓骑士团 | `oathguard_luminous_oath_v1` | 预支后清偿，以无隙、修复归零、守护、突进和屏障取得收益。 |
| 契术 · 渊契魔导院 | `pactmage_abyssal_pact_v1` | 主动动用未来，以裂痕阈值、必杀、吸血和疾驰取得节奏。 |

每副 30 张主牌、15 种定义、4 张不同的公开战备；共享 4 种中立牌。合计 34 个可构筑定义和 1 个衍生物。五格主战场由随从与护符共享，双方各有三个策略位和一个独立场地格；法术必须使用己方空策略位。牌表和规范规则见 [设计文档](docs/product-decks-v1-design.md) 与 [锁定清单](design/product-decks-v1/card-pool.lock.json)。

AnimeV1 现在是唯一产品视觉：菜单、竞技场、相机相对手牌、卡体、HUD 和弹层均使用原创日式幻想动漫资产/组件。卡图和主战者仍是可替换的原创临时资产，不代表商业发布美术已终审；本轮没有声音、联机或牌组编辑器。

### 旧入口已退役

- `midrange/advance` 旧产品定义、工厂、菜单选项与科幻卡图已经退出当前产品，不能作为隐藏牌组启动。
- 正式 `scgs_v04` 仅保留冻结 ABI/schema 形状、14 个导出和 wire 兼容边界；旧牌组创建会明确失败，不自动映射新牌组。
- 成功的 v04 协议回归使用独立 `scgs_v04_fixture` 与 `synthetic_alpha/synthetic_beta`，该库不安装、不进入玩家包。
- `--legacy-2d-board`、`--ci-smoke`、旧 `--r3-visual*`、`--ci-visual-suite*` 和 `--anime-*` 审批样片参数由当前 Bootstrap 拒绝。直接启动游戏即可进入产品菜单，不需要样片 launcher。
- 旧 Gate 文档/报告保留为历史证据，不再作为当前启动指南。详见 [v04 退役契约](docs/native-api-v04.md)。

## 怎么运行

完整解压本轮对应平台的产品包后，直接启动 Windows EXE 或 macOS `.app`。进入“本地热座”，两席各选上述牌组，再开始比赛；允许相同牌组。第一位玩家必须主动揭示后才能读取私密手牌，换人继续使用完全不透明遮挡。具体可用包、运行方式及平台限制以 [Godot README](client/godot/README.md) 和 [测试报告](TEST_REPORT.md) 为准。

源码运行需要先构建并审计同工作区的 `scgs_v05`，然后暂存到：

```text
client/godot/native/windows-x86_64/scgs_v05.dll
client/godot/native/macos-arm64/libscgs_v05.dylib
```

使用 `scripts/dev/start_godot_editor.ps1` 启动 Windows 编辑器，确保使用锁定的本地 SDK；不要依赖可能指向其他 dotnet 安装的系统 PATH。Godot MCP 仅供开发，导出必须剥离插件、autoload、probe 和本地连接信息。参见 [编辑器与 MCP 工作流](docs/godot-editor-mcp.md)。

## 架构与安全边界

```text
权威卡池 + 声明式效果 → ProductGame / ProductBoard / 可暂停结算
        → scgs_v05 / schema 2 → Scgs.Client.V05
        → ProductHotseatMatchController → ProductMatchScreen / AnimeV1
```

客户端只执行“快照 → 同 revision 合法查询 → 规范命令 → 脱敏事件”的循环，不复制规则。成功命令只增加一次 revision；失败不得改变状态/事件。私密选择只给选择者短生命周期 option ID；产品 seed 不出现在实时快照或开局事件。

提交前进入 `Resolving`，清除私密画面、候选、回调和拖拽数据，只显示中立公共投影；显示环境至少完成两次 `FramePostDraw` 后提交。换人先 `Covered`，下一 viewer 揭示前零读取。鼠标、键盘、点击和拖拽共享规范命令，落点保留玩家归属、区域及精确格位。

## 构建与测试

工具链：CMake 3.25+、C++20/C11 编译器、Ninja、Python 3.10+；客户端精确锁定 **Godot 4.7.2 .NET** 与 **.NET SDK 10.0.400**。正式目标是 Windows x86-64 和 macOS ARM64，不支持 Web 或 Linux 正式客户端。

```bash
cmake --preset release
cmake --build --preset release
ctest --preset release

cmake --preset asan
cmake --build --preset asan
ctest --preset asan

dotnet restore client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --locked-mode
dotnet build client/Scgs.Client.Tests/Scgs.Client.Tests.csproj -c Release --no-restore
dotnet test --project client/Scgs.Client.Tests/Scgs.Client.Tests.csproj --configuration Release --no-restore --minimum-expected-tests 1
dotnet build client/godot/SomeCardGameShit.csproj -c Release
```

`SCGS_ENABLE_LEGACY_YGO2_TESTS` 默认 ON；这个历史命名开关注册 Python 契约组，开启时必须找到 Python 3.10+。关闭它的 13 项针对性 CTest 不能被当作完整测试。历史报告 schema/wire 可以使用独立 fixture 验证，不要求恢复旧产品入口或素材。

产品 GUI smoke 使用 `--ci-product-smoke`，以当前 v05 场景的真实输入、遮挡、选择和终局证据为准；旧报告成功标记不证明新产品可玩。Windows 重型视觉/性能任务与日常构建分别记录，不能自动覆盖人工批准的 golden。

本轮具体原生数量、逐牌语义覆盖、合成/真实定义区别及未完成项见 [原生证据](docs/product-playable-v1-engine-evidence.md)；全部平台、托管、实机、导出和 CI 状态以 [总测试报告](TEST_REPORT.md) 为准。

## 文档入口

- [当前交接](docs/DSH-HANDOFF-v0.4-ui.md)、[开发计划](docs/GODOT-HOTSEAT-DEVELOPMENT-PLAN.md)、[路线图](docs/roadmap.md)
- [总体架构](docs/architecture.md)、[Godot 架构](docs/godot-client-architecture.md)、[UI 状态与隐私](docs/ui-state-map.md)
- [v05 原生接口](docs/native-api-v05.md)、[v04 历史接口](docs/native-api-v04.md)
- [资源/预支基础规则](docs/rules-v0.4.md)、[产品牌组增补](docs/product-decks-v1-design.md)
- [卡池清单](design/product-decks-v1/card-pool.lock.json)、[效果清单](design/product-decks-v1/product-effects.lock.json)
- [动漫美术圣经](docs/product-decks-v1-art-bible.md)、[全产品视觉锁](design/product-decks-v1/anime-v1-visual.lock.json)
- [素材声明](client/godot/ASSET_NOTICES.md)、[工具链](docs/toolchain.md)
- [历史 YGOPro2 交接](docs/DSH-HANDOFF.md)；仅 clean-room 参考，不复制其代码、坐标、素材或商标视觉。

代码按 [GPL-3.0-or-later](LICENSE) 维护；第三方组件和素材遵循各自声明。原生二进制不提交 Git。真人热座、物理目标机、性能/包体及商业美术终审仍须有对应实证。
