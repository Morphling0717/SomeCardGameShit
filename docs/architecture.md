# 架构说明

## 现行结构

项目采用“规则真值、客户端契约、表现层”分层：

```text
卡牌定义 + Game 状态机（C++20，唯一规则真值）
                         │
             共享 validate_* 合法性逻辑
                         │
     ┌───────────────────┴───────────────────┐
     │                                       │
观看者安全查询                         统一 GameCommand
MatchView / LegalAction / Preview       expected_revision
     │                                       │
     └────────── GameEventView ──────────────┘
                    │
       scgs_v04 C11 + UTF-8 JSON（Gate 2）
                    │
        Godot 4.7.2 .NET（Gate 3，后续）
```

客户端不能直接读取 `PlayerState`、自行扣费或复算目标。它只能读取安全快照和查询结果，提交带 revision 的命令，再按观看者读取脱敏事件。legacy YGOPro2/Unity 代码不在现行调用链中。

## 原生 ABI 边界

`scgs_v04` 是纯 C11 动态库接口。公开头只出现明确宽度整数、UTF-8 字节缓冲区和进程内不复用的 64 位 token handle；Windows 固定 `__cdecl`，任何异常都在导出边界转换为 native 状态码。复杂 DTO 不镜像成易碎的 C struct，而是写入调用方所有的两段式 JSON 缓冲区。

ABI version、JSON schema version 与项目包版本相互独立。native/transport 错误和规则 `ErrorCode` 分离；规则枚举由显式映射冻结，不依赖 C++ enum 的底层值。动态库不返回需要跨 CRT 释放的内存，同一 handle 第一版只承诺顺序调用。详见 [`native-api-v04.md`](native-api-v04.md)。

Native 适配层只序列化 Gate 1 的安全 DTO，并且只经 `make_view`、查询、统一命令和 `read_events` 访问对局；它不得先读取 `PlayerState` 或原始事件再做删字段式脱敏。

## 规则域

### `CardDefinition` 与 `CardInstance`

`CardDefinition` 保存不随比赛改变的卡牌数据；`CardInstance` 保存控制者、区域、战斗状态、进化状态、部署来源及运行时组件等实例数据。内部实例 ID 用于引擎关联，但隐藏区域的客户端视图不得暴露稳定 ID。

### `PlayerState`

保存主战者、PP、裂痕、进化能量、每回合限制，以及牌组、手牌、5 个单位位、3 个策略位、战备、墓地和封存区。它是内部状态，不是客户端 DTO。

### `Game`

`Game` 是唯一状态变更入口。产品默认随机先手；测试可强制 `Player0` 或 `Player1`，并可提供 seed。实际 seed 和先手写入开局事件与安全快照。本阶段只保证同一工具链下同 seed 可复现，不承诺 `std::shuffle` 跨标准库产生相同排列。

## 客户端安全契约

### 快照

`make_view(viewer)` 生成 `MatchView`：

- 自己手牌包含完整可操作数据；
- 对方手牌只包含数量；
- 对方背面伏策不包含 definition ID 或 instance ID；
- 战备、单位、墓地和封存区公开；
- 快照包含单调递增的 `revision`、实际 seed 与先手。

快照是最终状态真值；事件只用于日志和表现。

### 查询与命令

`list_legal_actions`、`list_valid_targets`、`list_valid_slots`、`list_valid_donors`、`preview_payment` 和 `get_reaction_context` 与 `submit_command` 共享同一套 `validate_*` 逻辑。不得为旧强类型命令和新统一命令维护两套规则判断。

每条 `GameCommand` 携带 `expected_revision`：

- 成功命令完整结算后，revision 恰好增加一次；
- 失败或过期命令不改变状态、事件历史或 revision；
- 所有公开入口先验证 `PlayerId`，非法枚举返回 `InvalidPlayer`，不得索引数组。

### 事件

`read_events(viewer, after_sequence)` 非破坏性读取追加式事件历史。两个观看者可以使用独立游标，不会互相消费事件。抽牌、调度、设置伏策等隐藏事件按观看者脱敏；隐藏文本不得包含卡名或稳定实例 ID。

Gate 1 的合法行动枚举以事务副本复用命令验证，优先保证固定牌组 alpha 的查询/执行一致性。它会随候选数和事件历史增长而变贵；在开放卡组编辑或扩大手牌上限前，应把纯验证路径从副本执行中抽出并做性能基准。

## 状态机与事务边界

普通命令的核心顺序是：

```text
验证完整输入 → 支付成本 → 执行动作/开响应窗 → LIFO 结算
→ 同时死亡批次 → 胜负检查 → 记录事件 → revision + 1
```

目标必须在支付前完整验证。如果目标在响应期间失效，只跳过依赖该目标的效果；同一效果记录中的其他效果继续，已支付成本不回滚。

结束回合顺序固定为：

```text
结束效果 → 清除临时状态 → 当前 PP 清零并发事件
→ TurnEnded → 对方回合开始
```

终局是幂等终态：一局只产生一个 `MatchEnded`。进入终局后不再抽牌、不处理设施倒计时，也不接受其他会改变状态的命令。

响应栈最多三层，按“反制 → 响应 → 原行动”的 LIFO 顺序结算。实现不得持有跨 `std::vector::push_back` 的元素引用；反制层过牌只关闭该机会，不得丢弃底层伏策或原行动。

## 同时死亡与 alpha 限制

同一批死亡先全部移出场，再按当前回合方、非当前回合方处理触发。人工排序尚未进入 alpha，同一玩家内部暂按确定性场地顺序。这是明确限制，不是最终 UI 规则。

Alpha 只验收现有两副固定牌组。主战技 UI、普通主动能力、人工触发排序和固定牌组未使用关键词延后。

## 不变量与兼容性

`Game::validate_invariants()` 检查区域唯一性、控制权、序列、区域类型、战斗数值、资源、响应层和终局一致性。无界面代理应在每个命令后检查不变量。

legacy v1 wire 的消息 ID、长度、字节序和金标字节保持冻结。当前引擎状态到 legacy 字段的投影语义见 [`protocol.md`](protocol.md)；它不是 Godot 同进程接口，Godot 后续只消费独立的 `scgs_v04` ABI。
