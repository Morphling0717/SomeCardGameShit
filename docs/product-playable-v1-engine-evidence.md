# Product Playable v1：原生规则与退役边界验收证据

## 范围与版本

- 日期：2026-09-05，本机 Windows x64 / MSVC Release。
- 分支：`codex/product-playable-v1`；基底提交：`6e0204e391d0d0b377f7b62d18f1b1fd65d56e81`。
- 证据对应该基底上的未提交工作区实现，不是上述提交已经包含全部改动的声明。
- 本文只记录原生引擎、生成清单和 C ABI 的实际验证；不证明 Godot 交互、视觉、导出包或远端 CI 已通过。

## 本轮修复

所有修复通过通用验证器、状态或声明式效果数据实现；运行时没有按卡名或 `design_id` 分支判断规则。

1. 随从目标同样经过职业、系列、类型和区域过滤器；AP-08 的强化模式不能选择中立随从，查询和提交共用验证。
2. 独立全局触发建立新的目标选择上下文，只继承触发事件的修复/动用未来数据；LO-10 不再误继承原行动的“已完成目标选择”。
3. 部署成功后记录本回合已部署，失败不消耗次数，拥有者下一回合重置。
4. 战备牌离场统一进入封存区，仍保留原 `MoveReason` 和是否被破坏；封存不触发遗言。
5. 效果数据增加内部 `trigger_owner_turn_only`；“每个自己的回合”不会在敌方回合触发，职业的回合周期充能不受此限制。
6. 战斗中防守方反击破坏敌方随从并存活，也触发通用战斗击杀后能力；吸血仍只属于主动攻击。
7. 燃耗降低容量不额外扣除已支付后的当前 PP；允许当前 PP 暂时高于容量，下一回合按实际容量补充。
8. 对主战者的过量攻击按剩余生命计算实际伤害及吸血，不用名义攻击力多回血。
9. 按印刷文本修正关键词持续时间：LO-11 获得的疾驰/突进和 AP-11 兜底突进持续保留；AP-11 的按期爆发 +2 攻击/疾驰仍仅本回合。
10. 区分“每回合”和“每回合周期”：新增内部通用 `SourceTurn` 作用域，在任意一方开始回合时重置来源次数。LO-08、LO-S02 战斗修复改用该作用域；`SourceOwnerTurn` 和职业周期历史保持不变，不修改任何公开枚举或卡牌数值。

## 两层覆盖，不混为一谈

### 通用合成能力测试

`engine/tests/test_product_runtime.cpp` 使用独立合成定义验证能力底座，维持 42 项能力登记（9 项修正、33 项新增）。当前为 **20 个测试、1,099 条断言、0 失败**。

新增战备离场矩阵使用全部 8 张真实战备定义，分别覆盖主动破坏、效果伤害致死、额外代价及终局清理：验证封存目的地、保留原因、破坏标记和区域不变量。合成测试仍负责混合五格、场地替换、倒数、关键词、私密选择、排序等通用能力，不等同于真实牌表的逐牌验证。

### 真实锁定定义的语义场景

`engine/tests/test_product_game.cpp` 当前为 **39 个测试、1,397 条断言、0 失败**，并输出 **35 个锁定定义均有通过的命名语义场景**。

逐牌场景使用未修改数值/效果的真实锁定定义。部分场景加入仅存在于测试中的合成准备牌，通过正常公开命令安排裂痕、生命、抽牌和目标，不直接修改私有状态，也不把这些准备牌加入产品牌池。另有使用两副精确锁定牌表的开局、自然终局及 32-seed 对局测试。后者只证明可执行/不变量，不代替以下逐牌断言或平衡测试。

