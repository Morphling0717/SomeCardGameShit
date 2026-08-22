# Legacy v1 → 未来网络协议字段差异

> 本文只做差异盘点。legacy v1 完全冻结，未来网络协议尚未设计/实现；Gate 1 安全 C++ API 与 Gate 2 已提供、供 Godot 后续消费的 [`scgs_v04` 同进程 ABI](native-api-v04.md) 都不以 v1 wire 作为接口。

## 1. v1 实际覆盖

### PlayerState（211）

| Wire 字段 | v0.4 当前投影 | 限制 |
|---|---|---|
| `player` | `PlayerId` | 仅 0/1 合法 |
| `leader_health` / `maximum_leader_health` | 主战者生命 | int16 |
| `current_pp` | 当前 PP | uint8 饱和至 255 |
| `maximum_pp` | `pp_capacity` | 历史名称；uint8 饱和至 255 |
| `evolution_points` | 进化能量 | 当前规则封顶 4 |
| `own_turn_number` | 自己回合数 | uint8 |
| flags bit0 | 本回合已进化 | 直接投影 |
| flags bit1 | **本回合已部署** | 旧名称是“已高级召唤”；不是“已预支” |
| flags bit2 | 本回合已设置伏策 | 直接投影 |
| flags bit3 | 本局已使用主战技 | 直接投影 |

### UnitState（212）

| Wire 字段 | v0.4 当前投影 | 限制 |
|---|---|---|
| `controller` / `sequence` | 控制者与单位位置 | sequence uint8 |
| `instance_id` | 内部稳定实例 ID | 不满足隐藏信息安全要求 |
| `attack` / `health` / `maximum_health` | 当前战斗数值 | int16 |
| `keywords` | 关键词掩码 | uint32 |
| `inherited_imprint` | 兼容字段 | v0.4 当前恒为 `None` |
| flags bit0 | 已进化 | 直接投影 |
| flags bit1 | 本回合已攻击 | 直接投影 |
| flags bit2 | 本回合入场 | 直接投影 |
| flags bit3 | **战备部署入场且仍为入场回合** | 旧名称是“高级召唤入场”；不是“临时突进” |
| flags bit4 | 背面 | 只含状态，不提供安全的隐藏卡视图 |

当前策略区是**每名玩家 3 格**，设施与伏策共用；任何写成 2 格的旧文档都属于 v0.1/M1 历史。

## 2. v0.4 / Gate 1 中 v1 缺失的状态

### 玩家与区域

- 裂痕；
- `advance_used_this_turn`（预支与燃耗共用的“动用未来”限制）；
- 进化是否已解锁、职业充能周期与进度；
- 公开战备区完整内容（0–6）；
- 3 个策略位的设施/伏策类型、倒计时和观看者脱敏内容；
- 手牌、牌组数量、墓地和封存区完整安全视图；
- 疲劳计数。

`deploy_used_this_turn` 仅被借位投影到 PlayerState flags bit1，不能据此认为 v1 支持完整部署流程。

### 单位与效果

- `temporary_rush`；
- `deployed_from_standby` 的持久来源状态（v1 bit3 只在 `entered_this_turn` 同时为真时置位）；
- 运行时组件能力；
- 卡牌定义、费用、完整效果和进化数值；
- 响应期间原行动/目标摘要。

### 比赛控制

- 单调 state revision 与过期命令保护；
- 实际随机 seed、实际先手和 `FirstPlayerMode`；
- phase、result 的完整权威状态；
- 三层响应深度、当前 responder、合法伏策和是否可过；
- 追加式事件 sequence、观看者独立游标和事件脱敏；
- `GameCommand`、`LegalAction`、目标/位置/组件来源与支付预览。

## 3. v1 不适合作为 Godot 接口的原因

v1 只是旧显示 overlay 的紧凑投影：

- 会把稳定实例 ID 直接放入消息，不具备隐藏区域隐私边界；
- 没有 revision，无法原子地拒绝过期 UI 操作；
- 没有查询契约，客户端会被迫复制合法性逻辑；
- 无法描述当前 3 格策略区、完整部署或响应栈；
- 字段被历史语义占用，继续借位会让协议不可审查。

因此 Gate 2 已通过版本化 `scgs_v04` C ABI 暴露 `MatchView` / 查询 / `GameCommand` / `GameEventView`；Godot 后续直接消费该接口，legacy v1 只继续跑金标回归。

## 4. 未来网络协议最低需求

如果 alpha 后启动异地联机，应另建新版本并至少覆盖：

| 域 | 必需能力 |
|---|---|
| 身份与并发 | viewer/actor、state revision、命令 ID、明确错误码 |
| 开局复现 | 协议版本、规则/卡池版本、seed、实际先手；若要求跨实现复现，需规定洗牌算法 |
| 安全快照 | 自己完整手牌、对方手牌数量、背面伏策匿名、公开区域完整 |
| 查询 | 合法行动、目标、位置、组件来源、支付预览、响应上下文 |
| 命令 | `ActionKind` 全集与各动作结构化参数 |
| 事件 | 全局 sequence、viewer 脱敏、重连后游标续读 |
| 终局 | 幂等 result 与唯一 MatchEnded |
| 扩展 | 长度/版本明确的字段，未知字段可跳过，不复用 v1 bit 含义 |

是否采用二进制、JSON 或其他编码留到联机设计阶段决定。不能提前将 C++ 类布局、STL 容器或文本日志当成网络协议。

## 5. 保持不变的 v1 验收项

- `kProtocolVersion = 1`；
- PlayerState 12 字节、UnitState 24 字节；
- 小端序；
- `docs/protocol.md` 金标；
- C++ encode/decode、桥接投影和 legacy C# 契约测试；
- Player flags bit1 当前投影部署、Unit flags bit3 当前投影战备部署入场；
- 任何未来工作都不能修改上述字节。
