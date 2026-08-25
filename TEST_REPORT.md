# Gate 5A：誓卫／契术双职业成品牌组设计锁定测试报告

**日期：** 2026-08-25（Asia/Shanghai）

**分支：** `codex/product-decks-v1-design`

**项目基线：** `codex/godot-hotseat-gate4b-r3-visual-slice@74768e29db04cde823f262cb302444445ad10b61`

**设计锁定提交：** `b78e5937812ecab5f64a145181d32d0505529b01`

**精确内容锁修复：** `0a5806d6813d10d57047110dfed8cb503a31416f`

**被测实现尖端：** `0a5806d6813d10d57047110dfed8cb503a31416f`

**范围：** Gate 5A 只增加状态为 `locked_not_implemented` 的机器可读卡池清单、Draft 2020-12 JSON Schema、中文设计文档、能力差距、美术圣经、严格校验器和测试。没有修改 C++、C#、Godot、C ABI、运行时 schema 1、`ActionKind`、精确 14 个原生导出、legacy v1 wire 或视觉素材。

## 结论

[GitHub Actions run 32843707956](https://github.com/Morphling0717/SomeCardGameShit/actions/runs/32843707956) 在被测实现尖端 `0a5806d` 上 **4/4 jobs 全绿**：

| Job | Job ID | 时长 | 结果 |
|---|---:|---:|---|
| Linux GCC Release | `97788600998` | 1 分 04 秒 | 通过 |
| Linux Clang ASan/UBSan | `97788600848` | 2 分 06 秒 | 通过 |
| macOS AppleClang ARM64 Release + Godot | `97788601087` | 3 分 43 秒 | 通过 |
| Windows MSVC Release + Godot | `97788600991` | 1 小时 27 分 12 秒 | 通过 |

Windows 同一干净 checkout 通过 18/18 CTest、锁定 NuGet restore、C# build/test、Godot 默认 3D 与 legacy 2D 整局、R3 候选真实 session、四分辨率 visual/performance 套件、正式导出、打包及 ZIP 往返真实启动；managed 结果为 75/75 succeeded、0 failed、0 skipped，构建为 0 warning、0 error。macOS 通过 ARM64 原生/managed/Godot 导出、ad-hoc 签名、结构审计和真实启动。Linux GCC 与 Clang sanitizer 继续验证原生 Release、压力、协议和制品契约。

这些远端运行仍使用当前旧运行时固定牌组，只证明 Gate 5A 设计文件没有破坏既有工程；它们没有执行誓卫／契术新牌组，也不是新牌组的平衡实战。

## 本地自动化结果

| 验证项 | 结果 | 口径 |
|---|---:|---|
| Gate 5A 设计契约 | 23/23 | `scripts.tests.test_validate_product_decks_v1`；同时是 Python 117 项的子集和第 18 个 CTest target，不重复相加 |
| Python 全集 | 117/117 | `scripts/tests/test_*.py`，0 failed |
| Release CTest | 18/18 targets | 现有原生、协议、Python、报告及 Gate 5A 契约全部通过 |
| 明确输出的原生断言 | 101,991 | 621 + 463 + 31 + 100,876 |
| Draft 2020-12 Schema | 通过 | 仓库内置的 Draft 2020-12 所用关键字子集验证器检查 Schema 与实例，0 error；严格语义校验补足跨字段约束 |
| Managed/.NET | 本机未执行 | 系统 `dotnet` Host 为 8.0.21，但没有任何 SDK；缺少 `global.json` 精确锁定的 10.0.400。远端 Windows/macOS managed 是本轮权威结果 |
| `git diff --check` | 通过 | 被测实现尖端工作区干净 |

第一次实现提交的设计契约为 18 项。对抗式审计随后发现“总数仍正确但单卡数值、文本、精确投入或能力状态被偷改”可能蒙混过关；`0a5806d` 增加 14 个设计分区的规范化 JSON SHA-256 锁，并补充 5 项负面测试。现在以下分区任一合法形状的内容漂移也会明确失败：元数据、规则、职业、主战者、卡牌类型、关键词、能力目录、衍生物、卡牌、牌表、视觉计划、纸面平衡目标、美术方向和旧产品迁移策略。

## 设计清单验收

- 两副主牌均精确 30 张、15 种，同名不超过 3 张；每副公开战备精确 4 张且互不重复；
- 共 34 种可构筑定义、1 个衍生物；两副都投入全部 `NT-01`～`NT-04` 四种中立卡；
- 曜誓主牌恰有 2 张伏策，渊契主牌为 0 张伏策；战备默认不可预支；
- 全部编号、名称、牌表、衍生物、战备条件、视觉主体和能力引用唯一且存在；
- 没有 0 费构筑／战备牌、无限检索、自身循环、额外战备次数或恢复当前 PP；
- 无隙／负契支付后检查、裂痕读取封顶 5、五格随从／护符共享主战场、独立场地格、替换非破坏、倒数离场时序和六个关键词语义均被锁定；
- 38 项未来视觉清单精确为 34 张可构筑卡、1 个衍生物、2 名主战者和 1 张统一卡背，状态全部为 `planned_not_generated`；
- 旧 `midrange`／`advance` 的迁移策略锁为 `delete_not_hide`：下一 Gate 先迁移到合成规则／协议 fixture，再删除旧定义、牌组键、菜单项和旧美术。

## 冻结边界与未完成声明

- 誓卫／契术仍是设计数据；当前客户端和引擎不能创建、提交或完成这两副产品牌组的对局；
- 48%～52% 互换先后手胜率、赢家自己的 T10～12 中位数、T2 行动概率、T6/T10 连动可见率、裂痕峰值及预支／修复范围均是下一 Gate 的纸面验收目标，尚未经过自动对局或真人实战验证；
- 38 项动漫视觉没有生成、下载或提交；本 Gate 只锁定原创日式幻想动漫方向和逐项艺术摘要；
- 当前旧 `midrange`／`advance` 仍服务既有运行时。它们不是隐藏兼容产品内容，后续必须按迁移计划彻底删除；
- 字符串设计编号不冻结 C++ 数字 `CardId` 或 wire 枚举；legacy v1 wire 字节保持不变；
- 本 Gate 没有创建 PR、合并或标签。

## 本地 CTest 明细

`ctest --preset release --output-on-failure`：18/18 targets 通过。

| CTest target | 输出计数 |
|---|---:|
| `scgs_unit_tests` | 30 test cases；621 assertions；0 failures |
| `scgs_client_api_contract` | 463 assertions |
| `scgs_documented_scenario` | 1 个场景；`verified=true`、`invariants_hold=true` |
| `scgs_wire_frozen_golden` | 31 assertions；0 failures |
| `scgs_native_api_c_contract` | C11 consumer smoke passed；程序未输出 assertion 数 |
| `scgs_native_api_contract` | 100,876 assertions |
| `scgs_native_api_dynamic_load` | 精确 14 exports |
| `scgs_ygo2_overlay_patcher` | 5/5 Python tests |
| `scgs_protocol_contract` | 5/5 Python tests |
| `scgs_native_artifact_audit_contract` | 5/5 Python tests |
| `scgs_godot_export_audit_contract` | 10/10 Python tests |
| `scgs_subprocess_timeout_contract` | 3/3 Python tests |
| `scgs_gate3b_report_contract` | 6/6 Python tests |
| `scgs_gate3c_report_contract` | 15/15 Python tests |
| `scgs_gate4a_report_contract` | 14/14 Python tests |
| `scgs_gate4b_visual_pipeline_contract` | 27/27 Python tests |
| `scgs_r3_visual_slice_contract` | 14/14 Python tests |
| `scgs_product_decks_v1_design_contract` | 23/23 Python tests |

CTest 内嵌 Python 共执行 127 项：`scripts/tests` 的 117 项，加上历史 YGOPro2 overlay 与 protocol 各 5 项；不能把这些嵌套计数与 18 个 CTest target 相加成一个“总测试数”。

## 主要复现命令

```text
python scripts/ci/validate_product_decks_v1.py
python -m unittest scripts.tests.test_validate_product_decks_v1
python -m unittest discover -s scripts/tests -p "test_*.py"

cmake --preset release
cmake --build --preset release
ctest --preset release --output-on-failure

dotnet --info
git diff --check
```

## 历史基线与最终尖端

Gate 4B-R3.1 的截图、隐私、制品和四平台证据保留在基线提交 [`74768e2` 的历史测试报告](https://github.com/Morphling0717/SomeCardGameShit/blob/74768e29db04cde823f262cb302444445ad10b61/TEST_REPORT.md)。它说明 Gate 5A 的起点，但不冒充当前实现尖端的验证。

本报告记录的是实现尖端 `0a5806d` 和 run `32843707956`。包含本报告的后续文档提交不会改变产品或测试代码，但分支最终尖端仍必须重新完成同一四项工作流；最终交付不得用实现尖端的绿色 run 冒充尚未运行的文档尖端。
