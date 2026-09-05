# v05 观察事件 v1（战斗表现第一阶段）

此扩展记录实际发生的状态变更，供产品表现层消费。它不参与规则判断、支付或
合法行动枚举，也不是通过两张快照的净差推算效果。C ABI 2.0、14 个导出、
JSON schema 主版本 2、既有事件枚举值与旧字段含义不变；这是可选输出成员的
兼容扩展，不能称作 JSON 合同完全未变化。v04 与 legacy wire 不修改。

## 输出合同

`GameEventView.observation` 可省略。存在时为 `ProductEventObservation`：

- `version = 1`、实际发生时的 `revision`、`kind`、`cause_sequence`、`public_to_all`。
- `kind` 为 `move / damage / heal / evolve / state_change / declaration`。
- `source / subject / target` 为端点：`player`、`kind = card|leader`、`hidden`，
  以及仅身份可见时的 `card / design_id`。`subject` 是被移动或被改变者，
  不能把原有事件 `card` 当作受伤者。
- `from / to` 为区域端点：`player / zone / slot?`；只有 MainBoard（0～4）和
  Tactic（0～2）有格位。手牌、牌组绝不包含牌序。
- 移动包含 `move_reason`；伤害包含 `actual_amount / damage_kind / barrier_consumed`。
  战斗分别记录攻方命中和反击命中，不让客户端拆算 `secondary_value`。
- `before / after` 只包含与该对象有关的已知状态：`health / max_health / attack /
  countdown / evolved / keywords`，不输出空状态对象。
- 声明可以包含 `declaration_kind = attack|attack_cancelled|trap_activation`。
  取消不是第二次攻击，伏策宣告也不是命中。

`cause_sequence` 关联导致该次变更的命令／能力，可能指向该命令的第一条事件，
必须不大于当前事件 sequence；开局无命令来源允许 0。原攻击跨响应／不过仍保留
原因果序号，已支付能力跨私密选择也保留原因果序号。它不是动画时长或帧号。

新增事实使用独立事件记录，现有 legacy 意义的事件继续存在。表现层只处理
`observation`，不得同时把旧 `Damage/CardMoved` 再播放一次。未知 observation
kind 可以通用降级，未知 version 和非法结构会报协议错误。

## 隐私和发生时语义

身份、格位和状态在实际变更点冻结，不在读取历史时根据当前区域重新推断。
公开出牌允许从匿名手牌区域移动到精确公开格位；设置背面伏策与抽牌不能因此
公开身份。一个公开对象的离场如果由尚未公开的手牌导致，会省略该私密来源。
不需要知道其来源身份也能准确呈现公开对象的离场。

`public_to_all` 由原生冻结的端点可见性计算。公共演出只接受 true 的事实；
自己能看见的私密事实仍可能为 false。对某观看者隐藏的事件不包含任何端点 ID、
definition、before/after 状态；隐藏牌不允许通过子字段或稳定手牌序号泄露。
两名观看者独立游标继续有效，公开事实在两边逐字段相同。

事实记录不会放宽 Covered/Resolving 的隐私门。演出控制器不能保存完整私密命令，
也不能为了动画提前读取下一观看者；不能解析 `text` 或重新计算规则结果。

## 第一阶段覆盖与边界

已接入真正的区域移动（包括破坏、替换、倒数、额外封存与结算清理）、逐次
随从／主战者伤害、屏障消耗、治疗、进化、显式属性／关键词／倒数变化及攻击声明。
代表回归使用现有定义：LO-11/AP-11 实际入场与进化；NT-04 精确策略位发动、
造成实际伤害，再从同一格送墓。另验证响应取消、支付后选择、终局与失败原子性。

这不宣称完整事件溯源或全部表现已经完成：PP、裂痕、进化能量、回合末临时状态
清除等仍以权威快照为准，尚未全部发出独立表现事实；战斗表现与实际 GPU 验收
也不能用下列规则／托管测试代替。

## 本轮实际检查

- MSVC 构建；5 项 product runtime/game/v05 C/schema/dynamic-load CTest 全通过。
- ProductGame：40 cases / 1,469 assertions；ProductRuntime：20 cases / 1,099 assertions。
- 新动态库实际托管集成、整局和 observation 合同：21/21，0 skipped；TRX 位于
  `build/battle-presentation-v2-observation-managed-final`。
- Clang 22：相关原生实现与测试 `-std=c++20 -Wall -Wextra -Wpedantic -Werror -fsyntax-only` 通过。
- 输出 DLL 仅在 `build/v05-msvc/scgs_v05.dll`；本组件工作不启动 Godot，不将构建结果
  冒充实机演出完成。