| 定义 | 已实际断言的主要语义 |
| --- | --- |
| LO-01 | 查看顶四，过滤本系列非随从，选择公开加入手牌，其余三张置底；排除中立和随从。 |
| LO-02 | 实际修复 1，归零才回血 2；进化修复继续执行。 |
| LO-03 | 登场修复、自回合归零额外倒数；敌回合不额外倒数；到期在原格召唤。 |
| LO-04 | 无隙入场获得屏障；真实随从身材/守护与场地增益目标可用。 |
| LO-05 | 实际修复历史使登场永久 +1/+1，突进保留。 |
| LO-06 | 修复 2；此次修复归零才抽牌。 |
| LO-07 | 敌方攻击时响应，修复后无隙取消攻击；敌回合修复可触发职业周期充能。 |
| LO-08 | 战斗破坏且存活后修复，包括防守反击；同回合第二次击杀不重复修复，己方回合与紧接的敌方回合各可触发一次；后一次归零获得屏障。 |
| LO-09 | 回复量取实际修复量而不是印刷量；修复归零获得屏障。 |
| LO-10 | 修复后暂停并重新选择合法本系列随从，+1/+1 与屏障作用于同一目标；敌回合不触发。 |
| LO-11 | 按期无隙获得疾驰/屏障，预支只能获突进；无本回合限制的关键词跨回合保留。 |
| LO-S01 | 本回合实际修复是部署前提；无隙时另一个己方随从获得增益。 |
| LO-S02 | 本回合系列随从获得屏障的历史条件；突进战斗击杀且存活后修复，己方击杀后下一敌回合反击击杀可再次修复。 |
| LO-S03 | 本局护符到期历史解锁；实际部署 5/7 守护/屏障。 |
| LO-S04 | 需要两次不同的修复归零历史和当前无裂痕；实际部署疾驰。 |
| AP-01 | 负契 2 遗言抽牌的正反分支；额外封存不会触发遗言。 |
| AP-02 | 燃耗以及己方正确场地的屏障联动。 |
| AP-03 | 普通 3 点伤害、负契 4 的 5 点伤害和合法随从目标。 |
| AP-04 | 不追溯自己的入场前燃耗；后续动用未来减倒数；到期抽 1/负契 4 抽 2。 |
| AP-05 | 登场抽牌后置底；动用未来再次循环；抽牌封存不强制置底；与 AP-04 同时触发排序。 |
| AP-06 | 负契 2 当回合突进以及必杀基线。 |
| AP-07 | 超前获得当回合突进；主动攻击实际伤害吸血，主战者过量致死不多治疗。 |
| AP-08 | 两种模式；修复 2，或对本系列随从永久 +2/+2 和屏障；中立目标被无副作用拒绝。 |
| AP-09 | 按期修复/屏障；超前抽二弃一/突进；临时突进在结束回合清除。 |
| AP-10 | 可不选目标；按裂痕伤害最多读 5，不能把护符作为随从目标。 |
| AP-11 | 超前不能绕过按期疾驰；按期负契爆发 +2 攻击/疾驰到回合结束清除，兜底突进保留。 |
| AP-S01 | 本回合新增裂痕条件；突进/必杀。 |
| AP-S02 | 裂痕与低生命条件必须同时满足；真实战斗压血后部署守护/屏障/吸血。 |
| AP-S03 | 本回合系列护符到期历史；实际部署并修复 1。 |
| AP-S04 | 同系列随从或护符额外代价均可；拒绝中立；封存不破坏/不遗言；可使用素材腾出的原格。 |
| NT-01 | 入场后对比双方混合主战场数量，落后时当回合突进。 |
| NT-02 | 真实 2/4 守护基线及不可登场立即攻击。 |
| NT-03 | 到期抽牌/治疗；提前被破坏不执行倒数结束效果。 |
| NT-04 | 伤害与永久物破坏模式；正确限制随从或护符/场地，不伤害主战者。 |
| LO-T01 | LO-03 到期在保留的原格生成 3/3 守护衍生物。 |

测试末尾把通过场景的定义登记与权威目录逐项比较；新增定义未获得场景登记会失败。此登记证明每张牌有具体语义断言，不声称全部组合、全部历史顺序或每个正反边界已穷举。

## v04 旧产品内容退役

- `engine/src/catalog.cpp` 不再提供旧产品定义、旧数字牌号或 `midrange/advance` 工厂；仍保留通用旧引擎类型以运行冻结协议夹具。
- 正式 `scgs_v04` 保留 ABI 1.0、schema 1 的形状、生命周期/缓冲行为、14 个导出和既有 wire bytes；不再提供任何可成功创建的旧产品牌组。
- `midrange`、`advance`、`synthetic_alpha`、`synthetic_beta`、`oathguard`、`pactmage` 在正式 v04 中都明确返回 `SCGS_V04_SCHEMA_MISMATCH` 和零 handle，不自动映射到新牌组。
- 独立测试库 `scgs_v04_fixture` 只接受 `synthetic_alpha/synthetic_beta`，用于 C11、托管和字节协议成功路径回归。它不安装、不进入 SDK 或玩家包。
- MSVC 多配置测试路径为 `build/ci-msvc/Release/scgs_v04_fixture.dll`；当前 Ninja 单配置为 `build/v05-msvc/scgs_v04_fixture.dll`。
- 已安装 SDK 的 C11 consumer 改验正式 v04 退役边界；v05 consumer 仍验新产品可创建/执行。详见 [v04 契约](native-api-v04.md)。

