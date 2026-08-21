# Test report — v0.4 rules engine rewrite

**Date:** 2026-08-21
**Branch:** `ooo/orch_aed84fd9cd53`（任务工作树）
**Scope:** docs/rules-v0.4.md 全量规则落地（PP 容量/预支/燃耗/裂痕/修复/增长、超前/按期、进化重做与职业充能、战备部署、组件能力、三层响应栈、策略区），协议 wire legacy v1 冻结。

**Executed environment:** macOS arm64 (Apple Silicon), AppleClang 17, CMake 4.4.2, Ninja 1.13.2, Python 3.14.

## Conclusion

本轮 v0.4 语义在本地三种配置（Debug / Release / Clang ASan+UBSan）下全部测试通过；协议 wire legacy v1 金标字节保持不变。GitHub 三平台 CI（GCC / Clang-sanitizer / MSVC）尚未在本机触发，需推送后核对。

## Test baseline

```text
22 C++ test cases
391 C++ assertions
0 failures
5/5 CTest targets passed in Debug
5/5 CTest targets passed in Release
5/5 CTest targets passed with Clang ASan + UBSan
```

CTest targets:

1. `scgs_unit_tests` — v0.4 规则套件（22 用例，含 32 种子 × 双方先手烟雾对局 + 每步不变量检查）
2. `scgs_documented_scenario` — 金标走查：§9 预支 → 回合补满 → §13 燃耗叠加 → §17 当前PP>容量 → §15 修复
3. `scgs_wire_frozen_golden` — legacy v1 wire 冻结回归（金标字节、截断/版本/消息 ID 拒绝、桥接饱和映射）
4. `scgs_ygo2_overlay_patcher` — overlay 注入器测试（5 项）
5. `scgs_protocol_contract` — C++/C# 协议契约测试（5 项）

## Golden scenario

`scgs_demo --verify` 输出（摘要）：

```json
{
  "scenario": "documented_overdraw_then_burn_then_repair",
  "verified": true,
  "step1_advance": { "current_pp": 0, "pp_capacity": 2, "cracks": 3 },
  "step2_refill": { "current_pp": 3, "pp_capacity": 3 },
  "step3_burn": { "current_pp": 2, "pp_capacity": 1, "cracks": 5, "pp_above_capacity": true },
  "step4_repair": { "current_pp": 0, "pp_capacity": 3, "cracks": 3 },
  "invariants_hold": true
}
```

## What is covered

- PP 容量无上限增长与回合补满（§7）
- 预支差额支付、每回合一次、容量不可低于 0、足额时不产生裂痕（§9/§10）
- 燃耗代价、燃耗与预支共享动用未来（§12/§13）
- 裂痕跨回合保留与卡牌读取（§14）
- 修复移除裂痕并恢复容量、无裂痕无效、不加当前 PP（§15）
- 增长直接提高容量（§16）
- 当前 PP 合法高于容量（§17）
- 超前/按期状态分支（§11）
- 进化解锁（先 5 后 4）、2 点消耗、每回合 1 次、进化状态数值与默认 +2/+2、进化时触发、本回合可攻单位（§22）
- 职业进化充能条件：死亡计数与法术计数原型、每周期至多 1 点（§23）
- 战备区公开、部署条件/费用/代价、每回合 1 次、部署不能预支、战备牌离场进封存（§24/§25/§5）
- 组件能力：支付部署代价授予、至多一个、离场清除（§31）
- 三层响应栈 LIFO：攻击取消伏策、登场效果伏策、法术窗口、支付类不开窗（§26）
- 策略区满格不替换（§5）
- 手牌上限与溢出封存、递增疲劳（§5/§33）
- 同时战斗伤害、伤害持续、守护阻挡、登场回合攻击限制（§21）
- 同时死亡批次（§28）
- 每步全局不变量 + 确定性烟雾对局（32 种子默认，`SCGS_SMOKE_SEEDS` 可调）

## Wire freeze

- `kProtocolVersion = 1`、PlayerState 12 字节 / UnitState 24 字节布局、docs/protocol.md 金标向量逐字不变
- 桥接投影（v0.4 → legacy wire）：`pp_capacity`/`current_pp` 饱和至 uint8 上限；flags bit1 = 本回合已部署、bit3 = 战备部署入场
- 协议字段增量清单见 `docs/protocol-v1-v2-field-delta.md`（为协议 v2 重设计输入）

## Explicitly not yet verified

- GitHub 三平台 CI（GCC / Clang-sanitizer / MSVC）——需推送任务分支后核对
- 行为覆盖清单的用户逐条过目（`docs/v0.4-behavior-coverage.md`，独立验收基线）
- Unity 客户端与 YGOPro2 overlay 的编辑器编译（本轮范围外）
