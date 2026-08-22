# SCGS ↔ YGOPro2 legacy 协议 v1（冻结）

> **历史兼容层，不是 Godot 客户端接口。** 消息 ID、字段顺序、长度、字节序和金标字节必须保持完全不变。字段的历史名称与 v0.4 引擎当前投影语义不同之处在下文明确列出。

所有多字节整数使用小端序。完整消息为：

```text
uint8 message_id
uint8 protocol_version  // 固定为 1
payload fields...
```

旧 YGOPro2 `Package` 将二者拆开：

```text
Package.Fuction     = message_id
Package.Data.reader = protocol_version + payload fields
```

因此 C++ 保留完整消息和 payload-only 两类编码/解码入口。

## PlayerState（211，完整消息 12 字节）

```text
uint8  message_id = 211
uint8  protocol_version = 1
uint8  player
int16  leader_health
int16  maximum_leader_health
uint8  current_pp
uint8  maximum_pp
uint8  evolution_points
uint8  own_turn_number
uint8  flags
```

`flags` 的**当前 v0.4 投影语义**：

| 位 | 当前投影 | 旧 overlay 历史名称 |
|---:|---|---|
| 0 | `evolution_used_this_turn` | 相同 |
| 1 | `deploy_used_this_turn`（本回合已战备部署） | `AdvancedSummonUsedThisTurn` |
| 2 | `trap_set_this_turn` | 相同 |
| 3 | `leader_skill_used` | 相同 |
| 4–7 | 0，保留 | 保留 |

这里的 bit 1 **不是** `advance_used_this_turn`。`current_pp` 和 `pp_capacity` 投影到 uint8 时饱和到 255；wire 字段仍保留历史名称 `maximum_pp`。

固定金标：

```text
D3 01 01 11 00 19 00 03 07 02 06 03
```

## UnitState（212，完整消息 24 字节）

```text
uint8  message_id = 212
uint8  protocol_version = 1
uint8  controller
uint8  sequence
uint64 instance_id
int16  attack
int16  health
int16  maximum_health
uint32 keywords
uint8  inherited_imprint
uint8  flags
```

`flags` 的**当前 v0.4 投影语义**：

| 位 | 当前投影 | 旧 overlay 历史名称 |
|---:|---|---|
| 0 | `evolved` | 相同 |
| 1 | `attacked_this_turn` | 相同 |
| 2 | `entered_this_turn` | 相同 |
| 3 | `deployed_from_standby && entered_this_turn` | `AdvancedSummonedThisTurn` |
| 4 | `face_down` | 相同 |
| 5–7 | 0，保留 | 保留 |

这里的 bit 3 **不是** `temporary_rush`。v0.4 不再使用旧高级召唤概念；bit 3 仅把“由战备部署入场且仍在入场回合”投影到历史位置。`inherited_imprint` 为兼容保留，在当前 v0.4 实例中为 `None`；组件能力没有进入 v1 wire。

固定金标：

```text
D4 01 00 03 08 07 06 05 04 03 02 01
07 00 05 00 08 00 03 00 00 00 01 09
```

## 消息编号

`Message` enum 继续保留 210–219。当前有冻结编解码和金标覆盖的是 211/212；其余编号的存在不代表 v0.4 状态已经完整传输或旧客户端已经可用。

## 冻结不变量

1. `kProtocolVersion == 1`。
2. PlayerState payload 11 字节、完整消息 12 字节。
3. UnitState payload 23 字节、完整消息 24 字节。
4. 多字节整数始终小端。
5. 上述两个金标逐字节不变。
6. C++ 与 legacy C# overlay 的 v1 字段布局不改名、不挪位；文档用“当前投影语义”解释差异。

Godot 同进程客户端将使用独立的版本化 [`scgs_v04` C ABI](native-api-v04.md) 和安全快照，不复用此 wire。未来若做网络协议，必须新建版本/消息，而不是改变 v1 字节含义。