## 实际执行记录

在已有 Release 缓存中使用 VS 2022 Developer PowerShell（MSVC 14.44）、CMake 3.31.6 构建。最终针对性日志：`build/product-native-acceptance/targeted-final.log`。

```powershell
& 'C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/Common7/Tools/Launch-VsDevShell.ps1' -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
cmake --build build/v05-msvc --parallel 4
ctest --test-dir build/v05-msvc --output-on-failure -R 'scgs_(unit_tests|client_api_contract|documented_scenario|wire_frozen_golden|product_runtime_foundation|product_game_core|native_api.*)'
.\build\v05-msvc\scgs_product_game_tests.exe
.\build\v05-msvc\scgs_product_runtime_tests.exe
.\build\v05-msvc\scgs_native_api_tests.exe
python scripts/design/generate_product_catalog_v2.py --check
python -m unittest scripts.tests.test_generate_product_catalog_v2
python scripts/audit_native_artifact.py --library build/v05-msvc/scgs_v04.dll --architecture x86_64 --api-version v04
python scripts/audit_native_artifact.py --library build/v05-msvc/scgs_v04_fixture.dll --architecture x86_64 --api-version v04
python scripts/audit_native_artifact.py --library build/v05-msvc/scgs_v05.dll --architecture x86_64 --api-version v05
git diff --check
```

| 检查 | 本轮结果 |
| --- | --- |
| MSVC Release 全目标构建 | 成功 |
| 上述针对性 CTest | 13/13 通过，最终一轮 4.02 秒 |
| ProductGame | 39 cases / 1,397 assertions / 0 failures；35 个定义登记齐全 |
| ProductRuntime | 20 cases / 1,099 assertions / 0 failures |
| v04 fixture C ABI contract | 100,876 assertions，通过 |
| v05 真实 schema 2 adapter | 65 commands，通过；到达付费选择边界，无旧 donor 投影 |
| 目录生成一致性与生成器测试 | `--check` 通过；17 个 Python 测试通过 |
| v04/v04 fixture/v05 三个动态库 | 均为 x86_64、精确 14 导出、无 C++ 导出、无动态 MSVC runtime |
| fresh installed SDK C11 consumers | v04/v05 均退出 0；安装树不含 fixture DLL |
| legacy wire golden | 包含于上述 CTest，字节比较通过 |
| `git diff --check` | 通过；仅既有 LF/CRLF 提示 |

SDK consumer 验证使用 `cmake --install build/v05-msvc --prefix build/product-native-acceptance/sdk` 后的库；执行前仅在子进程 PATH 前置该 `sdk/bin`。可执行文件为 `build/product-native-acceptance/consumer/scgs_native_v04_package_smoke.exe` 和 `scgs_native_v05_package_smoke.exe`，不是测试 fixture 库的替身。

### 追加：Windows 完整 CTest 与 2,048-seed 压力

随后发现现有缓存的 `SCGS_ENABLE_LEGACY_YGO2_TESTS` 为 `OFF`，因此前述 13 项仅为针对性矩阵。已在本地缓存打开该选项，找到 Python 3.10.11，并运行：

```powershell
cmake -S . -B build/v05-msvc -DSCGS_ENABLE_LEGACY_YGO2_TESTS=ON
cmake --build build/v05-msvc --parallel 4
$env:SCGS_SMOKE_SEEDS = '2048'
ctest --test-dir build/v05-msvc --output-on-failure
.\build\v05-msvc\scgs_tests.exe
```

- 本轮完整 CTest：**23/28 通过、5 项失败，39.78 秒**。不能据此声称完整矩阵全绿。
- 全部 13 项原生测试、legacy Python overlay/protocol、原生与导出审计合同、超时、设计和生成清单通过。
- 独立 `scgs_tests.exe`：**30 个测试、8,685 条断言、0 失败**。`SCGS_SMOKE_SEEDS=2048` 控制合成 v04 引擎压力场景，并对每个 seed 互换先后手；不是 2,048-seed 新产品 v05 平衡测试。当前 v05 真牌局仍为自己的固定 32-seed 场景。
- 失败属于正在退役迁移的视觉源码合同：Gate 3C/4A 仍查旧 report producer/Bootstrap 字符串；Gate 4B 仍断言旧资产数和旧 workflow 路径；R3 仍查旧 profile；Anime slice 仍查旧 Bootstrap 入口和旧 CI 参数。没有把失败标记为忽略或删除验证。
- 失败细节已交给 CI/产品路径迁移工作处理，修复后应另存最终完整重跑记录。
- 日志：`build/product-native-acceptance/ctest-windows-2048-full.log`、`build/product-native-acceptance/native-release-2048.log`。

### 最终本地重跑：Windows 全部 28 项通过

旧合同迁移完成后使用同一 Release 缓存、Python 测试组开启及 `SCGS_SMOKE_SEEDS=2048` 再次构建/运行上述完整命令：**28/28 CTest 通过、0 失败、37.00 秒**。最终日志为 `build/product-native-acceptance/ctest-windows-2048-final.log`；前一轮失败日志保留，不覆盖。

这次通过包括历史 Gate 3C/4A 独立 report fixture、已退役 R2/R3/Anime 入口边界和当前资产审计合同，不是重新启用旧产品画面。当前 Windows 原生/Python/legacy 合同收尾完成；它仍不是远端四平台 CI、Godot GPU 实机或玩家包验收的替代品。

### 最后一次规则修正后的完整重跑

复审发现原先 LO-08/LO-S02 的 `source_owner_turn` 把“每回合限一次”误实现成“每回合周期一次”。本轮将两条声明式效果改为 `source_turn`，同步效果 Schema、生成器、提交的 C++ 目录及生成器契约；没有按牌 ID 添加运行时分支。

新增 `locked_combat_repair_resets_each_players_turn_not_profession_cycle`：分别使用两张真实定义，先在己方回合主动战斗击杀修复，再于紧接的敌方回合防守反击击杀修复；断言自身回合计数未变、第二次修复成功、职业充能未再次增加、LO-10 的自回合触发没有误出现。原有同一敌回合两次反击测试继续验证单回合上限。

```powershell
python scripts/design/generate_product_catalog_v2.py
python -m unittest scripts.tests.test_generate_product_catalog_v2
cmake --build build/v05-msvc --parallel 4
.\build\v05-msvc\scgs_product_game_tests.exe
$env:SCGS_SMOKE_SEEDS = '2048'
ctest --test-dir build/v05-msvc --output-on-failure -C Release
```

- MSVC Release 构建成功，ProductGame **39 项 / 1,397 断言 / 0 失败**；生成器 **17 项通过**。
- 完整 **28/28 CTest 通过、0 失败、39.54 秒**。此前 37.00 秒记录保留为上一轮证据，不替代本次结果。
- 三个动态库再次通过 x64、精确 14 导出和静态 MSVC runtime 审计。
- 最终 v05 DLL SHA-256：`D125527D4F093434FAA00FFD37F8043772C1F5E7A04C7E7C9E89672BDB96C37A`。这里只记录 `build/v05-msvc/scgs_v05.dll`，不声称尚在运行的客户端已自动加载新库。
- 日志：`build/product-native-acceptance/build-source-turn.log`、`product-game-source-turn.log`、`ctest-windows-2048-source-turn-final.log`。生成产物和日志均不提交。

## 尚未承诺

- 本文没有 256-seed sanitizer、GCC、Clang、macOS ARM64 或远端四项 CI 完成证明；Windows 原生/Python 完整 CTest 已按上节重跑通过。
- 32-seed 自然对局以及逐牌场景不代表真人策略、平衡胜率、T10～12 终局目标已验收。
- 未穷举 35 张牌的全部排列/触发交叉组合；新的可复现边界仍需独立回归。
- Native 验证不等于产品 UI 可操作。v05 Godot 的 14 类行动、私密选择、交接遮挡、真实截图及 Windows/macOS 导出必须用新的产品路径另行验收，不能由旧 v04 smoke 代替。
